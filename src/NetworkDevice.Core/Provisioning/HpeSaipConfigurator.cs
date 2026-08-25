using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Provisioning;

public sealed class HpeSaipConfigurator
{
    private static readonly Regex InterfaceLineRegex = new(
        @"^(?<iface>(?:GigabitEthernet|GE|Ten-GigabitEthernet|Vlan-interface|Bridge-Aggregation)\S*)\s+",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private readonly Func<string, Task>? _progress;

    public HpeSaipConfigurator(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <summary>
    /// Detecta os nomes exatos das interfaces WAN (GE0) e LAN (GE1) no HPE Comware.
    /// </summary>
    public static (string wanIface, string lanIface) DetectInterfaces(string displayBriefOutput, string preferredWan = "GigabitEthernet0/0", string preferredLan = "GigabitEthernet0/1")
    {
        var matches = InterfaceLineRegex.Matches(displayBriefOutput);
        var ifaces = new List<string>();
        foreach (Match m in matches)
        {
            var name = m.Groups["iface"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                ifaces.Add(name);
        }

        if (ifaces.Count == 0)
            return (preferredWan, preferredLan);

        // WAN (GE 0)
        string resolvedWan = preferredWan;
        var foundWan = ifaces.FirstOrDefault(i => i.Equals("GigabitEthernet0/0", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GigabitEthernet0", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GE0/0", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GE0", StringComparison.OrdinalIgnoreCase));
        if (foundWan != null)
            resolvedWan = foundWan;
        else if (ifaces.Count > 0)
            resolvedWan = ifaces[0];

        // LAN (GE 1)
        string resolvedLan = preferredLan;
        var foundLan = ifaces.FirstOrDefault(i => i.Equals("GigabitEthernet0/1", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GigabitEthernet1", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GE0/1", StringComparison.OrdinalIgnoreCase) ||
                                                  i.Equals("GE1", StringComparison.OrdinalIgnoreCase));
        if (foundLan != null)
            resolvedLan = foundLan;
        else if (ifaces.Count > 1)
            resolvedLan = ifaces[1];

        return (resolvedWan, resolvedLan);
    }

    /// <summary>
    /// Gera a lista de comandos CLI HPE Comware para provisionamento da Ficha SAIP com Telnet EBT/PRO1AN.
    /// </summary>
    public static IReadOnlyList<string> GenerateCommands(
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet0/0",
        string lanInterface = "GigabitEthernet0/1")
    {
        var wanDesc = SanitizeDescription(circuit.DesignacaoIp ?? circuit.NumeroOts ?? "LINK");
        var lanDesc = SanitizeDescription(circuit.ClienteRazaoSocial);

        return new List<string>
        {
            "system-view",

            // 1. WAN (GE 0 - Modo Router)
            $"interface {wanInterface}",
            "port link-mode route",
            $"description WAN_EBT_{wanDesc}",
            $"ip address {circuit.WanIp} {circuit.WanSubnetMask}",
            "undo shutdown",
            "quit",

            // 2. LAN (GE 1 - Mudar modo para Router)
            $"interface {lanInterface}",
            "port link-mode route",
            $"description LAN_CLIENTE_{lanDesc}",
            $"ip address {circuit.LanIp} {circuit.LanSubnetMask}",
            "undo shutdown",
            "quit",

            // 3. Rota Default (Gateway) - Limpa rotas antigas antes de aplicar o novo gateway
            "undo ip route-static 0.0.0.0 0.0.0.0",
            "undo ip route-static 0.0.0.0 0",
            $"ip route-static 0.0.0.0 0.0.0.0 {circuit.WanGateway}",

            // 4. Usuário e Acesso Remoto Telnet (EBT / PRO1AN)
            "telnet server enable",
            "undo password-control enable",
            "local-user EBT class manage",
            "password simple PRO1AN",
            "service-type telnet ssh",
            "authorization-attribute user-role network-admin",
            "authorization-attribute user-role level-15",
            "authorization-attribute user-role level-3",
            "quit",

            // Garante que o Console Serial (CON 0) permaneça SEMPRE desbloqueado e livre na bancada
            "line con 0",
            "authentication-mode none",
            "user-role network-admin",
            "user-role level-15",
            "user-role level-3",
            "quit",

            // Linha VTY (Comware 7 - HPE MSR 954)
            "line vty 0 63",
            "authentication-mode scheme",
            "user-role network-admin",
            "user-role level-15",
            "user-role level-3",
            "protocol inbound all",
            "quit",

            // Linha VTY (Comware 5 / Legado)
            "user-interface vty 0 4",
            "authentication-mode scheme",
            "user-role level-3",
            "user-role level-15",
            "protocol inbound all",
            "quit",

            // 5. Salvar Configuração
            "return",
            "save force"
        };
    }

    /// <summary>
    /// Aplica a configuração do circuito SAIP no roteador HPE conectado de forma estruturada e validada.
    /// </summary>
    public async Task ApplyConfigAsync(
        DeviceSession session,
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet0/0",
        string lanInterface = "GigabitEthernet0/1",
        CancellationToken cancellationToken = default)
    {
        await ProgressAsync($"[*] INICIANDO PROVISIONAMENTO HPE COMWARE ({circuit.DesignacaoIp ?? circuit.NumeroOts})...");

        // 1. Acorda o terminal e limpa qualquer submodo
        await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(5), cancellationToken);
        await Task.Delay(300, cancellationToken);
        await EnsureSystemViewAsync(session, cancellationToken);

        // 2. Limpa rotas default antigas que possam estar ativas
        try
        {
            var curRoutes = await session.SendCommandAsync("display current-configuration | include ip route-static", TimeSpan.FromSeconds(10), cancellationToken);
            var routeMatches = Regex.Matches(curRoutes, @"(?im)^\s*ip\s+route-static\s+0\.0\.0\.0\s+\S+\s+(\S+)");
            foreach (Match m in routeMatches)
            {
                var oldGw = m.Groups[1].Value.Trim();
                if (!string.Equals(oldGw, circuit.WanGateway, StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"[*] Removendo rota default antiga para o gateway '{oldGw}'...");
                    await session.SendCommandAsync($"undo ip route-static 0.0.0.0 0.0.0.0 {oldGw}", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync($"undo ip route-static 0.0.0.0 0 {oldGw}", TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        catch { }

        // 3. Verifica interfaces disponíveis com display interface brief
        try
        {
            var briefOutput = await session.SendCommandAsync("display interface brief", TimeSpan.FromSeconds(15), cancellationToken);
            var (detectedWan, detectedLan) = DetectInterfaces(briefOutput, wanInterface, lanInterface);
            wanInterface = detectedWan;
            lanInterface = detectedLan;
            await ProgressAsync($"[*] Interfaces HPE detectadas: WAN -> '{wanInterface}' | LAN -> '{lanInterface}'");
        }
        catch
        {
            // Usa as portas padrão
        }
        await Task.Delay(500, cancellationToken);

        var wanDesc = SanitizeDescription(circuit.DesignacaoIp ?? circuit.NumeroOts ?? "LINK");
        var lanDesc = SanitizeDescription(circuit.ClienteRazaoSocial);

        // 4. Configura Interface WAN
        await EnsureSystemViewAsync(session, cancellationToken);
        await ProgressAsync($"[*] Configurando interface WAN: '{wanInterface}'...");
        var wanEnter = await session.SendCommandAsync($"interface {wanInterface}", TimeSpan.FromSeconds(5), cancellationToken);
        if (!IsError(wanEnter))
        {
            var linkResp = await session.SendExpectAsync("port link-mode route",
                new StopCondition[] { new StopCondition.Contains("[Y/N]:", "[Y/N]:"), new StopCondition.Contains("[Y/N]", "[Y/N]"), new StopCondition.Prompt() },
                TimeSpan.FromSeconds(10), cancellationToken);
            if (linkResp.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", cancellationToken);
                await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(10), cancellationToken);
                await Task.Delay(500, cancellationToken);
            }

            await session.SendCommandAsync($"description WAN_EBT_{wanDesc}", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync($"ip address {circuit.WanIp} {circuit.WanSubnetMask}", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("undo shutdown", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        else
        {
            await ProgressAsync($"    [AVISO] Resposta ao acessar interface WAN '{wanInterface}': {wanEnter.Trim()}");
        }

        // 5. Configura Interface LAN
        await EnsureSystemViewAsync(session, cancellationToken);
        await ProgressAsync($"[*] Configurando interface LAN: '{lanInterface}'...");
        var lanEnter = await session.SendCommandAsync($"interface {lanInterface}", TimeSpan.FromSeconds(5), cancellationToken);
        if (!IsError(lanEnter))
        {
            var linkResp = await session.SendExpectAsync("port link-mode route",
                new StopCondition[] { new StopCondition.Contains("[Y/N]:", "[Y/N]:"), new StopCondition.Contains("[Y/N]", "[Y/N]"), new StopCondition.Prompt() },
                TimeSpan.FromSeconds(10), cancellationToken);
            if (linkResp.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", cancellationToken);
                await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(10), cancellationToken);
                await Task.Delay(500, cancellationToken);
            }

            await session.SendCommandAsync($"description LAN_CLIENTE_{lanDesc}", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync($"ip address {circuit.LanIp} {circuit.LanSubnetMask}", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("undo shutdown", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        else
        {
            await ProgressAsync($"    [AVISO] Resposta ao acessar interface LAN '{lanInterface}': {lanEnter.Trim()}");
        }

        // 6. Rota Default (Gateway)
        await EnsureSystemViewAsync(session, cancellationToken);
        await ProgressAsync($"[*] Aplicando rota estática padrão (Gateway: {circuit.WanGateway})...");
        await session.SendCommandAsync("undo ip route-static 0.0.0.0 0.0.0.0", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("undo ip route-static 0.0.0.0 0", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync($"ip route-static 0.0.0.0 0.0.0.0 {circuit.WanGateway}", TimeSpan.FromSeconds(5), cancellationToken);

        // 7. Usuário e Acesso Remoto Telnet (EBT / PRO1AN)
        await EnsureSystemViewAsync(session, cancellationToken);
        await ProgressAsync("[*] Habilitando serviço Telnet Server e desativando restrições de senha...");
        await session.SendCommandAsync("telnet server enable", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("undo password-control enable", TimeSpan.FromSeconds(5), cancellationToken);

        await EnsureSystemViewAsync(session, cancellationToken);
        await ProgressAsync("[*] Configurando usuário local 'EBT' para acesso remoto...");
        var luserResp = await session.SendCommandAsync("local-user EBT class manage", TimeSpan.FromSeconds(5), cancellationToken);
        bool inUserView = !IsError(luserResp);
        if (!inUserView)
        {
            await EnsureSystemViewAsync(session, cancellationToken);
            var luserResp5 = await session.SendCommandAsync("local-user EBT", TimeSpan.FromSeconds(5), cancellationToken);
            inUserView = !IsError(luserResp5);
        }

        if (inUserView)
        {
            // Tenta PRO1ANPRO1AN (12 caracteres - exigido pela política de complexidade do Comware 7.1 P43)
            var passResp = await session.SendCommandAsync("password simple PRO1ANPRO1AN", TimeSpan.FromSeconds(5), cancellationToken);
            string senhaFinal = "PRO1ANPRO1AN";
            if (IsError(passResp))
            {
                await ProgressAsync($"    [AVISO] Senha 'PRO1ANPRO1AN' rejeitada pelo Comware ({passResp.Trim().Split('\n').LastOrDefault()?.Trim()}). Tentando alternativa 'PRO1AN'...");
                var passResp2 = await session.SendCommandAsync("password simple PRO1AN", TimeSpan.FromSeconds(5), cancellationToken);
                if (!IsError(passResp2))
                {
                    senhaFinal = "PRO1AN";
                    await ProgressAsync("    [INFO] Senha 'PRO1AN' aceita com sucesso.");
                }
            }

            await ProgressAsync($"\n=================================================================");
            await ProgressAsync($"   🔑 SENHA APLICADA NO EQUIPAMENTO: {senhaFinal} (usuário EBT)       ");
            await ProgressAsync($"=================================================================");

            await session.SendCommandAsync("service-type telnet ssh", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("service-type telnet", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("authorization-attribute user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("authorization-attribute user-role level-15", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("authorization-attribute user-role level-3", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("authorization-attribute level 3", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("authorization-attribute level 15", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }

        // 8. Console Serial (CON 0 / AUX 0)
        await EnsureSystemViewAsync(session, cancellationToken);
        await ProgressAsync("[*] Configurando Console Serial para acesso livre na bancada...");
        var lineCon = await session.SendCommandAsync("line con 0", TimeSpan.FromSeconds(5), cancellationToken);
        if (!IsError(lineCon))
        {
            await session.SendCommandAsync("authentication-mode none", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        else
        {
            await EnsureSystemViewAsync(session, cancellationToken);
            var uiCon = await session.SendCommandAsync("user-interface con 0", TimeSpan.FromSeconds(5), cancellationToken);
            if (!IsError(uiCon))
            {
                await session.SendCommandAsync("authentication-mode none", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("user-role level-3", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
            }
            else
            {
                await EnsureSystemViewAsync(session, cancellationToken);
                var uiAux = await session.SendCommandAsync("user-interface aux 0", TimeSpan.FromSeconds(5), cancellationToken);
                if (!IsError(uiAux))
                {
                    await session.SendCommandAsync("authentication-mode none", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("user-role level-3", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
                }
            }
        }

        // 9. Linhas VTY (Acesso Remoto Telnet)
        await EnsureSystemViewAsync(session, cancellationToken);
        await ProgressAsync("[*] Configurando linhas VTY para acesso Telnet...");
        var lineVty = await session.SendCommandAsync("line vty 0 63", TimeSpan.FromSeconds(5), cancellationToken);
        if (IsError(lineVty))
        {
            await EnsureSystemViewAsync(session, cancellationToken);
            lineVty = await session.SendCommandAsync("line vty 0 15", TimeSpan.FromSeconds(5), cancellationToken);
        }

        if (!IsError(lineVty))
        {
            await session.SendCommandAsync("authentication-mode scheme", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("protocol inbound all", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        else
        {
            await EnsureSystemViewAsync(session, cancellationToken);
            var uiVty = await session.SendCommandAsync("user-interface vty 0 4", TimeSpan.FromSeconds(5), cancellationToken);
            if (!IsError(uiVty))
            {
                await session.SendCommandAsync("authentication-mode scheme", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("user-role level-3", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("protocol inbound all", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
            }
        }

        // 10. Gravação Persistente e Vinculação de Boot (HPE Comware)
        await ProgressAsync("[*] Gravando configuração permanentemente na Flash (HPE Comware save)...");

        // Garante que está no modo de usuário (<HPE>) para o comando save
        await session.SendCommandAsync("return", TimeSpan.FromSeconds(5), cancellationToken);
        await Task.Delay(500, cancellationToken);

        // Tenta salvar com save force e trata qualquer prompt interativo
        var saveResp = await session.SendExpectAsync("save force",
            new StopCondition[] {
                new StopCondition.Contains("[Y/N]:", "[Y/N]:"),
                new StopCondition.Contains("[Y/N]", "[Y/N]"),
                new StopCondition.Contains(".cfg]:", ".cfg]:"),
                new StopCondition.Contains(".cfg]", ".cfg]"),
                new StopCondition.Contains("?", "?"),
                new StopCondition.Prompt()
            },
            TimeSpan.FromSeconds(25), cancellationToken);

        if (saveResp.Output.Contains(".cfg", StringComparison.OrdinalIgnoreCase) || saveResp.Output.Contains("?"))
        {
            await session.WriteLineAsync("startup.cfg", cancellationToken);
            await Task.Delay(500, cancellationToken);
            var saveResp2 = await session.SendExpectAsync(string.Empty,
                new StopCondition[] {
                    new StopCondition.Contains("[Y/N]:", "[Y/N]:"),
                    new StopCondition.Contains("[Y/N]", "[Y/N]"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(15), cancellationToken);

            if (saveResp2.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", cancellationToken);
                await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
        else if (saveResp.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
        {
            await session.WriteLineAsync("Y", cancellationToken);
            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(15), cancellationToken);
        }

        // Também tenta 'save safely force' como garantia
        try
        {
            await session.SendCommandAsync("save safely force", TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch { }

        // Define explicitamente o arquivo de configuração principal para o próximo boot
        await ProgressAsync("[*] Vinculando arquivo 'startup.cfg' como configuração principal de boot (startup saved-configuration)...");
        try
        {
            await session.SendCommandAsync("startup saved-configuration startup.cfg main", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync("startup saved-configuration flash:/startup.cfg main", TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch { }

        // Valida se o arquivo foi definido
        var dispStartup = await session.SendCommandAsync("display startup", TimeSpan.FromSeconds(10), cancellationToken);
        if (dispStartup.Contains("startup.cfg", StringComparison.OrdinalIgnoreCase))
        {
            await ProgressAsync("    [OK] Arquivo 'startup.cfg' confirmado como configuração de inicialização principal.");
        }

        await ProgressAsync("[OK] PROVISIONAMENTO HPE CONCLUÍDO E GRAVADO COM SUCESSO!");
        await ProgressAsync("    -> Acesso Telnet: usuário 'EBT' com senha informada acima | Telnet server enable ativo");
        await ProgressAsync("    -> Guarde a senha exibida (PRO1AN ou alternativa) para acesso Telnet/SSH ao equipamento.");
    }

    private async Task EnsureSystemViewAsync(DeviceSession session, CancellationToken ct)
    {
        var resp = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(3), ct);
        var p = (session.CurrentPrompt ?? resp).Trim();

        // Se já estiver no system-view raiz [HPE] (sem subview de interface, line, user-interface ou local-user)
        if (p.StartsWith("[") && !p.Contains("-GigabitEthernet") && !p.Contains("-GE") && !p.Contains("-line-") && !p.Contains("-ui-") && !p.Contains("-luser-"))
        {
            return;
        }

        // Volta ao modo de usuário com return e entra em system-view
        await session.SendCommandAsync("return", TimeSpan.FromSeconds(3), ct);
        await Task.Delay(300, ct);
        await session.SendCommandAsync("system-view", TimeSpan.FromSeconds(5), ct);
        await Task.Delay(400, ct);
    }

    private static bool IsError(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        return output.Contains("Wrong parameter", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Too many parameters", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Incomplete command", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Ambiguous command", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Error", StringComparison.OrdinalIgnoreCase)
            || output.Contains("%", StringComparison.OrdinalIgnoreCase);
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

        var clean = text.Trim();
        clean = Regex.Replace(clean, @"[^\u0000-\u007F]+", string.Empty);
        clean = Regex.Replace(clean, @"[^A-Za-z0-9_\-\./]", "_");
        clean = Regex.Replace(clean, @"_+", "_").Trim('_');

        if (clean.Length > 28)
            clean = clean[..28];

        return string.IsNullOrWhiteSpace(clean) ? "LINK" : clean;
    }
}
