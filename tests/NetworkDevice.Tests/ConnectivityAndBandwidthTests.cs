using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Provisioning;
using Xunit;

namespace NetworkDevice.Tests;

public class ConnectivityAndBandwidthTests
{
    [Fact]
    public async Task ConnectivityService_TestPing_Localhost_Succeeds()
    {
        var logs = new List<string>();
        var service = new ConnectivityService(msg =>
        {
            logs.Add(msg);
            return Task.CompletedTask;
        });

        // Ping em 127.0.0.1 (Loopback local - seguro e rápido para teste unitário)
        var result = await service.TestPingAsync("127.0.0.1", count: 2, timeoutMs: 1000);

        Assert.NotNull(result);
        Assert.Equal("127.0.0.1", result.Target);
        Assert.Equal(2, result.PacketsSent);
        Assert.True(result.PacketsReceived >= 1);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task ConnectivityService_ThrowsOnEmptyHost()
    {
        var service = new ConnectivityService();
        await Assert.ThrowsAsync<ArgumentException>(() => service.TestPingAsync(""));
    }

    [Fact]
    public void BandwidthTestService_InstantiatesProperly()
    {
        var logs = new List<string>();
        var service = new BandwidthTestService(msg =>
        {
            logs.Add(msg);
            return Task.CompletedTask;
        });

        Assert.NotNull(service);
    }

    [Fact]
    public async Task AndroidHostNetworkGuidance_ReturnsAppropriateGuidance()
    {
        var androidService = new AndroidHostNetworkGuidance();
        var adapters = androidService.GetAvailableAdapters();
        Assert.NotEmpty(adapters);

        var (success, msg) = await androidService.SetStaticIpAsync("eth0", "10.0.0.2", "255.255.255.248", "10.0.0.1");
        Assert.True(success);
        Assert.Contains("Android Guidance", msg);
        Assert.Contains("10.0.0.2", msg);
    }

    [Fact]
    public void HostNetworkManager_GetEthernetAdapters_ReturnsList()
    {
        var adapters = HostNetworkManager.GetEthernetAdapters();
        Assert.NotNull(adapters);
        Assert.NotEmpty(adapters);
    }

    [Fact]
    public void HostNetworkManager_GetDedicatedRouterEthernetInterface_ExecutesSafely()
    {
        var result = HostNetworkManager.GetDedicatedRouterEthernetInterface();
        if (result != null)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Name));
            Assert.True(result.IsPhysicalEthernet);
            Assert.DoesNotContain("Wi-Fi", result.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Wireless", result.Description, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task BandwidthTestService_UnboundSourceIp_AbortsWithIsolationError()
    {
        var logs = new List<string>();
        var service = new BandwidthTestService(msg =>
        {
            logs.Add(msg);
            return Task.CompletedTask;
        });

        // IP 198.51.100.254 (TEST-NET-2, não vinculado a nenhuma interface local)
        var result = await service.RunNativeHttpSpeedTestAsync(
            testPayloadMegaBytes: 1,
            sourceIpAddress: "198.51.100.254");

        Assert.False(result.IsSuccess);
        Assert.Contains("ISOLAMENTO", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logs, l => l.Contains("ERRO ISOLAMENTO", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class AvaliacaoBandaTests
{
    [Fact]
    public void SemNominal_SemJulgamento()
    {
        Assert.Null(BandwidthTestService.AvaliarBanda(null, 40.0));
        Assert.Null(BandwidthTestService.AvaliarBanda(0, 40.0));
    }

    [Theory]
    [InlineData(50.0, 50.0, true)]
    [InlineData(50.0, 46.0, true)]
    [InlineData(50.0, 45.99, false)]
    [InlineData(50.0, 30.0, false)]
    [InlineData(50.0, 0.0, false)]
    public void Limite92(double nominal, double medido, bool esperado)
    {
        var av = BandwidthTestService.AvaliarBanda(nominal, medido);
        Assert.NotNull(av);
        Assert.Equal(esperado, av.Aprovado);
        Assert.Equal(nominal, av.NominalMbps);
    }

    [Fact]
    public void PercentualEMinimo()
    {
        var av = BandwidthTestService.AvaliarBanda(50.0, 46.0)!;
        Assert.Equal(92.0, av.Percentual);
        Assert.Equal(46.0, av.MinimoMbps);
        Assert.Contains("APROVADO", av.Veredito);
    }
}
