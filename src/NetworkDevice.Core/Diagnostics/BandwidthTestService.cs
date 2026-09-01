using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace NetworkDevice.Core.Diagnostics;

public sealed record BandwidthTestResult(
    double DownloadMbps,
    double UploadMbps,
    double LatencyMs,
    double JitterMs,
    string Provider,
    string TestType,
    bool IsSuccess,
    string Message);

public class BandwidthTestService
{
    private readonly Func<string, Task>? _logger;
    private static readonly HttpClient HttpClientInstance = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public BandwidthTestService(Func<string, Task>? logger = null)
    {
        _logger = logger;
    }

    private async Task LogAsync(string message)
    {
        if (_logger != null)
        {
            await _logger(message);
        }
    }

    /// <summary>
    /// Executa teste de banda nativo HTTP medindo download em Mbps contra CDNs públicas neutras (Cloudflare / Fast CDN).
    /// Funciona 100% multiplataforma no Windows, Android e Linux sem necessidade de programas externos instalados.
    /// </summary>
    public async Task<BandwidthTestResult> RunNativeHttpSpeedTestAsync(
        int testPayloadMegaBytes = 50,
        Action<double, double>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        await LogAsync($"[*] Iniciando teste de banda nativo HTTP (Payload de teste: ~{testPayloadMegaBytes} MB)...");

        // Endpoints de teste de banda confiáveis com suporte a chunking CDN
        var endpoints = new[]
        {
            $"https://speed.cloudflare.com/__down?bytes={testPayloadMegaBytes * 1024 * 1024}",
            $"https://proof.ovh.net/files/{testPayloadMegaBytes}Mio.dat",
            "https://ipv4.download.thinkbroadband.com/10MB.zip"
        };

        var bytesReceived = 0L;
        var sw = new Stopwatch();
        var latencySw = Stopwatch.StartNew();

        try
        {
            // Mede latência básica para o endpoint
            HttpResponseMessage? response = null;
            string? usedEndpoint = null;

            foreach (var url in endpoints)
            {
                try
                {
                    latencySw.Restart();
                    var req = new HttpRequestMessage(HttpMethod.Get, url);
                    response = await HttpClientInstance.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        usedEndpoint = url;
                        break;
                    }
                }
                catch
                {
                    // Tenta próximo endpoint
                }
            }

            latencySw.Stop();
            var latencyMs = (double)latencySw.ElapsedMilliseconds;

            if (response == null || !response.IsSuccessStatusCode || usedEndpoint == null)
            {
                await LogAsync("[!] Não foi possível conectar aos servidores de teste HTTP.");
                return new BandwidthTestResult(0, 0, latencyMs, 0, "HTTP CDN", "Nativo HTTP", false, "Falha de conexão com servidores de teste de banda.");
            }

            await LogAsync($"[*] Servidor de teste conectado. Latência inicial: {latencyMs:F0}ms. Baixando stream de dados...");

            var totalBytesExpected = response.Content.Headers.ContentLength ?? (testPayloadMegaBytes * 1024 * 1024);
            var buffer = new byte[64 * 1024];

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            sw.Start();

            var lastReportTime = sw.ElapsedMilliseconds;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                    break;

                bytesReceived += read;

                var elapsedMs = sw.ElapsedMilliseconds;
                if (elapsedMs - lastReportTime >= 200 || bytesReceived >= totalBytesExpected)
                {
                    lastReportTime = elapsedMs;
                    var seconds = Math.Max(0.01, elapsedMs / 1000.0);
                    var currentMbps = (bytesReceived * 8.0) / (seconds * 1_000_000.0);
                    var progressPct = Math.Min(100.0, (bytesReceived / (double)totalBytesExpected) * 100.0);

                    onProgress?.Invoke(currentMbps, progressPct);
                }
            }

            sw.Stop();
            var totalSeconds = Math.Max(0.01, sw.ElapsedMilliseconds / 1000.0);
            var finalDownloadMbps = (bytesReceived * 8.0) / (totalSeconds * 1_000_000.0);

            await LogAsync($"[OK] Teste HTTP concluído! Dados recebidos: {bytesReceived / (1024.0 * 1024.0):F2} MB em {totalSeconds:F2}s");
            await LogAsync($"  Taxa de Download: {finalDownloadMbps:F2} Mbps | Latência: {latencyMs:F0} ms");

