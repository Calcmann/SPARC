using System.Text.RegularExpressions;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Session;
using NetworkDevice.Tests.TestDoubles;

namespace NetworkDevice.Tests;

public sealed class CiscoIOSBootDetectionTests
{
    [Fact]
    public async Task AguardarBootCiscoIOSAsync_ComZerar_HandlesDialogAndTerminatesAutoinstall()
    {
        var autoinstallTerminated = false;
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd.Equals("no", StringComparison.OrdinalIgnoreCase))
                    return "Would you like to terminate autoinstall? [yes/no]: ";
                if (cmd.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    autoinstallTerminated = true;
                    return "Press RETURN to get started!\r\n";
                }
                if (cmd == string.Empty)
                {
                    return autoinstallTerminated ? "Router>\r\n" : "Would you like to terminate autoinstall? [yes/no]: ";
                }
                return autoinstallTerminated ? "Router>\r\n" : "";
            },
            initialOutput: "System Bootstrap, Version 15.4\r\n" +
                           "Self decompressing the image : #################### [OK]\r\n" +
                           "Smart Init is enabled\r\n" +
                           "--- System Configuration Dialog ---\r\n" +
                           "Would you like to enter the initial configuration dialog? [yes/no]: ");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectRawAsync();

        var logs = new List<string>();
        var upgrader = new CiscoIOSUpgrader(msg => { logs.Add(msg); return Task.CompletedTask; });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await upgrader.AguardarBootCiscoIOSAsync(session, TimeSpan.FromSeconds(10), cts.Token);

        // Verifica que o autoinstall foi terminado com "yes" e o diálogo inicial com "no"
        Assert.Contains(transport.Commands, c => c.Equals("no", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(transport.Commands, c => c.Equals("yes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, l => l.Contains("autoinstall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AguardarBootCiscoIOSAsync_SemZerar_RecoversPromptBehindSyslogFlood()
    {
        var enterCount = 0;
        var transport = new ScriptedTransport(
            cmd =>
            {
                if (cmd == string.Empty)
                {
                    enterCount++;
                    // Após a descompressão e rajada de syslogs, o Enter redesenha o prompt do equipamento
                    return "S_FNS>\r\n";
                }
                if (cmd == "enable")
                {
                    return "S_FNS#\r\n";
                }
                if (cmd.StartsWith("terminal", StringComparison.OrdinalIgnoreCase))
                {
                    return "S_FNS#\r\n";
                }
                return "S_FNS>\r\n";
            },
            initialOutput: "System Bootstrap, Version 15.4(3)M3\r\n" +
                           "Total memory size = 512 MB\r\n" +
                           "Self decompressing the image : #################### [OK]\r\n" +
                           "Cisco IOS Software, C2900 Software (C2900-UNIVERSALK9-M)\r\n" +
                           "*Oct  7 14:22:15.112: %SYS-5-CONFIG_I: Configured from memory by console\r\n" +
                           "*Oct  7 14:22:16.001: %LINK-3-UPDOWN: Interface GigabitEthernet0/0, changed state to up\r\n" +
                           "*Oct  7 14:22:17.001: %LINEPROTO-5-UPDOWN: Line protocol on Interface GigabitEthernet0/0, changed state to up\r\n" +
                           "*Oct  7 14:22:18.001: %LINK-3-UPDOWN: Interface GigabitEthernet0/1, changed state to up\r\n" +
                           "*Oct  7 14:22:19.001: %LINEPROTO-5-UPDOWN: Line protocol on Interface GigabitEthernet0/1, changed state to up\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectRawAsync();

        var logs = new List<string>();
        var upgrader = new CiscoIOSUpgrader(msg => { logs.Add(msg); return Task.CompletedTask; });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await upgrader.AguardarBootCiscoIOSAsync(
            session,
            TimeSpan.FromSeconds(10),
            cts.Token,
            enableSecret: "PRO1AN",
            candidatePasswords: new[] { "PRO1AN" });

        await upgrader.EstabilizarCliPosBootAsync(
            session,
            enableSecret: "PRO1AN",
            candidatePasswords: new[] { "PRO1AN" },
            cts.Token);

        // Garante que enviou Enter para redesenhar o prompt sobreposto por syslogs
        Assert.True(enterCount >= 1, "Deveria ter enviado pelo menos um Enter de keepalive para recuperar o prompt atrás dos syslogs.");
        // Garante que elevou para modo privilegiado (enable)
        Assert.Contains(transport.Commands, c => c.Equals("enable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(transport.Commands, c => c.StartsWith("terminal length 0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AguardarBootCiscoIOSAsync_SemZerar_HandlesConsolePasswordAndElevates()
    {
        var transport = new ScriptedTransport(
            cmd => cmd switch
            {
                "PRO1AN" => "S_FNS>\r\n",
                "enable" => "Password: ",
                "" => "S_FNS#\r\n",
                _ => "S_FNS#\r\n"
            },
            initialOutput: "System Bootstrap, Version 15.4\r\n" +
                           "Self decompressing the image : #################### [OK]\r\n" +
                           "Cisco IOS Software, C2900 Software\r\n" +
                           "*Oct  7 14:22:15.112: %SYS-5-CONFIG_I: Configured from memory by console\r\n" +
                           "User Access Verification\r\n" +
                           "Password: ");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectRawAsync();

        var logs = new List<string>();
        var upgrader = new CiscoIOSUpgrader(msg => { logs.Add(msg); return Task.CompletedTask; });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await upgrader.AguardarBootCiscoIOSAsync(
            session,
            TimeSpan.FromSeconds(10),
            cts.Token,
            enableSecret: "PRO1AN",
            candidatePasswords: new[] { "PRO1AN" });

        // Confirma que enviou a senha PRO1AN no login do console
        Assert.Contains(transport.Commands, c => c == "PRO1AN");
    }
}
