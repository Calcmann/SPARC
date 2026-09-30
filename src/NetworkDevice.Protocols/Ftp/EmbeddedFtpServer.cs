using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NetworkDevice.Protocols.Ftp;

/// <summary>
/// Servidor FTP mínimo embarcado (RFC 959) para distribuir firmware ao roteador sem root:
/// controle em porta alta (default 2121) + dados em PASV (portas efêmeras altas) ou PORT
/// (conexão de retorno a porta alta do cliente). Aceita qualquer credencial por padrão
/// (ferramenta de campo) ou impõe usuário/senha configurados. Somente download (RETR).
/// Usado por: HPE Comware (<c>ftp servidor porta</c> — com suporte oficial a service-port)
/// e FortiOS (<c>execute restore image ftp arquivo servidor:porta</c>).
/// </summary>
public sealed class EmbeddedFtpServer : IAsyncDisposable
{
    private readonly string _rootDirectory;
    private readonly string _advertiseIp;
    private readonly int _preferredPort;
    private readonly string? _requiredUser;
    private readonly string? _requiredPass;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public int ActualPort { get; private set; }
    public bool IsRunning => _listener is not null;

    public event Action<string, long, long>? TransferProgress;
    public event Action<string>? LogMessage;

    public EmbeddedFtpServer(string rootDirectory, string advertiseIp, int preferredPort = 2121,
        string? username = null, string? password = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            throw new ArgumentException("Diretório raiz do FTP inválido.", nameof(rootDirectory));
        if (string.IsNullOrWhiteSpace(advertiseIp))
            throw new ArgumentException("IP de anúncio (celular na LAN) inválido.", nameof(advertiseIp));
        _rootDirectory = rootDirectory;
        _advertiseIp = advertiseIp.Trim();
        _preferredPort = preferredPort;
        _requiredUser = username;
        _requiredPass = password;
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
                $"Não foi possível abrir o FTP (portas {_preferredPort}-{_preferredPort + 5}).", lastError);

