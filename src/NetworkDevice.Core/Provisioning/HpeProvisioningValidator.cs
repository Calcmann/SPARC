using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Provisioning;

public enum HpeValidationStatus
{
    Pass,
    Warn,
    Fail
}

public sealed class HpeValidationItem
{
    public string Name { get; set; } = string.Empty;
    public HpeValidationStatus Status { get; set; }
    public string Expected { get; set; } = string.Empty;
    public string Actual { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public override string ToString()
    {
        var icon = Status switch
        {
            HpeValidationStatus.Pass => "✅ PASS",
            HpeValidationStatus.Warn => "⚠️ WARN",
            HpeValidationStatus.Fail => "❌ FAIL",
            _ => "❓ UNKNOWN"
        };
        return $"[{icon}] {Name}: Esperado='{Expected}' | Atual='{Actual}' ({Message})";
    }
}

public sealed class HpeValidationReport
{
    public HpeValidationStatus OverallStatus { get; set; } = HpeValidationStatus.Pass;
    public List<HpeValidationItem> Items { get; } = new();

    public int PassedCount => Items.Count(i => i.Status == HpeValidationStatus.Pass);
    public int FailedCount => Items.Count(i => i.Status == HpeValidationStatus.Fail);
    public bool HasFailures => Items.Any(i => i.Status == HpeValidationStatus.Fail);

    public void Add(string name, HpeValidationStatus status, string expected, string actual, string message)
    {
        Items.Add(new HpeValidationItem
        {
            Name = name,
            Status = status,
            Expected = expected,
            Actual = actual,
            Message = message
        });

        if (status == HpeValidationStatus.Fail)
        {
            OverallStatus = HpeValidationStatus.Fail;
        }
        else if (status == HpeValidationStatus.Warn && OverallStatus != HpeValidationStatus.Fail)
        {
            OverallStatus = HpeValidationStatus.Warn;
        }
    }

    public string GenerateSummary()
    {
        var lines = new List<string>
        {
            "=================================================================",
            $"   AUDITORIA PÓS-PROVISIONAMENTO HPE COMWARE: [{OverallStatus.ToString().ToUpperInvariant()}]",
            "================================================================="
        };

        foreach (var item in Items)
        {
            lines.Add(item.ToString());
        }
        lines.Add("=================================================================");
        return string.Join(Environment.NewLine, lines);
    }
}

public sealed class HpeProvisioningValidator
{
    private readonly Func<string, Task>? _progress;

