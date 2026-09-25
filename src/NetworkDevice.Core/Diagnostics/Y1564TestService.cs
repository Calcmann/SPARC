using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Core.Diagnostics;

public sealed record MefTierProfile(
    string Name,
    string DistanceLabel,
    double FlrMaxPercent, // FLR padrão 0.02% (ajustável)
    double FtdMaxMs,      // FTD (Delay / Latência) ms
    double IfdvMaxMs)     // IFDV (Jitter) ms
{
    public static readonly MefTierProfile UpTo250Km = new("≤ 250Km (Metropolitano)", "≤ 250Km", 0.02, 74.0, 28.0);
    public static readonly MefTierProfile UpTo1200Km = new("≤ 1200 Km (Interestadual)", "≤ 1200 Km", 0.02, 250.0, 160.0);
    public static readonly MefTierProfile UpTo7000Km = new("≤ 7000 Km (Nacional Extremo)", "≤ 7000 Km", 0.02, 460.0, 260.0);
}

public enum Y1564TestMode
{
    LoopbackBidirectional, // Padrão ITU-T Y.1564: Requer Refletor/Loopback UDP na ponta remota
    GatewayTrafficLoad     // Validação contra Gateway/PE: Carga de tráfego e análise de saturação/perda sob estresse
}

public sealed record Y1564TestConfig(
    string RemoteIp,
    int RemotePort = 5001,
    double TargetBandwidthMbps = 200.0,
    int FrameSizeBytes = 1500,
    bool IsVariableFrame = false,
    int[]? VariableFrameSizes = null,
    string FrameSizeLabel = "1500",
    TimeSpan Duration = default,
    string? ClientName = null,
    string? Designation = null,
    double SlaLossPercent = 0.02,
    double SlaDelayMs = 250.0,
    double SlaJitterMs = 160.0,
    string DistanceTier = "≤ 1200 Km",
    string? SourceIpAddress = null,
    Y1564TestMode Mode = Y1564TestMode.LoopbackBidirectional);

public sealed record Y1564LiveProgress(
    TimeSpan Elapsed,
    TimeSpan TotalDuration,
    double PercentComplete,
    long TxPackets,
    long RxPackets,
    double CurrentTxMbps,
    double CurrentRxMbps,
    double CurrentLossPercent,
    double CurrentDelayAvgMs,
    double CurrentDelayMinMs,
    double CurrentDelayMaxMs,
    double CurrentJitterAvgMs,
    double CurrentJitterMaxMs,
    long OosPackets,
    bool IsLoopbackDetected = false,
    long TxDroppedLocally = 0,
    double IcmpDelayAvgMs = 0);

public sealed record Y1564Result(
    DateTime StartTime,
    DateTime EndTime,
    TimeSpan Duration,
    string ClientName,
    string Designation,
    string RemoteIp,
    int RemotePort,
    int FrameSize,
    string FrameSizeDescription,
    double NetworkUlrMbps,
    double RxThroughputMbps,
    double LossPercentage,
    double DelayAvgMs,
    double DelayMinMs,
    double DelayMaxMs,
    double JitterAvgMs,
    double JitterMaxMs,
    double SlaJitterMs,
    double SlaDelayMs,
    double SlaLossPercent,
    string DistanceTier,
    long TxPackets,
    long RxPackets,
    double LostPercentage,
    long OosPackets,
    bool IsPass,
    string SummaryText,
    string DetailsMessage);

public class Y1564TestService
{
    private static readonly byte[] MagicBytes = "Y156"u8.ToArray();

    public async Task<Y1564Result> RunTestAsync(
        Y1564TestConfig config,
        IProgress<Y1564LiveProgress>? progress = null,
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(config.RemoteIp))
            throw new ArgumentException("O IP do QT Remoto não pode ser vazio.", nameof(config.RemoteIp));

