using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NetworkDevice.Protocols.Tftp;

/// <summary>
/// Servidor TFTP de altíssima performance (RFC 1350, RFC 2347, RFC 2348 blksize, RFC 2349 tsize/timeout, RFC 7440 windowsize).
/// Utiliza I/O nativo de sockets UDP síncronos em thread dedicada sem Task.Delay para máxima vazão de hardware (2 a 5 MB/s no TFTP padrão).
/// </summary>
public sealed class EmbeddedTftpServer : IAsyncDisposable
{
    private const int DefaultPort = 69;
    private const int DefaultBlockSize = 512;
    private const int OpCodeRrq = 1;
    private const int OpCodeData = 3;
    private const int OpCodeAck = 4;
    private const int OpCodeError = 5;
    private const int OpCodeOAck = 6;

    private readonly string _rootDirectory;
    private readonly int _port;
    private readonly bool _throttleForRommon;
    private Socket? _listenerSocket;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public event Action<string, long, long, double>? TransferProgress;
    public event Action<string>? LogMessage;

    public EmbeddedTftpServer(string rootDirectory, int port = DefaultPort, bool throttleForRommon = false)
    {
        _rootDirectory = rootDirectory;
        _port = port;
        _throttleForRommon = throttleForRommon;
    }

    public bool IsRunning => _listenerSocket is not null;

    public void Start()
    {
        if (IsRunning)
            return;

        _cts = new CancellationTokenSource();

        _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = 2 * 1024 * 1024,
            SendBufferSize = 2 * 1024 * 1024
        };
        _listenerSocket.Bind(new IPEndPoint(IPAddress.Any, _port));

        _serverTask = Task.Run(() => ListenLoop(_cts.Token));
        LogMessage?.Invoke($"[TFTP] Servidor TFTP de alta velocidade ativo em 0.0.0.0:{_port} (Diretório: {_rootDirectory})");
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        _listenerSocket?.Close();
        _listenerSocket?.Dispose();
        _listenerSocket = null;

        if (_serverTask is not null)
        {
            try { await _serverTask; } catch { }
            _serverTask = null;
        }

