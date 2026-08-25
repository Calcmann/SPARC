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
}
