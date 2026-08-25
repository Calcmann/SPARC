namespace NetworkDevice.Protocols.Ssh;

public sealed class SshHostKeyPolicy
{
    public IReadOnlySet<string> TrustedSha256Fingerprints { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool AcceptUnknownHosts { get; init; }
}
