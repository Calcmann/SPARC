using System.Text.RegularExpressions;

namespace NetworkDevice.Core.Recovery;

public sealed class BootInterruptProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Manufacturer { get; init; } = "Cisco";
    public string Family { get; init; } = "Generic";
    public IReadOnlyList<string> ModelPatterns { get; init; } = Array.Empty<string>();

    public BootInterruptMethod Method { get; init; } = BootInterruptMethod.Break;

    public bool RequiresManualIntervention { get; init; }
    public string? ManualInterventionPrompt { get; init; }

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMilliseconds(200);
    public int BurstCount { get; init; } = 3;
    public TimeSpan BurstInterval { get; init; } = TimeSpan.FromMilliseconds(25);
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromMilliseconds(1500);
    public TimeSpan MaxWindow { get; init; } = TimeSpan.FromSeconds(60);
    public int MaxTotalTransmissions { get; init; } = 40;

    public OsBootPolicy OsBootPolicy { get; init; } = OsBootPolicy.TerminalFail;

    public IReadOnlyList<Regex> RommonPatterns { get; init; } = new List<Regex>
    {
        new(@"(?i)(?:rommon|common)\s*\S*\s*[>#]", RegexOptions.Compiled),
        new(@"(?i)rommon\s*\d+\s*>", RegexOptions.Compiled),
        new(@"(?i)^rommon\s*>", RegexOptions.Compiled),
        new(@"(?i)switch\s*[:>]", RegexOptions.Compiled),
        new(@"(?i)loader\s*>", RegexOptions.Compiled)
    };

    public IReadOnlyList<Regex> OsBootPatterns { get; init; } = new List<Regex>
    {
        new(@"Self[-\s]decompressing\s+the\s+image", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Loading\s+.*(?:\.bin|\.image)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Digitally\s+Signed\s+Production\s+Software", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Smart\s+Init\s+is\s+enabled", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Cisco\s+IOS\s+Software", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"IOS\s+Image\s+Load\s+Test", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Initial Configuration Dialog", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"Press RETURN to get started", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"^(?:User|Username|login)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    };
}
