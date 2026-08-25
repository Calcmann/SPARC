using System.Text.RegularExpressions;

namespace NetworkDevice.Core.Recovery;

public static class BootInterruptProfiles
{
    public static readonly BootInterruptProfile Cisco900 = new()
    {
        Id = "cisco.c900.ctrl-c",
        Name = "Cisco Série 900 / C921-4P (Ctrl+C Puro @ 9600)",
        Manufacturer = "Cisco",
        Family = "ISR 900",
        ModelPatterns = new[] { "C921", "C926", "C927", "C931" },
        Method = BootInterruptMethod.CtrlC,
        RequiresManualIntervention = false,
        InitialDelay = TimeSpan.Zero,
        BurstCount = 1,
        BurstInterval = TimeSpan.Zero,
        RetryInterval = TimeSpan.FromMilliseconds(1000),
        MaxWindow = TimeSpan.FromSeconds(90),
        MaxTotalTransmissions = 95,
        OsBootPolicy = OsBootPolicy.TerminalFail
    };

    public static readonly BootInterruptProfile Cisco1900 = new()
    {
        Id = "cisco.c1900.break",
        Name = "Cisco Série 1900 / ISR G2 (Break @ 9600 - 1921/1941/1905)",
        Manufacturer = "Cisco",
        Family = "ISR 1900 / G2",
        ModelPatterns = new[] { "1900", "1921", "1941", "1905", "C1900", "C1921", "C1941", "C1905", "1900 Series" },
        Method = BootInterruptMethod.Break,
        RequiresManualIntervention = false,
        InitialDelay = TimeSpan.FromMilliseconds(100),
        BurstCount = 2,
        BurstInterval = TimeSpan.FromMilliseconds(40),
        RetryInterval = TimeSpan.FromMilliseconds(350),
        MaxWindow = TimeSpan.FromSeconds(90),
        MaxTotalTransmissions = 180,
        OsBootPolicy = OsBootPolicy.TerminalFail
    };

    public static readonly BootInterruptProfile CiscoStandardBreak = new()
    {
        Id = "cisco.standard.break",
        Name = "Cisco Tradicional / ISR (Break Elétrico)",
        Manufacturer = "Cisco",
        Family = "ISR / Clássico",
        ModelPatterns = new[] { "1800", "2800", "2900", "3800", "3900", "4000" },
        Method = BootInterruptMethod.Break,
        RequiresManualIntervention = false,
        InitialDelay = TimeSpan.FromMilliseconds(200),
        BurstCount = 2,
        BurstInterval = TimeSpan.FromMilliseconds(40),
        RetryInterval = TimeSpan.FromMilliseconds(1500),
        MaxWindow = TimeSpan.FromSeconds(90),
        MaxTotalTransmissions = 60,
        OsBootPolicy = OsBootPolicy.TerminalFail
    };

    public static readonly BootInterruptProfile CiscoCatalystManualMode = new()
    {
        Id = "cisco.catalyst.mode",
        Name = "Cisco Catalyst Switch (Botão MODE)",
        Manufacturer = "Cisco",
        Family = "Catalyst",
        ModelPatterns = new[] { "2960", "3560", "3750", "3850", "9200", "9300" },
        Method = BootInterruptMethod.None,
        RequiresManualIntervention = true,
        ManualInterventionPrompt = "Desconecte o cabo de força. Mantenha o botão frontal 'MODE' pressionado e reconecte o cabo de força. Solte o botão após o LED 'SYST' parar de piscar em âmbar (~15s) e entrar no prompt 'switch:'.",
        MaxWindow = TimeSpan.FromMinutes(3),
        OsBootPolicy = OsBootPolicy.TerminalFail
    };

    public static readonly BootInterruptProfile CiscoUniversal = new()
    {
        Id = "cisco.universal",
        Name = "Cisco Automático (Break + Ctrl+C)",
        Manufacturer = "Cisco",
        Family = "Universal",
        Method = BootInterruptMethod.Dual,
        RequiresManualIntervention = false,
        InitialDelay = TimeSpan.Zero,
        BurstCount = 1,
        BurstInterval = TimeSpan.Zero,
        RetryInterval = TimeSpan.FromMilliseconds(1000),
        MaxWindow = TimeSpan.FromSeconds(90),
        MaxTotalTransmissions = 95,
        OsBootPolicy = OsBootPolicy.TerminalFail
    };

    public static readonly BootInterruptProfile HpeMsr = new()
    {
        Id = "hpe.msr.ctrl-b",
        Name = "HPE / HP MSR / Comware (Ctrl+B @ 9600 - BootWare)",
        Manufacturer = "HPE",
        Family = "MSR / FlexNetwork / Comware",
        ModelPatterns = new[] { "MSR920", "MSR930", "MSR931", "MSR935", "MSR900", "921", "HPE", "HP", "Comware", "5130", "5500", "5900" },
        Method = BootInterruptMethod.CtrlB,
        RequiresManualIntervention = false,
        InitialDelay = TimeSpan.Zero,
        BurstCount = 2,
        BurstInterval = TimeSpan.FromMilliseconds(40),
        RetryInterval = TimeSpan.FromMilliseconds(160),
        MaxWindow = TimeSpan.FromMinutes(3),
        MaxTotalTransmissions = 400,
        OsBootPolicy = OsBootPolicy.Warning
    };

    public static readonly BootInterruptProfile GenericManual = new()
    {
        Id = "generic.manual",
        Name = "Genérico / Intervenção Manual",
        Manufacturer = "Generic",
        Family = "Generic",
        Method = BootInterruptMethod.None,
        RequiresManualIntervention = true,
        ManualInterventionPrompt = "Realize o procedimento manual de interrupção de boot específico do equipamento durante a inicialização até que o prompt do bootloader apareça.",
        MaxWindow = TimeSpan.FromMinutes(3),
        OsBootPolicy = OsBootPolicy.Warning
    };

    public static IReadOnlyList<BootInterruptProfile> All { get; } = new[]
    {
        HpeMsr,
        Cisco1900,
        Cisco900,
        CiscoStandardBreak,
        CiscoUniversal,
        CiscoCatalystManualMode,
        GenericManual
    };

    public static BootInterruptProfile FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return CiscoUniversal;

        return All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? (id.Contains("hpe", StringComparison.OrdinalIgnoreCase) || id.Contains("msr", StringComparison.OrdinalIgnoreCase) || id.Contains("comware", StringComparison.OrdinalIgnoreCase)
                ? HpeMsr
                : id.Contains("1900", StringComparison.OrdinalIgnoreCase) || id.Contains("1921", StringComparison.OrdinalIgnoreCase) || id.Contains("1941", StringComparison.OrdinalIgnoreCase) || id.Contains("1905", StringComparison.OrdinalIgnoreCase)
                    ? Cisco1900
                    : id.Contains("900", StringComparison.OrdinalIgnoreCase) || id.Contains("921", StringComparison.OrdinalIgnoreCase) || id.Contains("ctrl", StringComparison.OrdinalIgnoreCase)
                        ? Cisco900
                        : id.Contains("break", StringComparison.OrdinalIgnoreCase)
                            ? Cisco1900
                            : CiscoUniversal);
    }
}
