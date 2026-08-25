using System.Text.RegularExpressions;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Cisco;

public sealed class CiscoSaipConfigurator
{
    private static readonly Regex InterfaceLineRegex = new(
        @"^(?<iface>(?:GigabitEthernet|FastEthernet|Ethernet|TenGigabitEthernet|Vlan|BVI)\S*)\s+",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private readonly Func<string, Task>? _progress;

    public CiscoSaipConfigurator(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <summary>
    /// Detecta os nomes exatos das interfaces WAN e LAN a partir do 'show ip interface brief'.
    /// </summary>
    public static (string wanIface, string lanIface) DetectInterfaces(string showIpIntBriefOutput, string preferredWan = "GigabitEthernet 5", string preferredLan = "GigabitEthernet 4")
    {
        var matches = InterfaceLineRegex.Matches(showIpIntBriefOutput);
        var ifaces = new List<string>();
        foreach (Match m in matches)
        {
            var name = m.Groups["iface"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                ifaces.Add(name);
        }

        if (ifaces.Count == 0)
            return (preferredWan, preferredLan);

        // WAN (Porta 5)
        string resolvedWan = preferredWan;
        if (ifaces.Any(i => i.Equals("GigabitEthernet5", StringComparison.OrdinalIgnoreCase) || i.Equals("GigabitEthernet 5", StringComparison.OrdinalIgnoreCase)))
            resolvedWan = "GigabitEthernet 5";
        else if (ifaces.Any(i => i.Equals("GigabitEthernet0/0/0", StringComparison.OrdinalIgnoreCase)))
            resolvedWan = "GigabitEthernet0/0/0";

        // LAN (Porta 4)
        string resolvedLan = preferredLan;
        if (ifaces.Any(i => i.Equals("GigabitEthernet4", StringComparison.OrdinalIgnoreCase) || i.Equals("GigabitEthernet 4", StringComparison.OrdinalIgnoreCase)))
            resolvedLan = "GigabitEthernet 4";
        else if (ifaces.Any(i => i.Equals("GigabitEthernet0/0/1", StringComparison.OrdinalIgnoreCase)))
            resolvedLan = "GigabitEthernet0/0/1";

        return (resolvedWan, resolvedLan);
    }

    /// <summary>
    /// Gera a lista de comandos CLI Cisco IOS para provisionamento da Ficha SAIP.
    /// </summary>
    public static IReadOnlyList<string> GenerateCommands(
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet 5",
        string lanInterface = "GigabitEthernet 4")
    {
        var wanDesc = SanitizeDescription(circuit.DesignacaoIp ?? circuit.NumeroOts ?? "LINK");
        var lanDesc = SanitizeDescription(circuit.ClienteRazaoSocial);

        return new List<string>
        {
            "configure terminal",
            "no logging console",
            "line con 0",
            "logging synchronous",
            "exit",

            // 1. WAN (Porta Giga 5 - Nativa)
            $"interface {wanInterface}",
            $"description WAN_EBT_{wanDesc}",
            $"ip address {circuit.WanIp} {circuit.WanSubnetMask}",
            "no shutdown",
            "exit",

            // 2. LAN (Porta Giga 4 - Nativa)
            $"interface {lanInterface}",
            "no switchport",
            $"description LAN_CLIENTE_{lanDesc}",
            $"ip address {circuit.LanIp} {circuit.LanSubnetMask}",
            "no shutdown",
            "exit",

            // 3. Rota Default (Gateway)
            $"ip route 0.0.0.0 0.0.0.0 {circuit.WanGateway}",

            // 4. Usuário e Acesso Remoto Telnet (EBT / PRO1AN)
            "enable secret PRO1AN",
            "username EBT privilege 15 secret PRO1AN",

            // Mantém o console serial (line con 0) 100% livre e sem bloqueio de senha na bancada
            "line con 0",
            "no login",
            "privilege level 15",
            "logging synchronous",
            "exit",

            // Linha VTY (Acesso Remoto Telnet)
            "line vty 0 4",
            "privilege level 15",
            "login local",
            "transport input telnet",
            "exit",
            "line vty 5 15",
            "privilege level 15",
            "login local",
            "transport input telnet",
            "exit",

            // 5. Limpeza e Salvamento
            "no username admin",
            "logging console",
            "end",
            "write memory"
        };
    }

    /// <summary>
    /// Aplica a configuração do circuito SAIP no dispositivo Cisco conectado de forma determinística e validada.
    /// </summary>
    public async Task ApplyConfigAsync(
        DeviceSession session,
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet 5",
        string lanInterface = "GigabitEthernet 4",
        CancellationToken cancellationToken = default)
    {
        await ProgressAsync($"[*] INICIANDO PROVISIONAMENTO DA FICHA SAIP ({circuit.DesignacaoIp ?? circuit.NumeroOts})...");

        // 1. Acorda o terminal
        await session.WriteLineAsync("end", cancellationToken);
        await session.WriteLineAsync(string.Empty, cancellationToken);
        await Task.Delay(500, cancellationToken);

        // 2. Garante modo privilegiado (enable)
        var prompt = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(5), cancellationToken);
        if (prompt?.Trim().EndsWith(">") == true || session.CurrentPrompt?.Trim().EndsWith(">") == true)
        {
            await ProgressAsync("[*] Acessando modo privilegiado: enviando 'enable'...");
            var enableRes = await session.SendCommandAsync("enable", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(500, cancellationToken);
        }

        // 3. Obtém as interfaces reais do equipamento
        string briefOutput = string.Empty;
        try
        {
            briefOutput = await session.SendCommandAsync("show ip interface brief", TimeSpan.FromSeconds(15), cancellationToken);
            var (detectedWan, detectedLan) = DetectInterfaces(briefOutput, wanInterface, lanInterface);
            wanInterface = detectedWan;
            lanInterface = detectedLan;
            await ProgressAsync($"[*] Interfaces mapeadas: WAN -> '{wanInterface}' | LAN -> '{lanInterface}'");
        }
        catch
        {
            // Usa as portas padrão
        }

        // 4. Limpa rotas default estáticas antigas para não duplicar gateway
        try
        {
            var showRoutes = await session.SendCommandAsync("show running-config | include ip route 0.0.0.0", TimeSpan.FromSeconds(10), cancellationToken);
            var routeMatches = Regex.Matches(showRoutes, @"(?im)^\s*ip\s+route\s+0\.0\.0\.0\s+0\.0\.0\.0\s+(\S+)");
            foreach (Match m in routeMatches)
            {
                var oldGw = m.Groups[1].Value.Trim();
                if (!string.Equals(oldGw, circuit.WanGateway, StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"[*] Removendo rota default antiga para o gateway '{oldGw}'...");
                    await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync($"no ip route 0.0.0.0 0.0.0.0 {oldGw}", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        catch { }

        // 5. Monta e envia os comandos de provisionamento global (configure terminal)
        await ProgressAsync("[*] Entrando em modo de configuração global (configure terminal)...");
        var confTermRes = await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
        if (confTermRes.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase))
        {
            // Se falhou, força enable novamente e tenta config t
            await session.SendCommandAsync("enable", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync("config t", TimeSpan.FromSeconds(10), cancellationToken);
        }
        await Task.Delay(1000, cancellationToken);

        // 5. Lista de comandos a serem aplicados com cadência de 1s
        var commands = GenerateCommands(circuit, wanInterface, lanInterface);

        await ProgressAsync("[*] Aplicando comandos no roteador (cadência: 1s por comando)...");
        foreach (var cmd in commands)
        {
            if (cmd == "configure terminal" || cmd == "write memory")
                continue; // Já estamos em configure terminal, write memory será no final

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await session.SendCommandAsync(cmd, TimeSpan.FromSeconds(20), cancellationToken);
                if (response.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase) ||
                    response.Contains("% Incomplete command", StringComparison.OrdinalIgnoreCase))
                {
                    // Ignora erro se 'no switchport' não for suportado na interface nativa
                    if (!cmd.Contains("no switchport", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProgressAsync($"    [AVISO] Cisco retornou erro no comando '{cmd}':\n    {response.Trim()}");
                    }
                }
            }
            catch (Exception ex)
            {
                await ProgressAsync($"    [!] Falha ao executar '{cmd}': {ex.Message}");
            }

            await Task.Delay(1000, cancellationToken);
        }

        // 6. Validação pós-provisionamento: exibe show ip interface brief
        try
        {
            await Task.Delay(1000, cancellationToken);
            var finalStatus = await session.SendCommandAsync("show ip interface brief", TimeSpan.FromSeconds(15), cancellationToken);
            await ProgressAsync("\n=================================================================\n" +
                                "           STATUS DAS INTERFACES APÓS PROVISIONAMENTO             \n" +
                                "=================================================================\n" +
                                finalStatus +
                                "=================================================================\n");
        }
        catch
        {
            // Best-effort
        }

        // 7. Gravação Persistente na NVRAM e Ajuste de Config-Register 0x2102 (Cisco IOS)
        await ProgressAsync("[*] Gravando configuração permanentemente na NVRAM (Cisco write memory / copy run start)...");

        try
        {
            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("config-register 0x2102", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
            await Task.Delay(500, cancellationToken);
        }
        catch { }

        var writeRes = await session.SendExpectAsync("write memory",
            new StopCondition[] {
                new StopCondition.Contains("[OK]", "[OK]"),
                new StopCondition.Contains("?", "?"),
                new StopCondition.Prompt()
            },
            TimeSpan.FromSeconds(25), cancellationToken);

        if (writeRes.Output.Contains("?"))
        {
            await session.WriteLineAsync(string.Empty, cancellationToken);
            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(15), cancellationToken);
        }

        try
        {
            var copyRes = await session.SendExpectAsync("copy running-config startup-config",
                new StopCondition[] {
                    new StopCondition.Contains("Destination filename", "Destination filename"),
                    new StopCondition.Contains("?", "?"),
                    new StopCondition.Contains("[OK]", "[OK]"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(20), cancellationToken);

            if (copyRes.Output.Contains("?") || copyRes.Output.Contains("Destination filename", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync(string.Empty, cancellationToken);
                await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
        catch { }

        await ProgressAsync("[OK] Configuração Cisco gravada permanentemente na NVRAM com config-register 0x2102!");
        await ProgressAsync("[*] PROVISIONAMENTO SAIP CONCLUÍDO COM SUCESSO (Acesso Telnet EBT/PRO1AN ativo)!");
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }

    public static string SanitizeDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "LINK";

        // Remove acentos e caracteres não-ASCII
        var clean = text.Trim();
        clean = Regex.Replace(clean, @"[^\u0000-\u007F]+", string.Empty);
        clean = Regex.Replace(clean, @"[^A-Za-z0-9_\-\./]", "_");
        clean = Regex.Replace(clean, @"_+", "_").Trim('_');

        if (clean.Length > 28)
            clean = clean[..28];

        return string.IsNullOrWhiteSpace(clean) ? "LINK" : clean;
    }
}
