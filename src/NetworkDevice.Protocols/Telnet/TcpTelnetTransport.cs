using System.Net.Sockets;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Protocols.Telnet;

/// <summary>
/// Transporte Telnet (TCP/23) implementando <see cref="ITransport"/>, para CLI sobre a
/// LAN via adaptador Ethernet OTG (híbrido com o console serial, que segue necessário
/// para reload/ROMMON/Break). Responde à negociação IAC (WILL/WONT/DO/DONT -&gt; recusa
/// opções: sem eco remoto, sem NAWS, binário simples) e filtra IAC do fluxo de leitura.
/// </summary>
public sealed class TcpTelnetTransport : ITransport
{
    private const byte IAC = 255;
    private const byte DONT = 254;
    private const byte DO = 253;
    private const byte WONT = 252;
    private const byte WILL = 251;
    private const byte SB = 250;
    private const byte SE = 240;
    private const byte BRK = 243;

    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _connectTimeout;
    private readonly TimeSpan _readTimeout;
    private TcpClient? _client;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private bool _disposed;

    // Buffer de bytes já filtrados (IAC removido) aguardando entrega ao leitor
    private readonly Queue<byte> _pending = new();

    public TcpTelnetTransport(string host, int port = 23,
        TimeSpan? connectTimeout = null, TimeSpan? readTimeout = null)
    {
        _host = host?.Trim() ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _connectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);
        _readTimeout = readTimeout ?? TimeSpan.FromMilliseconds(200);
    }

    public string Host => _host;
    public int Port => _port;

    public bool IsOpen => _client?.Connected == true;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsOpen)
            return;

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (IsOpen)
                return;

            var client = new TcpClient { NoDelay = true };
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_connectTimeout);
            try
            {
                await client.ConnectAsync(_host, _port, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                client.Dispose();
                throw new DeviceSessionException($"Timeout conectando Telnet em {_host}:{_port}. Verifique o cabo LAN e o IP do roteador.");
            }
            catch (Exception ex)
            {
                client.Dispose();
                throw new DeviceSessionException($"Falha ao conectar Telnet em {_host}:{_port}: {ex.Message}", ex);
            }

            _client = client;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_client == null || !IsOpen)
            return 0;
        if (cancellationToken.IsCancellationRequested)
            return 0;

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (_client == null || !IsOpen)
                return 0;

            // Entrega bytes filtrados pendentes primeiro
            if (_pending.Count > 0)
                return DrainPending(buffer);

            var stream = _client.GetStream();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < _readTimeout && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_client.Available > 0)
                    {
                        var tmp = System.Buffers.ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length + 64, 4096));
                        try
                        {
                            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            timeoutCts.CancelAfter(_readTimeout);
                            var read = await stream.ReadAsync(tmp.AsMemory(0, tmp.Length), timeoutCts.Token);
                            if (read == 0)
                                return 0; // conexão encerrada pelo roteador
                            FilterIac(tmp.AsSpan(0, read));
                            if (_pending.Count > 0)
                                return DrainPending(buffer);
                        }
                        finally
                        {
                            System.Buffers.ArrayPool<byte>.Shared.Return(tmp);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    return DrainPending(buffer);
                }
                catch (Exception)
                {
                    return 0;
                }

                try { await Task.Delay(15, cancellationToken); }
                catch (OperationCanceledException) { return DrainPending(buffer); }
            }

            return DrainPending(buffer);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_client == null || !IsOpen)
            throw new DeviceSessionException("Conexão Telnet fechada.");

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (_client == null || !IsOpen)
                throw new DeviceSessionException("Conexão Telnet fechada.");
            // Escapa IAC (255) presente nos dados como IAC IAC
            var array = buffer.ToArray();
            var escaped = new byte[array.Length * 2];
            var n = 0;
            foreach (var b in array)
            {
                escaped[n++] = b;
                if (b == IAC) escaped[n++] = IAC;
            }
            await _client.GetStream().WriteAsync(escaped.AsMemory(0, n), cancellationToken);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Break via Telnet (IAC BRK) — best-effort; ROMMON confiável exige o console serial.
    /// </summary>
    public async Task SendBreakAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null || !IsOpen)
            return;
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (_client == null || !IsOpen)
                return;
            try { await _client.GetStream().WriteAsync(new byte[] { IAC, BRK }, cancellationToken); } catch { }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public Task CloseAsync()
    {
        try { _client?.Close(); } catch { }
        _client = null;
        lock (_pending) { _pending.Clear(); }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await CloseAsync();
            _ioLock.Dispose();
        }
    }

    private int DrainPending(Memory<byte> buffer)
    {
        var n = 0;
        while (n < buffer.Length && _pending.Count > 0)
            buffer.Span[n++] = _pending.Dequeue();
        return n;
    }

    /// <summary>
    /// Remove sequências IAC do fluxo, respondendo WONT a DO/WILL e DONT a WILL/DO
    /// (recusa: sem eco, sem negociação de janela, sem modo caractere especial).
    /// Sub-negociações (SB...SE) são descartadas.
    /// </summary>
    private void FilterIac(ReadOnlySpan<byte> data)
    {
        var outbox = new List<byte>(4);
        var i = 0;
        while (i < data.Length)
        {
            var b = data[i];
            if (b != IAC)
            {
                if (b != 0x00) // Telnet NUL de preenchimento: descarta
                    _pending.Enqueue(b);
                i++;
                continue;
            }

            if (i + 1 >= data.Length)
                break; // IAC truncado no fim do pacote: ignora (raro; próximo read completa)
            var cmd = data[i + 1];
            if (cmd == IAC)
            {
                _pending.Enqueue(IAC); // IAC escapado = dado 0xFF
                i += 2;
            }
            else if (cmd == DO || cmd == DONT)
            {
                // Recusa a opção solicitada: responde WONT
                if (i + 2 < data.Length)
                {
                    outbox.Add(IAC); outbox.Add(WONT); outbox.Add(data[i + 2]);
                    i += 3;
                }
                else break;
            }
            else if (cmd == WILL || cmd == WONT)
            {
                // Recusa a oferta: responde DONT
                if (i + 2 < data.Length)
                {
                    outbox.Add(IAC); outbox.Add(DONT); outbox.Add(data[i + 2]);
                    i += 3;
                }
                else break;
            }
            else if (cmd == SB)
            {
                // Descarta até IAC SE
                i += 2;
                while (i + 1 < data.Length && !(data[i] == IAC && data[i + 1] == SE))
                    i++;
                i += 2;
            }
            else
            {
                i += 2; // NOP, DM, BRK, IP, AO, AYT, EC, EL, GA: ignora
            }
        }

        if (outbox.Count > 0)
        {
            try { _client?.GetStream().Write(outbox.ToArray(), 0, outbox.Count); }
            catch { }
        }
    }
}
