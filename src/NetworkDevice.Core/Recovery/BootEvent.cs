namespace NetworkDevice.Core.Recovery;

public enum BootEventType
{
    Output,
    RommonDetected,
    OsBootDetected
}

public sealed record BootEvent(
    BootEventType Type,
    string Text,
    string? MatchedPattern = null,
    string? Line = null);
