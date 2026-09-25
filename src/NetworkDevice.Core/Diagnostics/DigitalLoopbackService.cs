using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Core.Diagnostics;

public sealed record DigitalLoopbackStats(
    bool IsActive,
    int Port,
    string BindAddress,
    long TotalPacketsRx,
    long TotalPacketsTx,
    long TotalBytesRx,
    long TotalBytesTx,
    double CurrentRxMbps,
    double CurrentTxMbps,
    long CurrentRxPps,
    long CurrentTxPps,
    string? LastRemoteEndPoint,
    TimeSpan Elapsed);

public class DigitalLoopbackService : IDisposable
{
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task[]? _workerTasks;
    private Task? _statsTask;

    private long _totalPacketsRx;
    private long _totalPacketsTx;
    private long _totalBytesRx;
    private long _totalBytesTx;

    private long _lastBytesRx;
    private long _lastBytesTx;
    private long _lastPacketsRx;
    private long _lastPacketsTx;

    private double _currentRxMbps;
    private double _currentTxMbps;
    private long _currentRxPps;
    private long _currentTxPps;

    private string? _lastRemoteEndPoint;
    private Stopwatch? _stopwatch;
    private int _port;
    private string _bindAddress = "0.0.0.0";

    public bool IsRunning => _socket != null && _cts != null && !_cts.IsCancellationRequested;

    public event Action<DigitalLoopbackStats>? StatsUpdated;
    public event Action<string>? LogMessage;

