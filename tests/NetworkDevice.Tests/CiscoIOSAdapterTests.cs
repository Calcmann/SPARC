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
    public async Task EnterPrivilegedExecAsync_WithoutPassword_EntersDirectly()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "enable" => "SW-DEPTO-01#\r\n",
                "show privilege" => "Current privilege level is 15\r\nSW-DEPTO-01#\r\n",
                _ => "SW-DEPTO-01#\r\n"
            },
            initialOutput: "SW-DEPTO-01>\r\n");

        await using var session = await ConnectAsync(transport);
        var adapter = new CiscoIOSAdapter();

        await adapter.EnterPrivilegedExecAsync(session);

        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        Assert.Contains("enable", transport.Commands);
    }

    [Fact]
    public async Task EnterPrivilegedExecAsync_WithPRO1AN_CandidatePassword_ElevatesSuccessfully()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "enable" => "Password:\r\n",
                "PRO1AN" => "S_FNS#\r\n",
                _ => "\r\n"
            },
            initialOutput: "S_FNS>\r\n");

        await using var session = await ConnectAsync(transport, enableSecret: null);
        var adapter = new CiscoIOSAdapter(candidatePasswords: new[] { "PRO1AN" });

        await adapter.EnterPrivilegedExecAsync(session);

        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        Assert.Contains("enable", transport.Commands);
        Assert.Contains("PRO1AN", transport.Commands);
        Assert.Equal("PRO1AN", adapter.ResolvedEnableSecret);
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

    [Theory]
    [InlineData("Router1900", true)]
    [InlineData("Router1900", false)]
    [InlineData("Router2900", true)]
    [InlineData("Router2900", false)]
    [InlineData("C921", true)]
    [InlineData("C921", false)]
    [InlineData("C841", true)]
    [InlineData("C841", false)]
    public async Task EnterPrivilegedExecAsync_AllCiscoSeries_SupportsPasswordAndNoPassword(string hostname, bool requiresPassword)
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "enable" when requiresPassword => "Password:\r\n",
                "enable" when !requiresPassword => $"{hostname}#\r\n",
                "PRO1AN" => $"{hostname}#\r\n",
                "show privilege" => $"Current privilege level is 15\r\n{hostname}#\r\n",
                _ => "\r\n"
            },
            initialOutput: $"{hostname}>\r\n");

        await using var session = await ConnectAsync(transport, enableSecret: null);
        var adapter = new CiscoIOSAdapter();

        await adapter.EnterPrivilegedExecAsync(session);

        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        Assert.Contains("enable", transport.Commands);
        if (requiresPassword)
        {
            Assert.Contains("PRO1AN", transport.Commands);
            Assert.Equal("PRO1AN", adapter.ResolvedEnableSecret);
        }
    }

    [Fact]
    public async Task EnsurePrivilegedExecAsync_InCiscoSaipConfigurator_ElevatesAutomatically()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "enable" => "Password:\r\n",
                "PRO1AN" => "Cisco-Core#\r\n",
                "show privilege" => "Current privilege level is 15\r\nCisco-Core#\r\n",
                _ => "\r\n"
            },
            initialOutput: "Cisco-Core>\r\n");

        await using var session = await ConnectAsync(transport, enableSecret: null);
        await CiscoSaipConfigurator.EnsurePrivilegedExecAsync(session);

        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        Assert.Contains("enable", transport.Commands);
        Assert.Contains("PRO1AN", transport.Commands);
    }

    [Fact]
    public async Task EnforceLanPortConnectedAsync_PortaLanUp_RetornaTrueEAtualizaProgresso()
    {
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("show ip interface brief"))
                {
                    return "Interface              IP-Address      OK? Method Status                Protocol\r\n" +
                           "GigabitEthernet0/0     unassigned      YES unset  administratively down down    \r\n" +
                           "GigabitEthernet0/1     200.250.163.41  YES manual up                    up      \r\n" +
                           "Router#";
                }
                return "Router#";
            },
            initialOutput: "Router#");

        await using var session = await ConnectAsync(transport, enableSecret: null);

        var progressLogs = new List<string>();
        var progressStatus = new List<(int Pct, string Title, string Sub)>();

        var result = await CiscoIOSAdapter.EnforceLanPortConnectedAsync(
            session,
            "GigabitEthernet 0/1",
            requestOperatorAction: null,
            progress: msg => { progressLogs.Add(msg); return Task.CompletedTask; },
            cancellationToken: default,
            onProgress: (pct, title, sub) => progressStatus.Add((pct, title, sub)));

        Assert.True(result);
        Assert.Contains(progressLogs, p => p.Contains("[OK] Link físico confirmado"));
        Assert.Contains(progressStatus, s => s.Title == "Porta LAN Conectada!");
    }

    [Fact]
    public async Task EnforceLanPortConnectedAsync_WanUpLanDown_NotificaTrocaDeCaboEAtualizaProgresso()
    {
        var iteration = 0;
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Contains("show ip interface brief"))
                {
                    iteration++;
                    if (iteration == 1)
                    {
                        // Primeira consulta: cabo ainda na WAN (GE0/0)
                        return "Interface              IP-Address      OK? Method Status                Protocol\r\n" +
                               "GigabitEthernet0/0     200.179.12.94   YES manual up                    up      \r\n" +
                               "GigabitEthernet0/1     unassigned      YES unset  down                  down    \r\n" +
                               "Router#";
                    }
                    else
                    {
                        // Segunda consulta (após operador trocar cabo): LAN (GE0/1) sobe
                        return "Interface              IP-Address      OK? Method Status                Protocol\r\n" +
                               "GigabitEthernet0/0     200.179.12.94   YES manual down                  down    \r\n" +
                               "GigabitEthernet0/1     200.250.163.41  YES manual up                    up      \r\n" +
                               "Router#";
                    }
                }
                return string.Empty;
            },
            initialOutput: "Router#");

        await using var session = await ConnectAsync(transport, enableSecret: null);

        var operatorNotified = false;
        var progressStatus = new List<(int Pct, string Title, string Sub)>();

        var result = await CiscoIOSAdapter.EnforceLanPortConnectedAsync(
            session,
            "GigabitEthernet 0/1",
            requestOperatorAction: (msg, ct) =>
            {
                operatorNotified = true;
                Assert.Contains("CABO DE REDE CONECTADO NA PORTA INCORRETA", msg);
                return Task.CompletedTask;
            },
            progress: _ => Task.CompletedTask,
            cancellationToken: default,
            onProgress: (pct, title, sub) => progressStatus.Add((pct, title, sub)));

        Assert.True(result);
        Assert.True(operatorNotified);
        Assert.Contains(progressStatus, s => s.Title.Contains("Troca de Cabo"));
        Assert.Contains(progressStatus, s => s.Title == "Porta LAN Conectada!");
    }
}