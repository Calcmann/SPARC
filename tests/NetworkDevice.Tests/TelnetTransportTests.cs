using System.Net;
using System.Net.Sockets;
using System.Text;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Telnet;

namespace NetworkDevice.Tests;

/// <summary>
/// Transporte Telnet: negociação IAC recusada (WONT/DONT) e fluxo limpo p/ DeviceSession.
/// </summary>
public sealed class TelnetTransportTests
{
    [Fact]
    public async Task ReadAsync_StripsIac_AndAnswersWont()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        byte[]? serverReceived = null;
        var serverTask = Task.Run(async () =>
        {
            using var server = await listener.AcceptTcpClientAsync();
            var stream = server.GetStream();
            // Servidor pede ECHO (DO 1) e envia prompt
            await stream.WriteAsync(new byte[] { 255, 253, 1 });
            await stream.WriteAsync(Encoding.ASCII.GetBytes("Router#"));
            await Task.Delay(300);
            // Lê a resposta de negociação do cliente
            var buf = new byte[64];
            using var cts = new CancellationTokenSource(2000);
            try
            {
                var n = await stream.ReadAsync(buf, cts.Token);
                serverReceived = buf[..n].ToArray();
            }
            catch { }
        });

        await using var transport = new TcpTelnetTransport("127.0.0.1", port);
        await transport.OpenAsync();

        var mem = new byte[256];
        var sb = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!sb.ToString().Contains("Router#"))
        {
            var n = await transport.ReadAsync(mem, cts.Token);
            if (n > 0) sb.Append(Encoding.ASCII.GetString(mem, 0, n));
        }

        await serverTask;

        // Fluxo entregue sem bytes IAC e resposta WONT 1 enviada ao servidor
        Assert.DoesNotContain(((char)255).ToString(), sb.ToString());
        Assert.NotNull(serverReceived);
        Assert.Equal(new byte[] { 255, 252, 1 }, serverReceived);
    }

    [Fact]
    public async Task DeviceSession_OverTelnet_DetectsPrivilegedPrompt()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var server = await listener.AcceptTcpClientAsync();
            var stream = server.GetStream();
            var buf = new byte[256];
            // Lê o wake-up inicial do ConnectAsync e responde o prompt
            await stream.ReadAsync(buf);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\nSW-01#"));
            await Task.Delay(5000);
        });

        await using var transport = new TcpTelnetTransport("127.0.0.1", port);
        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();

        Assert.Equal(ExecMode.PrivilegedExec, session.Mode);
        Assert.Equal("SW-01#", session.CurrentPrompt);
    }
}
