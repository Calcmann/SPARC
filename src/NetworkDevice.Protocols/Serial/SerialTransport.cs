using System.IO.Ports;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Protocols.Serial;

public sealed class SerialTransport : ITransport
{
    private readonly SerialPort _port;
    private readonly TimeSpan _breakDuration;
    private readonly TimeSpan _readTimeout;

    public SerialTransport(
        string portName,
        int baudRate = 9600,
        int dataBits = 8,
        Parity parity = Parity.None,
        StopBits stopBits = StopBits.One,
        TimeSpan? breakDuration = null,
        TimeSpan? readTimeout = null)
    {
        _breakDuration = breakDuration ?? TimeSpan.FromMilliseconds(250);
        _readTimeout = readTimeout ?? TimeSpan.FromMilliseconds(200);
        _port = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
        {
            ReadTimeout = (int)_readTimeout.TotalMilliseconds,
            WriteTimeout = Timeout.Infinite,
            Handshake = Handshake.None,
            DtrEnable = true,
            RtsEnable = true
        };
    }

    public bool IsOpen => _port.IsOpen;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { _port.Open(); }
        catch (UnauthorizedAccessException ex) { throw new DeviceSessionException($"Porta {_port.PortName} em uso ou sem permissão: {ex.Message}"); }
        catch (IOException ex) { throw new DeviceSessionException($"Falha ao abrir {_port.PortName}: {ex.Message}"); }
        catch (ArgumentException ex) { throw new DeviceSessionException($"Porta {_port.PortName} inválida: {ex.Message}"); }
        _port.DtrEnable = true;
        _port.RtsEnable = true;
        try { _port.DiscardInBuffer(); } catch { }
        try { _port.DiscardOutBuffer(); } catch { }
        return Task.CompletedTask;
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
            throw new DeviceSessionException("Porta serial fechada.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < _readTimeout && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var bytesAvailable = _port.BytesToRead;
                if (bytesAvailable > 0)
                {
                    var array = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
                    try
                    {
                        var toRead = Math.Min(buffer.Length, bytesAvailable);
                        var read = _port.Read(array, 0, toRead);
                        if (read > 0)
                        {
                            array.AsSpan(0, read).CopyTo(buffer.Span);
                            return read;
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(array);
                    }
                }
            }
            catch (Exception)
            {
                return 0;
            }

            try
            {
                await Task.Delay(25, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
        }

        return 0;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
            throw new DeviceSessionException("Porta serial fechada.");

        cancellationToken.ThrowIfCancellationRequested();

        var array = buffer.ToArray();
        _port.Write(array, 0, array.Length);
        await Task.Delay(30, cancellationToken);
    }

    public async Task SendBreakAsync(CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
            throw new DeviceSessionException("Porta serial fechada.");

        try
        {
            _port.BreakState = true;
            await Task.Delay(_breakDuration, cancellationToken);
        }
        finally
        {
            try { _port.BreakState = false; } catch { }
        }
    }

    public Task CloseAsync()
    {
        if (_port.IsOpen)
            _port.Close();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
