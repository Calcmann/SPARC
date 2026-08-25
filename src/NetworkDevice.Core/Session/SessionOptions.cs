namespace NetworkDevice.Core.Session;

public sealed class SessionOptions
{
    public IPromptMatcher PromptMatcher { get; set; } = RegexPromptMatcher.CiscoIos();

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public string? Username { get; set; }

    public string? Password { get; set; }
}
