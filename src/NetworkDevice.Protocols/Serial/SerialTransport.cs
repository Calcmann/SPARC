using System.IO.Ports;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Protocols.Serial;

public sealed class SerialTransport : ITransport
{
    private readonly SerialPort _port;
    private readonly TimeSpan _breakDuration;
    private readonly TimeSpan _readTimeout;
    private volatile bool _isBreaking;

    public SerialTransport(
        string portName,
        int baudRate = 9600,
        int dataBits = 8,
        Parity parity = Parity.None,
        StopBits stopBits = StopBits.One,
        TimeSpan? breakDuration = null,
        TimeSpan? readTimeout = null)
    {
        _breakDuration = breakDuration ?? TimeSpan.FromMilliseconds(180);
        _readTimeout = readTimeout ?? TimeSpan.FromMilliseconds(200);
        _port = new SerialPort(portName?.Trim() ?? "", baudRate, parity, dataBits, stopBits)
        {
            ReadTimeout = (int)_readTimeout.TotalMilliseconds,
            WriteTimeout = 3000,
            Handshake = Handshake.None
        };
        // DtrEnable e RtsEnable são inicializados de forma defensiva em OpenAsync()
        // para prevenir rejeição de DCB pelo driver CH341 v4.0/DCH no Windows 11.
    }

    public bool IsOpen => _port.IsOpen;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_port.IsOpen)
            return Task.CompletedTask;

        try { _port.Open(); }
        catch (UnauthorizedAccessException ex) { throw new DeviceSessionException($"Porta {_port.PortName} em uso ou sem permissão: {ex.Message}"); }
        catch (IOException ex) { throw new DeviceSessionException($"Falha ao abrir {_port.PortName}: {ex.Message}"); }
        catch (ArgumentException ex) { throw new DeviceSessionException($"Porta {_port.PortName} inválida: {ex.Message}"); }
        catch (InvalidOperationException) when (_port.IsOpen) { return Task.CompletedTask; }

        // DTR e RTS ativos garantem que o roteador detecte o terminal console conectado (DSR/CTS assertados)
        try { _port.DtrEnable = true; } catch { }
        try { _port.RtsEnable = true; } catch { }
        try { _port.DiscardInBuffer(); } catch { }
        try { _port.DiscardOutBuffer(); } catch { }
        return Task.CompletedTask;
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
            throw new DeviceSessionException("Porta serial fechada.");

        if (cancellationToken.IsCancellationRequested)
            return 0;

        // Leitura sem bloqueio agressivo no driver:
        // Polla BytesToRead e consome apenas bytes já presentes no buffer do driver.
        // Isso evita que o .NET chame CancelIoEx a cada timeout de 200ms, o que no CH340
        // reseta o endpoint USB e causa descarte de pacotes durante o boot do roteador.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < _readTimeout && !cancellationToken.IsCancellationRequested)
        {
            if (_isBreaking)
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                continue;
            }

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
                await Task.Delay(15, cancellationToken).ConfigureAwait(false);
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

        // Adaptadores CH340 no Windows ignoram o comando nativo SetCommBreak do driver.
        // Nesses adaptadores, aplicamos a quebra de enquadramento calibrada (1200 bps):
        if (SerialPorts.IsCh340Port(_port.PortName))
        {
            _isBreaking = true;
            var origBaud = _port.BaudRate;
            try
            {
                _port.BaudRate = 1200;
                // 25 bytes de 0x00 a 1200 bps = 250 bits / 1200 bps = exatamente 208 ms de sinal LOW contínuo na linha TX
                var breakBytes = new byte[25];
                _port.Write(breakBytes, 0, breakBytes.Length);
                await Task.Delay(250, cancellationToken);
            }
            catch { }
            finally
            {
                try { _port.BaudRate = origBaud; } catch { }
                _isBreaking = false;
            }
            return;
        }

        // Para adaptadores com suporte nativo (FTDI, Prolific, CP210x, portas COM nativas):
        // Usa BreakState nativo (SetCommBreak/ClearCommBreak Win32) a 9600 bps estável
        try
        {
            _port.BreakState = true;
            await Task.Delay(_breakDuration, cancellationToken);
        }
        catch
        {
            // Fallback caso o driver rejeite SetCommBreak
            _isBreaking = true;
            var origBaud = _port.BaudRate;
            try
            {
                _port.BaudRate = 1200;
                var breakBytes = new byte[25];
                _port.Write(breakBytes, 0, breakBytes.Length);
                await Task.Delay(220, cancellationToken);
                _port.BaudRate = origBaud;
            }
            catch { }
            finally
            {
                try { _port.DiscardInBuffer(); } catch { }
                _isBreaking = false;
            }
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
