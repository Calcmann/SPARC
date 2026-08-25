using System.Text.RegularExpressions;

namespace NetworkDevice.Core.Session;

public sealed class RegexPromptMatcher : IPromptMatcher
{
    private static readonly Regex UniversalPromptRegex = new(
        @"^(?:(?<rommon>rommon(?:\s+\d+)?\s*>)|(?<cisco>[A-Za-z0-9_.+()/-]+?(?:\([A-Za-z0-9_.+()/-]+\))?[#>])|\[[~*]?(?<hpe_sys>[A-Za-z0-9_.+()/-]+?)\]|<(?<hpe_user>[A-Za-z0-9_.+()/-]+?)>)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Regex _regex;
    private readonly Func<string, string, string, ExecMode>? _modeResolver;

    public RegexPromptMatcher(Regex regex, Func<string, string, string, ExecMode>? modeResolver = null)
    {
        _regex = regex;
        _modeResolver = modeResolver;
    }

    public static RegexPromptMatcher CiscoIos() => Universal();

    public static RegexPromptMatcher Universal() =>
        new(UniversalPromptRegex);

    public static RegexPromptMatcher HpeComware() => Universal();

    public PromptMatch? TryMatch(string lastLine)
    {
        if (string.IsNullOrWhiteSpace(lastLine))
            return null;

        var trimmed = lastLine.Trim();
        if (trimmed.Equals("[OK]", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("[confirm]", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("[yes/no]", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("[yes]", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("[no]", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("[y/n]", StringComparison.OrdinalIgnoreCase))
            return null;

        var m = _regex.Match(trimmed);
        if (!m.Success)
            return null;

        ExecMode mode = ExecMode.UserExec;
        if (trimmed.StartsWith("rommon", StringComparison.OrdinalIgnoreCase))
        {
            mode = ExecMode.Rommon;
        }
        else if (trimmed.EndsWith("#"))
        {
            mode = trimmed.Contains("(config-") ? ExecMode.ConfigSubmode
                 : trimmed.Contains("(config") ? ExecMode.GlobalConfig
                 : ExecMode.PrivilegedExec;
        }
        else if (trimmed.EndsWith(">"))
        {
            mode = ExecMode.UserExec;
        }
        else if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
        {
            // HPE / Comware: [HPE] é GlobalConfig, [HPE-GigabitEthernet0/0] é ConfigSubmode
            mode = trimmed.Contains("-") ? ExecMode.ConfigSubmode : ExecMode.GlobalConfig;
        }
        else if (trimmed.StartsWith("<") && trimmed.EndsWith(">"))
        {
            mode = ExecMode.UserExec;
        }

        return new PromptMatch(trimmed, mode);
    }
}