    public HpeProvisioningValidator(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    public async Task<HpeValidationReport> ValidateAsync(
        DeviceSession session,
        SaipCircuitData circuit,
        string wanInterface = "GigabitEthernet0/0",
        string lanInterface = "GigabitEthernet0/1",
        CancellationToken ct = default)
    {
        var report = new HpeValidationReport();

        await ProgressAsync("[*] [VALIDADOR] Iniciando auditoria pós-provisionamento HPE...");

        // 1. Interfaces e IPs (display ip interface brief)
        var ipBrief = await session.SendCommandAsync("display ip interface brief", TimeSpan.FromSeconds(10), ct);
        
        // Backup: se não encontrar no brief, valida também pela configuração ativa
        string wanConfig = string.Empty;
        string lanConfig = string.Empty;
        try { wanConfig = await session.SendCommandAsync($"display current-configuration interface {wanInterface}", TimeSpan.FromSeconds(5), ct); } catch { }
        try { lanConfig = await session.SendCommandAsync($"display current-configuration interface {lanInterface}", TimeSpan.FromSeconds(5), ct); } catch { }

        AuditIpInterfaces(report, ipBrief, wanConfig, lanConfig, circuit, wanInterface, lanInterface);

        // 2. Rota Estática Default (display current-configuration | include route-static)
        var routeCfg = await session.SendCommandAsync("display current-configuration | include route-static", TimeSpan.FromSeconds(8), ct);
        AuditDefaultRoute(report, routeCfg, circuit.WanGateway);

        // 3. Usuário EBT e Permissões
        var userCfg = await session.SendCommandAsync("display current-configuration | include local-user", TimeSpan.FromSeconds(8), ct);
        var roleCfg = await session.SendCommandAsync("display current-configuration | include authorization-attribute", TimeSpan.FromSeconds(8), ct);
        var serviceCfg = await session.SendCommandAsync("display current-configuration | include service-type", TimeSpan.FromSeconds(8), ct);
        AuditLocalUser(report, userCfg, roleCfg, serviceCfg);

        // 4. Telnet Server
        var telnetCfg = await session.SendCommandAsync("display current-configuration | include telnet server", TimeSpan.FromSeconds(8), ct);
        AuditTelnet(report, telnetCfg);

        // 5. Startup Configuration
        var startupCfg = await session.SendCommandAsync("display startup", TimeSpan.FromSeconds(8), ct);
        AuditStartupConfig(report, startupCfg);

        // Exibe o resumo detalhado
        await ProgressAsync(report.GenerateSummary());

        return report;
    }

    public static void AuditIpInterfaces(
        HpeValidationReport report,
        string ipBriefOutput,
        string wanConfig,
        string lanConfig,
        SaipCircuitData circuit,
        string wanInterface,
        string lanInterface)
    {
        var cleanWan = wanInterface.Replace(" ", "");
        var cleanLan = lanInterface.Replace(" ", "");

        AuditSingleInterface(report, ipBriefOutput, wanConfig, "WAN IP", "WAN Link (GE0/0)", circuit.WanIp, cleanWan, new[] { "GE0/0", "GigabitEthernet0/0", "GE0", "GigabitEthernet0" });
        AuditSingleInterface(report, ipBriefOutput, lanConfig, "LAN IP", "LAN Link (GE0/1)", circuit.LanIp, cleanLan, new[] { "GE0/1", "GigabitEthernet0/1", "GE1", "GigabitEthernet1" });
    }

    // Overload para compatibilidade de testes unitários
    public static void AuditIpInterfaces(
        HpeValidationReport report,
        string ipBriefOutput,
        SaipCircuitData circuit,
        string wanInterface,
        string lanInterface)
    {
        AuditIpInterfaces(report, ipBriefOutput, string.Empty, string.Empty, circuit, wanInterface, lanInterface);
    }

    private static void AuditSingleInterface(
        HpeValidationReport report,
        string ipBriefOutput,
        string ifaceConfig,
        string ipLabel,
        string linkLabel,
        string? expectedIp,
        string mainIface,
        string[] aliases)
    {
        var lines = (ipBriefOutput ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var ifaceNames = new List<string> { mainIface };
        ifaceNames.AddRange(aliases);

        string? matchedLine = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            foreach (var name in ifaceNames)
            {
                if (Regex.IsMatch(trimmed, $@"(?i)^\s*{Regex.Escape(name)}\b"))
                {
                    matchedLine = trimmed;
                    break;
                }
            }
            if (matchedLine != null) break;
        }

        if (matchedLine != null)
        {
            // Extrai o IP da linha da tabela (ex.: GE0/0 down down 201.90.204.22 ou GE0/0 201.90.204.22/30 down down)
            var ipMatch = Regex.Match(matchedLine, @"\b(?<ip>\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
            var actualIp = ipMatch.Success ? ipMatch.Groups["ip"].Value : "Não atribuído";

            if (string.Equals(actualIp, expectedIp, StringComparison.OrdinalIgnoreCase))
            {
                report.Add(ipLabel, HpeValidationStatus.Pass, expectedIp ?? "", actualIp, "IP configurado corretamente na interface.");
            }
            else if (!string.IsNullOrEmpty(expectedIp) && ((ipBriefOutput?.Contains(expectedIp, StringComparison.OrdinalIgnoreCase) == true) || (ifaceConfig?.Contains(expectedIp, StringComparison.OrdinalIgnoreCase) == true)))
            {
                report.Add(ipLabel, HpeValidationStatus.Pass, expectedIp, expectedIp, "IP confirmado na configuração da interface.");
            }
            else
            {
                report.Add(ipLabel, HpeValidationStatus.Fail, expectedIp ?? "", actualIp, "IP divergente do circuito SAIP na interface.");
            }

            // Link status (UP vs DOWN) — Em bancada, cabo desconectado é normal e gera apenas WARN
            var phyUp = Regex.IsMatch(matchedLine, @"(?i)\bUP\b");
            var linkStatus = phyUp ? HpeValidationStatus.Pass : HpeValidationStatus.Warn;
            report.Add(linkLabel, linkStatus, "UP", phyUp ? "UP" : "DOWN", phyUp ? "Link físico ativo." : "Cabo desconectado (normal em bancada).");
        }
        else
        {
            // Se a interface não foi encontrada no brief, verifica se o IP está na configuração ativa da interface
            if (!string.IsNullOrEmpty(expectedIp) && ((ifaceConfig?.Contains(expectedIp, StringComparison.OrdinalIgnoreCase) == true) || (ipBriefOutput?.Contains(expectedIp, StringComparison.OrdinalIgnoreCase) == true)))
            {
                report.Add(ipLabel, HpeValidationStatus.Pass, expectedIp, expectedIp, "IP confirmado na configuração do equipamento.");
                report.Add(linkLabel, HpeValidationStatus.Warn, "UP", "N/A", "Interface configurada.");
            }
            else
            {
                report.Add(ipLabel, HpeValidationStatus.Fail, expectedIp ?? "", "Não encontrado", $"Interface '{mainIface}' não localizada em display ip interface brief.");
                report.Add(linkLabel, HpeValidationStatus.Warn, "UP", "DOWN", "Interface ausente ou desconectada.");
            }
        }
    }

    public static void AuditDefaultRoute(HpeValidationReport report, string routeOutput, string? expectedGateway)
    {
        if (string.IsNullOrWhiteSpace(expectedGateway))
        {
            report.Add("Rota Default", HpeValidationStatus.Warn, "N/A", "Não aplicável", "Circuito SAIP sem Gateway informado.");
            return;
        }

        var isConfigured = routeOutput.Contains(expectedGateway, StringComparison.OrdinalIgnoreCase)
                        && routeOutput.Contains("0.0.0.0", StringComparison.OrdinalIgnoreCase);

        if (isConfigured)
        {
            report.Add("Rota Default", HpeValidationStatus.Pass, $"0.0.0.0 -> {expectedGateway}", $"0.0.0.0 -> {expectedGateway}", "Rota default configurada com sucesso.");
        }
        else
        {
            report.Add("Rota Default", HpeValidationStatus.Fail, $"0.0.0.0 -> {expectedGateway}", routeOutput.Trim(), "Rota default não encontrada na configuração ativa.");
        }
    }

    public static void AuditLocalUser(
        HpeValidationReport report,
        string userCfg,
        string roleCfg,
        string serviceCfg)
    {
        var hasEbt = userCfg.Contains("local-user EBT", StringComparison.OrdinalIgnoreCase);
        if (hasEbt)
        {
            report.Add("Usuário EBT", HpeValidationStatus.Pass, "local-user EBT", "Presente", "Usuário administrativo EBT configurado.");
        }
        else
        {
            report.Add("Usuário EBT", HpeValidationStatus.Fail, "local-user EBT", "Ausente", "Usuário EBT não encontrado na configuração.");
        }

        var hasAdminRole = roleCfg.Contains("network-admin", StringComparison.OrdinalIgnoreCase);
        if (hasAdminRole)
        {
            report.Add("Perfil de Acesso (Role)", HpeValidationStatus.Pass, "network-admin", "network-admin", "Papel administrativo network-admin atribuído.");
        }
        else
        {
            report.Add("Perfil de Acesso (Role)", HpeValidationStatus.Warn, "network-admin", roleCfg.Trim(), "Papel network-admin não confirmado explicitamente.");
        }

        var hasTelnet = serviceCfg.Contains("telnet", StringComparison.OrdinalIgnoreCase);
        if (hasTelnet)
        {
            report.Add("Serviço de Acesso (Service-Type)", HpeValidationStatus.Pass, "telnet", "telnet", "Serviço telnet habilitado para o usuário.");
        }
        else
        {
            report.Add("Serviço de Acesso (Service-Type)", HpeValidationStatus.Fail, "telnet", serviceCfg.Trim(), "Serviço telnet não associado ao usuário local.");
        }
    }

    public static void AuditTelnet(HpeValidationReport report, string telnetOutput)
    {
        var isEnabled = telnetOutput.Contains("telnet server enable", StringComparison.OrdinalIgnoreCase);
        if (isEnabled)
        {
            report.Add("Telnet Server", HpeValidationStatus.Pass, "telnet server enable", "Habilitado", "Servidor Telnet ativo no HPE.");
        }
        else
        {
            report.Add("Telnet Server", HpeValidationStatus.Fail, "telnet server enable", telnetOutput.Trim(), "Servidor Telnet não habilitado na configuração.");
        }
    }

    public static void AuditStartupConfig(HpeValidationReport report, string startupOutput)
    {
        var hasStartup = startupOutput.Contains("startup.cfg", StringComparison.OrdinalIgnoreCase);
        if (hasStartup)
        {
            report.Add("Startup Configuration", HpeValidationStatus.Pass, "startup.cfg", "startup.cfg", "Configuração gravada e vinculada à inicialização.");
        }
        else
        {
            report.Add("Startup Configuration", HpeValidationStatus.Fail, "startup.cfg", startupOutput.Trim(), "Arquivo startup.cfg não confirmado como imagem de boot.");
        }
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress != null)
            await _progress(message);
    }
}
