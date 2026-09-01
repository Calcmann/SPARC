using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Provisioning;

public enum HpeComwareView
{
    UserView,
    SystemView,
    InterfaceView,
    LocalUserView,
    LineView,
    Unknown
}

public sealed class HpeSaipConfigurator
{
    private static readonly Regex InterfaceLineRegex = new(
        @"^(?<iface>(?:GigabitEthernet|GE|Ten-GigabitEthernet|Vlan-interface|Bridge-Aggregation)\S*)\s+",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex StaticRouteRegex = new(
        @"(?im)^\s*(?<route>ip\s+route-static\s+\S+\s+\S+.*)$",
        RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;

    public HpeSaipConfigurator(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <summary>
    /// Detecta a View / Contexto atual do HPE Comware a partir do prompt serial.
    /// </summary>
    public static HpeComwareView DetectView(string promptOrOutput)
    {
        if (string.IsNullOrWhiteSpace(promptOrOutput))
            return HpeComwareView.Unknown;

        var lastLine = promptOrOutput.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? string.Empty;

        // User View: <HPE>
        if (lastLine.StartsWith("<") && lastLine.EndsWith(">"))
            return HpeComwareView.UserView;

        // Sub-views ou System View: [...]
        if (lastLine.StartsWith("[") && lastLine.EndsWith("]"))
        {
            if (lastLine.Contains("-GigabitEthernet", StringComparison.OrdinalIgnoreCase) ||
                lastLine.Contains("-GE", StringComparison.OrdinalIgnoreCase) ||
                lastLine.Contains("-Vlan-interface", StringComparison.OrdinalIgnoreCase) ||
                lastLine.Contains("-Bridge-Aggregation", StringComparison.OrdinalIgnoreCase))
            {
                return HpeComwareView.InterfaceView;
            }

            if (lastLine.Contains("-luser-", StringComparison.OrdinalIgnoreCase))
                return HpeComwareView.LocalUserView;

            if (lastLine.Contains("-line-", StringComparison.OrdinalIgnoreCase) ||
                lastLine.Contains("-ui-", StringComparison.OrdinalIgnoreCase) ||
                lastLine.Contains("-vty-", StringComparison.OrdinalIgnoreCase))
            {
                return HpeComwareView.LineView;
            }

            // [HPE] (sem hífen de submodo)
            return HpeComwareView.SystemView;
        }

        return HpeComwareView.Unknown;
    }

    /// <summary>
    /// Garante que o terminal esteja no System View [HPE], sem enviar comandos redundantes.
    /// </summary>
    public static async Task EnsureSystemViewAsync(DeviceSession session, Func<string, Task>? progress = null, CancellationToken ct = default)
    {
        var prompt = (session.CurrentPrompt ?? string.Empty).Trim();
        var currentView = DetectView(prompt);

        if (currentView == HpeComwareView.Unknown)
        {
            var testRes = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(2), ct);
            currentView = DetectView(testRes);
        }

        if (currentView == HpeComwareView.SystemView)
            return;

        if (currentView == HpeComwareView.UserView)
        {
            await session.SendCommandAsync("system-view", TimeSpan.FromSeconds(5), ct);
            return;
        }

        // Se estiver em sub-view (Interface, luser, line, etc.), envia quit ou return
        if (currentView is HpeComwareView.InterfaceView or HpeComwareView.LocalUserView or HpeComwareView.LineView)
        {
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), ct);
            var nextPrompt = (session.CurrentPrompt ?? string.Empty).Trim();
            if (DetectView(nextPrompt) == HpeComwareView.SystemView)
                return;
        }

        // Fallback: return para User View e entra em system-view
        await session.SendCommandAsync("return", TimeSpan.FromSeconds(3), ct);
        await Task.Delay(200, ct);
        await session.SendCommandAsync("system-view", TimeSpan.FromSeconds(5), ct);
    }

    /// <summary>
    /// Garante que o terminal esteja no User View raiz &lt;HPE&gt;, nunca enviando return se já estiver no User View.
    /// </summary>
    public static async Task EnsureUserViewAsync(DeviceSession session, Func<string, Task>? progress = null, CancellationToken ct = default)
    {
        var prompt = (session.CurrentPrompt ?? string.Empty).Trim();
        var currentView = DetectView(prompt);

        if (currentView == HpeComwareView.Unknown)
        {
            var testRes = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(2), ct);
            currentView = DetectView(testRes);
        }

        if (currentView == HpeComwareView.UserView)
            return;

