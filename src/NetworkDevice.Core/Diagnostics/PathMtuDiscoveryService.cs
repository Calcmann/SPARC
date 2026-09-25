using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Core.Diagnostics;

public sealed record MtuTestResult(
    string TargetIp,
    int DiscoveredPathMtu,
    bool SupportsStandardMtu1500,
    bool SupportsJumboFrame9000,
    long LatencyMs,
    string Details);

public class PathMtuDiscoveryService
{
    private const int IpIcmpHeaderSize = 28; // 20 bytes IP + 8 bytes ICMP

    public async Task<MtuTestResult> DiscoverPathMtuAsync(
        string targetIp,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetIp))
            throw new ArgumentException("O IP de destino não pode ser vazio.", nameof(targetIp));

        if (!IPAddress.TryParse(targetIp, out var ipAddress))
        {
            var hostEntry = await Dns.GetHostEntryAsync(targetIp, cancellationToken).ConfigureAwait(false);
            ipAddress = hostEntry.AddressList[0];
        }

        progress?.Report($"[MTU] Iniciando teste de Path MTU Discovery contra {ipAddress}...");

        using var ping = new Ping();
        var options = new PingOptions(64, dontFragment: true);

        // 1. Testa conectividade básica primeiro (64 bytes = 36 payload + 28 header)
        var baseReply = await SendPingWithPayloadAsync(ping, ipAddress, 36, options, cancellationToken).ConfigureAwait(false);
        if (baseReply.Status != IPStatus.Success)
        {
            progress?.Report($"[MTU] Falha: Host {ipAddress} não respondeu ao ping básico ({baseReply.Status}).");
            return new MtuTestResult(ipAddress.ToString(), 0, false, false, -1, $"Host inacessível ({baseReply.Status})");
        }

        var baseLatency = baseReply.RoundtripTime;
        progress?.Report($"[MTU] Host acessível. Latência base: {baseLatency} ms.");

        // 2. Testa MTU 1500 padrão (1472 payload + 28 cabeçalho)
        bool supports1500 = false;
        var reply1500 = await SendPingWithPayloadAsync(ping, ipAddress, 1472, options, cancellationToken).ConfigureAwait(false);
        if (reply1500.Status == IPStatus.Success)
        {
            supports1500 = true;
            progress?.Report("[MTU] ✅ MTU 1500 bytes suportado sem fragmentação (Padrão Claro OK).");
        }
        else
        {
            progress?.Report($"[MTU] ⚠️ MTU 1500 falhou ({reply1500.Status}). Verificando MTUs menores (PPPoE / Túnel / QinQ)...");
        }

        // 3. Testa Jumbo Frame (9000 bytes = 8972 payload + 28 cabeçalho)
        bool supportsJumbo = false;
        var replyJumbo = await SendPingWithPayloadAsync(ping, ipAddress, 8972, options, cancellationToken).ConfigureAwait(false);
        if (replyJumbo.Status == IPStatus.Success)
        {
            supportsJumbo = true;
            progress?.Report("[MTU] 🚀 Jumbo Frame 9000 bytes SUPORTADO sem fragmentação!");
        }

        // 4. Se 1500 passou e Jumbo passou, MTU mínimo garantido é >= 9000
        int finalMtu = supportsJumbo ? 9000 : (supports1500 ? 1500 : 0);

        if (!supports1500)
        {
            // Busca binária para achar o MTU exato entre 1400 e 1500
            int lowPayload = 1372;  // 1400 MTU
            int highPayload = 1472; // 1500 MTU
            int bestPayload = 0;

            while (lowPayload <= highPayload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int midPayload = (lowPayload + highPayload) / 2;
                var testReply = await SendPingWithPayloadAsync(ping, ipAddress, midPayload, options, cancellationToken).ConfigureAwait(false);

                if (testReply.Status == IPStatus.Success)
                {
                    bestPayload = midPayload;
                    lowPayload = midPayload + 1; // Tenta maior
                }
                else
                {
                    highPayload = midPayload - 1; // Tenta menor
                }
            }

            finalMtu = bestPayload > 0 ? (bestPayload + IpIcmpHeaderSize) : 0;
            progress?.Report($"[MTU] MTU Máximo detectado no enlace: {finalMtu} bytes (Payload: {bestPayload} B).");
        }

        string details = supportsJumbo
            ? $"Link com Jumbo Frame ativo (MTU >= 9000 bytes). RTT: {baseLatency}ms"
            : supports1500
                ? $"Link Ethernet padrão (MTU 1500 bytes OK). RTT: {baseLatency}ms"
                : $"Link com restrição de MTU ({finalMtu} bytes - Possível PPPoE, GRE ou QinQ). RTT: {baseLatency}ms";

        return new MtuTestResult(
            TargetIp: ipAddress.ToString(),
            DiscoveredPathMtu: finalMtu,
            SupportsStandardMtu1500: supports1500,
            SupportsJumboFrame9000: supportsJumbo,
            LatencyMs: baseLatency,
            Details: details);
    }

    private static async Task<PingReply> SendPingWithPayloadAsync(
        Ping ping,
        IPAddress address,
        int payloadSize,
        PingOptions options,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Max(1, payloadSize)];
        Array.Fill(buffer, (byte)'A');

        try
        {
            using var reg = cancellationToken.Register(() => ping.SendAsyncCancel());
            return await ping.SendPingAsync(address, 2000, buffer, options).ConfigureAwait(false);
        }
        catch (PingException)
        {
            // Retorna reply com erro através de exceção encapsulada se necessário
            throw;
        }
    }
}
