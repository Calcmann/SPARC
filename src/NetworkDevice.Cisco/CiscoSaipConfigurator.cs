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

    /// <summary>
    /// Opção secreta (atalho Ctrl+Shift+V, checkbox default desligado): quando true, o
    /// provisionamento aplica NAT overload de LAB e o mantém (a limpeza 4b reseta as
    /// interfaces, então o NAT precisa fazer parte do template).
    /// </summary>
    public bool IncluirNatLab { get; set; }

    public CiscoSaipConfigurator(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <summary>
    /// Detecta os nomes exatos das interfaces WAN e LAN a partir do 'show ip interface brief'.
    /// </summary>
    public static (string wanIface, string lanIface) DetectInterfaces(string showIpIntBriefOutput, string preferredWan = "GigabitEthernet 4", string preferredLan = "GigabitEthernet 5")
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

        // WAN (Porta 4 / GE0/4 / GE4 ou GE0/0 ou GE0/0/0)
        string resolvedWan = preferredWan;
        if (ifaces.Any(i => i.Equals("GigabitEthernet0/4", StringComparison.OrdinalIgnoreCase) || i.Equals("GigabitEthernet 0/4", StringComparison.OrdinalIgnoreCase)))
            resolvedWan = "GigabitEthernet0/4";
        else if (ifaces.Any(i => i.Equals("GigabitEthernet4", StringComparison.OrdinalIgnoreCase) || i.Equals("GigabitEthernet 4", StringComparison.OrdinalIgnoreCase)))
            resolvedWan = "GigabitEthernet 4";
        else if (ifaces.Any(i => i.Equals("GigabitEthernet0/0/0", StringComparison.OrdinalIgnoreCase)))
            resolvedWan = "GigabitEthernet0/0/0";
        else if (ifaces.Any(i => i.Equals("GigabitEthernet0/0", StringComparison.OrdinalIgnoreCase) || i.Equals("GigabitEthernet 0/0", StringComparison.OrdinalIgnoreCase)))
            resolvedWan = "GigabitEthernet 0/0";

        // LAN (Porta 5 / GE0/5 / GE5 ou GE0/1 ou GE0/0/1)
        string resolvedLan = preferredLan;
        if (ifaces.Any(i => i.Equals("GigabitEthernet0/5", StringComparison.OrdinalIgnoreCase) || i.Equals("GigabitEthernet 0/5", StringComparison.OrdinalIgnoreCase)))
            resolvedLan = "GigabitEthernet0/5";
        else if (ifaces.Any(i => i.Equals("GigabitEthernet5", StringComparison.OrdinalIgnoreCase) || i.Equals("GigabitEthernet 5", StringComparison.OrdinalIgnoreCase)))
            resolvedLan = "GigabitEthernet 5";
        else if (ifaces.Any(i => i.Equals("GigabitEthernet0/0/1", StringComparison.OrdinalIgnoreCase)))
            resolvedLan = "GigabitEthernet0/0/1";
        else if (ifaces.Any(i => i.Equals("GigabitEthernet0/1", StringComparison.OrdinalIgnoreCase) || i.Equals("GigabitEthernet 0/1", StringComparison.OrdinalIgnoreCase)))
            resolvedLan = "GigabitEthernet 0/1";

        return (resolvedWan, resolvedLan);
    }

    /// <summary>
    /// Gera a lista de comandos CLI Cisco IOS para provisionamento da Ficha SAIP.
    /// </summary>
    public static IReadOnlyList<string> GenerateCommands(
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet 4",
        string lanInterface = "GigabitEthernet 5",
        bool incluirNatLab = false)
    {
        var wanSource = !string.IsNullOrWhiteSpace(circuit.DescriptionRoteador)
            ? circuit.DescriptionRoteador
            : (circuit.DesignacaoIp ?? circuit.NumeroOts ?? "LINK");
        var wanDesc = SanitizeDescription(wanSource, "LINK");
        var lanDesc = SanitizeDescription(circuit.ClienteRazaoSocial, "CLIENTE");

        var cmds = new List<string>
        {
            "configure terminal",
            "no ip domain-lookup",
            "no ip domain lookup",
            "no logging console",
            "line con 0",
            "logging synchronous",
            "exit",

            // 1. WAN (Porta 4 - Nativa WAN)
            $"interface {wanInterface}",
            "no switchport",
            $"description WAN - {wanDesc}",
            $"ip address {circuit.WanIp} {circuit.WanSubnetMask}",
            "no shutdown",
            "exit",

            // 2. LAN (Porta 5 - Nativa LAN)
            $"interface {lanInterface}",
            "no switchport",
            $"description LAN - {lanDesc}",
            $"ip address {circuit.LanIp} {circuit.LanSubnetMask}",
            "no shutdown",
            "exit",

            // 3. Rota Default (Gateway)
            $"ip route 0.0.0.0 0.0.0.0 {circuit.WanGateway}",
        };

        // 3b. NAT overload de LAB (opção secreta, default desligado): reaplica as
        // designações inside/outside DEPOIS do reset das interfaces, então sobrevive à esteira.
        if (incluirNatLab)
        {
            var wildcard = WildcardFromMask(circuit.LanSubnetMask, circuit.LanCidr);
            var lanNet = string.IsNullOrWhiteSpace(circuit.LanBlockNetwork) ? circuit.LanIp : circuit.LanBlockNetwork;
            cmds.AddRange(new[]
            {
                $"interface {wanInterface}",
                "ip nat outside",
                "exit",
                $"interface {lanInterface}",
                "ip nat inside",
                "exit",
                $"access-list 1 permit {lanNet} {wildcard}",
                $"ip nat inside source list 1 interface {wanInterface} overload",
            });
        }

        cmds.AddRange(new[]
        {
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
        });

        return cmds;
    }

    public static string WildcardFromMask(string mask, int cidr)
    {
        string? candidate = mask;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var oct = candidate.Split('.');
                if (oct.Length == 4)
                    return string.Join(".", oct.Select(o => (255 - int.Parse(o.Trim())).ToString()));
            }
            catch { }
            try { candidate = IpCalculator.CidrToSubnetMask(cidr); }
            catch { break; }
        }
        return "0.0.0.7";
    }

    /// <summary>
    /// Aplica a configuração do circuito SAIP no dispositivo Cisco conectado de forma determinística e validada.
    /// </summary>
    public async Task ApplyConfigAsync(
        DeviceSession session,
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet 4",
        string lanInterface = "GigabitEthernet 5",
        CancellationToken cancellationToken = default)
    {
        await ProgressAsync($"[*] INICIANDO PROVISIONAMENTO DA FICHA SAIP ({circuit.DesignacaoIp ?? circuit.NumeroOts})...");

        // 1. Verificação impeditiva: se a sessão estiver em modo ROMMON (sem SO)
        var prompt = (session.CurrentPrompt ?? string.Empty).Trim();
        if (session.Mode == ExecMode.Rommon ||
            prompt.StartsWith("rommon", StringComparison.OrdinalIgnoreCase) ||
            prompt.StartsWith("switch:", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("cannot determine first executable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O equipamento se encontra em modo ROMMON (sem sistema operacional carregado). É OBRIGATÓRIO executar a recuperação de firmware antes de provisionar.");
        }

        // 2. Acorda o terminal e cancela qualquer comando/submodo pendente de forma segura (Ctrl+C + Enter)
        try
        {
            await session.Transport.WriteAsync(new byte[] { 0x03 }, cancellationToken);
            await Task.Delay(100, cancellationToken);
            await session.WriteLineAsync(string.Empty, cancellationToken);
            await Task.Delay(200, cancellationToken);
        }
        catch { }

        // 2. Normaliza o modo do terminal para PrivilegedExec (#) antes de qualquer 'show'.
        // Cenário que quebrava o 1905 com configuração: console parado em (config)# ou
        // (config-if)# faz 'show ip interface brief' retornar '% Invalid input' (no IOS o
        // correto dentro de config seria 'do show ...'), o DetectInterfaces caía no fallback
        // GE4/GE5 (inexistentes no 1905 = GE0/0 + GE0/1) e toda a esteira errava.
        await EnsurePrivilegedExecAsync(session, cancellationToken);

        // 2b. Desativa paginação (--More--) para os 'show' não truncarem em equipo com config
        try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

        // 3. Obtém as interfaces reais do equipamento
        string briefOutput = string.Empty;
        try
        {
            briefOutput = await SendShowAsync(session, "show ip interface brief", TimeSpan.FromSeconds(15), cancellationToken);
            var (detectedWan, detectedLan) = DetectInterfaces(briefOutput, wanInterface, lanInterface);
            wanInterface = detectedWan;
            lanInterface = detectedLan;
            await ProgressAsync($"[*] Interfaces mapeadas: WAN -> '{wanInterface}' | LAN -> '{lanInterface}'");
        }
        catch
        {
            // Usa as portas padrão
        }

        // 4. Limpeza pré-provisionamento — garante configuração limpa (remove vestígios)
        await ProgressAsync("[*] [FASE C] Limpando vestígios de configuração anterior (baseline limpo)...");
        try
        {
            // Desativa lookup DNS imediatamente para evitar travamentos com "Translating... domain server"
            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("no ip domain-lookup", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("no ip domain lookup", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
            // 4a. Remove todas as rotas default antigas (qualquer gateway)
            var showRoutes = await SendShowAsync(session, "show running-config | include ip route", TimeSpan.FromSeconds(10), cancellationToken);
            var routeMatches = Regex.Matches(showRoutes, @"(?im)^\s*ip\s+route\s+0\.0\.0\.0\s+0\.0\.0\.0\s+(\S+)(?:\s+\S+)*");
            foreach (Match m in routeMatches)
            {
                var oldGw = m.Groups[1].Value.Trim();
                await ProgressAsync($"[*] Removendo rota default antiga -> gateway '{oldGw}'...");
                await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync($"no ip route 0.0.0.0 0.0.0.0 {oldGw}", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
            }

            // 4b. Limpa IPs/descrições antigas das interfaces WAN/LAN (default interface = estado limpo Cisco)
            foreach (var iface in new[] { wanInterface, lanInterface })
            {
                await ProgressAsync($"[*] Resetando interface {iface} (default + no ip address)...");
                await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(5), cancellationToken);
                // 'default interface' restaura ao padrão; fallback 'no ip address' + 'no description' se não suportado
                var defRes = await session.SendCommandAsync($"default interface {iface}", TimeSpan.FromSeconds(8), cancellationToken);
                if (defRes.Contains("% Invalid", StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendCommandAsync($"interface {iface}", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("no ip address", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("no description", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("shutdown", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("exit", TimeSpan.FromSeconds(5), cancellationToken);
                }
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
            }

            // 4c. Remove usuários/credenciais residuais que conflitam com EBT/PRO1AN
            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("no username admin", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (Exception ex)
        {
            await ProgressAsync($"[AVISO] Limpeza best-effort falhou parcialmente: {ex.Message} — prosseguindo.");
        }

        // 5. Monta e envia os comandos de provisionamento global (configure terminal)
        await ProgressAsync("[*] Entrando em modo de configuração global (configure terminal)...");
        var confTermRes = await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
        if (confTermRes.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase) ||
            confTermRes.Contains("command not found", StringComparison.OrdinalIgnoreCase) ||
            confTermRes.Contains("syntax error", StringComparison.OrdinalIgnoreCase))
        {
            // Se falhou, tenta enable novamente e tenta config t
            await session.SendCommandAsync("enable", TimeSpan.FromSeconds(10), cancellationToken);
            confTermRes = await session.SendCommandAsync("config t", TimeSpan.FromSeconds(10), cancellationToken);
        }

        var curPrompt = (session.CurrentPrompt ?? string.Empty).Trim();
        if (session.Mode == ExecMode.Rommon ||
            curPrompt.StartsWith("rommon", StringComparison.OrdinalIgnoreCase) ||
            curPrompt.StartsWith("switch:", StringComparison.OrdinalIgnoreCase) ||
            confTermRes.Contains("command not found", StringComparison.OrdinalIgnoreCase) ||
            (confTermRes.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase) && !curPrompt.Contains("(config")))
        {
            throw new InvalidOperationException($"Não foi possível entrar no modo de configuração global do Cisco (configure terminal). Resposta: '{confTermRes.Trim()}'. Verifique se o equipamento possui sistema operacional válido e privilégios de escrita.");
        }
        await Task.Delay(300, cancellationToken);

        // 5. Lista de comandos a serem aplicados com cadência otimizada e segura de 200ms
        var commands = GenerateCommands(circuit, wanInterface, lanInterface, IncluirNatLab);
        if (IncluirNatLab)
            await ProgressAsync("[*] Opção secreta NAT (LAB) ATIVA: inside/outside + overload serão aplicados.");

        await ProgressAsync("[*] Aplicando comandos no roteador (cadência: 200ms por comando)...");
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
                    // Fallback para comando description: se a versão do IOS rejeitar caracteres residuais, aplica versão padrão garantida
                    if (cmd.StartsWith("description ", StringComparison.OrdinalIgnoreCase))
                    {
                        var fallbackCmd = cmd.Contains("WAN", StringComparison.OrdinalIgnoreCase) ? "description WAN" : "description LAN";
                        await ProgressAsync($"    [AVISO] Cisco rejeitou '{cmd}'. Aplicando fallback garantido '{fallbackCmd}'...");
                        await session.SendCommandAsync(fallbackCmd, TimeSpan.FromSeconds(10), cancellationToken);
                    }
                    // Ignora erro se 'no switchport' não for suportado na interface nativa
                    else if (!cmd.Contains("no switchport", StringComparison.OrdinalIgnoreCase))
                    {
                        await ProgressAsync($"    [AVISO] Cisco retornou erro no comando '{cmd}':\n    {response.Trim()}");
                    }
                }
            }
            catch (Exception ex)
            {
                await ProgressAsync($"    [!] Falha ao executar '{cmd}': {ex.Message}");
            }

            await Task.Delay(200, cancellationToken);
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

        try
        {
            var writeRes = await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(30), cancellationToken);
            if (writeRes.Contains("?"))
            {
                await session.WriteLineAsync(string.Empty, cancellationToken);
                var exp = await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(15), cancellationToken);
                writeRes += "\n" + exp.Output;
            }

            if (writeRes.Contains("% Invalid", StringComparison.OrdinalIgnoreCase) ||
                writeRes.Contains("command not found", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Roteador rejeitou gravação na NVRAM (write memory): {writeRes.Trim()}");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Não foi possível confirmar a gravação na NVRAM (write memory): {ex.Message}");
        }

        await ProgressAsync("[OK] Configuração Cisco gravada permanentemente na NVRAM com config-register 0x2102!");

        // 8. Ativação do pool DHCP temporário na RAM (running-config) para os testes automáticos do Android
        try
        {
            await ProgressAsync("[*] Habilitando servidor DHCP temporário na LAN para validação automática do Android...");
            var lanNet = string.IsNullOrWhiteSpace(circuit.LanBlockNetwork)
                ? IpCalculator.CalculateNetworkAddress(circuit.LanIp, circuit.LanCidr)
                : circuit.LanBlockNetwork;

            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync($"ip dhcp excluded-address {circuit.LanIp}", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("ip dhcp pool SPARC_LAN", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync($"network {lanNet} {circuit.LanSubnetMask}", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync($"default-router {circuit.LanIp}", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("dns-server 1.1.1.1 8.8.8.8", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("lease 0 2", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
            await EnsurePrivilegedExecAsync(session, cancellationToken);

            await ProgressAsync("[✓] Servidor DHCP temporário ativo na RAM (running-config). Será removido ao final dos testes.");
        }
        catch (Exception ex)
        {
            await ProgressAsync($"[AVISO] Falha ao ativar DHCP temporário: {ex.Message}. Teste seguirá.");
        }

        await ProgressAsync("[*] PROVISIONAMENTO SAIP CONCLUÍDO COM SUCESSO (Acesso Telnet EBT/PRO1AN ativo)!");
    }

    /// <summary>
    /// Remove o servidor DHCP temporário da memória do roteador e regrava a NVRAM com a configuração 100% estática.
    /// </summary>
    public static async Task RemoverDhcpTemporarioAsync(DeviceSession session, SaipCircuitData circuit, Action<string>? logger = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsurePrivilegedExecAsync(session, cancellationToken);
            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync("no ip dhcp pool SPARC_LAN", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync($"no ip dhcp excluded-address {circuit.LanIp}", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(30), cancellationToken);
            logger?.Invoke("[✓] Servidor DHCP temporário removido da LAN do roteador e NVRAM regravada 100% estática!");
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[AVISO] Não foi possível remover o DHCP temporário automaticamente: {ex.Message}");
        }
    }

    /// <summary>
    /// Normaliza o terminal para PrivilegedExec (hostname#), saindo de qualquer submodo
    /// de configuração ((config)# / (config-if)#) com 'end' e subindo de UserExec (>) com
    /// 'enable'. Sem isso, 'show ...' executado dentro de config retorna '% Invalid input'
    /// e a detecção de interfaces cai no fallback errado (ex: 1905 sem GE4/GE5).
    /// </summary>
    public static async Task EnsurePrivilegedExecAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var output = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(5), cancellationToken);
            var prompt = (session.CurrentPrompt ?? output?.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? string.Empty).Trim();
            var mode = session.Mode;

            var inConfig = mode is ExecMode.GlobalConfig or ExecMode.ConfigSubmode
                || (prompt.Contains("(config", StringComparison.OrdinalIgnoreCase) && prompt.EndsWith("#"));
            if (inConfig)
            {
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
                await Task.Delay(200, cancellationToken);
                continue;
            }

            if (prompt.EndsWith(">") || mode == ExecMode.UserExec)
            {
                await session.SendCommandAsync("enable", TimeSpan.FromSeconds(10), cancellationToken);
                await Task.Delay(300, cancellationToken);
                continue;
            }

            if (prompt.EndsWith("#") || mode is ExecMode.PrivilegedExec or ExecMode.GlobalConfig)
                return;
        }
    }

    /// <summary>
    /// Executa um comando 'show' com segurança em qualquer modo: dentro de config-mode o
    /// IOS exige o prefixo 'do' ('do show ...'); fora dele, o comando vai puro.
    /// </summary>
    public static async Task<string> SendShowAsync(DeviceSession session, string showCommand, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var prompt = (session.CurrentPrompt ?? string.Empty).Trim();
        var inConfig = session.Mode is ExecMode.GlobalConfig or ExecMode.ConfigSubmode
            || (prompt.Contains("(config", StringComparison.OrdinalIgnoreCase) && prompt.EndsWith("#"));
        var cmd = inConfig && !showCommand.TrimStart().StartsWith("do ", StringComparison.OrdinalIgnoreCase)
            ? "do " + showCommand
            : showCommand;
        return await session.SendCommandAsync(cmd, timeout ?? TimeSpan.FromSeconds(15), cancellationToken);
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }

    public static string SanitizeDescription(string? text, string fallback = "LINK")
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        // 1. Remove quebras de linha e tabs substituindo por espaço
        var clean = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");

        // 2. Normalização Unicode FormD para decompor diacríticos (ex: 'Ã' -> 'A' + '~') e manter caracteres legíveis em ASCII puro
        var normalized = clean.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        clean = sb.ToString();

        // 3. Substitui barras '/' e '\' por hífen para não quebrar a sintaxe do Cisco CLI
        clean = clean.Replace('/', '-').Replace('\\', '-');

        // 4. Cisco IOS description: aceita apenas caracteres alfanuméricos, espaços, pontos e hífens
        // Caracteres como '?', '!', '"', ''', '#', '&', ';', etc. causam erro de sintaxe ou disparam help contextual no IOS
        clean = Regex.Replace(clean, @"[^A-Za-z0-9\.\-\s]", " ");

        // 5. Compacta espaços e hífens múltiplos
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        clean = Regex.Replace(clean, @"\-+", "-").Trim('-');

        // 6. Limita tamanho máximo seguro (30 caracteres)
        if (clean.Length > 30)
            clean = clean[..30].Trim().TrimEnd('-');

        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }
}
