namespace NetworkDevice.Core.Session;

public class DeviceSessionException : Exception
{
    public DeviceSessionException(string message) : base(message)
    {
    }

    public DeviceSessionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class SessionTimeoutException : DeviceSessionException
{
    public SessionTimeoutException(string message) : base(message)
    {
    }
}

public sealed class LoginException : DeviceSessionException
{
    public LoginException(string message) : base(message)
    {
    }
}