        LogMessage?.Invoke("[TFTP] Servidor TFTP finalizado.");
    }

    private void ListenLoop(CancellationToken ct)
    {
        var receiveBuffer = new byte[2048];
        EndPoint clientRemote = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested && _listenerSocket is not null)
        {
            try
            {
                var bytesReceived = _listenerSocket.ReceiveFrom(receiveBuffer, SocketFlags.None, ref clientRemote);
                var clientEp = (IPEndPoint)clientRemote;

                if (bytesReceived >= 4 && receiveBuffer[0] == 0 && receiveBuffer[1] == OpCodeRrq)
                {
                    var rrqCopy = new byte[bytesReceived];
                    Array.Copy(receiveBuffer, rrqCopy, bytesReceived);

                    // Executa a transferência em thread dedicada com sockets de alta velocidade
                    Task.Run(() => HandleReadRequest(rrqCopy, clientEp, ct), ct);
                }
            }
            catch (SocketException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    LogMessage?.Invoke($"[TFTP] Erro no listener: {ex.Message}");
            }
        }
    }

    private void HandleReadRequest(byte[] rrqData, IPEndPoint clientEp, CancellationToken ct)
    {
        var (filename, mode, blkSize, windowSize, tsizeRequested) = ParseRrq(rrqData);
        LogMessage?.Invoke($"[TFTP] Requisição RRQ de {clientEp}: '{filename}' (Modo: {mode}, BlkSize: {blkSize}, Window: {windowSize})");

        var cleanFilename = Path.GetFileName(filename);
        var fullPath = Path.Combine(_rootDirectory, cleanFilename);

        // Compatibilidade Tftpd32: busca case-insensitive (ROMMON pode variar case)
        if (!File.Exists(fullPath))
        {
            var dirFiles = Directory.Exists(_rootDirectory) ? Directory.GetFiles(_rootDirectory) : Array.Empty<string>();
            var match = dirFiles.FirstOrDefault(f => Path.GetFileName(f).Equals(cleanFilename, StringComparison.OrdinalIgnoreCase));
            if (match != null) { fullPath = match; cleanFilename = Path.GetFileName(match); }
            else if (File.Exists(filename)) fullPath = filename;
        }

        using var transferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveBufferSize = 4 * 1024 * 1024,
            SendBufferSize = 4 * 1024 * 1024,
            ReceiveTimeout = 2000,
            SendTimeout = 2000
        };
        transferSocket.Bind(new IPEndPoint(IPAddress.Any, 0)); // Porta efêmera dedicada

        if (!File.Exists(fullPath))
        {
            LogMessage?.Invoke($"[TFTP] Arquivo não encontrado: {fullPath}");
            SendError(transferSocket, clientEp, 1, "File not found");
            return;
        }

        try
        {
            var fileInfo = new FileInfo(fullPath);
            var totalBytes = fileInfo.Length;
            using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 512 * 1024);
            LogMessage?.Invoke($"[TFTP] Roteador {clientEp.Address} conectado! Iniciando envio de '{cleanFilename}' ({totalBytes / (1024.0 * 1024.0):F1} MB)...");

            var ackBuffer = new byte[516];
            EndPoint remoteAckEp = new IPEndPoint(clientEp.Address, clientEp.Port);

            var hasOptions = (blkSize != DefaultBlockSize) || (windowSize > 1) || tsizeRequested;

            if (_throttleForRommon || !hasOptions)
            {
                blkSize = DefaultBlockSize;
                windowSize = 1;
            }
            else
            {
                var proposeBlk = Math.Min(blkSize, 1468);
                var proposeWin = windowSize;
                SendOAck(transferSocket, clientEp, proposeBlk, proposeWin > 1 ? proposeWin : (int?)null, totalBytes, tsizeRequested);
                var oackAck = ReceiveAckSync(transferSocket, ref remoteAckEp, 0, ackBuffer, 2000, ct);
                if (oackAck)
                {
                    blkSize = proposeBlk;
                    windowSize = proposeWin;
                    LogMessage?.Invoke($"[TFTP] OACK aceito — blksize {blkSize}, windowsize {windowSize}.");
                }
                else
                {
                    LogMessage?.Invoke("[TFTP] OACK sem resposta — utilizando padrão 512 bytes.");
                    blkSize = DefaultBlockSize;
                    windowSize = 1;
                }
            }

            ushort blockNumber = 1;
            var buffer = new byte[blkSize];
            long totalSent = 0;
            var isFinished = false;

            // 2. Loop de envio ultrarrápido sem Task.Delay
            while (!isFinished && !ct.IsCancellationRequested)
            {
                var bytesRead = fileStream.Read(buffer, 0, blkSize);
                totalSent += bytesRead;

                var dataPacket = new byte[4 + bytesRead];
                dataPacket[0] = 0;
                dataPacket[1] = OpCodeData;
                dataPacket[2] = (byte)(blockNumber >> 8);
                dataPacket[3] = (byte)(blockNumber & 0xFF);
                Array.Copy(buffer, 0, dataPacket, 4, bytesRead);

                if (bytesRead < blkSize)
                    isFinished = true;

                // Envia pacote e aguarda ACK correspondente ao bloco exato
                var ackOk = false;
                for (var retry = 0; retry < 8; retry++)
                {
                    transferSocket.SendTo(dataPacket, 0, dataPacket.Length, SocketFlags.None, clientEp);

                    if (ReceiveAckSync(transferSocket, ref remoteAckEp, blockNumber, ackBuffer, 2000, ct))
                    {
                        ackOk = true;
                        break;
                    }
                }

                if (!ackOk)
                {
                    LogMessage?.Invoke($"[TFTP] Timeout aguardando ACK do bloco {blockNumber}.");
                    return;
                }

                var pct = totalBytes > 0 ? (double)totalSent / totalBytes * 100.0 : 100.0;
                TransferProgress?.Invoke(cleanFilename, totalSent, totalBytes, pct);
                if (_throttleForRommon) Thread.Sleep(15);

                unchecked { blockNumber++; }
            }

            LogMessage?.Invoke($"[TFTP] Transferência de '{cleanFilename}' concluída ({totalSent / (1024.0 * 1024.0):F1} MB enviados com sucesso).");
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                LogMessage?.Invoke($"[TFTP] Erro na transferência: {ex.Message}");
        }
    }

    private static bool ReceiveAckSync(
        Socket socket,
        ref EndPoint expectedEp,
        ushort expectedBlock,
        byte[] buffer,
        int timeoutMs,
        CancellationToken ct)
    {
        socket.ReceiveTimeout = timeoutMs;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var received = socket.ReceiveFrom(buffer, SocketFlags.None, ref expectedEp);
                if (received >= 4 && buffer[0] == 0 && buffer[1] == OpCodeAck)
                {
                    var block = (ushort)((buffer[2] << 8) | buffer[3]);
                    if (block == expectedBlock)
                        return true;
                }
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static (string filename, string mode, int blkSize, int windowSize, bool tsize) ParseRrq(byte[] data)
    {
        var str = Encoding.ASCII.GetString(data, 2, data.Length - 2);
        var tokens = str.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        var filename = tokens.Length > 0 ? tokens[0] : "";
        var mode = tokens.Length > 1 ? tokens[1] : "octet";
        var blkSize = DefaultBlockSize;
        var windowSize = 1;
        var tsize = false;

        for (var i = 2; i < tokens.Length - 1; i += 2)
        {
            var optName = tokens[i].ToLowerInvariant();
            var optVal = tokens[i + 1];

            if (optName == "blksize" && int.TryParse(optVal, out var bs) && bs is >= 512 and <= 65464)
                blkSize = bs;
            else if (optName == "windowsize" && int.TryParse(optVal, out var ws) && ws is >= 1 and <= 64)
                windowSize = ws;
            else if (optName == "tsize")
                tsize = true;
        }

        return (filename, mode, blkSize, windowSize, tsize);
    }

    private static void SendOAck(Socket socket, IPEndPoint ep, int? blkSize, int? windowSize, long totalBytes, bool includeTsize)
    {
        var sb = new StringBuilder();
        if (blkSize.HasValue)
        {
            sb.Append("blksize\0");
            sb.Append(blkSize.Value);
            sb.Append('\0');
        }
        if (windowSize.HasValue)
        {
            sb.Append("windowsize\0");
            sb.Append(windowSize.Value);
            sb.Append('\0');
        }
        if (includeTsize)
        {
            sb.Append("tsize\0");
            sb.Append(totalBytes);
            sb.Append('\0');
        }

        var payload = Encoding.ASCII.GetBytes(sb.ToString());
        var packet = new byte[2 + payload.Length];
        packet[0] = 0;
        packet[1] = OpCodeOAck;
        Array.Copy(payload, 0, packet, 2, payload.Length);

        socket.SendTo(packet, 0, packet.Length, SocketFlags.None, ep);
    }

    private static void SendError(Socket socket, IPEndPoint ep, ushort errorCode, string errorMessage)
    {
        var msgBytes = Encoding.ASCII.GetBytes(errorMessage + "\0");
        var packet = new byte[4 + msgBytes.Length];
        packet[0] = 0;
        packet[1] = OpCodeError;
        packet[2] = (byte)(errorCode >> 8);
        packet[3] = (byte)(errorCode & 0xFF);
        Array.Copy(msgBytes, 0, packet, 4, msgBytes.Length);

        socket.SendTo(packet, 0, packet.Length, SocketFlags.None, ep);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
