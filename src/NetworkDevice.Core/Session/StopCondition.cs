using System.Text.RegularExpressions;

namespace NetworkDevice.Core.Session;

public abstract record StopCondition
{
    public sealed record Prompt : StopCondition;

    public sealed record LineRegex(string Name, Regex Regex) : StopCondition;

    public sealed record Contains(string Name, string Text, StringComparison Comparison = StringComparison.OrdinalIgnoreCase) : StopCondition;
}
