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
}
