using System.Net;
using System.Text;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Protocols.Tftp;
using Xunit;

namespace NetworkDevice.Tests;

public class TftpAndHostNetworkTests
{
    [Fact]
    public void CalculateHostLanIp_Computes_SecondUsableIp_Correctly()
    {
        // 189.16.20.80/29 -> Rede .80, Router (1º útil) .81, Host PC (2º útil) .82
        var hostIp = IpCalculator.CalculateHostLanIp("189.016.020.080", 29);
        Assert.Equal("189.16.20.82", hostIp);
    }

    [Fact]
    public void CalculateHostLanIp_Computes_Slash24_Correctly()
    {
        // 192.168.1.0/24 -> Rede .0, Router .1, Host PC .2
        var hostIp = IpCalculator.CalculateHostLanIp("192.168.1.0", 24);
        Assert.Equal("192.168.1.2", hostIp);
    }

    [Fact]
    public void GetEthernetAdapters_Returns_List()
    {
        var adapters = HostNetworkManager.GetEthernetAdapters();
        Assert.NotNull(adapters);
        Assert.NotEmpty(adapters);
    }

    [Fact]
    public void SerialPorts_Available_Returns_NonNull_List()
    {
        var ports = NetworkDevice.Protocols.Serial.SerialPorts.Available();
        Assert.NotNull(ports);
        // Garante que todas as portas retornadas comecem com "COM"
        foreach (var port in ports)
        {
            Assert.StartsWith("COM", port);
        }
    }

    [Fact]
    public async Task EmbeddedTftpServer_Starts_And_Stops_Cleanly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tftp_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var testFile = Path.Combine(tempDir, "test.bin");
            await File.WriteAllBytesAsync(testFile, Encoding.ASCII.GetBytes("Cisco IOS Test Firmware Content"));

            await using var server = new EmbeddedTftpServer(tempDir, port: 16969);
            server.Start();
            Assert.True(server.IsRunning);

            await server.StopAsync();
            Assert.False(server.IsRunning);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EmbeddedTftpServer_FuzzyMatchAndFallback_DeliversFirmware()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tftp_fuzzy_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var realFirmwareName = "FGT_40F-v7.2.11.M-build1740-FORTINET.out";
            var firmwareContent = "FortiGate 40F Firmware Content Header Test OK";
            var testFile = Path.Combine(tempDir, realFirmwareName);
            await File.WriteAllBytesAsync(testFile, Encoding.ASCII.GetBytes(firmwareContent));

            await using var server = new EmbeddedTftpServer(tempDir, port: 16970);
            server.Start();

            // Simula cliente TFTP da BIOS pedindo 'GT_40F-v7.2.11.M-build1740-FORTINET.out' (sem a primeira letra 'F')
            using var client = new System.Net.Sockets.UdpClient();
            var rrq = new List<byte> { 0, 1 };
            rrq.AddRange(Encoding.ASCII.GetBytes("GT_40F-v7.2.11.M-build1740-FORTINET.out"));
            rrq.Add(0);
            rrq.AddRange(Encoding.ASCII.GetBytes("octet"));
            rrq.Add(0);

            var rrqBytes = rrq.ToArray();
            await client.SendAsync(rrqBytes, rrqBytes.Length, new IPEndPoint(IPAddress.Loopback, 16970));

            var receiveTask = client.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(2000));
            Assert.True(completed == receiveTask, "Timeout aguardando bloco DATA do TFTP");

            var result = await receiveTask;
            Assert.True(result.Buffer.Length >= 4);
            // Opcode 3 = DATA
            Assert.Equal(0, result.Buffer[0]);
            Assert.Equal(3, result.Buffer[1]);
            // Bloco 1
            Assert.Equal(0, result.Buffer[2]);
            Assert.Equal(1, result.Buffer[3]);

            var payload = Encoding.ASCII.GetString(result.Buffer, 4, result.Buffer.Length - 4);
            Assert.StartsWith("FortiGate 40F", payload);

            await server.StopAsync();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void UdpPortDiagnostics_BuildFriendlyConflictMessage_TftpdExternal_ReturnsInstructiveAction()
    {
        var conflict = new UdpPortConflict(69, 1234, "tftpd64", @"C:\Program Files\Tftpd64\tftpd64.exe", false);
        var msg = UdpPortDiagnostics.BuildFriendlyConflictMessage(69, conflict);

        Assert.Contains("tftpd64", msg);
        Assert.Contains("COMO RESOLVER", msg);
        Assert.Contains("servidor TFTP integrado", msg);
    }

    [Fact]
    public void UdpPortDiagnostics_BuildFriendlyConflictMessage_OtherProcess_ReturnsTaskMgrInstructions()
    {
        var conflict = new UdpPortConflict(69, 5678, "app-servico", @"C:\Tools\app-servico.exe", false);
        var msg = UdpPortDiagnostics.BuildFriendlyConflictMessage(69, conflict);

        Assert.Contains("app-servico", msg);
        Assert.Contains("Gerenciador de Tarefas", msg);
        Assert.Contains("UDP 69", msg);
    }

    [Fact]
    public void UdpPortDiagnostics_BuildFriendlyConflictMessage_CurrentProcess_ReturnsRestartInstruction()
    {
        var conflict = new UdpPortConflict(69, Environment.ProcessId, "SPARC", null, true);
        var msg = UdpPortDiagnostics.BuildFriendlyConflictMessage(69, conflict);

        Assert.Contains("tarefa anterior", msg);
        Assert.Contains("SPARC", msg);
    }

    [Fact]
    public void UdpPortDiagnostics_BuildFriendlyConflictMessage_NullConflict_ReturnsGeneralTftpdAdvice()
    {
        var msg = UdpPortDiagnostics.BuildFriendlyConflictMessage(69, null);

        Assert.Contains("Tftpd32, Tftpd64", msg);
        Assert.Contains("COMO RESOLVER", msg);
    }

    [Fact]
    public void EmbeddedTftpServer_WhenPortAlreadyInUse_ThrowsDescriptiveInvalidOperationException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tftp_conflict_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var port = 16975;
            using var blockerSocket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram,
                System.Net.Sockets.ProtocolType.Udp);
            blockerSocket.ExclusiveAddressUse = true;
            blockerSocket.Bind(new IPEndPoint(IPAddress.Any, port));

            using var server = new EmbeddedTftpServer(tempDir, port: port);

            var ex = Assert.Throws<InvalidOperationException>(() => server.Start(autoCloseKnownTftpConflicts: false));
            Assert.Contains(port.ToString(), ex.Message);
            Assert.Contains("TFTP", ex.Message);
            Assert.NotNull(ex.InnerException);
            Assert.IsType<System.Net.Sockets.SocketException>(ex.InnerException);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
