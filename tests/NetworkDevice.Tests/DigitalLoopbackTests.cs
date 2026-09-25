using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetworkDevice.Core.Diagnostics;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class DigitalLoopbackTests
{
    [Fact]
    public async Task LoopbackService_ReflectsUdpPacketsAccurately()
    {
        using var service = new DigitalLoopbackService();
        int testPort = 15888; // Porta efêmera para teste
        service.Start(testPort, IPAddress.Loopback);

        Assert.True(service.IsRunning);

        using var client = new UdpClient();
        client.Client.ReceiveTimeout = 3000;
        var endpoint = new IPEndPoint(IPAddress.Loopback, testPort);

        byte[] payload = Encoding.UTF8.GetBytes("PING_VIAVI_JDSU_SPARC_TEST_FRAME");
        await client.SendAsync(payload, payload.Length, endpoint);

        var rcvResult = await client.ReceiveAsync();

        Assert.Equal(payload, rcvResult.Buffer);

        await service.StopAsync();
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task LoopbackService_MultiWorker_ReflectsMultiplePacketsConcurrently()
    {
        using var service = new DigitalLoopbackService();
        int testPort = 15889;
        // Inicia com 4 workers e buffer de 2MB
        service.Start(testPort, IPAddress.Loopback, workerCount: 4, bufferSizeBytes: 2 * 1024 * 1024);

        Assert.True(service.IsRunning);

        using var client = new UdpClient();
        client.Client.ReceiveTimeout = 3000;
        var endpoint = new IPEndPoint(IPAddress.Loopback, testPort);

        for (int i = 0; i < 5; i++)
        {
            byte[] payload = Encoding.UTF8.GetBytes($"PACKET_BURST_{i}");
            await client.SendAsync(payload, payload.Length, endpoint);
            var rcv = await client.ReceiveAsync();
            Assert.Equal(payload, rcv.Buffer);
        }

        await service.StopAsync();
        Assert.False(service.IsRunning);
    }
}
