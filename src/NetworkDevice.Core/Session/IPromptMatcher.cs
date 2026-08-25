namespace NetworkDevice.Core.Session;

public interface IPromptMatcher
{
    PromptMatch? TryMatch(string lastLine);
}
