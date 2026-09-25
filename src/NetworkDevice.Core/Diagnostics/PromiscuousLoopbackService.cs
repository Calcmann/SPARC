using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SharpPcap;
using SharpPcap.LibPcap;

namespace NetworkDevice.Core.Diagnostics;

public enum LoopbackMode
{
    Layer2MacSwap,
    Layer3IpSwap,
    Layer4UdpSocket
}

public sealed record PromiscuousLoopStats(
    bool IsActive,
    LoopbackMode Mode,
    string DeviceName,
    long TotalPacketsRx,
    long TotalPacketsTx,
    long TotalBytesRx,
    long TotalBytesTx,
    double CurrentRxMbps,
    double CurrentTxMbps,
    long CurrentRxPps,
    long CurrentTxPps,
    string? LastRemoteMac,
    string? LastRemoteIp,
    TimeSpan Elapsed);

public readonly struct PooledPacket(byte[] buffer, int length)
{
    public byte[] Buffer { get; } = buffer;
    public int Length { get; } = length;
}

public class PromiscuousLoopbackService : IDisposable
{
    private static readonly Action<SendQueue, int>? SetQueueLength = InitSetQueueLength();

    private static Action<SendQueue, int>? InitSetQueueLength()
    {
        try
        {
            var setter = typeof(SendQueue).GetProperty("CurrentLength", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetSetMethod(true);
            if (setter != null)
            {
                return (Action<SendQueue, int>)Delegate.CreateDelegate(typeof(Action<SendQueue, int>), setter);
            }
        }
        catch { }
        return null;
    }

    private LibPcapLiveDevice? _pcapDevice;
    private CancellationTokenSource? _cts;
    private Task? _statsTask;
    private Channel<PooledPacket>? _sendChannel;
    private Thread? _senderThread;
    private GCLatencyMode _prevGcMode = GCLatencyMode.Interactive;

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

    private string? _lastRemoteMac;
    private string? _lastRemoteIp;
    private Stopwatch? _stopwatch;
    private LoopbackMode _mode = LoopbackMode.Layer3IpSwap;
    private string _deviceName = string.Empty;
    private byte[] _localMac = new byte[6];

    public bool IsRunning => _pcapDevice != null && _cts != null && !_cts.IsCancellationRequested;

    public event Action<PromiscuousLoopStats>? StatsUpdated;
    public event Action<string>? LogMessage;

    public static bool IsNpcapInstalled()
    {
        try
        {
            var version = Pcap.Version;
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }

    public static string GetPcapVersion()
    {
        try
        {
            return Pcap.Version;
        }
        catch
        {
            return "Não instalado";
        }
    }

    public void Start(
        string adapterNameOrIp, 
        LoopbackMode mode = LoopbackMode.Layer3IpSwap, 
        int bufferSizeBytes = 4 * 1024 * 1024, 
        bool enableKernelBpfFilter = true)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("O serviço de Loop Promíscuo já está em execução.");
        }

        if (!IsNpcapInstalled())
        {
            throw new InvalidOperationException("O driver Npcap não está instalado no sistema. Instale o Npcap para habilitar o modo promíscuo de enlace (L2/L3).");
        }

        _mode = mode;
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
        _lastRemoteMac = null;
        _lastRemoteIp = null;

        var devices = LibPcapLiveDeviceList.Instance;
        LibPcapLiveDevice? selectedDevice = null;

        // Tenta associar por endereço IP ou por nome/descrição da interface
        foreach (var dev in devices)
        {
            var matchIp = dev.Addresses.Any(a => a.Addr != null && a.Addr.ToString().Contains(adapterNameOrIp));
            var matchName = (!string.IsNullOrEmpty(dev.Description) && dev.Description.Contains(adapterNameOrIp, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(dev.Interface.FriendlyName) && dev.Interface.FriendlyName.Contains(adapterNameOrIp, StringComparison.OrdinalIgnoreCase));

            if (matchIp || matchName)
            {
                selectedDevice = dev;
                break;
            }
        }

        selectedDevice ??= devices.FirstOrDefault();

        if (selectedDevice == null)
        {
            throw new InvalidOperationException($"Nenhuma interface de rede compatível com Npcap foi localizada para '{adapterNameOrIp}'.");
        }

        _pcapDevice = selectedDevice;
        _deviceName = _pcapDevice.Interface.FriendlyName ?? _pcapDevice.Description ?? _pcapDevice.Name;

        // Descobre o MAC local da interface para filtrar pacotes próprios
        if (_pcapDevice.MacAddress != null)
        {
            _localMac = _pcapDevice.MacAddress.GetAddressBytes();
        }
        else
        {
            _localMac = new byte[6];
        }

        // Buffer Anti-Bufferbloat otimizado (default 4MB = folga segura para 200M a 1G com delay < 10ms)
        var config = new DeviceConfiguration
        {
            Mode = DeviceModes.Promiscuous,
            ReadTimeout = 1,
            BufferSize = bufferSizeBytes
        };
        _pcapDevice.Open(config);

        // Aplicação de Filtro BPF em nível de Kernel NDIS
        if (enableKernelBpfFilter && _localMac.Any(b => b != 0))
        {
            string macStr = $"{_localMac[0]:x2}:{_localMac[1]:x2}:{_localMac[2]:x2}:{_localMac[3]:x2}:{_localMac[4]:x2}:{_localMac[5]:x2}";
            string bpf = mode == LoopbackMode.Layer3IpSwap
                ? $"ip and not ether src {macStr}"
                : $"not ether src {macStr}";

            try
            {
                _pcapDevice.Filter = bpf;
                LogMessage?.Invoke($"[LOOP PROMÍSCUO] Filtro BPF Kernel ativo: '{bpf}' (Ruídos do Windows bloqueados)");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"[LOOP PROMÍSCUO] Aviso: Filtro BPF não pôde ser ativado ({ex.Message}). Usando filtro em espaço de usuário.");
            }
        }

        _cts = new CancellationTokenSource();
        _stopwatch = Stopwatch.StartNew();

        // Configura baixa latência sustentada no GC durante testes de alta taxa (200M+)
        try
        {
            _prevGcMode = GCSettings.LatencyMode;
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
        catch { }

        // Pipeline desacoplado de transmissão em alta velocidade com fila lock-free expandida (32k slots)
        _sendChannel = Channel.CreateBounded<PooledPacket>(new BoundedChannelOptions(32768)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        var token = _cts.Token;
        _senderThread = new Thread(() => RunSenderLoop(token))
        {
            IsBackground = true,
            Priority = ThreadPriority.Highest,
            Name = "SPARC-Pcap-Sender"
        };
        _senderThread.Start();

        _pcapDevice.OnPacketArrival += OnPacketArrival;
        _pcapDevice.StartCapture();

        LogMessage?.Invoke($"[LOOP PROMÍSCUO L2/L3] Refletor de Enlace ativo em '{_deviceName}' (Modo: {_mode}, Buffer: {bufferSizeBytes / 1024} KB, Zero-Alloc: Ativo, Driver: {GetPcapVersion()})");

        _statsTask = Task.Run(() => RunStatsAsync(token), token);
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var rawPacket = e.GetPacket();
            var data = rawPacket.Data;

            if (data == null || data.Length < 14) return;

            // Filtro de Segurança em C#: Não reflete pacotes originados pelo próprio MAC local
            if (data.Length >= 12 &&
                data[6] == _localMac[0] && data[7] == _localMac[1] && data[8] == _localMac[2] &&
                data[9] == _localMac[3] && data[10] == _localMac[4] && data[11] == _localMac[5])
            {
                return;
            }

            Interlocked.Increment(ref _totalPacketsRx);
            Interlocked.Add(ref _totalBytesRx, data.Length);

            _lastRemoteMac = $"{data[6]:X2}:{data[7]:X2}:{data[8]:X2}:{data[9]:X2}:{data[10]:X2}:{data[11]:X2}";

            // Zero-Alloc: Renta buffer de pool compartilhado para eliminar pausas de GC
            byte[] rented = ArrayPool<byte>.Shared.Rent(data.Length);
            Buffer.BlockCopy(data, 0, rented, 0, data.Length);

            // In-place Wire Swap no buffer rentado:
            // 1. Layer 2 MAC Swap (Bytes 0..5 DA <-> Bytes 6..11 SA)
            for (int i = 0; i < 6; i++)
            {
                byte tmp = rented[i];
                rented[i] = rented[i + 6];
                rented[i + 6] = tmp;
            }

            ushort etherType = (ushort)((rented[12] << 8) | rented[13]);
            int ipOffset = 14;

            // Tratamento de VLAN 802.1Q (se presente, pula 4 bytes)
            if (etherType == 0x8100 && rented.Length >= 18)
            {
                etherType = (ushort)((rented[16] << 8) | rented[17]);
                ipOffset = 18;
            }

            // 2. Layer 3 IP Swap (se IPv4)
            if (_mode == LoopbackMode.Layer3IpSwap && etherType == 0x0800 && rented.Length >= ipOffset + 20)
            {
                int srcIpOffset = ipOffset + 12;
                int dstIpOffset = ipOffset + 16;

                _lastRemoteIp = $"{rented[srcIpOffset]}.{rented[srcIpOffset + 1]}.{rented[srcIpOffset + 2]}.{rented[srcIpOffset + 3]}";

                // Swap IP DA <-> SA
                for (int i = 0; i < 4; i++)
                {
                    byte tmp = rented[srcIpOffset + i];
                    rented[srcIpOffset + i] = rented[dstIpOffset + i];
                    rented[dstIpOffset + i] = tmp;
                }

                // Recalcula IPv4 Header Checksum
                int checksumOffset = ipOffset + 10;
                rented[checksumOffset] = 0;
                rented[checksumOffset + 1] = 0;

                int ihl = (rented[ipOffset] & 0x0F) * 4;
                if (rented.Length >= ipOffset + ihl)
                {
                    ushort newChecksum = CalculateIpChecksum(rented, ipOffset, ihl);
                    rented[checksumOffset] = (byte)(newChecksum >> 8);
                    rented[checksumOffset + 1] = (byte)(newChecksum & 0xFF);
                }

                // Se UDP, recalcula ou anula o checksum UDP para evitar descarte pelo gerador
                byte protocol = rented[ipOffset + 9];
                if (protocol == 17 && rented.Length >= ipOffset + ihl + 8) // UDP
                {
                    int udpOffset = ipOffset + ihl;
                    // Swap portas UDP
                    byte p0 = rented[udpOffset];
                    byte p1 = rented[udpOffset + 1];
                    rented[udpOffset] = rented[udpOffset + 2];
                    rented[udpOffset + 1] = rented[udpOffset + 3];
                    rented[udpOffset + 2] = p0;
                    rented[udpOffset + 3] = p1;

                    // No IPv4 UDP, checksum 0x0000 significa "checksum desabilitado / ignorado"
                    rented[udpOffset + 6] = 0;
                    rented[udpOffset + 7] = 0;
                }
            }

            // Despacha instantaneamente para a fila concorrente sem bloquear a thread de captura
            if (_sendChannel != null && !_sendChannel.Writer.TryWrite(new PooledPacket(rented, data.Length)))
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        catch
        {
            // Protege o callback de captura contra interrupções transitórias
        }
    }

    private void RunSenderLoop(CancellationToken token)
    {
        if (_sendChannel == null) return;
        var reader = _sendChannel.Reader;
        var batch = new List<PooledPacket>(64);

        // Pre-aloca SendQueue reutilizável (128 KB) para zero-allocation contínuo
        SendQueue? reusableQueue = null;
        try
        {
            reusableQueue = new SendQueue(131072);
        }
        catch { }

        while (!token.IsCancellationRequested)
        {
            try
            {
                // Aguarda de forma reativa a chegada de pelo menos um quadro
                if (!reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult())
                {
                    break;
                }

                batch.Clear();
                while (batch.Count < 64 && reader.TryRead(out var packet))
                {
                    batch.Add(packet);
                }

                if (batch.Count == 0 || _pcapDevice == null) continue;

                if (batch.Count == 1)
                {
                    // Tráfego leve / Ping / Frame único: Envio imediato
                    var p = batch[0];
                    _pcapDevice.SendPacket(p.Buffer, p.Length);
                    Interlocked.Increment(ref _totalPacketsTx);
                    Interlocked.Add(ref _totalBytesTx, p.Length);
                    ArrayPool<byte>.Shared.Return(p.Buffer);
                }
                else
                {
                    // Alta carga (ex: 200M a 1G / JDSU Y.1564): Transmissão em Lote via SendQueue Reutilizável
                    bool sentViaQueue = false;
                    if (reusableQueue != null && SetQueueLength != null)
                    {
                        try
                        {
                            SetQueueLength(reusableQueue, 0);
                            int added = 0;

                            for (int i = 0; i < batch.Count; i++)
                            {
                                var pkt = batch[i];
                                if (reusableQueue.CurrentLength + pkt.Length + 16 > 128000) break;

                                var hdr = new PcapHeader(0, 0, (uint)pkt.Length, (uint)pkt.Length);
                                reusableQueue.Add(hdr, pkt.Buffer);
                                added++;
                            }

                            if (added > 0)
                            {
                                reusableQueue.Transmit(_pcapDevice, SendQueueTransmitModes.Normal);
                                for (int i = 0; i < added; i++)
                                {
                                    Interlocked.Increment(ref _totalPacketsTx);
                                    Interlocked.Add(ref _totalBytesTx, batch[i].Length);
                                }
                                sentViaQueue = true;

                                // Envia pacotes residuais que excederam a fila
                                for (int i = added; i < batch.Count; i++)
                                {
                                    _pcapDevice.SendPacket(batch[i].Buffer, batch[i].Length);
                                    Interlocked.Increment(ref _totalPacketsTx);
                                    Interlocked.Add(ref _totalBytesTx, batch[i].Length);
                                }
                            }
                        }
                        catch
                        {
                            sentViaQueue = false;
                        }
                    }

                    if (!sentViaQueue)
                    {
                        // Fallback veloz
                        for (int i = 0; i < batch.Count; i++)
                        {
                            _pcapDevice.SendPacket(batch[i].Buffer, batch[i].Length);
                            Interlocked.Increment(ref _totalPacketsTx);
                            Interlocked.Add(ref _totalBytesTx, batch[i].Length);
                        }
                    }

                    // Retorna todos os buffers ao pool (Zero-Allocation contínuo)
                    for (int i = 0; i < batch.Count; i++)
                    {
                        ArrayPool<byte>.Shared.Return(batch[i].Buffer);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Protege contra falhas transitórias
            }
        }

        reusableQueue?.Dispose();
    }

    private static ushort CalculateIpChecksum(byte[] buffer, int offset, int length)
    {
        uint sum = 0;
        for (int i = 0; i < length; i += 2)
        {
            ushort word = (ushort)((buffer[offset + i] << 8) | buffer[offset + i + 1]);
            sum += word;
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
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

                _currentRxMbps = (deltaBytesRx * 8.0) / (deltaSec * 1_000_000.0);
                _currentTxMbps = (deltaBytesTx * 8.0) / (deltaSec * 1_000_000.0);
                _currentRxPps = (long)(deltaPktsRx / deltaSec);
                _currentTxPps = (long)(deltaPktsTx / deltaSec);

                var stats = new PromiscuousLoopStats(
                    IsActive: true,
                    Mode: _mode,
                    DeviceName: _deviceName,
                    TotalPacketsRx: curPktsRx,
                    TotalPacketsTx: curPktsTx,
                    TotalBytesRx: curBytesRx,
                    TotalBytesTx: curBytesTx,
                    CurrentRxMbps: _currentRxMbps,
                    CurrentTxMbps: _currentTxMbps,
                    CurrentRxPps: _currentRxPps,
                    CurrentTxPps: _currentTxPps,
                    LastRemoteMac: _lastRemoteMac,
                    LastRemoteIp: _lastRemoteIp,
                    Elapsed: curElapsed);

                StatsUpdated?.Invoke(stats);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }
        }
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
        _lastRemoteMac = null;
        _lastRemoteIp = null;
        _stopwatch?.Restart();

        LogMessage?.Invoke("[LOOP PROMÍSCUO] Contadores de tráfego e volume foram zerados.");

        StatsUpdated?.Invoke(new PromiscuousLoopStats(
            IsActive: IsRunning,
            Mode: _mode,
            DeviceName: _deviceName,
            TotalPacketsRx: 0,
            TotalPacketsTx: 0,
            TotalBytesRx: 0,
            TotalBytesTx: 0,
            CurrentRxMbps: 0,
            CurrentTxMbps: 0,
            CurrentRxPps: 0,
            CurrentTxPps: 0,
            LastRemoteMac: null,
            LastRemoteIp: null,
            Elapsed: TimeSpan.Zero));
    }

    public async Task StopAsync()
    {
        if (!IsRunning) return;

        try
        {
            _cts?.Cancel();
            _sendChannel?.Writer.TryComplete();
            _pcapDevice?.StopCapture();
            _pcapDevice?.Close();
        }
        catch { }

        if (_senderThread != null && _senderThread.IsAlive)
        {
            try
            {
                _senderThread.Join(500);
            }
            catch { }
        }

        if (_statsTask != null)
        {
            try
            {
                await _statsTask.ConfigureAwait(false);
            }
            catch { }
        }

        try
        {
            GCSettings.LatencyMode = _prevGcMode;
        }
        catch { }

        _pcapDevice = null;
        _cts?.Dispose();
        _cts = null;
        _senderThread = null;
        _sendChannel = null;
        _stopwatch?.Stop();

        LogMessage?.Invoke($"[LOOP PROMÍSCUO] Refletor desativado. Total refletido: {_totalPacketsTx:N0} quadros.");
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _sendChannel?.Writer.TryComplete();
            _pcapDevice?.Dispose();
            _cts?.Dispose();
        }
        catch { }
        finally
        {
            try { GCSettings.LatencyMode = _prevGcMode; } catch { }
            _pcapDevice = null;
            _cts = null;
            _sendChannel = null;
            _senderThread = null;
        }
    }
}