        _cts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        LogMessage?.Invoke($"[FTP] Servindo '{_rootDirectory}' em {_advertiseIp}:{ActualPort}.");
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
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => ServeControlAsync(client, ct), ct);
        }
    }

    private async Task ServeControlAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var authed = _requiredUser is null;
            string? pendingUser = null;
            TcpListener? pasvListener = null;
            IPEndPoint? portTarget = null;

            async Task Reply(string code, string text)
            {
                var bytes = Encoding.ASCII.GetBytes($"{code} {text}\r\n");
                await client.GetStream().WriteAsync(bytes, ct);
                await client.GetStream().FlushAsync(ct);
            }

            async Task ClosePasvAsync()
            {
                if (pasvListener is not null)
                {
                    try { pasvListener.Stop(); } catch { }
                    pasvListener = null;
                }
                await Task.CompletedTask;
            }

            try
            {
                await Reply("220", "SPARC FTPd ready");
                using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, false, 1024, leaveOpen: true);

                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var line = await ReadLineAsync(reader, ct);
                    if (line is null)
                        break;
                    if (line.Length == 0)
                        continue;

                    var space = line.IndexOf(' ');
                    var verb = (space < 0 ? line : line[..space]).Trim().ToUpperInvariant();
                    var arg = space < 0 ? string.Empty : line[(space + 1)..].Trim();

                    switch (verb)
                    {
                        case "USER":
                            pendingUser = arg;
                            authed = _requiredUser is null || pendingUser == _requiredUser;
                            await Reply("331", "Password required");
                            break;
                        case "PASS":
                            if (_requiredUser is not null && !(pendingUser == _requiredUser && arg == _requiredPass))
                            {
                                authed = false;
                                await Reply("530", "Login incorrect");
                            }
                            else
                            {
                                authed = true;
                                await Reply("230", "Logged in");
                            }
                            break;
                        case "SYST": await Reply("215", "UNIX Type: L8"); break;
                        case "FEAT": await Reply("211", "PASV UTF8"); break;
                        case "PWD": await Reply("257", "\"/\""); break;
                        case "TYPE":
                            await Reply("200", $"Type set to {(arg.StartsWith("A", StringComparison.OrdinalIgnoreCase) ? "A" : "I")}");
                            break;
                        case "NOOP": await Reply("200", "OK"); break;
                        case "PASV":
                            if (!authed) { await Reply("530", "Login first"); break; }
                            await ClosePasvAsync();
                            pasvListener = new TcpListener(IPAddress.Any, 0);
                            pasvListener.Start(1);
                            var ep = (IPEndPoint)pasvListener.LocalEndpoint;
                            var ipParts = _advertiseIp.Split('.');
                            if (ipParts.Length != 4) { await Reply("425", "Bad IP"); break; }
                            await Reply("227", $"Entering Passive Mode ({string.Join(",", ipParts)},{ep.Port / 256},{ep.Port % 256})");
                            break;
                        case "PORT":
                            if (!authed) { await Reply("530", "Login first"); break; }
                            portTarget = ParsePortArg(arg);
                            await Reply(portTarget is null ? "501" : "200", portTarget is null ? "Bad PORT" : "PORT ok");
                            break;
                        case "RETR":
                            if (!authed) { await Reply("530", "Login first"); break; }
                            await RetrieveAsync(arg, pasvListener, portTarget, Reply, ClosePasvAsync, ct);
                            pasvListener = null;
                            portTarget = null;
                            break;
                        case "QUIT":
                            await Reply("221", "Bye");
                            return;
                        default:
                            await Reply("502", "Not implemented");
                            break;
                    }
                }
            }
            catch { /* cliente desconectou / cancelado */ }
            finally
            {
                try { pasvListener?.Stop(); } catch { }
            }
        }
    }

    private async Task RetrieveAsync(string requested, TcpListener? pasvListener, IPEndPoint? portTarget,
        Func<string, string, Task> reply, Func<Task> closePasv, CancellationToken ct)
    {
        var name = Path.GetFileName(requested.Trim().Trim('"'));
        string? match = null;
        if (!string.IsNullOrWhiteSpace(name))
        {
            match = Directory.GetFiles(_rootDirectory)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        if (match is null)
        {
            await reply("550", "File not found");
            return;
        }

        TcpClient? data = null;
        try
        {
            if (pasvListener is not null)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
                try { data = await pasvListener.AcceptTcpClientAsync(timeoutCts.Token); }
                catch { await reply("425", "No data connection"); return; }
                finally { await closePasv(); }
            }
            else if (portTarget is not null)
            {
                data = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
                try { await data.ConnectAsync(portTarget.Address, portTarget.Port, timeoutCts.Token); }
                catch { await reply("425", "Cannot connect back"); return; }
            }
            else
            {
                await reply("425", "Use PASV or PORT first");
                return;
            }

            using (data)
            {
                var total = new FileInfo(match).Length;
                await reply("150", $"Opening data connection ({total} bytes)");
                var stream = data.GetStream();
                const int chunk = 64 * 1024;
                var buffer = new byte[chunk];
                long sent = 0;
                using var fs = new FileStream(match, FileMode.Open, FileAccess.Read, FileShare.Read, chunk, useAsync: true);
                while (sent < total && !ct.IsCancellationRequested)
                {
                    var toRead = (int)Math.Min(chunk, total - sent);
                    var read = await fs.ReadAsync(buffer.AsMemory(0, toRead), ct);
                    if (read == 0) break;
                    await stream.WriteAsync(buffer.AsMemory(0, read), ct);
                    sent += read;
                    TransferProgress?.Invoke(Path.GetFileName(match), sent, total);
                }
                await stream.FlushAsync(ct);
            }
            await reply("226", "Transfer complete");
            LogMessage?.Invoke($"[FTP] '{Path.GetFileName(match)}' entregue.");
        }
        catch
        {
            try { await reply("426", "Transfer aborted"); } catch { }
        }
        finally
        {
            try { data?.Dispose(); } catch { }
        }
    }

    private static IPEndPoint? ParsePortArg(string arg)
    {
        var parts = arg.Split(',');
        if (parts.Length != 6) return null;
        try
        {
            var ip = new IPAddress(parts.Take(4).Select(byte.Parse).ToArray());
            var port = int.Parse(parts[4]) * 256 + int.Parse(parts[5]);
            return new IPEndPoint(ip, port);
        }
        catch { return null; }
    }

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new char[1];
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            int read;
            try { read = await reader.ReadAsync(buf.AsMemory(0, 1), ct); }
            catch { return null; }
            if (read == 0) return sb.Length > 0 ? sb.ToString() : null;
            if (buf[0] == '\n') break;
            if (buf[0] != '\r') sb.Append(buf[0]);
            if (sb.Length > 1024) break;
        }
        return sb.ToString();
    }
}
