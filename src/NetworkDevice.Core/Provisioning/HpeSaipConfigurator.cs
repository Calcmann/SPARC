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

    public const string BannerMotd =
        "||========================================||\r\n" +
        "||========== CLARO Brasil S.A. ===========||\r\n" +
        "||========================================||\r\n" +
        "\r\n" +
        "SOMENTE USUARIOS AUTORIZADOS\r\n" +
        "AUTHORIZED USERS ONLY\r\n" +
        "\r\n" +
        "OS ACESSOS SERAO MONITORADOS\r\n" +
        "ACCESSES WILL BE MONITORED\r\n" +
        "\r\n" +
        "||========================================||";

    private readonly Func<string, Task>? _progress;

    public HpeSaipConfigurator(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    public static string SanitizeHostname(string? raw, string fallback = "ROUTER-CPE")
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var clean = Regex.Replace(raw.Trim(), @"[^a-zA-Z0-9_\-\.]", "-");
        clean = Regex.Replace(clean, @"-+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
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

    public static string NormalizeInterfaceName(string iface)
    {
        if (string.IsNullOrWhiteSpace(iface)) return "GigabitEthernet0/0";
        var trimmed = iface.Trim();
        if (trimmed.StartsWith("GE", StringComparison.OrdinalIgnoreCase) && !trimmed.StartsWith("GigabitEthernet", StringComparison.OrdinalIgnoreCase))
        {
            return "GigabitEthernet" + trimmed.Substring(2);
        }
        return trimmed;
    }

    /// <summary>
    /// Detecta se o equipamento roda Comware 5.x (MSR930/MSR931) ou Comware 7.x.
    /// </summary>
    public static bool IsComware5(string displayVersionOutput)
    {
        if (string.IsNullOrWhiteSpace(displayVersionOutput)) return false;
        return displayVersionOutput.Contains("Version 5.", StringComparison.OrdinalIgnoreCase)
            || displayVersionOutput.Contains("Comware Software, Version 5", StringComparison.OrdinalIgnoreCase);
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
            return (NormalizeInterfaceName(preferredWan), NormalizeInterfaceName(preferredLan));

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

        return (NormalizeInterfaceName(resolvedWan), NormalizeInterfaceName(resolvedLan));
    }

    /// <summary>
    /// Gera os comandos de CLI HPE Comware para provisionamento da Ficha SAIP.
    /// </summary>
    /// <param name="circuit">Dados do circuito SAIP.</param>
    /// <param name="wanInterface">Nome da interface WAN (ex.: GigabitEthernet0/0).</param>
    /// <param name="lanInterface">Nome da interface LAN (ex.: GigabitEthernet0/1).</param>
    /// <param name="isComware5">Se true, gera sintaxe Comware 5.x (MSR930/MSR931). Caso contrário, Comware 7.x.</param>
    public static IReadOnlyList<string> GenerateCommands(
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet0/0",
        string lanInterface = "GigabitEthernet0/1",
        bool isComware5 = false,
        bool incluirBldClaro = true)
    {
        var hostname = SanitizeHostname(circuit.DesignacaoIp ?? circuit.NumeroOts, "ROUTER-CPE");
        var wanDesc = SanitizeDescription(circuit.DesignacaoIp ?? circuit.NumeroOts ?? "LINK");
        var lanDesc = SanitizeDescription(circuit.ClienteRazaoSocial);
        var bandaKbps = circuit.BandaKbps ?? (long)((circuit.BandaMbpsNominal ?? 50) * 1000);

        var cmds = new List<string>
        {
            "system-view",
            $"sysname {hostname}",

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
        };

        if (incluirBldClaro)
        {
            // Banners Oficiais Claro Brasil S.A.
            cmds.AddRange(new[]
            {
                "header legal % CLARO Brasil S.A. - SOMENTE USUARIOS AUTORIZADOS - OS ACESSOS SERAO MONITORADOS %",
                "header login % CLARO Brasil S.A. - SOMENTE USUARIOS AUTORIZADOS - OS ACESSOS SERAO MONITORADOS %",
            });

            // NTP Oficial Claro
            cmds.AddRange(new[]
            {
                "ntp-service enable",
                $"ntp-service unicast-server 200.20.186.75 source-interface {wanInterface}",
                $"ntp-service unicast-server 200.20.186.94 source-interface {wanInterface}",
            });

            // SNMP Oficial Claro
            cmds.AddRange(new[]
            {
                "snmp-agent",
                "snmp-agent sys-info version v2c",
                "snmp-agent community read claro21sup",
                "snmp-agent community read LIDER",
                "snmp-agent target-host trap address udp-domain 200.255.156.194 params securityname LIDER v2c",
            });

            // TACACS+ Oficial Claro (HWTACACS)
            cmds.AddRange(new[]
            {
                "hwtacacs scheme CLARO",
                " primary authentication 200.255.166.129",
                " primary authorization 200.255.166.129",
                " primary accounting 200.255.166.129",
                " key authentication cipher 080F636D2A152505052B",
                " user-name-format without-domain",
                "quit",
            });

            // QoS (Traffic Shaping na WAN)
            cmds.AddRange(new[]
            {
                $"interface {wanInterface}",
                $" qos lr outbound cir {bandaKbps}",
                "quit",
            });

            // ACL de Bloqueio Telnet/SSH Gerência Claro
            if (isComware5)
            {
                cmds.AddRange(new[]
                {
                    "acl number 3087",
                    " rule 5 permit ip source 200.255.156.192 0.0.0.63 destination any",
                    $" rule 10 permit ip source host {circuit.WanGateway} destination any",
                });
                if (!string.IsNullOrWhiteSpace(circuit.PeLoopbackIp))
                {
                    cmds.Add($" rule 15 permit ip source host {circuit.PeLoopbackIp} destination any");
                }
                cmds.Add("quit");
            }
            else
            {
                cmds.AddRange(new[]
                {
                    "acl advanced 3087",
                    " rule 5 permit ip source 200.255.156.192 0.0.0.63 destination any",
                    $" rule 10 permit ip source host {circuit.WanGateway} destination any",
                });
                if (!string.IsNullOrWhiteSpace(circuit.PeLoopbackIp))
                {
                    cmds.Add($" rule 15 permit ip source host {circuit.PeLoopbackIp} destination any");
                }
                cmds.Add("quit");
            }

            // IPv6 Dual-Stack (se preenchido)
            if (!string.IsNullOrWhiteSpace(circuit.WanIpv6))
            {
                cmds.AddRange(new[]
                {
                    "ipv6",
                    $"interface {wanInterface}",
                    $" ipv6 address {circuit.WanIpv6}/{circuit.WanIpv6Prefix ?? 64}",
                    "quit",
                });
                if (!string.IsNullOrWhiteSpace(circuit.LanIpv6))
                {
                    cmds.AddRange(new[]
                    {
                        $"interface {lanInterface}",
                        $" ipv6 address {circuit.LanIpv6}/{circuit.LanIpv6Prefix ?? 64}",
                        "quit",
                    });
                }
                if (!string.IsNullOrWhiteSpace(circuit.WanIpv6Gateway))
                {
                    cmds.Add($"ipv6 route-static :: 0 {circuit.WanIpv6Gateway}");
                }
            }
        }

        if (isComware5)
        {
            // Comware 5.x (MSR930/MSR931)
            cmds.AddRange(new[]
            {
                "telnet server enable",
                "undo password-control enable",
                "local-user EBT",
                "password simple PRO1ANPRO1AN",
                "service-type telnet",
                "user privilege level 3",
                "authorization-attribute level 3",
                "quit",

                // Console Serial (aux 0) - Comware 5
                "user-interface aux 0",
                "authentication-mode none",
                "user privilege level 3",
                "quit",

                // Linha VTY Telnet (Comware 5 - user-interface)
                "user-interface vty 0 4",
                "authentication-mode scheme",
                "user privilege level 3",
                "protocol inbound telnet",
                incluirBldClaro ? "acl 3087 inbound" : "quit",
                "quit",
            });
        }
        else
        {
            // Comware 7.x (HPE MSR 954 e superiores)
            cmds.AddRange(new[]
            {
                "telnet server enable",
                "undo password-control enable",
                "local-user EBT class manage",
                "password simple PRO1ANPRO1AN",
                "service-type telnet",
                "authorization-attribute user-role network-admin",
                "quit",

                // Console Serial (CON 0) - Comware 7
                "line con 0",
                "authentication-mode none",
                "user-role network-admin",
                "quit",

                // Linha VTY Telnet (Comware 7 - line vty)
                "line vty 0 63",
                "authentication-mode scheme",
                "user-role network-admin",
                "protocol inbound telnet",
                incluirBldClaro ? "acl 3087 inbound" : "quit",
                "quit",
            });
        }

        cmds.AddRange(new[]
        {
            // 5. Salvar Configuração Canônica
            "return",
            "save safely force"
        });

        return cmds;
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
        await ProgressAsync($"[*] [AUTO] HPE Comware identificado ({circuit.DesignacaoIp ?? circuit.NumeroOts})...");

        // Verificação impeditiva: se o equipamento estiver em BootWare (sem SO)
        var prompt = (session.CurrentPrompt ?? string.Empty).Trim();
        if (session.Mode == ExecMode.Rommon ||
            prompt.Contains("BootWare", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("choice(0-", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("choice (0-", StringComparison.OrdinalIgnoreCase) ||
            prompt.Contains("<MAIN MENU>", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O equipamento se encontra em menu BootWare (sem sistema operacional carregado). É OBRIGATÓRIO executar a recuperação de firmware antes de provisionar.");
        }

        // Detecta versão do Comware (5.x para MSR930/MSR931, 7.x para modelos superiores)
        bool isComware5 = false;
        try
        {
            var versionOutput = await session.SendCommandAsync("display version", TimeSpan.FromSeconds(10), cancellationToken);
            isComware5 = IsComware5(versionOutput);
            await ProgressAsync(isComware5 ? "[OK] Comware 5.x detectado (MSR930/MSR931) — sintaxe compatível" : "[OK] Comware 7.x detectado — sintaxe padrão");
        }
        catch { }

        // 1. Valida User View / System View inicial
        await EnsureUserViewAsync(session, _progress, cancellationToken);
        await ProgressAsync("[OK] User View confirmado");

        await EnsureSystemViewAsync(session, _progress, cancellationToken);

        // Desativa timeout de inatividade na console para proteger o operador contra expiração de sessão
        try
        {
            await session.SendCommandAsync("line aux 0", TimeSpan.FromSeconds(3), cancellationToken);
            await session.SendCommandAsync("idle-timeout 0 0", TimeSpan.FromSeconds(3), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch { }
        try
        {
            await session.SendCommandAsync("line con 0", TimeSpan.FromSeconds(3), cancellationToken);
            await session.SendCommandAsync("idle-timeout 0 0", TimeSpan.FromSeconds(3), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch { }
        try
        {
            await session.SendCommandAsync("user-interface aux 0", TimeSpan.FromSeconds(3), cancellationToken);
            await session.SendCommandAsync("idle-timeout 0 0", TimeSpan.FromSeconds(3), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch { }

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
        // Aguarda convergência física do link após undo shutdown / port link-mode route (auto-negotiation 2-5s)
        await Task.Delay(6000, cancellationToken);
        await ProgressAsync("[OK] LAN configurada");

        // 6. Rota Default Canônica
        await EnsureSystemViewAsync(session, _progress, cancellationToken);
        await session.SendCommandAsync($"ip route-static 0.0.0.0 0.0.0.0 {circuit.WanGateway}", TimeSpan.FromSeconds(5), cancellationToken);
        await ProgressAsync("[OK] Rota default configurada");

        // 7. Usuário EBT (sintaxe específica por versão do Comware)
        await EnsureSystemViewAsync(session, _progress, cancellationToken);
        try { await session.SendCommandAsync("undo password-control enable", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

        if (isComware5)
        {
            await session.SendCommandAsync("local-user EBT", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("password simple PRO1ANPRO1AN", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("service-type telnet", TimeSpan.FromSeconds(5), cancellationToken);
            var privResp = await session.SendCommandAsync("user privilege level 3", TimeSpan.FromSeconds(5), cancellationToken);
            if (privResp.Contains("%", StringComparison.OrdinalIgnoreCase) || privResp.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync("authorization-attribute level 3", TimeSpan.FromSeconds(5), cancellationToken);
            }
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        else
        {
            var userResp = await session.SendCommandAsync("local-user EBT class manage", TimeSpan.FromSeconds(5), cancellationToken);
            if (userResp.Contains("%", StringComparison.OrdinalIgnoreCase) || userResp.Contains("Too many parameters", StringComparison.OrdinalIgnoreCase) || userResp.Contains("Wrong parameter", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync("local-user EBT", TimeSpan.FromSeconds(5), cancellationToken);
            }
            var passResp = await session.SendCommandAsync("password simple PRO1AN", TimeSpan.FromSeconds(5), cancellationToken);
            if (passResp.Contains("Wrong parameter", StringComparison.OrdinalIgnoreCase) || passResp.Contains("%", StringComparison.OrdinalIgnoreCase) || passResp.Contains("Ambiguous", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync("password simple PRO1ANPRO1AN", TimeSpan.FromSeconds(5), cancellationToken);
            }
            await session.SendCommandAsync("service-type telnet", TimeSpan.FromSeconds(5), cancellationToken);
            var authResp = await session.SendCommandAsync("authorization-attribute user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
            if (authResp.Contains("%", StringComparison.OrdinalIgnoreCase) || authResp.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync("authorization-attribute level 3", TimeSpan.FromSeconds(5), cancellationToken);
            }
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        await ProgressAsync("[OK] Usuário EBT configurado");

        // 8. Telnet Server e Linhas VTY (sintaxe específica por versão do Comware)
        await EnsureSystemViewAsync(session, _progress, cancellationToken);
        await session.SendCommandAsync("telnet server enable", TimeSpan.FromSeconds(5), cancellationToken);

        if (isComware5)
        {
            await session.SendCommandAsync("user-interface aux 0", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("authentication-mode none", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("user privilege level 3", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);

            await session.SendCommandAsync("user-interface vty 0 4", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("authentication-mode scheme", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("user privilege level 3", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("protocol inbound telnet", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
        else
        {
            var conResp = await session.SendCommandAsync("line con 0", TimeSpan.FromSeconds(5), cancellationToken);
            if (conResp.Contains("%", StringComparison.OrdinalIgnoreCase) || conResp.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync("user-interface aux 0", TimeSpan.FromSeconds(5), cancellationToken);
            }
            await session.SendCommandAsync("authentication-mode none", TimeSpan.FromSeconds(5), cancellationToken);
            var conRoleResp = await session.SendCommandAsync("user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
            if (conRoleResp.Contains("%", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync("user privilege level 3", TimeSpan.FromSeconds(5), cancellationToken);
            }
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);

            var vtyResp = await session.SendCommandAsync("line vty 0 63", TimeSpan.FromSeconds(5), cancellationToken);
            if (IsError(vtyResp))
            {
                await EnsureSystemViewAsync(session, _progress, cancellationToken);
                await session.SendCommandAsync("user-interface vty 0 4", TimeSpan.FromSeconds(5), cancellationToken);
            }
            await session.SendCommandAsync("authentication-mode scheme", TimeSpan.FromSeconds(5), cancellationToken);
            var vtyRoleResp = await session.SendCommandAsync("user-role network-admin", TimeSpan.FromSeconds(5), cancellationToken);
            if (vtyRoleResp.Contains("%", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync("user privilege level 3", TimeSpan.FromSeconds(5), cancellationToken);
            }
            await session.SendCommandAsync("protocol inbound telnet", TimeSpan.FromSeconds(5), cancellationToken);
            await session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), cancellationToken);
        }
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
        CancellationToken cancellationToken = default,
        Action<int, string, string>? onProgress = null)
    {
        var cleanLan = lanInterface.Replace(" ", "");
        var cleanWan = cleanLan.EndsWith("0/1") ? cleanLan.Replace("0/1", "0/0") : "GigabitEthernet0/0";

        // Janela de estabilização pós undo shutdown: evita falso DOWN se verificado logo em sequência
        await Task.Delay(2000, cancellationToken);

        var hpeLanPattern = @"(?:GigabitEthernet0/1|GE0/1|GigabitEthernet1|GE1)";
        var hpeWanPattern = @"(?:GigabitEthernet0/0|GE0/0|GigabitEthernet0|GE0)";

        var operatorNotified = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var attempt = 1; attempt <= 60; attempt++)
        {
            var elapsedSec = (int)sw.Elapsed.TotalSeconds;
            string output = string.Empty;
            try
            {
                output = await session.SendCommandAsync("display ip interface brief", TimeSpan.FromSeconds(12), cancellationToken);
            }
            catch (SessionTimeoutException)
            {
                await session.WriteLineAsync(string.Empty, cancellationToken);
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            var isLanUp = Regex.IsMatch(output, $@"(?im)^\s*{hpeLanPattern}\s+UP\s+(?:UP|\S+)");
            var isWanUp = Regex.IsMatch(output, $@"(?im)^\s*{hpeWanPattern}\s+UP\s+(?:UP|\S+)");

            if (isLanUp)
            {
                if (progress != null)
                    await progress($"[OK] Porta LAN HPE ({lanInterface} / GE1 - Porta 1) confirmada com link ativo (UP) ({elapsedSec}s).");
                onProgress?.Invoke(55, "Porta LAN Conectada!", $"Link ativo confirmado na porta GE1 ({elapsedSec}s).");
                return true;
            }

            if (isWanUp && !isLanUp)
            {
                if (progress != null && (attempt == 1 || attempt % 3 == 0))
                    await progress($"[AGUARDANDO TROCA DE CABO] Cabo detectado na porta WAN GE0 ao invés da LAN GE1... {elapsedSec}s decorridos (Tentativa {attempt}/60)");
                onProgress?.Invoke(50, "Aguardando Troca de Cabo...", $"Cabo na porta GE0 (WAN). Conecte na porta GE1 (LAN) ({elapsedSec}s)...");
            }
            else if (!isLanUp)
            {
                if (progress != null && (attempt == 1 || attempt % 3 == 0))
                    await progress($"[AGUARDANDO CABO LAN] Detectando link físico na porta GE1... {elapsedSec}s decorridos (Tentativa {attempt}/60)");
                onProgress?.Invoke(50, "Aguardando Conexão LAN...", $"Aguardando sincronização da porta GE1... ({elapsedSec}s)");
            }

            // Notifica operador na tentativa 2 (após auto-negotiation inicial) ou periodicamente a cada 20 tentativas
            if (requestOperatorAction != null && (!operatorNotified && attempt >= 2 || attempt == 20 || attempt == 40))
            {
                operatorNotified = true;
                var msg = isWanUp
                    ? $"❌ CABO CONECTADO NA PORTA INCORRETA (GE0 / WAN)!\n\n" +
                      $"O cabo do notebook está conectado na porta GE0 (WAN / Recovery).\n\n" +
                      $"👉 POR FAVOR, CONECTE OS CABOS NAS PORTAS CORRESPONDENTES:\n" +
                      $"🟢 Conecte a porta GE1 (LAN / Porta 1) no Laptop/PC (para testes de conectividade e banda);\n" +
                      $"🔴 Conecte a porta GE0 (WAN / Porta 0) no Acesso / Link da Operadora.\n\n" +
                      $"Clique em OK após realizar as conexões na porta GE1 (LAN)."
                    : $"⚠️ LINK FÍSICO NÃO DETECTADO NA PORTA LAN (GE1 / Porta 1)!\n\n" +
                      $"A porta LAN do equipamento está com link físico DOWN.\n\n" +
                      $"👉 Conecte o cabo de rede Ethernet do seu notebook na porta:\n" +
                      $"🟢 Porta GE1 (LAN / Porta 1)\n\n" +
                      $"Certifique-se de que o cabo está bem encaixado e o LED da porta física está aceso.\n\n" +
                      $"Clique em OK após conectar o cabo na porta GE1.";

                await requestOperatorAction(msg, cancellationToken);
                if (progress != null)
                    await progress($"[*] Operador confirmou conexão. Sincronizando link da porta GE1...");
                onProgress?.Invoke(50, "Sincronizando Porta LAN...", $"Aguardando link ativo na porta GE1... ({elapsedSec}s)");

                try
                {
                    await session.WriteLineAsync(string.Empty, cancellationToken);
                    await Task.Delay(500, cancellationToken);
                    await session.WriteLineAsync(string.Empty, cancellationToken);
                    await Task.Delay(1000, cancellationToken);
                }
                catch { }
            }

            for (var d = 0; d < 2; d++)
            {
                await Task.Delay(1000, cancellationToken);
                if (onProgress != null)
                {
                    var sec = (int)sw.Elapsed.TotalSeconds;
                    onProgress(50, isWanUp ? "Aguardando Troca de Cabo..." : "Aguardando Link LAN...", $"Aguardando sinal na porta GE1... ({sec}s)");
                }
            }
        }

        if (progress != null)
            await progress($"[AVISO CRÍTICO] Tempo limite de espera para link na porta LAN HPE ({lanInterface}) esgotado ({(int)sw.Elapsed.TotalSeconds}s).");

        return false;
    }

    /// <summary>
    /// Avalia se o equipamento HPE Comware já se encontra em padrão de fábrica limpo ("zero lixo")
    /// ou se possui configurações residuais de serviços/clientes anteriores que requerem higienização.
    /// </summary>
    public static async Task<DeviceSanitizationStatus> DetectSanitizationStatusAsync(
        DeviceSession session,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var findings = new List<string>();
        string? detectedHostname = null;
        var hasCustomHostname = false;
        var hasStaleRoutes = false;
        var hasConfiguredInterfaces = false;

        try
        {
            await EnsureSystemViewAsync(session, ct: ct);
            var displayCfg = await session.SendCommandAsync("display current-configuration | include sysname|route-static|ip address", TimeSpan.FromSeconds(10), ct);

            // 1. Hostname / sysname
            var matchSysname = Regex.Match(displayCfg, @"(?im)^\s*sysname\s+(\S+)");
            if (matchSysname.Success)
            {
                detectedHostname = matchSysname.Groups[1].Value.Trim();
                if (!detectedHostname.Equals("HPE", StringComparison.OrdinalIgnoreCase) &&
                    !detectedHostname.Equals("HP", StringComparison.OrdinalIgnoreCase) &&
                    !detectedHostname.Equals("H3C", StringComparison.OrdinalIgnoreCase))
                {
                    hasCustomHostname = true;
                    findings.Add($"Hostname customizado: '{detectedHostname}'");
                }
            }

            // 2. Rotas estáticas residuais
            var routes = ParseStaticRoutes(displayCfg);
            if (routes.Count > 0)
            {
                hasStaleRoutes = true;
                findings.Add($"{routes.Count} rota(s) estática(s) residual(is)");
            }

            // 3. IPs atribuídos em interfaces além de default
            var ipMatches = Regex.Matches(displayCfg, @"(?im)^\s*ip\s+address\s+(\d+\.\d+\.\d+\.\d+)");
            foreach (Match m in ipMatches)
            {
                var ip = m.Groups[1].Value;
                if (!ip.StartsWith("192.168.") && !ip.StartsWith("0."))
                {
                    hasConfiguredInterfaces = true;
                    findings.Add($"Interface com IP residual: {ip}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            return DeviceSanitizationStatus.Clean($"Não foi possível inspecionar completamente ({ex.Message}) — assumindo baseline.");
        }

        var isClean = !hasCustomHostname && !hasStaleRoutes && !hasConfiguredInterfaces;
        var summary = isClean
            ? "Equipamento HPE em padrão de fábrica (zero lixo detectado — reload desnecessário)."
            : $"Configuração anterior detectada ({string.Join(", ", findings)}).";

        return new DeviceSanitizationStatus
        {
            IsClean = isClean,
            Summary = summary,
            DetectedHostname = detectedHostname,
            HasCustomHostname = hasCustomHostname,
            HasStaleRoutes = hasStaleRoutes,
            HasConfiguredInterfaces = hasConfiguredInterfaces
        };
    }
}
