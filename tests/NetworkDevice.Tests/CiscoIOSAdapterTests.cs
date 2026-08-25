using NetworkDevice.Cisco;
using NetworkDevice.Core.Session;
using NetworkDevice.Tests.TestDoubles;

namespace NetworkDevice.Tests;

public class CiscoIOSAdapterTests
{
    private const string ShowVersion =
        """
        Cisco IOS Software, C2960X Software (C2960X-UNIVERSALK9-M), Version 15.2(7)E6, RELEASE SOFTWARE (fc3)
        cisco WS-C2960X-48FPS-L (APM86XXX) processor with 512000K bytes of memory.
        Processor board ID FOC1234ABCD
        System model number            : WS-C2960X-48FPS-L
        System serial number           : FOC1234ABCD
        SW-DEPTO-01#
        """;

    private static async Task<DeviceSession> ConnectAsync(ScriptedTransport transport, string? enableSecret = "admin123")
    {
        var session = new DeviceSession(transport, CiscoIOSAdapter.CreateSessionOptions(enableSecret));
        await session.ConnectAsync();
        return session;
    }

    [Fact]
    public async Task EnterPrivilegedExecAsync_WithEnableSecret_EntersPrivilegedMode()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "enable" => "Password:\r\n",
                "admin123" => "SW-DEPTO-01#\r\n",
                _ => "\r\n"
            },
            initialOutput: "SW-DEPTO-01>\r\n");

        await using var session = await ConnectAsync(transport);
        var adapter = new CiscoIOSAdapter("admin123");

        await adapter.EnterPrivilegedExecAsync(session);

        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        Assert.Contains("enable", transport.Commands);
        Assert.Contains("admin123", transport.Commands);
    }

    [Fact]
    public async Task EnterPrivilegedExecAsync_WithoutSecret_Throws()
    {
        var transport = new ScriptedTransport(_ => "SW-DEPTO-01>\r\n", initialOutput: "SW-DEPTO-01>\r\n");
        var options = CiscoIOSAdapter.CreateSessionOptions(null);
        await using var session = new DeviceSession(transport, options);
        await session.ConnectAsync();

        var adapter = new CiscoIOSAdapter();

        await Assert.ThrowsAsync<DeviceSessionException>(() => adapter.EnterPrivilegedExecAsync(session));
    }

    [Fact]
    public async Task IdentifyAsync_ParsesModelVersionAndSerial()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "enable" => "Password:\r\n",
                "admin123" => "SW-DEPTO-01#\r\n",
                "terminal length 0" => "SW-DEPTO-01#\r\n",
                "terminal width 0" => "SW-DEPTO-01#\r\n",
                "show version" => ShowVersion,
                _ => "\r\n"
            },
            initialOutput: "SW-DEPTO-01>\r\n");

        await using var session = await ConnectAsync(transport);
        var adapter = new CiscoIOSAdapter("admin123");

        var info = await adapter.IdentifyAsync(session);

        Assert.Equal("Cisco", info.Vendor);
        Assert.Equal("WS-C2960X-48FPS-L", info.Model);
        Assert.Equal("15.2(7)E6", info.OsVersion);
        Assert.Equal("FOC1234ABCD", info.SerialNumber);
        Assert.Equal("SW-DEPTO-01", info.Hostname);
    }

    [Fact]
    public async Task GetRunningConfigAsync_StripsEchoAndPrompt()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "enable" => "Password:\r\n",
                "admin123" => "SW-DEPTO-01#\r\n",
                "terminal length 0" => "SW-DEPTO-01#\r\n",
                "terminal width 0" => "SW-DEPTO-01#\r\n",
                "show running-config" =>
                    "show running-config\r\n" +
                    "Building configuration...\r\n" +
                    "!\r\n" +
                    "hostname SW-DEPTO-01\r\n" +
                    "!\r\n" +
                    "interface GigabitEthernet0/1\r\n" +
                    " switchport mode access\r\n" +
                    "!\r\n" +
                    "end\r\n" +
                    "SW-DEPTO-01#\r\n",
                _ => "\r\n"
            },
            initialOutput: "SW-DEPTO-01>\r\n");

        await using var session = await ConnectAsync(transport);
        var adapter = new CiscoIOSAdapter("admin123");

        var config = await adapter.GetRunningConfigAsync(session);

        Assert.Contains("hostname SW-DEPTO-01", config);
        Assert.Contains("switchport mode access", config);
        Assert.DoesNotContain("show running-config", config);
        Assert.DoesNotContain("SW-DEPTO-01#", config);
    }

    [Fact]
    public async Task SaveConfigAsync_IssuesWriteMemory()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "enable" => "Password:\r\n",
                "admin123" => "SW-DEPTO-01#\r\n",
                "write memory" => "Building configuration...\r\n[OK]\r\nSW-DEPTO-01#\r\n",
                _ => "\r\n"
            },
            initialOutput: "SW-DEPTO-01>\r\n");

        await using var session = await ConnectAsync(transport);
        var adapter = new CiscoIOSAdapter("admin123");

        await adapter.SaveConfigAsync(session);

        Assert.Contains("write memory", transport.Commands);
    }
}