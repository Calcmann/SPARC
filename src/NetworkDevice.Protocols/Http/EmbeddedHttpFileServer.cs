using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NetworkDevice.Protocols.Http;

/// <summary>
/// Servidor HTTP mínimo embarcado para distribuição de firmware ao roteador
/// (<c>copy http://host:porta/arquivo flash:</c> no Cisco IOS, que aceita porta
/// não-padrão — doc oficial Cisco usa :8080 nos exemplos).
/// Existe porque apps Android não-root não podem abrir a porta TFTP 69 (bloqueio de
/// kernel: sem CAP_NET_BIND_SERVICE), mas portas altas (>= 1024) são livres.
/// Suporta GET/HEAD + Range simples (resume), Content-Length e Connection: close.
/// </summary>
public sealed class EmbeddedHttpFileServer : IAsyncDisposable
{
    private readonly string _filePath;
    private readonly string _fileName;
    private readonly long _fileLength;
    private readonly int _preferredPort;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    /// <summary>Porta efetiva (tenta a preferida e as 5 seguintes se ocupada).</summary>
    public int ActualPort { get; private set; }

    public bool IsRunning => _listener is not null;

    /// <summary>URL pronta para o comando do roteador, ex: http://10.0.0.2:8080/c1900.bin</summary>
    public string BuildUrl(string hostIp) => $"http://{hostIp}:{ActualPort}/{_fileName}";

    public event Action<string, long, long>? TransferProgress;
    public event Action<string>? LogMessage;

    public EmbeddedHttpFileServer(string filePath, int preferredPort = 8080)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Caminho do firmware inválido.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Arquivo de firmware não encontrado: {filePath}");
        _filePath = filePath;
        _fileName = Path.GetFileName(filePath);
        _fileLength = new FileInfo(filePath).Length;
        _preferredPort = preferredPort;
    }

    public void Start()
    {
        if (IsRunning)
            return;

        SocketException? lastError = null;
        for (var port = _preferredPort; port < _preferredPort + 6; port++)
        {
            var listener = new TcpListener(IPAddress.Any, port);
            try
            {
                listener.Start(backlog: 4);
                _listener = listener;
                ActualPort = port;
                lastError = null;
                break;
            }
            catch (SocketException ex)
            {
                lastError = ex;
                try { listener.Stop(); } catch { }
            }
        }

        if (_listener is null)
            throw new InvalidOperationException(
                $"Não foi possível abrir o servidor HTTP (portas {_preferredPort}-{_preferredPort + 5}).", lastError);

        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        LogMessage?.Invoke($"[HTTP] Servindo '{_fileName}' ({_fileLength} bytes) na porta {ActualPort}.");
    }

    public async Task StopAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        if (_acceptTask is not null)
        {
            try { await _acceptTask; } catch { }
            _acceptTask = null;
        }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => ServeClientAsync(client, ct), ct);
        }
    }

    private async Task ServeClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                var request = await ReadRequestHeaderAsync(stream, ct);
                if (request is null)
                    return;

                var (method, target, _) = request.Value;
                var path = target.Split('?', 2)[0];
                var expected = "/" + _fileName;

                if (!path.Equals(expected, StringComparison.OrdinalIgnoreCase) &&
                    !path.Equals(Uri.UnescapeDataString(expected), StringComparison.OrdinalIgnoreCase))
                {
                    await WriteStatusAsync(stream, "404 Not Found", 0, null, ct);
                    return;
                }

                var isHead = method.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
                if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) && !isHead)
                {
                    await WriteStatusAsync(stream, "405 Method Not Allowed", 0, null, ct);
                    return;
                }

                // Range simples: bytes=start- (resume do cliente IOS, se apoiado)
                long start = 0;
                string? rangeHeader = null;
                if (request.Value.Headers.TryGetValue("range", out var rv))
                    rangeHeader = rv;
                if (!string.IsNullOrWhiteSpace(rangeHeader) &&
                    rangeHeader.Trim().StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(rangeHeader.Split('=')[1].Split('-')[0], out var parsed) &&
                    parsed > 0 && parsed < _fileLength)
                {
                    start = parsed;
                }

                var length = _fileLength - start;
                var extra = start > 0
                    ? new Dictionary<string, string> { ["Content-Range"] = $"bytes {start}-{_fileLength - 1}/{_fileLength}" }
                    : null;
                await WriteStatusAsync(stream, start > 0 ? "206 Partial Content" : "200 OK", length, extra, ct);

                if (isHead)
                    return;

                await SendFileAsync(stream, start, length, ct);
            }
            catch { /* cliente desconectou / cancelado */ }
        }
    }

    private async Task SendFileAsync(NetworkStream stream, long start, long length, CancellationToken ct)
    {
        const int chunk = 64 * 1024;
        var buffer = new byte[chunk];
        long sent = 0;
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, chunk, useAsync: true);
        fs.Seek(start, SeekOrigin.Begin);

        while (sent < length && !ct.IsCancellationRequested)
        {
            var toRead = (int)Math.Min(chunk, length - sent);
            var read = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct);
            if (read == 0)
                break;
            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
            sent += read;
            TransferProgress?.Invoke(_fileName, start + sent, _fileLength);
        }

        await stream.FlushAsync(ct);
    }

    private static async Task WriteStatusAsync(NetworkStream stream, string status, long contentLength,
        Dictionary<string, string>? extraHeaders, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append("\r\n");
        sb.Append("Content-Type: application/octet-stream\r\n");
        sb.Append("Content-Length: ").Append(contentLength).Append("\r\n");
        sb.Append("Accept-Ranges: bytes\r\n");
        sb.Append("Connection: close\r\n");
        if (extraHeaders is not null)
            foreach (var kv in extraHeaders)
                sb.Append(kv.Key).Append(": ").Append(kv.Value).Append("\r\n");
        sb.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<(string Method, string Target, Dictionary<string, string> Headers)?> ReadRequestHeaderAsync(
        NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            int read;
            try { read = await stream.ReadAsync(buf.AsMemory(0, 1), ct); }
            catch { return null; }
            if (read == 0)
                break;
            sb.Append((char)buf[0]);
            if (sb.Length >= 4 && sb.ToString(sb.Length - 4, 4) == "\r\n\r\n")
                break;
            if (sb.Length > 8192)
                return null;
        }

        var headerText = sb.ToString();
        var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return null;

        var requestParts = lines[0].Split(' ');
        if (requestParts.Length < 2)
            return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf(':');
            if (colon > 0)
                headers[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim();
        }

        return (requestParts[0], requestParts[1], headers);
    }
}
