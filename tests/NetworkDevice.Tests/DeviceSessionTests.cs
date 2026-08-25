using NetworkDevice.Core.Session;
using NetworkDevice.Tests.TestDoubles;

namespace NetworkDevice.Tests;

public class DeviceSessionTests
{
    [Fact]
    public async Task SendCommandAsync_ReturnsOutputAndDetectsPrivilegedMode()
    {
        var transport = new ScriptedTransport(
            _ => "show version output\r\nSW-DEPTO-01#\r\n",
            initialOutput: "SW-DEPTO-01#\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();

        var output = await session.SendCommandAsync("show version");

        Assert.Contains("show version output", output);
        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        Assert.Equal("SW-DEPTO-01#", session.CurrentPrompt);
    }

    [Fact]
    public async Task SendCommandAsync_DetectsUserExecMode()
    {
        var transport = new ScriptedTransport(_ => "SW-01>\r\n", initialOutput: "SW-01>\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();

        await session.SendCommandAsync("show version");

        Assert.Equal(ExecMode.UserExec, session.Mode);
    }

    [Fact]
    public async Task SendCommandAsync_HandlesPagination()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "show running-config" =>
                    "Building configuration...\r\n" +
                    "interface GigabitEthernet0/1\r\n" +
                    " switchport mode access\r\n" +
                    "--More--\r\n",
                " " => "!\r\nend\r\nSW-01#\r\n",
                _ => "SW-01#\r\n"
            },
            initialOutput: null);

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();

        var output = await session.SendCommandAsync("show running-config");

        Assert.Contains("interface GigabitEthernet0/1", output);
        Assert.Contains("switchport mode access", output);
        Assert.DoesNotContain("--More--", output);
        Assert.Contains(" ", transport.Commands);
    }

    [Fact]
    public async Task SendCommandAsync_TimesOut_WhenDeviceDoesNotRespond()
    {
        var transport = new ScriptedTransport(_ => "", initialOutput: "SW-01#\r\n");
        var options = new SessionOptions { CommandTimeout = TimeSpan.FromMilliseconds(500) };

        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        await Assert.ThrowsAsync<SessionTimeoutException>(
            () => session.SendCommandAsync("show version"));
    }

    [Fact]
    public async Task ConnectAsync_WithSerialLogin_LogsIn()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "admin" => "Password: ",
                "secret" => "SW-01#\r\n",
                _ => ""
            },
            initialOutput: "User Access Verification\r\n\r\nUsername: ");

        var options = new SessionOptions { Username = "admin", Password = "secret" };
        await using var session = new DeviceSession(transport, options);

        await session.ConnectAsync();

        Assert.True(session.IsConnected);
        Assert.Contains("admin", transport.Commands);
        Assert.Contains("secret", transport.Commands);
    }

    [Fact]
    public async Task ConnectAsync_WithMissingCredentials_ThrowsLoginException()
    {
        var transport = new ScriptedTransport(_ => "", initialOutput: "Username: ");
        await using var session = new DeviceSession(transport, new SessionOptions());

        await Assert.ThrowsAsync<LoginException>(() => session.ConnectAsync());
    }

    [Fact]
    public async Task SendCommandAsync_BeforeConnect_Throws()
    {
        var transport = new ScriptedTransport(_ => "");
        await using var session = new DeviceSession(transport, new SessionOptions());

        await Assert.ThrowsAsync<DeviceSessionException>(() => session.SendCommandAsync("show version"));
    }

    [Theory]
    [InlineData("rommon 1 >")]
    [InlineData("rommon 2 >")]
    [InlineData("rommon >")]
    public async Task ConnectAsync_DetectsRommonMode(string prompt)
    {
        var transport = new ScriptedTransport(_ => $"{prompt}\r\n", initialOutput: $"{prompt}\r\n");
        await using var session = new DeviceSession(transport, new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal()
        });

        await session.ConnectAsync();

        Assert.Equal(ExecMode.Rommon, session.Mode);
        Assert.Equal(prompt, session.CurrentPrompt);
    }
}