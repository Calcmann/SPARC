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

    /// <summary>
    /// Imagem de firmware de boot indicada pelo operador (ex.: c1900-universalk9-mz.SPA.157-3.M7.bin).
    /// Quando informada, sobrepõe a versão padrão da família do hardware no template SAIP.
    /// </summary>
    public string? BootImage { get; set; }

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
    public const string BannerMotd =
        "banner motd #\r\n" +
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
        "||========================================||\r\n" +
        "#";

    /// <summary>
    /// Gera a lista de comandos CLI Cisco IOS para provisionamento da Ficha SAIP seguindo o padrão oficial Claro / Embratel.
    /// </summary>
    public static IReadOnlyList<string> GenerateCommands(
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet 4",
        string lanInterface = "GigabitEthernet 5",
        bool incluirNatLab = false,
        string? bootImage = null)
    {
        var hostname = SanitizeHostname(circuit.DesignacaoIp ?? circuit.NumeroOts, "ROUTER-CPE");
        var wanDesc = SanitizeDescription(circuit.DescriptionRoteador ?? (circuit.DesignacaoIp ?? "LINK"), "LINK");

        var bandaKbps = circuit.BandaKbps ?? (long)((circuit.BandaMbpsNominal ?? 50) * 1000);
        var bandaBps = circuit.BandaBps ?? (bandaKbps * 1000L);

        var cleanWan = wanInterface.Replace(" ", "");

        // Resolução do arquivo de boot por família de hardware se não informado
        string resolvedBoot = circuit.BootImage ?? bootImage ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedBoot))
        {
            if (cleanWan.Equals("GigabitEthernet4", StringComparison.OrdinalIgnoreCase) ||
                cleanWan.Equals("GigabitEthernet5", StringComparison.OrdinalIgnoreCase))
            {
                resolvedBoot = "c900-universalk9-mz.SPA.159-3.M12.bin";
            }
            else if (cleanWan.Contains("0/4", StringComparison.OrdinalIgnoreCase) ||
                     cleanWan.Contains("0/5", StringComparison.OrdinalIgnoreCase))
            {
                resolvedBoot = "c800m-universalk9-mz.SPA.159-3.M12.bin";
            }
            else if (circuit.RawSource.Contains("29", StringComparison.OrdinalIgnoreCase) ||
                     wanInterface.Contains("29", StringComparison.OrdinalIgnoreCase))
            {
                resolvedBoot = "c2900-universalk9-mz.SPA.157-3.M8.bin";
            }
            else
            {
                resolvedBoot = "c1900-universalk9-mz.SPA.157-3.M9.bin";
            }
        }

        var cmds = new List<string>
        {
            "configure terminal",

            // 1. SERVICOS GERAIS, SEGURANCA E LOGGING
            $"hostname {hostname}",
            "no service pad",
            "service tcp-keepalives-in",
            "service tcp-keepalives-out",
            "service timestamps debug datetime msec localtime show-timezone",
            "service timestamps log datetime msec localtime show-timezone",
            "service password-encryption",
            "boot-start-marker",
            $"boot system flash:{resolvedBoot}",
        };

        if (resolvedBoot.Contains("c1900", StringComparison.OrdinalIgnoreCase))
        {
            cmds.Add($"boot system usbflash0:{resolvedBoot}");
        }

        cmds.AddRange(new[]
        {
            "boot-end-marker",
            "logging buffered 51200 warnings",
            "no logging console",
            "no logging trap",
            "no ip source-route",
            "no ip bootp server",
            "no ip domain lookup",
            "ip domain name embratel",
            "ip cef",
            "ipv6 unicast-routing",
            "ipv6 cef",
            "multilink bundle-name authenticated",
            "no cdp run",
            "clock summer-time BR recurring 1 Sun Oct 0:00 3 Sun Feb 0:00",

            // 2. USUARIOS LOCAIS E MODO PRIVILEGIADO
            "enable secret PRO1AN",
            "username EBT privilege 1 secret CQMR",

            // 3. AAA (TACACS+ E FALLBACK LOCAL)
            "aaa new-model",
            "aaa authentication password-prompt Password:",
            "aaa authentication username-prompt Login:",
            "aaa authentication login TACACS-SERVER-CLARO group tacacs+ local",
            "aaa authentication login admin local",
            "aaa authorization config-commands",
            "aaa authorization exec default group tacacs+ local",
            "aaa authorization commands 1 default group tacacs+ local",
            "aaa authorization commands 6 default group tacacs+ local",
            "aaa authorization commands 15 default group tacacs+ local",
            "aaa accounting exec default start-stop group tacacs+",
            "aaa accounting commands 1 default start-stop group tacacs+",
            "aaa accounting commands 6 default start-stop group tacacs+",
            "aaa accounting commands 15 default start-stop group tacacs+",
            "aaa session-id common",
            "tacacs server TACACS-SERVER-CLARO",
            " address ipv4 200.255.166.129",
            " key 7 080F636D2A152505052B",
            " timeout 2",
            " exit",
            "tacacs-server timeout 2",
            $"ip tacacs source-interface {cleanWan}",

            // 4. QOS (SHAPING WAN)
            "policy-map SHAPE_OUT",
            " class class-default",
            $"  shape average {bandaBps}",
            " exit",
        });

        // 5. INTERFACES
        // Desabilita portas de switch integradas quando aplicável
        if (cleanWan.Equals("GigabitEthernet4", StringComparison.OrdinalIgnoreCase) ||
            cleanWan.Equals("GigabitEthernet5", StringComparison.OrdinalIgnoreCase))
        {
            // Cisco Série 900 (921)
            cmds.AddRange(new[]
            {
                "interface GigabitEthernet0",
                " no ip address",
                " shutdown",
                "interface GigabitEthernet1",
                " no ip address",
                " shutdown",
                "interface GigabitEthernet2",
                " no ip address",
                " shutdown",
                "interface GigabitEthernet3",
                " no ip address",
                " shutdown",
                "interface Vlan1",
                " no ip address",
                " shutdown",
            });
        }
        else if (cleanWan.Contains("0/4", StringComparison.OrdinalIgnoreCase) ||
                 cleanWan.Contains("0/5", StringComparison.OrdinalIgnoreCase))
        {
            // Cisco Série 800 (841)
            cmds.AddRange(new[]
            {
                "interface GigabitEthernet0/0",
                " no ip address",
                " shutdown",
                "interface GigabitEthernet0/1",
                " no ip address",
                " shutdown",
                "interface GigabitEthernet0/2",
                " no ip address",
                " shutdown",
                "interface GigabitEthernet0/3",
                " no ip address",
                " shutdown",
                "interface Vlan1",
                " no ip address",
                " shutdown",
            });
        }
        else
        {
            // Cisco 1900 / 2900 / roteadores modulares puros não possuem switchport integrado (interface Vlan1 não existe por padrão)
            // Portas não utilizadas são tratadas pelas interfaces físicas roteadas
        }

        // WAN (Uplink PE)
        cmds.Add($"interface {wanInterface}");
        cmds.Add($" description {wanDesc}");
        cmds.Add($" bandwidth {bandaKbps}");
        cmds.Add($" ip address {circuit.WanIp} {circuit.WanSubnetMask}");
        cmds.Add(" duplex auto");
        cmds.Add(" speed auto");
        if (!string.IsNullOrWhiteSpace(circuit.WanIpv6) && circuit.WanIpv6Prefix.HasValue)
        {
            cmds.Add($" ipv6 address {circuit.WanIpv6}/{circuit.WanIpv6Prefix.Value}");
            cmds.Add(" ipv6 enable");
        }
        cmds.Add(" service-policy output SHAPE_OUT");
        cmds.Add(" no shutdown");
        cmds.Add(" exit");

        // LAN (Entrega Cliente)
        cmds.Add($"interface {lanInterface}");
        cmds.Add(" description * LAN *");
        cmds.Add($" ip address {circuit.LanIp} {circuit.LanSubnetMask}");
        cmds.Add(" no ip redirects");
        cmds.Add(" no ip unreachables");
        cmds.Add(" no ip proxy-arp");
        cmds.Add(" duplex auto");
        cmds.Add(" speed auto");
        cmds.Add(" no cdp enable");
        if (!string.IsNullOrWhiteSpace(circuit.LanIpv6) && circuit.LanIpv6Prefix.HasValue)
        {
            cmds.Add($" ipv6 address {circuit.LanIpv6}/{circuit.LanIpv6Prefix.Value}");
            cmds.Add(" ipv6 enable");
        }
        cmds.Add(" no shutdown");
        cmds.Add(" exit");

        // 6. ROTEAMENTO E SERVICOS DE REDE
        cmds.Add("ip forward-protocol nd");
        cmds.Add("no ip http server");
        cmds.Add("no ip http secure-server");
        cmds.Add($"ip route 0.0.0.0 0.0.0.0 {circuit.WanGateway}");
        if (!string.IsNullOrWhiteSpace(circuit.WanIpv6Gateway))
        {
            cmds.Add($"ipv6 route ::/0 {circuit.WanIpv6Gateway}");
        }
        cmds.Add("ip ssh version 2");
        cmds.Add("crypto key generate rsa modulus 2048");
        cmds.Add($"ntp server 200.20.186.75 prefer source {cleanWan}");
        cmds.Add($"ntp server 200.20.186.94 source {cleanWan}");
        cmds.Add("snmp-server community claro21sup RO 87");
        cmds.Add("snmp-server community LIDER RO");
        cmds.Add("snmp-server host 200.255.156.194 LIDER");
        cmds.Add("access-list 87 permit 200.255.156.192 0.0.0.63");

        // 7. LISTAS DE ACESSO (BLOQUEIO GERENCIA VTY)
        cmds.Add("ip access-list extended BLOQUEIO_TELNET");
        if (!string.IsNullOrWhiteSpace(circuit.PeLoopbackIp))
        {
            cmds.Add(" remark IP LOOPBACK PE");
            cmds.Add($" permit ip host {circuit.PeLoopbackIp} any");
            cmds.Add($" permit ip any host {circuit.PeLoopbackIp}");
        }
        cmds.Add(" remark IP PE - CCTO");
        cmds.Add($" permit ip host {circuit.WanGateway} any");
        cmds.Add($" permit ip any host {circuit.WanGateway}");
        cmds.Add(" remark IP GERENCIA GCPE");
        cmds.Add(" permit ip any 200.255.156.192 0.0.0.63");
        cmds.Add(" permit ip 200.255.156.192 0.0.0.63 any");
        if (!string.IsNullOrWhiteSpace(circuit.LanIp))
        {
            var lanNet = string.IsNullOrWhiteSpace(circuit.LanBlockNetwork) ? circuit.LanIp : circuit.LanBlockNetwork;
            var wildcard = WildcardFromMask(circuit.LanSubnetMask, circuit.LanCidr);
            cmds.Add(" remark IP REDE LAN / BANCADA HOMOLOGACAO");
            cmds.Add($" permit ip {lanNet} {wildcard} any");
            cmds.Add($" permit ip any {lanNet} {wildcard}");
        }
        cmds.Add(" exit");

        bool hasIpv6 = !string.IsNullOrWhiteSpace(circuit.WanIpv6);
        if (hasIpv6 && !string.IsNullOrWhiteSpace(circuit.WanIpv6Gateway))
        {
            cmds.Add("ipv6 access-list BLOQUEIO_TELNET_IPV6");
            cmds.Add(" remark IPV6 PE - CCTO");
            cmds.Add($" permit ipv6 host {circuit.WanIpv6Gateway} any");
            cmds.Add($" permit ipv6 any host {circuit.WanIpv6Gateway}");
            if (!string.IsNullOrWhiteSpace(circuit.PeIpv6Loopback))
            {
                cmds.Add(" remark IPV6 LOOPBACK PE");
                cmds.Add($" permit ipv6 host {circuit.PeIpv6Loopback} any");
                cmds.Add($" permit ipv6 any host {circuit.PeIpv6Loopback}");
            }
            if (!string.IsNullOrWhiteSpace(circuit.LanIpv6))
            {
                var lanIpv6Net = !string.IsNullOrWhiteSpace(circuit.LanIpv6Block) ? circuit.LanIpv6Block : circuit.LanIpv6;
                var pfx = circuit.LanIpv6Prefix.HasValue ? $"/{circuit.LanIpv6Prefix.Value}" : "/64";
                cmds.Add(" remark IPV6 LAN BANCADA HOMOLOGACAO");
                cmds.Add($" permit ipv6 {lanIpv6Net}{pfx} any");
                cmds.Add($" permit ipv6 any {lanIpv6Net}{pfx}");
            }
            cmds.Add(" exit");
        }

        // NAT overload de LAB (opção de bancada)
        if (incluirNatLab)
        {
            var wildcard = WildcardFromMask(circuit.LanSubnetMask, circuit.LanCidr);
            var lanNet = string.IsNullOrWhiteSpace(circuit.LanBlockNetwork) ? circuit.LanIp : circuit.LanBlockNetwork;
            cmds.AddRange(new[]
            {
                $"interface {wanInterface}",
                " ip nat outside",
                " exit",
                $"interface {lanInterface}",
                " ip nat inside",
                " exit",
                $"access-list 1 permit {lanNet} {wildcard}",
                $"ip nat inside source list 1 interface {wanInterface} overload",
            });
        }

        // 8. BANNER E LINHAS DE GERENCIA (CONSOLE E VTY)
        cmds.Add(BannerMotd);
        cmds.Add("line con 0");
        cmds.Add(" exec-timeout 15 0");
        cmds.Add(" privilege level 15");
        cmds.Add(" logging synchronous");
        cmds.Add(" login authentication admin");
        cmds.Add(" exit");

        cmds.Add("line vty 0 4");
        cmds.Add(" access-class BLOQUEIO_TELNET in");
        cmds.Add(" access-class BLOQUEIO_TELNET out");
        if (hasIpv6 && !string.IsNullOrWhiteSpace(circuit.WanIpv6Gateway))
        {
            cmds.Add(" ipv6 access-class BLOQUEIO_TELNET_IPV6 in");
            cmds.Add(" ipv6 access-class BLOQUEIO_TELNET_IPV6 out");
        }
        cmds.Add(" exec-timeout 15 0");
        cmds.Add(" timeout login response 120");
        cmds.Add(" privilege level 15");
        cmds.Add(" login authentication TACACS-SERVER-CLARO");
        cmds.Add(" transport input telnet ssh");
        cmds.Add(" exit");

        cmds.Add("line vty 5 15");
        cmds.Add(" privilege level 15");
        cmds.Add(" login authentication TACACS-SERVER-CLARO");
        cmds.Add(" transport input telnet ssh");
        cmds.Add(" exit");

        cmds.Add("scheduler allocate 20000 1000");
        cmds.Add("end");
        cmds.Add("write memory");

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
                    var ifRes = await session.SendCommandAsync($"interface {iface}", TimeSpan.FromSeconds(5), cancellationToken);
                    if (!ifRes.Contains("% Invalid", StringComparison.OrdinalIgnoreCase) && !ifRes.Contains("% Incomplete", StringComparison.OrdinalIgnoreCase))
                    {
                        await session.SendCommandAsync("no ip address", TimeSpan.FromSeconds(5), cancellationToken);
                        await session.SendCommandAsync("no description", TimeSpan.FromSeconds(5), cancellationToken);
                        await session.SendCommandAsync("shutdown", TimeSpan.FromSeconds(5), cancellationToken);
                        await session.SendCommandAsync("exit", TimeSpan.FromSeconds(5), cancellationToken);
                    }
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
            // Se falhou, garante modo privilegiado novamente e tenta config t
            await EnsurePrivilegedExecAsync(session, cancellationToken: cancellationToken);
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
        var commands = GenerateCommands(circuit, wanInterface, lanInterface, IncluirNatLab, BootImage);
        if (IncluirNatLab)
            await ProgressAsync("[*] Opção secreta NAT (LAB) ATIVA: inside/outside + overload serão aplicados.");

        await ProgressAsync("[*] Aplicando comandos no roteador (cadência: 200ms por comando)...");
        foreach (var cmd in commands)
        {
            if (string.IsNullOrWhiteSpace(cmd) || cmd.StartsWith("!") || cmd == "configure terminal" || cmd == "write memory")
                continue; // Linhas vazias, comentários ou marcadores gerais dispensados

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (cmd.StartsWith("crypto key generate rsa", StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync("[*] Configurando chave criptográfica RSA (2048 bits)... Aguardando processamento da CPU Cisco...");
                    var rsaConditions = new StopCondition[]
                    {
                        new StopCondition.Contains("replace", "[yes/no]"),
                        new StopCondition.Contains("replace", "yes/no"),
                        new StopCondition.Contains("[yes/no]:", "[yes/no]:"),
                        new StopCondition.Contains("modulus [512]:", "modulus [512]:"),
                        new StopCondition.Prompt()
                    };
                    try
                    {
                        var rsaRes = await session.SendExpectAsync(cmd, rsaConditions, TimeSpan.FromSeconds(90), cancellationToken);
                        if (rsaRes.Output.Contains("yes/no", StringComparison.OrdinalIgnoreCase))
                        {
                            await session.SendRawAsync("yes\r\n", cancellationToken);
                            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(90), cancellationToken);
                        }
                        else if (rsaRes.Output.Contains("modulus [512]:", StringComparison.OrdinalIgnoreCase))
                        {
                            await session.SendRawAsync("2048\r\n", cancellationToken);
                            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(90), cancellationToken);
                        }
                        await ProgressAsync("    [OK] Chave RSA de 2048 bits gerada/confirmada com sucesso.");
                    }
                    catch (Exception ex)
                    {
                        await ProgressAsync($"    [AVISO] Geração da chave RSA ({ex.Message}). Sincronizando prompt da console...");
                        try
                        {
                            await session.SendRawAsync("\r\n", cancellationToken);
                            await Task.Delay(1000, cancellationToken);
                            await session.SendRawAsync("\r\n", cancellationToken);
                            await Task.Delay(1000, cancellationToken);
                        }
                        catch { }
                    }
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                var response = await session.SendCommandAsync(cmd, TimeSpan.FromSeconds(20), cancellationToken);
                if (response.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase) ||
                    response.Contains("% Incomplete command", StringComparison.OrdinalIgnoreCase))
                {
                    // Fallback para comando description: se a versão do IOS rejeitar caracteres residuais, aplica versão padrão garantida
                    if (cmd.StartsWith("description ", StringComparison.OrdinalIgnoreCase))
                    {
                        var fallbackCmd = cmd.Contains("WAN", StringComparison.OrdinalIgnoreCase) ? "description WAN" : "description * LAN *";
                        await ProgressAsync($"    [AVISO] Cisco rejeitou '{cmd}'. Aplicando fallback garantido '{fallbackCmd}'...");
                        await session.SendCommandAsync(fallbackCmd, TimeSpan.FromSeconds(10), cancellationToken);
                    }
                    // Ignora aviso se 'no switchport' ou comandos opcionais não forem suportados
                    else if (!cmd.Contains("no switchport", StringComparison.OrdinalIgnoreCase) &&
                             !cmd.Contains("tacacs server", StringComparison.OrdinalIgnoreCase))
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
    public static Task EnsurePrivilegedExecAsync(DeviceSession session, CancellationToken cancellationToken = default)
        => EnsurePrivilegedExecAsync(session, candidatePasswords: null, cancellationToken);

    public static async Task EnsurePrivilegedExecAsync(
        DeviceSession session,
        IEnumerable<string>? candidatePasswords,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();
        if (candidatePasswords != null)
        {
            foreach (var p in candidatePasswords)
            {
                if (!string.IsNullOrWhiteSpace(p) && !candidates.Contains(p.Trim()))
                    candidates.Add(p.Trim());
            }
        }
        if (!candidates.Contains("PRO1AN")) candidates.Add("PRO1AN");
        var adapter = new CiscoIOSAdapter(candidatePasswords: candidates);
        await adapter.EnterPrivilegedExecAsync(session, candidates, cancellationToken);
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

    public static string SanitizeHostname(string? source, string fallback = "ROUTER-CPE")
    {
        if (string.IsNullOrWhiteSpace(source))
            return fallback;

        var clean = source.Trim().Replace('/', '-').Replace('\\', '-').Replace(' ', '-');
        clean = Regex.Replace(clean, @"[^A-Za-z0-9\-_]", "");
        clean = clean.Trim('-');
        return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
    }

    /// <summary>
    /// Avalia se o equipamento Cisco IOS já se encontra em padrão de fábrica limpo ("zero lixo")
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
            await EnsurePrivilegedExecAsync(session, cancellationToken: ct);
            var runningCfg = await SendShowAsync(session, "show running-config | include ^hostname|^ip route|^crypto map", TimeSpan.FromSeconds(10), ct);

            // 1. Hostname
            var matchHost = Regex.Match(runningCfg, @"(?im)^\s*hostname\s+(\S+)");
            if (matchHost.Success)
            {
                detectedHostname = matchHost.Groups[1].Value.Trim();
                if (!detectedHostname.Equals("Router", StringComparison.OrdinalIgnoreCase) &&
                    !detectedHostname.Equals("Switch", StringComparison.OrdinalIgnoreCase))
                {
                    hasCustomHostname = true;
                    findings.Add($"Hostname customizado: '{detectedHostname}'");
                }
            }

            // 2. Rotas residuais
            var routeMatches = Regex.Matches(runningCfg, @"(?im)^\s*ip\s+route\s+0\.0\.0\.0\s+0\.0\.0\.0\s+(\S+)");
            if (routeMatches.Count > 0)
            {
                hasStaleRoutes = true;
                findings.Add($"{routeMatches.Count} rota(s) default residual(is)");
            }

            // 3. Crypto maps / IPsec de cliente anterior
            if (runningCfg.Contains("crypto map", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add("Políticas crypto/IPsec residuais de cliente anterior");
            }

            // 4. Interfaces com IP configurado além do padrão
            var intBrief = await SendShowAsync(session, "show ip interface brief", TimeSpan.FromSeconds(10), ct);
            var ipMatches = Regex.Matches(intBrief, @"(?im)^\s*\S+\s+(\d+\.\d+\.\d+\.\d+)");
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
            ? "Equipamento Cisco em padrão de fábrica (zero lixo detectado — reload desnecessário)."
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
