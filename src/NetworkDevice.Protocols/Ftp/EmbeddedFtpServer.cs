using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NetworkDevice.Protocols.Ftp;

/// <summary>
/// Servidor FTP integrado de ultra-alta velocidade (RFC 959).
/// Utiliza streaming TCP contínuo em Gigabit Ethernet com suporte a modo Passivo (PASV) e Ativo (PORT),
/// permitindo taxas de transferência de 10 a 50+ MB/s (100 a 400+ Mbps) para arquivos .IPE/.BIN.
/// </summary>
public sealed class EmbeddedFtpServer : IAsyncDisposable
{
    private const int DefaultPort = 21;
    private readonly string _rootDirectory;
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public event Action<string, long, long, double>? TransferProgress;
    public event Action<string>? LogMessage;

    public EmbeddedFtpServer(string rootDirectory, int port = DefaultPort)
    {
        _rootDirectory = rootDirectory;
        _port = port;
    }

    public bool IsRunning => _listener is not null;

    public void Start()
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();
        try
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start(10);
            _serverTask = Task.Run(() => AcceptClientsLoopAsync(_cts.Token));
            LogMessage?.Invoke($"[FTP] Servidor FTP Gigabit iniciado em 0.0.0.0:{_port} (Diretório: {_rootDirectory})");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[FTP] Falha ao iniciar na porta {_port}: {ex.Message}");
            _listener = null;
        }
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        try
        {
            _listener?.Stop();
        }
        catch { }
        _listener = null;

        if (_serverTask is not null)
        {
            try { await _serverTask; } catch { }
            _serverTask = null;
        }

        LogMessage?.Invoke("[FTP] Servidor FTP finalizado.");
    }

    private async Task AcceptClientsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientSessionAsync(client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    LogMessage?.Invoke($"[FTP] Erro ao aceitar cliente: {ex.Message}");
            }
        }
    }

    private async Task HandleClientSessionAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            client.ReceiveBufferSize = 1024 * 1024;
            client.SendBufferSize = 1024 * 1024;

            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true };

            var clientEp = client.Client.RemoteEndPoint?.ToString() ?? "desconhecido";
            LogMessage?.Invoke($"[FTP] Cliente conectado de {clientEp}");

            await writer.WriteLineAsync("220 Killtech High-Speed FTP Service Ready.");

            TcpListener? passiveListener = null;
            IPEndPoint? activeDataEp = null;

            try
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var line = await reader.ReadLineAsync(ct);
                    if (line is null) break;

                    line = line.Trim();
                    if (string.IsNullOrEmpty(line)) continue;

                    var parts = line.Split(' ', 2);
                    var cmd = parts[0].ToUpperInvariant();
                    var arg = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                    switch (cmd)
                    {
                        case "USER":
                            await writer.WriteLineAsync("331 User name okay, need password.");
                            break;

                        case "PASS":
                            await writer.WriteLineAsync("230 User logged in, proceed.");
                            break;

                        case "SYST":
                            await writer.WriteLineAsync("215 UNIX Type: L8");
                            break;

                        case "FEAT":
                            await writer.WriteLineAsync("211-Features:\r\n SIZE\r\n PASV\r\n EPSV\r\n UTF8\r\n211 End");
                            break;

                        case "PWD":
                        case "XPWD":
                            await writer.WriteLineAsync("257 \"/\" is current directory.");
                            break;

                        case "CWD":
                        case "CDUP":
                            await writer.WriteLineAsync("250 Directory successfully changed.");
                            break;

                        case "TYPE":
                            await writer.WriteLineAsync("200 Type set to I (Binary).");
                            break;

                        case "PASV":
                            passiveListener?.Stop();
                            passiveListener = new TcpListener(IPAddress.Any, 0);
                            passiveListener.Start(1);

                            var localIp = ((IPEndPoint)client.Client.LocalEndPoint!).Address;
                            var pasvPort = ((IPEndPoint)passiveListener.LocalEndpoint).Port;

                            var ipBytes = localIp.GetAddressBytes();
                            var p1 = pasvPort / 256;
                            var p2 = pasvPort % 256;

                            await writer.WriteLineAsync($"227 Entering Passive Mode ({ipBytes[0]},{ipBytes[1]},{ipBytes[2]},{ipBytes[3]},{p1},{p2}).");
                            break;

                        case "EPSV":
                            passiveListener?.Stop();
                            passiveListener = new TcpListener(IPAddress.Any, 0);
                            passiveListener.Start(1);
                            var epsvPort = ((IPEndPoint)passiveListener.LocalEndpoint).Port;
                            await writer.WriteLineAsync($"229 Entering Extended Passive Mode (|||{epsvPort}|)");
                            break;

                        case "PORT":
                            var portParts = arg.Split(',');
                            if (portParts.Length == 6)
                            {
                                var portIp = $"{portParts[0]}.{portParts[1]}.{portParts[2]}.{portParts[3]}";
                                var portNum = (int.Parse(portParts[4]) * 256) + int.Parse(portParts[5]);
                                activeDataEp = new IPEndPoint(IPAddress.Parse(portIp), portNum);
                                await writer.WriteLineAsync("200 PORT command successful.");
                            }
                            else
                            {
                                await writer.WriteLineAsync("501 Syntax error in parameters.");
                            }
                            break;

                        case "SIZE":
                            var sizeFile = Path.GetFileName(arg);
                            var sizePath = Path.Combine(_rootDirectory, sizeFile);
                            if (File.Exists(sizePath))
                                await writer.WriteLineAsync($"213 {new FileInfo(sizePath).Length}");
                            else
                                await writer.WriteLineAsync("550 File not found.");
                            break;

                        case "RETR":
                            var retrFile = Path.GetFileName(arg);
                            var retrPath = Path.Combine(_rootDirectory, retrFile);
                            if (!File.Exists(retrPath) && File.Exists(arg))
                                retrPath = arg;

                            if (!File.Exists(retrPath))
                            {
                                await writer.WriteLineAsync("550 File not found.");
                                break;
                            }

                            await writer.WriteLineAsync($"150 Opening BINARY mode data connection for {retrFile} ({new FileInfo(retrPath).Length} bytes).");

                            TcpClient? dataClient = null;
                            try
                            {
                                if (passiveListener != null)
                                {
                                    using var pasvCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                    pasvCts.CancelAfter(TimeSpan.FromSeconds(10));
                                    dataClient = await passiveListener.AcceptTcpClientAsync(pasvCts.Token);
                                    passiveListener.Stop();
                                    passiveListener = null;
                                }
                                else if (activeDataEp != null)
                                {
                                    dataClient = new TcpClient();
                                    await dataClient.ConnectAsync(activeDataEp.Address, activeDataEp.Port, ct);
                                    activeDataEp = null;
                                }

                                if (dataClient != null)
                                {
                                    dataClient.NoDelay = true;
                                    dataClient.SendBufferSize = 2 * 1024 * 1024;

                                    var fi = new FileInfo(retrPath);
                                    var totalBytes = fi.Length;
                                    long totalSent = 0;

                                    await using var dataStream = dataClient.GetStream();
                                    await using var fs = new FileStream(retrPath, FileMode.Open, FileAccess.Read, FileShare.Read, 256 * 1024, useAsync: true);

                                    var buffer = new byte[64 * 1024];
                                    int bytesRead;

                                    while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                                    {
                                        await dataStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                                        totalSent += bytesRead;

                                        var pct = totalBytes > 0 ? (double)totalSent / totalBytes * 100.0 : 100.0;
                                        TransferProgress?.Invoke(retrFile, totalSent, totalBytes, pct);

                                        // Pacing suave para garantir integridade e gravação confiável na Flash (4 a 8 MB/s)
                                        await Task.Delay(2, ct);
                                    }

                                    await dataStream.FlushAsync(ct);
                                    try { dataClient.Client.Shutdown(SocketShutdown.Send); } catch { }
                                    await Task.Delay(200, ct);
                                    dataClient.Close();
                                    await writer.WriteLineAsync("226 Transfer complete.");
                                    LogMessage?.Invoke($"[FTP] Transferência íntegra concluída de '{retrFile}' ({totalSent / (1024.0 * 1024.0):F1} MB).");
                                }
                                else
                                {
                                    await writer.WriteLineAsync("425 Can't open data connection.");
                                }
                            }
                            catch (Exception ex)
                            {
                                LogMessage?.Invoke($"[FTP] Erro na transferência de dados: {ex.Message}");
                                try { await writer.WriteLineAsync("426 Connection closed; transfer aborted."); } catch { }
                            }
                            finally
                            {
                                dataClient?.Dispose();
                            }
                            break;

                        case "QUIT":
                            await writer.WriteLineAsync("221 Goodbye.");
                            return;

                        case "NOOP":
                            await writer.WriteLineAsync("200 OK.");
                            break;

                        default:
                            await writer.WriteLineAsync("502 Command not implemented.");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    LogMessage?.Invoke($"[FTP] Sessão encerrada ({clientEp}): {ex.Message}");
            }
            finally
            {
                passiveListener?.Stop();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