        // Envia return apenas se estiver em SystemView ou Sub-views
        await session.SendCommandAsync("return", TimeSpan.FromSeconds(3), ct);
        await Task.Delay(200, ct);
    }

    /// <summary>
    /// Interpreta as rotas estáticas existentes a partir do output de 'display current-configuration'.
    /// </summary>
    public static IReadOnlyList<string> ParseStaticRoutes(string displayConfigOutput)
    {
        var routes = new List<string>();
        if (string.IsNullOrWhiteSpace(displayConfigOutput))
            return routes;

        var matches = StaticRouteRegex.Matches(displayConfigOutput);
        foreach (Match m in matches)
        {
            var route = m.Groups["route"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(route) && !routes.Contains(route, StringComparer.OrdinalIgnoreCase))
            {
                routes.Add(route);
            }
        }
        return routes;
    }

    /// <summary>
    /// Gera os comandos de undo correspondentes para as rotas estáticas encontradas.
    /// </summary>
    public static IReadOnlyList<string> GenerateUndoStaticRoutes(IEnumerable<string> existingRoutes)
    {
        var undos = new List<string>();
        foreach (var route in existingRoutes)
        {
            var trimmed = route.Trim();
            if (!trimmed.StartsWith("undo ", StringComparison.OrdinalIgnoreCase))
                undos.Add($"undo {trimmed}");
        }
        return undos;
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
    /// Gera a lista de comandos CLI HPE Comware para provisionamento da Ficha SAIP com sintaxe determinística Comware 7.
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

            // 2. LAN (GE 1 - Modo Router)
            $"interface {lanInterface}",
            "port link-mode route",
            $"description LAN_CLIENTE_{lanDesc}",
            $"ip address {circuit.LanIp} {circuit.LanSubnetMask}",
            "undo shutdown",
            "quit",

            // 3. Rota Default Canônica (Única sintaxe)
            $"ip route-static 0.0.0.0 0.0.0.0 {circuit.WanGateway}",

            // 4. Usuário e Acesso Remoto Telnet (EBT / PRO1ANPRO1AN) - Padrão Comware 7
            "telnet server enable",
            "undo password-control enable",
            "local-user EBT class manage",
            "password simple PRO1ANPRO1AN",
            "service-type telnet",
            "authorization-attribute user-role network-admin",
            "quit",

            // Console Serial (CON 0)
            "line con 0",
            "authentication-mode none",
            "user-role network-admin",
            "quit",

            // Linha VTY Telnet (Comware 7 - HPE MSR 954)
            "line vty 0 63",
            "authentication-mode scheme",
            "user-role network-admin",
            "protocol inbound telnet",
            "quit",

            // 5. Salvar Configuração Canônica
            "return",
            "save safely force"
        };
    }

    /// <summary>
    /// Aplica a configuração do circuito SAIP no roteador HPE com sintaxe determinística e validação completa.
    /// </summary>
    public async Task<HpeValidationReport> ApplyConfigAsync(
        DeviceSession session,
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet0/0",
        string lanInterface = "GigabitEthernet0/1",
        CancellationToken cancellationToken = default)
    {
        await ProgressAsync($"[*] [AUTO] HPE MSR954 identificado ({circuit.DesignacaoIp ?? circuit.NumeroOts})...");

        // 1. Valida User View / System View inicial
        await EnsureUserViewAsync(session, _progress, cancellationToken);
        await ProgressAsync("[OK] User View confirmado");

        await EnsureSystemViewAsync(session, _progress, cancellationToken);

        // 2. Limpeza pré-provisionamento determinística
        await ProgressAsync("[*] [FASE C] Limpando vestígios HPE (rotas existentes + interfaces)...");
        try
        {
            // 2a. Interpreta e remove exatamente as rotas existentes
            var curRoutes = await session.SendCommandAsync("display current-configuration | include route-static", TimeSpan.FromSeconds(10), cancellationToken);
            var existingRoutes = ParseStaticRoutes(curRoutes);
            var undos = GenerateUndoStaticRoutes(existingRoutes);

            foreach (var undoCmd in undos)
            {
                await ProgressAsync($"    [-] Removendo rota: {undoCmd}");
                await session.SendCommandAsync(undoCmd, TimeSpan.FromSeconds(5), cancellationToken);
            }

            // 2b. Limpa IPs residuais em Vlan-interface1 se houver conflito
            var vlanCfg = await session.SendCommandAsync("display current-configuration interface Vlan-interface 1", TimeSpan.FromSeconds(5), cancellationToken);
            if (vlanCfg.Contains("ip address", StringComparison.OrdinalIgnoreCase) &&
                (vlanCfg.Contains(circuit.WanIp ?? "---") || vlanCfg.Contains(circuit.LanIp ?? "---")))
            {
                await ProgressAsync("    [-] Limpando IP conflitante em Vlan-interface 1...");
                await session.SendCommandAsync("interface Vlan-interface 1", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("undo ip address", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
            }

            // 2c. Reseta interfaces WAN e LAN
            foreach (var iface in new[] { wanInterface, lanInterface })
            {
                await session.SendCommandAsync($"interface {iface}", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("undo ip address", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("undo description", TimeSpan.FromSeconds(5), cancellationToken);
                await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
            }
            await ProgressAsync("[OK] Configuração antiga removida");
        }
        catch (Exception ex)
        {
            await ProgressAsync($"[AVISO] Limpeza prévia: {ex.Message}");
        }

        // 3. Detecta interfaces exatas disponíveis
        try
        {
            var briefOutput = await session.SendCommandAsync("display interface brief", TimeSpan.FromSeconds(15), cancellationToken);
            var (detectedWan, detectedLan) = DetectInterfaces(briefOutput, wanInterface, lanInterface);
            wanInterface = detectedWan;
            lanInterface = detectedLan;
        }
        catch { }

        var wanDesc = SanitizeDescription(circuit.DesignacaoIp ?? circuit.NumeroOts ?? "LINK");
        var lanDesc = SanitizeDescription(circuit.ClienteRazaoSocial);

        // 4. Configura Interface WAN
        await EnsureSystemViewAsync(session, _progress, cancellationToken);
        await session.SendCommandAsync($"interface {wanInterface}", TimeSpan.FromSeconds(5), cancellationToken);

        var linkRespWan = await session.SendExpectAsync("port link-mode route",
            new StopCondition[] { new StopCondition.Contains("[Y/N]:", "[Y/N]:"), new StopCondition.Contains("[Y/N]", "[Y/N]"), new StopCondition.Prompt() },
            TimeSpan.FromSeconds(10), cancellationToken);
        if (linkRespWan.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
        {
            await session.WriteLineAsync("Y", cancellationToken);
            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(10), cancellationToken);
        }

        await session.SendCommandAsync($"description WAN_EBT_{wanDesc}", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync($"ip address {circuit.WanIp} {circuit.WanSubnetMask}", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("undo shutdown", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        await ProgressAsync("[OK] WAN configurada");

        // 5. Configura Interface LAN
        await EnsureSystemViewAsync(session, _progress, cancellationToken);
        await session.SendCommandAsync($"interface {lanInterface}", TimeSpan.FromSeconds(5), cancellationToken);

        var linkRespLan = await session.SendExpectAsync("port link-mode route",
            new StopCondition[] { new StopCondition.Contains("[Y/N]:", "[Y/N]:"), new StopCondition.Contains("[Y/N]", "[Y/N]"), new StopCondition.Prompt() },
            TimeSpan.FromSeconds(10), cancellationToken);
        if (linkRespLan.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
        {
            await session.WriteLineAsync("Y", cancellationToken);
            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(10), cancellationToken);
        }

        await session.SendCommandAsync($"description LAN_CLIENTE_{lanDesc}", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync($"ip address {circuit.LanIp} {circuit.LanSubnetMask}", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("undo shutdown", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        await ProgressAsync("[OK] LAN configurada");

        // 6. Rota Default Canônica
        await EnsureSystemViewAsync(session, _progress, cancellationToken);
        await session.SendCommandAsync($"ip route-static 0.0.0.0 0.0.0.0 {circuit.WanGateway}", TimeSpan.FromSeconds(5), cancellationToken);
        await ProgressAsync("[OK] Rota default configurada");

        // 7. Usuário EBT (Comware 7)
        await EnsureSystemViewAsync(session, _progress, cancellationToken);
        await session.SendCommandAsync("undo password-control enable", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("local-user EBT class manage", TimeSpan.FromSeconds(5), cancellationToken);
        var passResp = await session.SendCommandAsync("password simple PRO1ANPRO1AN", TimeSpan.FromSeconds(5), cancellationToken);
        if (passResp.Contains("Wrong parameter", StringComparison.OrdinalIgnoreCase) || passResp.Contains("%", StringComparison.OrdinalIgnoreCase))
        {
            await session.SendCommandAsync("password simple PRO1AN", TimeSpan.FromSeconds(5), cancellationToken);
        }
        await session.SendCommandAsync("service-type telnet", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("authorization-attribute user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        await ProgressAsync("[OK] Usuário EBT configurado");

        // 8. Telnet Server e Linhas VTY
        await EnsureSystemViewAsync(session, _progress, cancellationToken);
        await session.SendCommandAsync("telnet server enable", TimeSpan.FromSeconds(5), cancellationToken);

        await session.SendCommandAsync("line con 0", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("authentication-mode none", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);

        var vtyResp = await session.SendCommandAsync("line vty 0 63", TimeSpan.FromSeconds(5), cancellationToken);
        if (IsError(vtyResp))
        {
            await EnsureSystemViewAsync(session, _progress, cancellationToken);
            await session.SendCommandAsync("user-interface vty 0 4", TimeSpan.FromSeconds(5), cancellationToken);
        }
        await session.SendCommandAsync("authentication-mode scheme", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("protocol inbound telnet", TimeSpan.FromSeconds(5), cancellationToken);
        await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        await ProgressAsync("[OK] Telnet habilitado");

        // 9. Persistência Canônica (save safely force)
        await EnsureUserViewAsync(session, _progress, cancellationToken);
        var saveResp = await session.SendExpectAsync("save safely force",
            new StopCondition[] {
                new StopCondition.Contains("[Y/N]:", "[Y/N]:"),
                new StopCondition.Contains("[Y/N]", "[Y/N]"),
                new StopCondition.Prompt()
            },
            TimeSpan.FromSeconds(25), cancellationToken);

        if (saveResp.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
        {
            await session.WriteLineAsync("Y", cancellationToken);
            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(15), cancellationToken);
        }
        await ProgressAsync("[OK] Configuração salva");

        // 10. Validação de Startup Configuration
        var dispStartup = await session.SendCommandAsync("display startup", TimeSpan.FromSeconds(10), cancellationToken);
        if (dispStartup.Contains("startup.cfg", StringComparison.OrdinalIgnoreCase))
        {
            await ProgressAsync("[OK] Startup configuration validada");
        }
        else
        {
            await session.SendCommandAsync("startup saved-configuration startup.cfg main", TimeSpan.FromSeconds(8), cancellationToken);
            await ProgressAsync("[OK] Startup configuration vinculada (startup.cfg)");
        }

        // 11. Auditoria Completa Pós-Provisionamento
        var validator = new HpeProvisioningValidator(_progress);
        var report = await validator.ValidateAsync(session, circuit, wanInterface, lanInterface, cancellationToken);

        if (report.OverallStatus == HpeValidationStatus.Fail)
        {
            var failItems = report.Items.Where(i => i.Status == HpeValidationStatus.Fail).Select(i => i.Name);
            await ProgressAsync($"[AVISO] Auditoria pós-provisionamento apontou divergências em: {string.Join(", ", failItems)} — prosseguindo para testes de conectividade.");
        }
        else
        {
            await ProgressAsync("[OK] Provisionamento HPE concluído com sucesso!");
        }

        return report;
    }

    private static bool IsError(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        return output.Contains("Wrong parameter", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Too many parameters", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Incomplete command", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Ambiguous command", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Error:", StringComparison.OrdinalIgnoreCase)
            || output.Contains("% Error", StringComparison.OrdinalIgnoreCase)
            || output.Contains("% Unrecognized", StringComparison.OrdinalIgnoreCase)
            || output.Contains("% Incomplete", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeDescription(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "CIRCUITO";
        return Regex.Replace(input, @"[^\w\-\.]", "_").Trim('_');
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }

    public static async Task<bool> EnforceLanPortConnectedAsync(
        DeviceSession session,
        string lanInterface = "GigabitEthernet0/1",
        Func<string, CancellationToken, Task>? requestOperatorAction = null,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var cleanLan = lanInterface.Replace(" ", "");
        var cleanWan = cleanLan.EndsWith("0/1") ? cleanLan.Replace("0/1", "0/0") : "GigabitEthernet0/0";

        for (var attempt = 1; attempt <= 15; attempt++)
        {
            var output = await session.SendCommandAsync("display ip interface brief", TimeSpan.FromSeconds(8), cancellationToken);

            var isLanUp = Regex.IsMatch(output, $@"(?i)(?:{cleanLan}|GE0/1)\s+UP\s+(?:UP|\S+)");
            var isWanUp = Regex.IsMatch(output, $@"(?i)(?:{cleanWan}|GE0/0)\s+UP\s+(?:UP|\S+)");

            if (isLanUp)
            {
                if (progress != null)
                    await progress($"[OK] Porta LAN ({lanInterface}) confirmada com link ativo (UP).");
                return true;
            }

            if (requestOperatorAction != null)
            {
                var msg = isWanUp
                    ? $"[ATENÇÃO] O cabo de rede está conectado na porta GE0 (WAN / recovery).\n\n" +
                      $"Por favor, MUDE O CABO DE REDE para a porta GE1 (LAN / Porta 1) para dar continuidade aos testes de conectividade e banda."
                    : $"[ATENÇÃO] Nenhuma porta de rede ativa detectada.\n\n" +
                      $"Por favor, CONECTE O CABO DE REDE na porta GE1 (LAN / Porta 1) do roteador HPE.";

                await requestOperatorAction(msg, cancellationToken);
            }

            await Task.Delay(2000, cancellationToken);
        }

        return false;
    }
}