        if (!IPAddress.TryParse(config.RemoteIp, out var remoteIpAddress))
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(config.RemoteIp, cancellationToken);
                remoteIpAddress = entry.AddressList[0];
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Não foi possível resolver o IP ou nome do QT Remoto '{config.RemoteIp}': {ex.Message}", ex);
            }
        }

        var duration = config.Duration <= TimeSpan.Zero ? TimeSpan.FromMinutes(15) : config.Duration;
        var isVariable = config.IsVariableFrame || (config.VariableFrameSizes != null && config.VariableFrameSizes.Length > 1);
        int[] frameSizes = isVariable && config.VariableFrameSizes?.Length > 0
            ? config.VariableFrameSizes
            : new[] { Math.Clamp(config.FrameSizeBytes, 64, 9216) };

        var frameSizeLabel = !string.IsNullOrWhiteSpace(config.FrameSizeLabel)
            ? config.FrameSizeLabel
            : isVariable
                ? "Variável (IMIX)"
                : frameSizes[0] >= 9000
                    ? $"{frameSizes[0]} (Jumbo)"
                    : frameSizes[0].ToString();

        var targetMbps = Math.Max(1.0, config.TargetBandwidthMbps);
        var remoteEndpoint = new IPEndPoint(remoteIpAddress, Math.Clamp(config.RemotePort, 1, 65535));

        logger?.Invoke($"[Y.1564] Iniciando certificação ITU-T Y.1564 contra QT {remoteEndpoint.Address}:{remoteEndpoint.Port}");
        logger?.Invoke($"[Y.1564] Parâmetros: CIR/ULR = {targetMbps:F1} Mbps, Frame = {frameSizeLabel}, Duração = {duration.TotalMinutes:F1} min");
        logger?.Invoke($"[Y.1564] SLA Limites: FLR <= {config.SlaLossPercent:F2}%, FTD <= {config.SlaDelayMs:F0} ms, IFDV <= {config.SlaJitterMs:F0} ms ({config.DistanceTier})");

        var startTime = DateTime.Now;
        var sw = Stopwatch.StartNew();

        // Buffers pré-alocados para cada tamanho de quadro (suporte a Fixo, IMIX Variável e Jumbo Frames)
        var packetBuffers = new byte[frameSizes.Length][];
        var l1Sizes = new double[frameSizes.Length];
        for (int b = 0; b < frameSizes.Length; b++)
        {
            var fs = frameSizes[b];
            l1Sizes[b] = fs + 20; // 20 bytes ethernet L1 framing overhead (7 Preamble + 1 SFD + 12 IFG)
            var pSize = Math.Max(24, fs - 28); // 20 bytes IP + 8 bytes UDP
            packetBuffers[b] = new byte[pSize];
            Buffer.BlockCopy(MagicBytes, 0, packetBuffers[b], 0, 4);
        }

        var avgL1PacketSize = l1Sizes.Length == 1 ? l1Sizes[0] : (l1Sizes.Sum() / l1Sizes.Length);
        var targetPacketsPerSec = (targetMbps * 1_000_000.0) / (avgL1PacketSize * 8.0);

        long totalTx = 0;
        long totalRx = 0;
        long totalOos = 0;
        long lastExpectedSeq = -1;

        double udpDelaySum = 0;
        long udpDelaySamples = 0;
        double udpMinDelay = double.MaxValue;
        double udpMaxDelay = 0;

        double udpJitterSum = 0;
        long udpJitterSamples = 0;
        double udpMaxJitter = 0;
        double lastUdpRtt = -1;

        double icmpDelaySum = 0;
        long icmpDelaySamples = 0;
        double icmpMinDelay = double.MaxValue;
        double icmpMaxDelay = 0;

        double icmpJitterSum = 0;
        long icmpJitterSamples = 0;
        double icmpMaxJitter = 0;
        double lastIcmpRtt = -1;

        var lockObj = new object();
        bool warnedNoReflector = false;

        long totalTxDropped = 0;
        long probeSent = 0;
        long probeReceived = 0;

        using var udpClient = new UdpClient();
        udpClient.Client.ReceiveBufferSize = 4 * 1024 * 1024;
        udpClient.Client.SendBufferSize = 4 * 1024 * 1024;

        IPAddress? localIp = null;
        if (!string.IsNullOrWhiteSpace(config.SourceIpAddress) && IPAddress.TryParse(config.SourceIpAddress, out var parsedIp))
        {
            localIp = parsedIp;
            try
            {
                udpClient.Client.Bind(new IPEndPoint(localIp, 0));
                logger?.Invoke($"[Y.1564] Interface local vinculada ao IP {localIp}");

                // Validação preventiva da velocidade física da interface local
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    var props = nic.GetIPProperties();
                    if (props.UnicastAddresses.Any(u => u.Address.Equals(localIp)))
                    {
                        if (nic.Speed > 0)
                        {
                            var nicSpeedMbps = nic.Speed / 1_000_000.0;
                            if (targetMbps > (nicSpeedMbps * 0.98))
                            {
                                logger?.Invoke($"[Y.1564] ALERTA CRÍTICO: A banda solicitada ({targetMbps:F0} Mbps) excede a capacidade física da interface '{nic.Name}' ({nicSpeedMbps:F0} Mbps)!");
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.Invoke($"[Y.1564] Aviso: Falha ao vincular IP local {localIp}: {ex.Message}");
            }
        }

        void ProcessReceivedPacket(byte[] buffer, int length)
        {
            if (length >= 24 &&
                buffer[0] == MagicBytes[0] &&
                buffer[1] == MagicBytes[1] &&
                buffer[2] == MagicBytes[2] &&
                buffer[3] == MagicBytes[3])
            {
                var seq = BitConverter.ToInt64(buffer, 4);
                var sendTicks = BitConverter.ToInt64(buffer, 12);
                var nowTicks = Stopwatch.GetTimestamp();

                Interlocked.Increment(ref totalRx);

                if (lastExpectedSeq >= 0 && seq != lastExpectedSeq + 1)
                {
                    Interlocked.Increment(ref totalOos);
                }
                lastExpectedSeq = seq;

                var rttMs = Math.Max(0.01, (nowTicks - sendTicks) * 1000.0 / Stopwatch.Frequency);
                lock (lockObj)
                {
                    udpDelaySum += rttMs;
                    udpDelaySamples++;
                    if (rttMs < udpMinDelay) udpMinDelay = rttMs;
                    if (rttMs > udpMaxDelay) udpMaxDelay = rttMs;

                    if (lastUdpRtt >= 0)
                    {
                        var diff = Math.Abs(rttMs - lastUdpRtt);
                        udpJitterSum += diff;
                        udpJitterSamples++;
                        if (diff > udpMaxJitter) udpMaxJitter = diff;
                    }
                    lastUdpRtt = rttMs;
                }
            }
        }

        using var ctsLinked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Tarefa receptora de pacotes refletidos pelo QT (UDP Reflector / Loopback)
        var receiverTask = Task.Run(async () =>
        {
            while (!ctsLinked.Token.IsCancellationRequested)
            {
                try
                {
                    var rcvResult = await udpClient.ReceiveAsync(ctsLinked.Token);
                    ProcessReceivedPacket(rcvResult.Buffer, rcvResult.Buffer.Length);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignora erros de recepção pontuais
                }
            }
        }, ctsLinked.Token);

        // Amostrador ICMP/Probe complementar (para medição contínua de latência, jitter e detecção de saturação/perda sob carga)
        var probeTask = Task.Run(async () =>
        {
            using var ping = new Ping();
            var pingOptions = new PingOptions(64, true);
            var pingData = new byte[32];

            while (!ctsLinked.Token.IsCancellationRequested && sw.Elapsed < duration)
            {
                Interlocked.Increment(ref probeSent);
                try
                {
                    var psw = Stopwatch.StartNew();
                    var reply = await ping.SendPingAsync(remoteEndpoint.Address, 1500, pingData, pingOptions);
                    psw.Stop();

                    if (reply.Status == IPStatus.Success)
                    {
                        Interlocked.Increment(ref probeReceived);
                        var rtt = reply.RoundtripTime > 0 ? (double)reply.RoundtripTime : psw.Elapsed.TotalMilliseconds;
                        rtt = Math.Max(0.1, rtt);

                        lock (lockObj)
                        {
                            icmpDelaySum += rtt;
                            icmpDelaySamples++;
                            if (rtt < icmpMinDelay) icmpMinDelay = rtt;
                            if (rtt > icmpMaxDelay) icmpMaxDelay = rtt;

                            if (lastIcmpRtt >= 0)
                            {
                                var diff = Math.Abs(rtt - lastIcmpRtt);
                                icmpJitterSum += diff;
                                icmpJitterSamples++;
                                if (diff > icmpMaxJitter) icmpMaxJitter = diff;
                            }
                            lastIcmpRtt = rtt;
                        }
                    }
                }
                catch
                {
                    // Timeout ou falha de ICMP (conta como perda de sonda sob carga)
                }

                try
                {
                    await Task.Delay(100, ctsLinked.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, ctsLinked.Token);

        // Transmissor de fluxo calibrado ao CIR (Network ULR) via Token Bucket com alta precisão
        var lastProgressReport = Stopwatch.StartNew();
        var lastTxCount = 0L;
        var lastRxCount = 0L;
        var lastSampleElapsed = sw.Elapsed;
        double smoothedTxRate = 0;
        double smoothedRxRate = 0;

        var startTicks = Stopwatch.GetTimestamp();

        try
        {
            while (sw.Elapsed < duration && !cancellationToken.IsCancellationRequested)
            {
                var nowTicks = Stopwatch.GetTimestamp();
                var elapsedSec = (double)(nowTicks - startTicks) / Stopwatch.Frequency;
                var expectedTotalPackets = (long)(elapsedSec * targetPacketsPerSec);

                var curTx = Interlocked.Read(ref totalTx);
                var packetsDeficit = expectedTotalPackets - curTx;

                if (packetsDeficit > 0)
                {
                    // Envia em rajadas dosadas (máximo 16 pacotes por ciclo para manter fluxo uniforme sem jitter)
                    var burstSize = (int)Math.Min(16, packetsDeficit);

                    for (int i = 0; i < burstSize; i++)
                    {
                        var seq = curTx + i + 1;
                        var ts = Stopwatch.GetTimestamp();

                        var bufIdx = (int)(seq % packetBuffers.Length);
                        var pBuf = packetBuffers[bufIdx];

                        BitConverter.TryWriteBytes(pBuf.AsSpan(4, 8), seq);
                        BitConverter.TryWriteBytes(pBuf.AsSpan(12, 8), ts);

                        try
                        {
                            int bytesSent = udpClient.Client.SendTo(pBuf, 0, pBuf.Length, SocketFlags.None, remoteEndpoint);
                            if (bytesSent > 0)
                            {
                                Interlocked.Increment(ref totalTx);
                            }
                        }
                        catch
                        {
                            // Buffer de socket local esgotado (indício de saturação da interface local)
                            Interlocked.Increment(ref totalTxDropped);
                            Thread.SpinWait(80);
                        }
                    }
                }
                else
                {
                    // Estamos adiantados em relação à cadência exata do CIR
                    var packetsAhead = curTx - expectedTotalPackets;
                    var msAhead = (packetsAhead / targetPacketsPerSec) * 1000.0;

                    if (msAhead >= 2.0)
                    {
                        await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        Thread.SpinWait(80);
                    }
                }

                // Emissão periódica de progresso (a cada ~200ms com suavização EMA)
                if (lastProgressReport.ElapsedMilliseconds >= 200)
                {
                    var curElapsed = sw.Elapsed;
                    var deltaSec = Math.Max(0.001, (curElapsed - lastSampleElapsed).TotalSeconds);

                    var currentTxSnap = Interlocked.Read(ref totalTx);
                    var currentRxSnap = Interlocked.Read(ref totalRx);

                    var deltaTx = currentTxSnap - lastTxCount;
                    var deltaRx = currentRxSnap - lastRxCount;

                    var rawTxRate = (deltaTx * avgL1PacketSize * 8.0) / (deltaSec * 1_000_000.0);
                    var rawRxRate = (deltaRx * avgL1PacketSize * 8.0) / (deltaSec * 1_000_000.0);

                    // Suavização exponencial (EMA) para estabilizar a medição visual instantânea
                    smoothedTxRate = (smoothedTxRate <= 0)
                        ? rawTxRate
                        : (0.75 * smoothedTxRate + 0.25 * rawTxRate);

                    smoothedRxRate = (smoothedRxRate <= 0)
                        ? rawRxRate
                        : (0.75 * smoothedRxRate + 0.25 * rawRxRate);

                    lastTxCount = currentTxSnap;
                    lastRxCount = currentRxSnap;
                    lastSampleElapsed = curElapsed;
                    lastProgressReport.Restart();

                    double avgDelay, minD, maxD, avgJitter, maxJ, icmpAvg;
                    lock (lockObj)
                    {
                        icmpAvg = icmpDelaySamples > 0 ? (icmpDelaySum / icmpDelaySamples) : 0;
                        if (config.Mode == Y1564TestMode.LoopbackBidirectional)
                        {
                            avgDelay = udpDelaySamples > 0 ? (udpDelaySum / udpDelaySamples) : 0;
                            minD = udpDelaySamples > 0 ? udpMinDelay : 0;
                            maxD = udpDelaySamples > 0 ? udpMaxDelay : 0;
                            avgJitter = udpJitterSamples > 0 ? (udpJitterSum / udpJitterSamples) : 0;
                            maxJ = udpMaxJitter;
                        }
                        else // GatewayTrafficLoad
                        {
                            avgDelay = icmpAvg;
                            minD = icmpDelaySamples > 0 ? icmpMinDelay : 0;
                            maxD = icmpDelaySamples > 0 ? icmpMaxDelay : 0;
                            avgJitter = icmpJitterSamples > 0 ? (icmpJitterSum / icmpJitterSamples) : 0;
                            maxJ = icmpMaxJitter;
                        }
                    }

                    long sentProbes = Interlocked.Read(ref probeSent);
                    long rcvProbes = Interlocked.Read(ref probeReceived);
                    double currentProbeLoss = sentProbes > 0 ? Math.Max(0.0, ((double)(sentProbes - rcvProbes) / sentProbes) * 100.0) : 0.0;

                    // Alerta em tempo real de diagnóstico caso o host responda a ping mas nenhum UDP seja refletido
                    if (config.Mode == Y1564TestMode.LoopbackBidirectional && !warnedNoReflector && sw.Elapsed.TotalSeconds >= 3.0)
                    {
                        if (currentTxSnap >= 100 && currentRxSnap == 0)
                        {
                            warnedNoReflector = true;
                            if (rcvProbes > 0)
                            {
                                logger?.Invoke($"[Y.1564] ⚠️ AVISO: O host {config.RemoteIp} responde a Ping ICMP (~{icmpAvg:F1} ms), mas NENHUM pacote UDP foi refletido na porta {config.RemotePort} (Rx = 0).");
                                logger?.Invoke($"[Y.1564] 💡 DICA: Se você está certificando o link direto contra o Gateway/PE (sem equipamento VIAVI na ponta remota), utilize o modo '⚡ Validação de Circuito contra Gateway / PE' para testar saturação e perda sob estresse sem refletor.");
                                logger?.Invoke($"[Y.1564] 💡 Se houver um testador VIAVI na outra ponta, confira se o Smart Loopback UDP {config.RemotePort} está ativo e se a Interface de Origem (PC) selecionada é a interface de rede correta.");
                            }
                        }
                    }

                    double currentLossPct;
                    double displayRxRate;

                    if (config.Mode == Y1564TestMode.LoopbackBidirectional)
                    {
                        if (currentRxSnap > 0 && currentTxSnap > 0)
                        {
                            currentLossPct = Math.Max(0.0, ((double)(currentTxSnap - currentRxSnap) / currentTxSnap) * 100.0);
                            displayRxRate = smoothedRxRate;
                        }
                        else
                        {
                            // Se não há nenhum pacote refletido recebido, a perda é 100% (sem loopback)
                            currentLossPct = currentTxSnap > 0 ? 100.0 : 0.0;
                            displayRxRate = 0.0;
                        }
                    }
                    else // GatewayTrafficLoad
                    {
                        currentLossPct = currentProbeLoss;
                        displayRxRate = smoothedTxRate;
                    }

                    var pct = Math.Min(100.0, (curElapsed.TotalMilliseconds / duration.TotalMilliseconds) * 100.0);

                    progress?.Report(new Y1564LiveProgress(
                        Elapsed: curElapsed,
                        TotalDuration: duration,
                        PercentComplete: pct,
                        TxPackets: currentTxSnap,
                        RxPackets: currentRxSnap,
                        CurrentTxMbps: smoothedTxRate,
                        CurrentRxMbps: displayRxRate,
                        CurrentLossPercent: currentLossPct,
                        CurrentDelayAvgMs: avgDelay,
                        CurrentDelayMinMs: minD,
                        CurrentDelayMaxMs: maxD,
                        CurrentJitterAvgMs: avgJitter,
                        CurrentJitterMaxMs: maxJ,
                        OosPackets: Interlocked.Read(ref totalOos),
                        IsLoopbackDetected: currentRxSnap > 0,
                        TxDroppedLocally: Interlocked.Read(ref totalTxDropped),
                        IcmpDelayAvgMs: icmpAvg));
                }
            }
        }
        finally
        {
            ctsLinked.Cancel();
            try { await Task.WhenAll(receiverTask, probeTask); } catch { }
            sw.Stop();
        }

        var endTime = DateTime.Now;
        var actualDuration = sw.Elapsed;

        double finalAvgDelay, finalMinDelay, finalMaxDelay, finalAvgJitter, finalMaxJitter;
        lock (lockObj)
        {
            if (config.Mode == Y1564TestMode.LoopbackBidirectional)
            {
                finalAvgDelay = udpDelaySamples > 0 ? (udpDelaySum / udpDelaySamples) : 0;
                finalMinDelay = udpDelaySamples > 0 ? udpMinDelay : 0;
                finalMaxDelay = udpDelaySamples > 0 ? udpMaxDelay : 0;
                finalAvgJitter = udpJitterSamples > 0 ? (udpJitterSum / udpJitterSamples) : 0;
                finalMaxJitter = udpMaxJitter;
            }
            else
            {
                finalAvgDelay = icmpDelaySamples > 0 ? (icmpDelaySum / icmpDelaySamples) : 0;
                finalMinDelay = icmpDelaySamples > 0 ? icmpMinDelay : 0;
                finalMaxDelay = icmpDelaySamples > 0 ? icmpMaxDelay : 0;
                finalAvgJitter = icmpJitterSamples > 0 ? (icmpJitterSum / icmpJitterSamples) : 0;
                finalMaxJitter = icmpMaxJitter;
            }
        }

        var finalTx = Interlocked.Read(ref totalTx);
        var finalRx = Interlocked.Read(ref totalRx);
        var finalOos = Interlocked.Read(ref totalOos);
        var finalTxDropped = Interlocked.Read(ref totalTxDropped);
        long finalProbeSent = Interlocked.Read(ref probeSent);
        long finalProbeReceived = Interlocked.Read(ref probeReceived);

        double finalLossPct;
        double finalRxMbps;
        bool isPass;
        string summaryText;
        string detailsMsg;

        if (config.Mode == Y1564TestMode.LoopbackBidirectional)
        {
            if (finalRx > 0)
            {
                finalLossPct = Math.Max(0.0, ((double)(finalTx - finalRx) / finalTx) * 100.0);
                finalRxMbps = (finalRx * avgL1PacketSize * 8.0) / (actualDuration.TotalSeconds * 1_000_000.0);
            }
            else
            {
                // Sem pacotes refletidos: perda total (100%), vazão recebida zero!
                finalLossPct = 100.0;
                finalRx = 0;
                finalRxMbps = 0.0;
            }

            // Critérios de Aceite Y.1564 Bidirecional:
            // 1. Refletor deve responder (finalRx > 0 e finalLossPct <= SlaLossPercent)
            // 2. Vazão deve atingir a meta CIR (tolerância estrita de SLA)
            // 3. Latência e Jitter dentro do SLA
            bool bandMet = finalRxMbps >= (targetMbps * (1.0 - (config.SlaLossPercent / 100.0)));
            bool lossMet = finalRx > 0 && finalLossPct <= config.SlaLossPercent;
            bool delayMet = udpDelaySamples > 0 && (finalAvgDelay <= config.SlaDelayMs || finalMinDelay <= config.SlaDelayMs);
            bool jitterMet = udpJitterSamples == 0 || (finalMaxJitter <= config.SlaJitterMs || finalAvgJitter <= config.SlaJitterMs);

            isPass = bandMet && lossMet && delayMet && jitterMet;

            if (finalRx == 0)
            {
                summaryText = "FAIL";
                detailsMsg = $"Certificação Y.1564 REPROVADA (FAIL). Nenhum pacote refletido foi recebido (Rx = 0 de Tx = {finalTx:N0}, Perda = 100,00%). O destino {config.RemoteIp}:{config.RemotePort} não possui um Refletor / Loopback UDP ativo. Para testar sem loopback contra o Gateway da operadora, utilize o modo 'Validação contra Gateway'.";
            }
            else if (!bandMet || !lossMet)
            {
                summaryText = "FAIL";
                detailsMsg = $"Certificação Y.1564 REPROVADA (FAIL). Capacidade do link excedida ou policiamento ativo! Vazão recebida: {finalRxMbps:F2} Mbps (Alvo CIR: {targetMbps:F0} Mbps). Perda de quadros (FLR): {finalLossPct:F2}% (Limite SLA: <= {config.SlaLossPercent:F2}%).";
            }
            else if (!delayMet || !jitterMet)
            {
                summaryText = "FAIL";
                detailsMsg = $"Certificação Y.1564 REPROVADA (FAIL). SLA de atraso/variação não atendido. Latência: {finalAvgDelay:F2} ms (SLA: <= {config.SlaDelayMs:F0} ms), Jitter: {finalMaxJitter:F2} ms (SLA: <= {config.SlaJitterMs:F0} ms).";
            }
            else
            {
                summaryText = "PASS";
                detailsMsg = $"Certificação Y.1564 APROVADA (PASS). Perfil MEF: {config.DistanceTier} | Vazão L1: {finalRxMbps:F2} Mbps (Alvo: {targetMbps:F0} Mbps) | Perda FLR: {finalLossPct:F2}% (SLA: <= {config.SlaLossPercent:F2}%) | Latência: {finalAvgDelay:F2} ms | Jitter: {finalAvgJitter:F2} ms.";
            }
        }
        else // GatewayTrafficLoad
        {
            double probeLossPct = finalProbeSent > 0 ? Math.Max(0.0, ((double)(finalProbeSent - finalProbeReceived) / (double)finalProbeSent) * 100.0) : (icmpDelaySamples > 0 ? 0.0 : 100.0);
            finalLossPct = probeLossPct;
            finalRx = finalProbeReceived;
            double actualTxMbps = (finalTx * avgL1PacketSize * 8.0) / (actualDuration.TotalSeconds * 1_000_000.0);
            finalRxMbps = actualTxMbps;

            bool bandMet = actualTxMbps >= (targetMbps * 0.95);
            bool lossMet = finalProbeSent > 0 && probeLossPct <= config.SlaLossPercent;
            bool delayMet = icmpDelaySamples > 0 && (finalAvgDelay <= config.SlaDelayMs);
            bool jitterMet = icmpJitterSamples == 0 || (finalMaxJitter <= config.SlaJitterMs || finalAvgJitter <= config.SlaJitterMs);
            bool noLocalDrop = finalTxDropped == 0;

            isPass = bandMet && lossMet && delayMet && jitterMet && noLocalDrop;

            if (!noLocalDrop || !bandMet)
            {
                summaryText = "FAIL";
                detailsMsg = $"Validação de Link REPROVADA (FAIL). A interface local ou o link não suportou a taxa de {targetMbps:F0} Mbps (Taxa Tx escoada: {actualTxMbps:F2} Mbps, {finalTxDropped:N0} descartes de buffer local).";
            }
            else if (!lossMet)
            {
                summaryText = "FAIL";
                detailsMsg = $"Validação de Link REPROVADA (FAIL). Saturação de link detectada sob carga de {targetMbps:F0} Mbps! Perda de pacotes das sondas: {probeLossPct:F2}% (Limite SLA: <= {config.SlaLossPercent:F2}%).";
            }
            else if (!delayMet || !jitterMet)
            {
                summaryText = "FAIL";
                detailsMsg = $"Validação de Link REPROVADA (FAIL). Congestionamento de link (Bufferbloat) sob carga de {targetMbps:F0} Mbps! Latência: {finalAvgDelay:F2} ms (SLA: <= {config.SlaDelayMs:F0} ms), Jitter: {finalMaxJitter:F2} ms (SLA: <= {config.SlaJitterMs:F0} ms).";
            }
            else
            {
                summaryText = "PASS";
                detailsMsg = $"Validação de Link APROVADA (PASS). O circuito suportou a carga contínua de {actualTxMbps:F2} Mbps contra o Gateway {config.RemoteIp} sem saturação. Perda: {probeLossPct:F2}%, Latência: {finalAvgDelay:F2} ms, Jitter: {finalAvgJitter:F2} ms.";
            }
        }

        logger?.Invoke($"[Y.1564] {detailsMsg}");

        return new Y1564Result(
            StartTime: startTime,
            EndTime: endTime,
            Duration: actualDuration,
            ClientName: config.ClientName ?? "NORTEL ELETR",
            Designation: config.Designation ?? "LFS/IP/02924",
            RemoteIp: config.RemoteIp,
            RemotePort: config.RemotePort,
            FrameSize: frameSizes[0],
            FrameSizeDescription: frameSizeLabel,
            NetworkUlrMbps: targetMbps,
            RxThroughputMbps: finalRxMbps,
            LossPercentage: finalLossPct,
            DelayAvgMs: finalAvgDelay,
            DelayMinMs: finalMinDelay,
            DelayMaxMs: finalMaxDelay,
            JitterAvgMs: finalAvgJitter,
            JitterMaxMs: finalMaxJitter,
            SlaJitterMs: config.SlaJitterMs,
            SlaDelayMs: config.SlaDelayMs,
            SlaLossPercent: config.SlaLossPercent,
            DistanceTier: config.DistanceTier,
            TxPackets: finalTx,
            RxPackets: finalRx,
            LostPercentage: finalLossPct,
            OosPackets: finalOos,
            IsPass: isPass,
            SummaryText: summaryText,
            DetailsMessage: detailsMsg);
    }
}
