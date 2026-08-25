using System.Text.RegularExpressions;
using NetworkDevice.Core.Device;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Cisco;

public sealed class CiscoIOSAdapter : IDeviceAdapter
{
    private static readonly Regex ModelNumber = new(
        @"\b(?:system\s+)?model\s+number\s*:?\s*(?<m>[A-Za-z0-9._/-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModelBanner = new(
        @"\bcisco\s+(?<m>[A-Za-z0-9._/-]+)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModelUdiTable = new(
        @"(?m)^\*?\d+\s+(?<m>CISCO[A-Za-z0-9._/-]+)\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModelUdi = new(
        @"(?i)(?:PID|Product\s+ID)\s*:\s*(?<m>[A-Za-z0-9._/-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VersionRegex = new(
        @"\bVersion\s+(?<v>[\d()A-Za-z.-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SerialRegex = new(
        @"\b(?:system\s+)?serial\s+number\s*:?\s*(?<s>[A-Za-z0-9._-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SerialProcessorBoard = new(
        @"(?i)Processor\s+board\s+ID\s+(?<s>[A-Za-z0-9._-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SerialUdiTable = new(
        @"(?m)^\*?\d+\s+\S+\s+(?<s>[A-Za-z0-9._-]+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string? _enableSecret;

    public CiscoIOSAdapter(string? enableSecret = null)
    {
        _enableSecret = enableSecret;
    }

    public string Vendor => "Cisco";

    public static SessionOptions CreateSessionOptions(string? enableSecret, string? username = null, string? password = null) =>
        new()
        {
            PromptMatcher = RegexPromptMatcher.CiscoIos(),
            Username = username,
            Password = password
        };

    public async Task EnterPrivilegedExecAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        if (session.Mode is ExecMode.PrivilegedExec or ExecMode.GlobalConfig || (session.CurrentPrompt != null && session.CurrentPrompt.EndsWith("#")))
            return;

        var result = await session.SendExpectAsync(
            "enable",
            new StopCondition[] { new StopCondition.Contains("Password", "Password:"), new StopCondition.Prompt() },
            TimeSpan.FromSeconds(20),
            cancellationToken);

        if (result.Matched is StopCondition.Contains)
        {
            if (_enableSecret is null)
                throw new DeviceSessionException("Dispositivo solicitou senha de enable (modo privilegiado protegido), mas nenhuma foi configurada.");
            await session.SendCommandAsync(_enableSecret, cancellationToken: cancellationToken);
        }

        if (session.Mode is not (ExecMode.PrivilegedExec or ExecMode.GlobalConfig) && (session.CurrentPrompt == null || !session.CurrentPrompt.EndsWith("#")))
        {
            // Se o output terminou com '#' ou '>'
            if (result.Output.TrimEnd().EndsWith("#"))
                return;

            throw new DeviceSessionException("Falha ao entrar em modo privilegiado (enable).");
        }
    }

    public async Task<DeviceInfo> IdentifyAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        await EnterPrivilegedExecAsync(session, cancellationToken);
        try { await DisablePaginationAsync(session, cancellationToken); } catch { }

        var output = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(45), cancellationToken);

        var model = ParseModel(output)
            ?? throw new DeviceSessionException("Modelo não identificado em 'show version'.");

        return new DeviceInfo(
            "Cisco",
            model,
            "Cisco IOS",
            ParseVersion(output),
            ParseSerial(output),
            CleanHostname(session.CurrentPrompt));
    }

    public async Task<string> GetRunningConfigAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        await EnterPrivilegedExecAsync(session, cancellationToken);
        await DisablePaginationAsync(session, cancellationToken);

        var output = await session.SendCommandAsync(
            "show running-config",
            TimeSpan.FromSeconds(120),
            cancellationToken);

        return StripEchoAndPrompt(output, "show running-config");
    }

    public async Task<string> GetStartupConfigAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        await EnterPrivilegedExecAsync(session, cancellationToken);
        await DisablePaginationAsync(session, cancellationToken);

        var output = await session.SendCommandAsync(
            "show startup-config",
            TimeSpan.FromSeconds(120),
            cancellationToken);

        return StripEchoAndPrompt(output, "show startup-config");
    }

    public async Task SaveConfigAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        await EnterPrivilegedExecAsync(session, cancellationToken);
        await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(60), cancellationToken);
    }

    public async Task DisablePaginationAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        if (session.Mode is not (ExecMode.PrivilegedExec or ExecMode.GlobalConfig) && (session.CurrentPrompt == null || !session.CurrentPrompt.EndsWith("#")))
            throw new DeviceSessionException("'terminal length 0' exige modo privilegiado.");

        await session.SendCommandAsync("terminal length 0", cancellationToken: cancellationToken);
        await session.SendCommandAsync("terminal width 0", cancellationToken: cancellationToken);
    }

    private static string? ParseModel(string output)
    {
        var match = ModelNumber.Match(output);
        if (match.Success)
            return match.Groups["m"].Value;

        match = ModelBanner.Match(output);
        if (match.Success)
            return match.Groups["m"].Value;

        match = ModelUdiTable.Match(output);
        if (match.Success)
            return match.Groups["m"].Value;

        match = ModelUdi.Match(output);
        return match.Success ? match.Groups["m"].Value : null;
    }

    private static string? ParseVersion(string output)
    {
        var match = VersionRegex.Match(output);
        return match.Success ? match.Groups["v"].Value : null;
    }

    private static string? ParseSerial(string output)
    {
        var match = SerialRegex.Match(output);
        if (match.Success)
            return match.Groups["s"].Value;

        match = SerialProcessorBoard.Match(output);
        if (match.Success)
            return match.Groups["s"].Value;

        match = SerialUdiTable.Match(output);
        return match.Success ? match.Groups["s"].Value : null;
    }

    private static string CleanHostname(string? prompt)
    {
        if (string.IsNullOrEmpty(prompt))
            return string.Empty;

        var host = prompt;
        var paren = prompt.IndexOf('(');
        if (paren >= 0)
            host = prompt[..paren];

        return host.TrimEnd('#', '>');
    }

    private static string StripEchoAndPrompt(string output, string command)
    {
        var lines = output.Replace("\r", "").Split('\n').ToList();
        if (lines.Count > 0 && lines[0].Trim().Equals(command, StringComparison.Ordinal))
            lines.RemoveAt(0);

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].Trim().Length == 0)
                continue;

            if (RegexPromptMatcher.CiscoIos().TryMatch(lines[i].Trim()) is not null)
                lines.RemoveAt(i);
            break;
        }

        return string.Join(Environment.NewLine, lines.Select(l => l.TrimEnd())).TrimEnd() + Environment.NewLine;
    }
}
