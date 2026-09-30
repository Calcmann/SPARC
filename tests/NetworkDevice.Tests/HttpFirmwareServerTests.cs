using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using NetworkDevice.Protocols.Http;

namespace NetworkDevice.Tests;

/// <summary>
/// Servidor HTTP embarcado de firmware (porta alta — alternativa ao TFTP :69 no Android).
/// </summary>
public sealed class HttpFirmwareServerTests : IAsyncDisposable
{
    private readonly string _tempFile;
    private readonly byte[] _payload;

    public HttpFirmwareServerTests()
    {
        _payload = new byte[300_000];
        new Random(42).NextBytes(_payload);
        _tempFile = Path.Combine(Path.GetTempPath(), $"sparc-fw-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(_tempFile, _payload);
    }

    public async ValueTask DisposeAsync()
    {
        try { File.Delete(_tempFile); } catch { }
        await ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Get_ServesFullFile_WithContentLength()
    {
        await using var server = new EmbeddedHttpFileServer(_tempFile, preferredPort: 18080);
        server.Start();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var url = server.BuildUrl("127.0.0.1");
        Assert.Contains($":{server.ActualPort}/", url);

        var bytes = await http.GetByteArrayAsync(url);
        Assert.Equal(_payload, bytes);
    }

    [Fact]
    public async Task Head_ReturnsLength_WithoutBody()
    {
        await using var server = new EmbeddedHttpFileServer(_tempFile, preferredPort: 18081);
        server.Start();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, server.BuildUrl("127.0.0.1")));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(_payload.Length, resp.Content.Headers.ContentLength);
        Assert.Equal(0, (await resp.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task Get_UnknownFile_Returns404()
    {
        await using var server = new EmbeddedHttpFileServer(_tempFile, preferredPort: 18082);
        server.Start();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var resp = await http.GetAsync($"http://127.0.0.1:{server.ActualPort}/outro.bin");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Get_Range_ReturnsPartialContent()
    {
        await using var server = new EmbeddedHttpFileServer(_tempFile, preferredPort: 18083);
        server.Start();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var req = new HttpRequestMessage(HttpMethod.Get, server.BuildUrl("127.0.0.1"));
        req.Headers.Range = new RangeHeaderValue(1000, null);
        var resp = await http.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, resp.StatusCode);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(_payload.Skip(1000).ToArray(), bytes);
    }

    [Fact]
    public async Task Start_OccupiedPort_FallsBackToNext()
    {
        await using var first = new EmbeddedHttpFileServer(_tempFile, preferredPort: 18084);
        first.Start();

        await using var second = new EmbeddedHttpFileServer(_tempFile, preferredPort: 18084);
        second.Start();

        Assert.NotEqual(first.ActualPort, second.ActualPort);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var bytes = await http.GetByteArrayAsync(second.BuildUrl("127.0.0.1"));
        Assert.Equal(_payload, bytes);
    }
}
