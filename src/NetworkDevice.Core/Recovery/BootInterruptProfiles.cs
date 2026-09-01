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
        RetryInterval = TimeSpan.FromMilliseconds(500),
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
        ModelPatterns = new[] { "MSR954", "954", "HP 954", "HPE 954", "MSR920", "MSR930", "MSR931", "MSR935", "MSR900", "921", "HPE", "HP", "Comware", "5130", "5500", "5900" },
        Method = BootInterruptMethod.CtrlB,
        RequiresManualIntervention = false,
        InitialDelay = TimeSpan.Zero,
        BurstCount = 2,
        BurstInterval = TimeSpan.FromMilliseconds(40),
        RetryInterval = TimeSpan.FromMilliseconds(160),
        MaxWindow = TimeSpan.FromMinutes(3),
        MaxTotalTransmissions = 400,
        OsBootPolicy = OsBootPolicy.Warning,
        RommonPatterns = new List<Regex>
        {
            new(@"(?i)(?:BOOT\s*MENU|<(?:EXTENDED-)?BOOTWARE\s*MENU>|<MAIN\s*MENU>|<BASIC\s*BOOT\s*MENU>|<ETHERNET\s*SUBMENU>|Enter\s+your\s+choice|choice\s*\(\s*0\s*-\s*[0-9]\s*\)|choice\s*:|BootWare\s+Operation\s+Menu)", RegexOptions.Compiled),
            new(@"(?i)(?:Press\s+Ctrl\+[BD]\s+to\s+enter|Press\s+Ctrl\+B\s+to\s+access|Press\s+Ctrl\+B\s+to\s+stop)", RegexOptions.Compiled),
            new(@"(?i)(?:The\s+image\s+does\s+not\s+exist|Loading\s+images\s+fails|Loading\s+boot\s+image\s+fails|operating\s+device\s+is\s+flash)", RegexOptions.Compiled)
        }
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

        var trimmed = id.Trim();

        // 1. Correspondência exata por Id
        var byId = All.FirstOrDefault(p => p.Id.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId;

        // 2. Correspondência exata por ModelPatterns
        var byModel = All.FirstOrDefault(p => p.ModelPatterns.Any(m => m.Equals(trimmed, StringComparison.OrdinalIgnoreCase)));
        if (byModel != null) return byModel;

        // 3. Heurística contextual por substrings (priorizando HPE antes de Cisco)
        if (trimmed.Contains("hpe", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("msr", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("comware", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("954", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("958", StringComparison.OrdinalIgnoreCase))
        {
            return HpeMsr;
        }

        if (trimmed.Contains("1900", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("1921", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("1941", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("1905", StringComparison.OrdinalIgnoreCase))
        {
            return Cisco1900;
        }

        if (trimmed.Contains("900", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("921", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("ctrl", StringComparison.OrdinalIgnoreCase))
        {
            return Cisco900;
        }

        if (trimmed.Contains("break", StringComparison.OrdinalIgnoreCase))
        {
            return Cisco1900;
        }

        return CiscoUniversal;
    }
}
