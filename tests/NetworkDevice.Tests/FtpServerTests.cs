using System.Net;
using System.Net.Sockets;
using System.Text;
using NetworkDevice.Protocols.Ftp;

namespace NetworkDevice.Tests;

/// <summary>
/// Servidor FTP embarcado de firmware (controle :2121 + PASV/PORT, sem root).
/// </summary>
public sealed class FtpServerTests : IAsyncDisposable
{
    private readonly string _dir;
    private readonly string _fileName = "msr930-cmw710-r9999.ipe";
    private readonly byte[] _payload;

    public FtpServerTests()
    {
        _payload = new byte[200_000];
        new Random(7).NextBytes(_payload);
        _dir = Path.Combine(Path.GetTempPath(), $"sparc-ftp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, _fileName), _payload);
    }

    public async ValueTask DisposeAsync()
    {
        try { Directory.Delete(_dir, true); } catch { }
        await ValueTask.CompletedTask;
    }

    private static async Task<string> ReadReplyAsync(StreamReader reader, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var line = await reader.ReadLineAsync(linked.Token);
        return line ?? string.Empty;
    }

    private static int ParsePasvPort(string reply227)
    {
        var s = reply227.Split('(', ')')[1];
        var p = s.Split(',');
        return int.Parse(p[4]) * 256 + int.Parse(p[5]);
    }

    [Fact]
    public async Task Retr_PassiveMode_DeliversExactBytes()
    {
        await using var server = new EmbeddedFtpServer(_dir, "127.0.0.1", preferredPort: 21212);
        server.Start();

        using var ctrl = new TcpClient();
        await ctrl.ConnectAsync(IPAddress.Loopback, server.ActualPort);
        using var stream = ctrl.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        Assert.StartsWith("220", await ReadReplyAsync(reader, cts.Token));
        await writer.WriteLineAsync("USER sparc");
        Assert.StartsWith("331", await ReadReplyAsync(reader, cts.Token));
        await writer.WriteLineAsync("PASS sparc");
        Assert.StartsWith("230", await ReadReplyAsync(reader, cts.Token));
        await writer.WriteLineAsync("TYPE I");
        Assert.StartsWith("200", await ReadReplyAsync(reader, cts.Token));
        await writer.WriteLineAsync("PASV");
        var pasvPort = ParsePasvPort(await ReadReplyAsync(reader, cts.Token));

        using var data = new TcpClient();
        await data.ConnectAsync(IPAddress.Loopback, pasvPort);
        await writer.WriteLineAsync($"RETR {_fileName}");
        Assert.StartsWith("150", await ReadReplyAsync(reader, cts.Token));

        var received = new MemoryStream();
        await data.GetStream().CopyToAsync(received, cts.Token);
        Assert.StartsWith("226", await ReadReplyAsync(reader, cts.Token));
        Assert.Equal(_payload, received.ToArray());

        await writer.WriteLineAsync("QUIT");
    }

    [Fact]
    public async Task Retr_ActiveMode_ServerConnectsBack()
    {
        await using var server = new EmbeddedFtpServer(_dir, "127.0.0.1", preferredPort: 21213);
        server.Start();

        using var ctrl = new TcpClient();
        await ctrl.ConnectAsync(IPAddress.Loopback, server.ActualPort);
        using var stream = ctrl.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        Assert.StartsWith("220", await ReadReplyAsync(reader, cts.Token));
        await writer.WriteLineAsync("USER a");
        await ReadReplyAsync(reader, cts.Token);
        await writer.WriteLineAsync("PASS b");
        await ReadReplyAsync(reader, cts.Token);

        using var dataListener = new TcpListener(IPAddress.Loopback, 0);
        dataListener.Start();
        var dp = (IPEndPoint)dataListener.LocalEndpoint;
        var acceptTask = dataListener.AcceptTcpClientAsync(cts.Token);

        await writer.WriteLineAsync($"PORT 127,0,0,1,{dp.Port / 256},{dp.Port % 256}");
        Assert.StartsWith("200", await ReadReplyAsync(reader, cts.Token));
        await writer.WriteLineAsync($"RETR {_fileName}");
        Assert.StartsWith("150", await ReadReplyAsync(reader, cts.Token));

        using var data = await acceptTask;
        var received = new MemoryStream();
        await data.GetStream().CopyToAsync(received, cts.Token);
        Assert.Equal(_payload, received.ToArray());
        Assert.StartsWith("226", await ReadReplyAsync(reader, cts.Token));
    }

    [Fact]
    public async Task Auth_WrongPassword_DeniesRetr()
    {
        await using var server = new EmbeddedFtpServer(_dir, "127.0.0.1", preferredPort: 21214,
            username: "tec", password: "segredo");
        server.Start();

        using var ctrl = new TcpClient();
        await ctrl.ConnectAsync(IPAddress.Loopback, server.ActualPort);
        using var stream = ctrl.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await ReadReplyAsync(reader, cts.Token);
        await writer.WriteLineAsync("USER tec");
        await ReadReplyAsync(reader, cts.Token);
        await writer.WriteLineAsync("PASS errada");
        Assert.StartsWith("530", await ReadReplyAsync(reader, cts.Token));
        await writer.WriteLineAsync($"RETR {_fileName}");
        Assert.StartsWith("530", await ReadReplyAsync(reader, cts.Token));
    }

    [Fact]
    public async Task Retr_MissingFile_Returns550()
    {
        await using var server = new EmbeddedFtpServer(_dir, "127.0.0.1", preferredPort: 21215);
        server.Start();

        using var ctrl = new TcpClient();
        await ctrl.ConnectAsync(IPAddress.Loopback, server.ActualPort);
        using var stream = ctrl.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await ReadReplyAsync(reader, cts.Token);
        await writer.WriteLineAsync("RETR inexistente.ipe");
        Assert.StartsWith("550", await ReadReplyAsync(reader, cts.Token));
    }
}
