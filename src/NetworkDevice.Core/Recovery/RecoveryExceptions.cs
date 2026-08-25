using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Recovery;

/// <summary>
/// Lançada quando o processo de interrupção de boot falha (ex.: o equipamento ignorou o sinal e prosseguiu com o boot normal do sistema operacional).
/// </summary>
public sealed class BootInterruptionFailedException : DeviceSessionException
{
    public string? Reason { get; }
    public string? MatchedBootPattern { get; }
    public string? CapturedOutput { get; }

    public BootInterruptionFailedException(
        string message,
        string? reason = null,
        string? matchedBootPattern = null,
        string? capturedOutput = null)
        : base(message)
    {
        Reason = reason;
        MatchedBootPattern = matchedBootPattern;
        CapturedOutput = capturedOutput;
    }
}