    public void Start(
        int port = 5001, 
        IPAddress? bindAddress = null, 
        int workerCount = 0, 
        int bufferSizeBytes = 2 * 1024 * 1024)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("O serviço de Loop Digital já está em execução.");
        }

        _port = port;
        var ip = bindAddress ?? IPAddress.Any;
        _bindAddress = ip.ToString();

        _totalPacketsRx = 0;
        _totalPacketsTx = 0;
        _totalBytesRx = 0;
        _totalBytesTx = 0;
        _lastBytesRx = 0;
        _lastBytesTx = 0;
        _lastPacketsRx = 0;
        _lastPacketsTx = 0;
        _currentRxMbps = 0;
        _currentTxMbps = 0;
        _currentRxPps = 0;
        _currentTxPps = 0;
        _lastRemoteEndPoint = null;

        _cts = new CancellationTokenSource();
        _stopwatch = Stopwatch.StartNew();

        _socket = new Socket(ip.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

        if (workerCount <= 0)
        {
            workerCount = Math.Clamp(Environment.ProcessorCount, 2, 8);
        }

        try
        {
            // Otimização de buffers com prevenção de Bufferbloat (default 2MB)
            _socket.ReceiveBufferSize = bufferSizeBytes;
            _socket.SendBufferSize = bufferSizeBytes;
        }
        catch
        {
            // Fallback para buffer padrão do SO caso haja restrição
        }

        _socket.Bind(new IPEndPoint(ip, port));

        LogMessage?.Invoke($"[LOOP DIGITAL] Refletor iniciado em {_bindAddress}:{port} ({workerCount} workers multi-core, Buffer: {bufferSizeBytes / 1024} KB)");

        var token = _cts.Token;
        _workerTasks = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
        {
            _workerTasks[i] = Task.Run(() => RunLoopWorkerAsync(_socket, token), token);
        }
        _statsTask = Task.Run(() => RunStatsAsync(token), token);
    }

    public void ResetStats()
    {
        Interlocked.Exchange(ref _totalPacketsRx, 0);
        Interlocked.Exchange(ref _totalPacketsTx, 0);
        Interlocked.Exchange(ref _totalBytesRx, 0);
        Interlocked.Exchange(ref _totalBytesTx, 0);
        Interlocked.Exchange(ref _lastBytesRx, 0);
        Interlocked.Exchange(ref _lastBytesTx, 0);
        Interlocked.Exchange(ref _lastPacketsRx, 0);
        Interlocked.Exchange(ref _lastPacketsTx, 0);
        _currentRxMbps = 0;
        _currentTxMbps = 0;
        _currentRxPps = 0;
        _currentTxPps = 0;
        _lastRemoteEndPoint = null;
        _stopwatch?.Restart();

        LogMessage?.Invoke("[LOOP DIGITAL] Contadores de tráfego e volume foram zerados.");

        StatsUpdated?.Invoke(new DigitalLoopbackStats(
            IsActive: IsRunning,
            Port: _port,
            BindAddress: _bindAddress,
            TotalPacketsRx: 0,
            TotalPacketsTx: 0,
            TotalBytesRx: 0,
            TotalBytesTx: 0,
            CurrentRxMbps: 0,
            CurrentTxMbps: 0,
            CurrentRxPps: 0,
            CurrentTxPps: 0,
            LastRemoteEndPoint: null,
            Elapsed: TimeSpan.Zero));
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        try
        {
            _cts?.Cancel();
            _socket?.Close();
        }
        catch
        {
            // Ignora exceções de encerramento
        }

        if (_workerTasks != null && _workerTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(_workerTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        if (_statsTask != null)
        {
            try
            {
                await _statsTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        _socket?.Dispose();
        _socket = null;
        _cts?.Dispose();
        _cts = null;
        _stopwatch?.Stop();

        LogMessage?.Invoke($"[LOOP DIGITAL] Refletor finalizado. Total refletido: {_totalPacketsTx:N0} pacotes ({_totalBytesTx / (1024.0 * 1024.0):F2} MB).");

        StatsUpdated?.Invoke(new DigitalLoopbackStats(
            IsActive: false,
            Port: _port,
            BindAddress: _bindAddress,
            TotalPacketsRx: _totalPacketsRx,
            TotalPacketsTx: _totalPacketsTx,
            TotalBytesRx: _totalBytesRx,
            TotalBytesTx: _totalBytesTx,
            CurrentRxMbps: 0,
            CurrentTxMbps: 0,
            CurrentRxPps: 0,
            CurrentTxPps: 0,
            LastRemoteEndPoint: _lastRemoteEndPoint,
            Elapsed: _stopwatch?.Elapsed ?? TimeSpan.Zero));
    }

    private async Task RunLoopWorkerAsync(Socket socket, CancellationToken token)
    {
        var buffer = new byte[65536];
        EndPoint senderEp = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);

        while (!token.IsCancellationRequested)
        {
            try
            {
                var rcvResult = await socket.ReceiveFromAsync(buffer, SocketFlags.None, senderEp, token).ConfigureAwait(false);

                if (rcvResult.ReceivedBytes > 0)
                {
                    Interlocked.Increment(ref _totalPacketsRx);
                    Interlocked.Add(ref _totalBytesRx, rcvResult.ReceivedBytes);

                    _lastRemoteEndPoint = rcvResult.RemoteEndPoint.ToString();

                    // Smart Loopback: Reflete o payload recebido diretamente para a origem (L3/L4 Swap)
                    await socket.SendToAsync(
                        new ReadOnlyMemory<byte>(buffer, 0, rcvResult.ReceivedBytes),
                        SocketFlags.None,
                        rcvResult.RemoteEndPoint,
                        token).ConfigureAwait(false);

                    Interlocked.Increment(ref _totalPacketsTx);
                    Interlocked.Add(ref _totalBytesTx, rcvResult.ReceivedBytes);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted || ex.SocketErrorCode == SocketError.Interrupted)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    LogMessage?.Invoke($"[LOOP DIGITAL] Erro na reflexão: {ex.Message}");
                    await Task.Delay(50, token).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task RunStatsAsync(CancellationToken token)
    {
        var prevElapsed = TimeSpan.Zero;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, token).ConfigureAwait(false);

                var curElapsed = _stopwatch?.Elapsed ?? TimeSpan.Zero;
                var deltaSec = Math.Max(0.1, (curElapsed - prevElapsed).TotalSeconds);
                prevElapsed = curElapsed;

                long curBytesRx = Interlocked.Read(ref _totalBytesRx);
                long curBytesTx = Interlocked.Read(ref _totalBytesTx);
                long curPktsRx = Interlocked.Read(ref _totalPacketsRx);
                long curPktsTx = Interlocked.Read(ref _totalPacketsTx);

                long deltaBytesRx = Math.Max(0, curBytesRx - _lastBytesRx);
                long deltaBytesTx = Math.Max(0, curBytesTx - _lastBytesTx);
                long deltaPktsRx = Math.Max(0, curPktsRx - _lastPacketsRx);
                long deltaPktsTx = Math.Max(0, curPktsTx - _lastPacketsTx);

                _lastBytesRx = curBytesRx;
                _lastBytesTx = curBytesTx;
                _lastPacketsRx = curPktsRx;
                _lastPacketsTx = curPktsTx;

                // Cálculo L1 framing aproximado
                _currentRxMbps = (deltaBytesRx * 8.0) / (deltaSec * 1_000_000.0);
                _currentTxMbps = (deltaBytesTx * 8.0) / (deltaSec * 1_000_000.0);
                _currentRxPps = (long)(deltaPktsRx / deltaSec);
                _currentTxPps = (long)(deltaPktsTx / deltaSec);

                var stats = new DigitalLoopbackStats(
                    IsActive: true,
                    Port: _port,
                    BindAddress: _bindAddress,
                    TotalPacketsRx: curPktsRx,
                    TotalPacketsTx: curPktsTx,
                    TotalBytesRx: curBytesRx,
                    TotalBytesTx: curBytesTx,
                    CurrentRxMbps: _currentRxMbps,
                    CurrentTxMbps: _currentTxMbps,
                    CurrentRxPps: _currentRxPps,
                    CurrentTxPps: _currentTxPps,
                    LastRemoteEndPoint: _lastRemoteEndPoint,
                    Elapsed: curElapsed);

                StatsUpdated?.Invoke(stats);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignora falhas temporárias no timer de stats
            }
        }
    }

    public void Stop() => Dispose();

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _socket?.Dispose();
            _cts?.Dispose();
        }
        catch { }
        finally
        {
            _socket = null;
            _cts = null;
        }
    }
}