            return new BandwidthTestResult(
                Math.Round(finalDownloadMbps, 2),
                0, // Upload opcional em teste leve
                latencyMs,
                0,
                "Cloudflare / CDN Global",
                "Nativo HTTP",
                true,
                $"Download: {finalDownloadMbps:F2} Mbps | Latência: {latencyMs:F0}ms");
        }
        catch (OperationCanceledException)
        {
            await LogAsync("[!] Teste de banda cancelado.");
            throw;
        }
        catch (Exception ex)
        {
            await LogAsync($"[ERRO] Falha no teste de banda HTTP: {ex.Message}");
            return new BandwidthTestResult(0, 0, 0, 0, "HTTP CDN", "Nativo HTTP", false, ex.Message);
        }
    }

    /// <summary>
    /// Tenta executar o Speedtest CLI oficial (Ookla / speedtest-cli) se estiver instalado na máquina.
    /// </summary>
    public async Task<BandwidthTestResult> RunSpeedtestCliAsync(
        string? customCliPath = null,
        CancellationToken cancellationToken = default)
    {
        var cliPath = customCliPath;
        if (string.IsNullOrEmpty(cliPath))
        {
            // Tenta encontrar speedtest no diretório do app ou no PATH
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var localExe = Path.Combine(appDir, "speedtest.exe");
            if (File.Exists(localExe))
            {
                cliPath = localExe;
            }
            else
            {
                cliPath = "speedtest";
            }
        }

        await LogAsync($"[*] Executando SpeedTest CLI ({cliPath})...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = "--format=json --accept-license --accept-gdpr",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                throw new InvalidOperationException($"Não foi possível iniciar o utilitário '{cliPath}'.");

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                var errorMsg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                await LogAsync($"[AVISO] Speedtest CLI retornou código {process.ExitCode}: {errorMsg}");
                return new BandwidthTestResult(0, 0, 0, 0, "Speedtest CLI", "CLI", false, errorMsg);
            }

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            // Formato oficial Ookla CLI JSON:
            // "download": { "bandwidth": bytesPerSec, "bytes": ... } -> bandwidth * 8 / 1,000,000 = Mbps
            // "upload": { "bandwidth": bytesPerSec }
            // "ping": { "jitter": ms, "latency": ms }
            // "server": { "name": "...", "location": "..." }

            var downloadBps = root.TryGetProperty("download", out var dl) && dl.TryGetProperty("bandwidth", out var dlBps) ? dlBps.GetDouble() : 0;
            var uploadBps = root.TryGetProperty("upload", out var ul) && ul.TryGetProperty("bandwidth", out var ulBps) ? ulBps.GetDouble() : 0;
            var latency = root.TryGetProperty("ping", out var ping) && ping.TryGetProperty("latency", out var lat) ? lat.GetDouble() : 0;
            var jitter = root.TryGetProperty("ping", out var pingJ) && pingJ.TryGetProperty("jitter", out var jit) ? jit.GetDouble() : 0;
            var serverName = root.TryGetProperty("server", out var srv) && srv.TryGetProperty("name", out var srvName) ? srvName.GetString() ?? "Ookla Server" : "Ookla Server";

            var downloadMbps = Math.Round((downloadBps * 8) / 1_000_000.0, 2);
            var uploadMbps = Math.Round((uploadBps * 8) / 1_000_000.0, 2);

            await LogAsync($"[OK] SpeedTest CLI Finalizado com Sucesso!");
            await LogAsync($"  Servidor : {serverName}");
            await LogAsync($"  Download : {downloadMbps} Mbps");
            await LogAsync($"  Upload   : {uploadMbps} Mbps");
            await LogAsync($"  Latência : {latency:F1} ms (Jitter: {jitter:F1} ms)");

            return new BandwidthTestResult(
                downloadMbps,
                uploadMbps,
                latency,
                jitter,
                serverName,
                "Ookla CLI",
                true,
                $"Download: {downloadMbps} Mbps | Upload: {uploadMbps} Mbps | Latência: {latency:F1}ms");
        }
        catch (Exception ex)
        {
            await LogAsync($"[AVISO] Speedtest CLI não instalado ({ex.Message}) — usando Teste HTTP Nativo (sem dependência externa).");
            return new BandwidthTestResult(0, 0, 0, 0, "Speedtest CLI", "CLI", false, ex.Message);
        }
    }

    /// <summary>
    /// Abre o teste de velocidade no navegador web padrão do sistema operacional.
    /// </summary>
    public static void OpenSpeedTestInBrowser(string url = "https://www.speedtest.net")
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignora falha de abertura do browser
        }
    }
}
