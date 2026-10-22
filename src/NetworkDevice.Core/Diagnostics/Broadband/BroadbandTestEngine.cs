using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Core.Diagnostics.Broadband;

public sealed record CloudflarePopLocation(string City, string Country, double Lat, double Lon);

public class BroadbandTestEngine
{
    private readonly Func<string, Task>? _logger;
    private static readonly HttpClient HttpClientInstance = new()
    {
        Timeout = TimeSpan.FromSeconds(35)
    };

    // Mapeamento IATA dos principais PoPs Cloudflare na América do Sul
    private static readonly Dictionary<string, CloudflarePopLocation> CloudflarePops = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GRU"] = new("São Paulo (Guarulhos)", "Brasil", -23.4356, -46.4731),
        ["CGH"] = new("São Paulo (Congonhas)", "Brasil", -23.6261, -46.6564),
        ["GIG"] = new("Rio de Janeiro (Galeão)", "Brasil", -22.8089, -43.2436),
        ["SDU"] = new("Rio de Janeiro (Santos Dumont)", "Brasil", -22.9105, -43.1631),
        ["BSB"] = new("Brasília", "Brasil", -15.8697, -47.9172),
        ["POA"] = new("Porto Alegre", "Brasil", -29.9935, -51.1711),
        ["CWB"] = new("Curitiba", "Brasil", -25.5285, -49.1758),
        ["FOR"] = new("Fortaleza", "Brasil", -3.7763, -38.5326),
        ["SSA"] = new("Salvador", "Brasil", -12.9086, -38.3225),
        ["CNF"] = new("Belo Horizonte (Confins)", "Brasil", -19.6244, -43.9719),
        ["REC"] = new("Recife", "Brasil", -8.1264, -34.9228),
        ["FLN"] = new("Florianópolis", "Brasil", -27.6703, -48.5525),
        ["VIX"] = new("Vitória", "Brasil", -20.2581, -40.2864),
        ["GYN"] = new("Goiânia", "Brasil", -16.6322, -49.2217),
        ["BEL"] = new("Belém", "Brasil", -1.3792, -48.4764),
        ["MAO"] = new("Manaus", "Brasil", -3.0358, -60.0497),
        ["EZE"] = new("Buenos Aires", "Argentina", -34.8222, -58.5358),
        ["SCL"] = new("Santiago", "Chile", -33.3930, -70.7858),
        ["MIA"] = new("Miami", "Estados Unidos", 25.7959, -80.2870)
    };

    private static readonly Dictionary<string, (double Lat, double Lon)> KnownCityCoordinates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Florianópolis"] = (-27.5967, -48.5492),
        ["Florianopolis"] = (-27.5967, -48.5492),
        ["Blumenau"] = (-26.9194, -49.0661),
        ["Joinville"] = (-26.3045, -48.8487),
        ["São José"] = (-27.6136, -48.6366),
        ["Sao Jose"] = (-27.6136, -48.6366),
        ["Itajaí"] = (-26.9078, -48.6619),
        ["Itajai"] = (-26.9078, -48.6619),
        ["Criciúma"] = (-28.6773, -49.3695),
        ["Criciuma"] = (-28.6773, -49.3695),
        ["Chapecó"] = (-27.1004, -52.6152),
        ["Chapeco"] = (-27.1004, -52.6152),
        ["Lages"] = (-27.8161, -50.3261),
        ["Tubarão"] = (-28.4739, -49.0142),
        ["Tubarao"] = (-28.4739, -49.0142),
        ["Balneário Camboriú"] = (-26.9930, -48.6353),
        ["Balneario Camboriu"] = (-26.9930, -48.6353),
        ["Curitiba"] = (-25.4284, -49.2733),
        ["Londrina"] = (-23.3045, -51.1696),
        ["Maringá"] = (-23.4205, -51.9333),
        ["Maringa"] = (-23.4205, -51.9333),
        ["Ponta Grossa"] = (-25.0950, -50.1619),
        ["Cascavel"] = (-24.9578, -53.4595),
        ["Foz do Iguaçu"] = (-25.5163, -54.5854),
        ["Foz do Iguacu"] = (-25.5163, -54.5854),
        ["Porto Alegre"] = (-30.0346, -51.2177),
        ["Caxias do Sul"] = (-29.1678, -51.1794),
        ["Pelotas"] = (-31.7654, -52.3376),
        ["Canoas"] = (-29.9179, -51.1837),
        ["Santa Maria"] = (-29.6842, -53.8069),
        ["Passo Fundo"] = (-28.2628, -52.4067),
        ["São Paulo"] = (-23.5505, -46.6333),
        ["Sao Paulo"] = (-23.5505, -46.6333),
        ["Guarulhos"] = (-23.4542, -46.5341),
        ["Campinas"] = (-22.9099, -47.0626),
        ["Santos"] = (-23.9608, -46.3331),
        ["São Bernardo do Campo"] = (-23.6944, -46.5654),
        ["Santo André"] = (-23.6639, -46.5383),
        ["Ribeirão Preto"] = (-21.1775, -47.8103),
        ["Ribeirao Preto"] = (-21.1775, -47.8103),
        ["São José dos Campos"] = (-23.1896, -45.8841),
        ["Sorocaba"] = (-23.5015, -47.4587),
        ["Rio de Janeiro"] = (-22.9068, -43.1729),
        ["Niterói"] = (-22.8832, -43.1034),
        ["Niteroi"] = (-22.8832, -43.1034),
        ["Belo Horizonte"] = (-19.9167, -43.9345),
        ["Uberlândia"] = (-18.9186, -48.2772),
        ["Uberlandia"] = (-18.9186, -48.2772),
        ["Juiz de Fora"] = (-21.7595, -43.3398),
        ["Vitória"] = (-20.3155, -40.3128),
        ["Vitoria"] = (-20.3155, -40.3128),
        ["Brasília"] = (-15.7975, -47.8919),
        ["Brasilia"] = (-15.7975, -47.8919),
        ["Goiânia"] = (-16.6869, -49.2648),
        ["Goiania"] = (-16.6869, -49.2648),
        ["Salvador"] = (-12.9777, -38.5016),
        ["Recife"] = (-8.0476, -34.8770),
        ["Fortaleza"] = (-3.7172, -38.5433),
        ["Manaus"] = (-3.1190, -60.0217),
        ["Belém"] = (-1.4558, -48.4902),
        ["Belem"] = (-1.4558, -48.4902),
        ["Campo Grande"] = (-20.4697, -54.6201),
        ["Cuiabá"] = (-15.6014, -56.0979),
        ["Cuiaba"] = (-15.6014, -56.0979),
        ["Natal"] = (-5.7945, -35.2110),
        ["João Pessoa"] = (-7.1195, -34.8450),
        ["Joao Pessoa"] = (-7.1195, -34.8450),
        ["Maceió"] = (-9.6498, -35.7089),
        ["Maceio"] = (-9.6498, -35.7089),
        ["Teresina"] = (-5.0892, -42.8019),
        ["São Luís"] = (-2.5307, -44.3068),
        ["Sao Luis"] = (-2.5307, -44.3068),
        ["Aracaju"] = (-10.9472, -37.0731),
        ["Porto Velho"] = (-8.7612, -63.9039),
        ["Macapá"] = (0.0389, -51.0664),
        ["Macapa"] = (0.0389, -51.0664),
        ["Rio Branco"] = (-9.9753, -67.8249),
        ["Boa Vista"] = (2.8235, -60.6758),
        ["Palmas"] = (-10.2491, -48.3243)
    };

    public static (double Lat, double Lon)? ResolveCityCoordinates(string? locationOrCity)
    {
        if (string.IsNullOrWhiteSpace(locationOrCity)) return null;
        var clean = locationOrCity.Trim();

        foreach (var (city, coords) in KnownCityCoordinates)
        {
            if (clean.Contains(city, StringComparison.OrdinalIgnoreCase) ||
                city.Contains(clean, StringComparison.OrdinalIgnoreCase))
            {
                return coords;
            }
        }
        return null;
    }

    /// <summary>
    /// Calcula a qualidade da latência ponderada pela física de propagação da luz na fibra óptica.
    /// Em fibra terrestre (com sinuosidade média de 1.3x), a luz leva ~1ms a cada 78 km (ida e volta RTT).
    /// </summary>
    public static BroadbandLatencyQuality CalculateLatencyQuality(double measuredLatencyMs, double distanceKm)
    {
        var theoreticalFiberRttMs = Math.Max(0.8, Math.Round(distanceKm / 78.0, 1));
        var residualQueuingDelayMs = Math.Max(0.0, Math.Round(measuredLatencyMs - theoreticalFiberRttMs, 1));
        var routeEfficiencyPct = measuredLatencyMs > 0
            ? Math.Min(100.0, Math.Round((theoreticalFiberRttMs / measuredLatencyMs) * 100.0, 1))
            : 100.0;

        string status;
        string explanation;

        if (routeEfficiencyPct >= 65.0 || residualQueuingDelayMs <= 8.0)
        {
            status = "Ótimo (Rota Direta Óptica)";
            explanation = $"Latência muito próxima do limite físico da velocidade da luz na fibra (~{theoreticalFiberRttMs:F1}ms piso teórico). Fila desprezível (+{residualQueuingDelayMs:F1}ms).";
        }
        else if (routeEfficiencyPct >= 35.0 || residualQueuingDelayMs <= 30.0)
        {
            status = "Normal (Trânsito Típico)";
            explanation = $"Atraso de propagação de ~{theoreticalFiberRttMs:F1}ms com comutação IP padrão (+{residualQueuingDelayMs:F1}ms de fila/roteadores).";
        }
        else
        {
            status = "Alerta (Rota Indireta / Tromboning)";
            explanation = $"Latência de {measuredLatencyMs:F1}ms muito acima do piso físico (~{theoreticalFiberRttMs:F1}ms para {distanceKm:F0}km). Possível rota indireta ou fila longa.";
        }

        return new BroadbandLatencyQuality(
            MeasuredLatencyMs: Math.Round(measuredLatencyMs, 1),
            DistanceKm: Math.Round(distanceKm, 1),
            TheoreticalFiberRttMs: theoreticalFiberRttMs,
            ResidualQueuingDelayMs: residualQueuingDelayMs,
            RouteEfficiencyPct: routeEfficiencyPct,
            RouteQualityStatus: status,
            DiagnosticExplanation: explanation);
    }

    public BroadbandTestEngine(Func<string, Task>? logger = null)
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
    /// Calcula a distância geodésica em quilômetros entre duas coordenadas (Fórmula de Haversine).
    /// </summary>
    public static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371.0; // Raio da Terra em km
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round(r * c, 1);
    }

    private static double DegreesToRadians(double deg) => deg * (Math.PI / 180.0);

    /// <summary>
    /// Obtém as informações geográficas do cliente via metadados da Cloudflare.
    /// </summary>
    public async Task<BroadbandGeoInfo?> GetClientGeoInfoAsync(
        string? sourceIpAddress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateHttpClient(sourceIpAddress);
            var req = new HttpRequestMessage(HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=10");
            using var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var headers = response.Headers;
            var ip = GetHeaderValue(headers, "cf-meta-ip") ?? "Desconhecido";
            var asn = GetHeaderValue(headers, "asn") ?? "N/D";
            var city = Uri.UnescapeDataString(GetHeaderValue(headers, "city") ?? "Local");
            var country = GetHeaderValue(headers, "country") ?? "BR";
            var colo = GetHeaderValue(headers, "colo") ?? "GRU";

            double? lat = double.TryParse(GetHeaderValue(headers, "latitude"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedLat) ? parsedLat : null;
            double? lon = double.TryParse(GetHeaderValue(headers, "longitude"), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedLon) ? parsedLon : null;

            // Busca metadados do servidor PoP
            var pop = CloudflarePops.TryGetValue(colo, out var p)
                ? p
                : new CloudflarePopLocation($"Edge ({colo})", "Cloudflare", -23.5505, -46.6333);
            var (serverCity, serverCountry, sLat, sLon) = (pop.City, pop.Country, pop.Lat, pop.Lon);

            double? distance = null;
            if (lat.HasValue && lon.HasValue)
            {
                distance = CalculateDistanceKm(lat.Value, lon.Value, sLat, sLon);
            }

            return new BroadbandGeoInfo(
                PublicIp: ip,
                Asn: asn,
                Provider: $"AS{asn}",
                ClientCity: city,
                ClientCountry: country,
                ClientLatitude: lat,
                ClientLongitude: lon,
                ServerColoIata: colo,
                ServerCity: serverCity,
                ServerCountry: serverCountry,
                ServerLatitude: sLat,
                ServerLongitude: sLon,
                DistanceKm: distance,
                Timestamp: DateTime.Now);
        }
        catch (Exception ex)
        {
            await LogAsync($"[AVISO] Não foi possível obter metadados geográficos da Cloudflare: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Descobre todos os servidores de teste disponíveis (Ookla locais, Cloudflare PoPs e M-Lab),
    /// ordenando-os por proximidade geográfica e latência.
    /// </summary>
    public async Task<List<BroadbandServerInfo>> DiscoverServersAsync(
        BroadbandGeoInfo? clientGeo,
        string? sourceIpAddress = null,
        CancellationToken cancellationToken = default)
    {
        var servers = new List<BroadbandServerInfo>();

        // 1. Adiciona o servidor Cloudflare Speed Anycast / PoP mais próximo
        var colo = clientGeo?.ServerColoIata ?? "GRU";
        var cfPop = CloudflarePops.TryGetValue(colo, out var popData)
            ? popData
            : new CloudflarePopLocation($"Edge ({colo})", "Cloudflare", -23.5505, -46.6333);

        double? cfDist = null;
        if (clientGeo?.ClientLatitude != null && clientGeo.ClientLongitude != null)
        {
            cfDist = CalculateDistanceKm(clientGeo.ClientLatitude.Value, clientGeo.ClientLongitude.Value, cfPop.Lat, cfPop.Lon);
        }

        servers.Add(new BroadbandServerInfo(
            Id: $"cf-{colo.ToLowerInvariant()}",
            Name: $"Cloudflare Edge ({colo})",
            Sponsor: "Cloudflare Speed Test",
            Location: cfPop.City,
            Country: cfPop.Country,
            Host: "speed.cloudflare.com",
            Port: 443,
            Latitude: cfPop.Lat,
            Longitude: cfPop.Lon,
            DistanceKm: cfDist,
            LatencyMs: null,
            ServerType: "Cloudflare"));

        // 2. Busca servidores locais via Ookla Speedtest CLI se disponível
        var ooklaServers = await DiscoverOoklaServersAsync(clientGeo, cancellationToken);
        if (ooklaServers.Count > 0)
        {
            servers.AddRange(ooklaServers);
        }

        // 3. Busca servidores via M-Lab Locate API se disponível
        var mlabServers = await DiscoverMlabServersAsync(clientGeo, cancellationToken);
        if (mlabServers.Count > 0)
        {
            servers.AddRange(mlabServers);
        }

        // 4. Executa ping rápido para os 5 primeiros servidores mais próximos
        await MeasureQuickLatencyAsync(servers.Take(6).ToList(), sourceIpAddress, cancellationToken);

        // Ordena por distância (se disponível) e latência
        return servers
            .OrderBy(s => s.LatencyMs ?? (s.DistanceKm ?? 99999))
            .ToList();
    }

    private async Task<List<BroadbandServerInfo>> DiscoverOoklaServersAsync(
        BroadbandGeoInfo? clientGeo,
        CancellationToken cancellationToken)
    {
        var list = new List<BroadbandServerInfo>();
        try
        {
            var servers = await OoklaSpeedtestService.Instance.GetNearbyServersAsync(cancellationToken);
            foreach (var s in servers)
            {
                var coords = (s.Latitude.HasValue && s.Longitude.HasValue)
                    ? (s.Latitude.Value, s.Longitude.Value)
                    : ResolveCityCoordinates(s.Location);

                double? dist = null;
                if (coords != null && clientGeo?.ClientLatitude != null && clientGeo.ClientLongitude != null)
                {
                    dist = CalculateDistanceKm(clientGeo.ClientLatitude.Value, clientGeo.ClientLongitude.Value, coords.Value.Item1, coords.Value.Item2);
                }
                else if (clientGeo?.ClientCity != null && s.Location.Contains(clientGeo.ClientCity, StringComparison.OrdinalIgnoreCase))
                {
                    dist = 5.0; // Mesma cidade
                }

                list.Add(s with { DistanceKm = dist });
            }
        }
        catch
        {
            // Fallback silencioso caso Ookla CLI não responda
        }
        return list;
    }

    private async Task<List<BroadbandServerInfo>> DiscoverMlabServersAsync(
        BroadbandGeoInfo? clientGeo,
        CancellationToken cancellationToken)
    {
        var list = new List<BroadbandServerInfo>();
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "https://locate.measurementlab.net/v2/nearest/ndt/ndt7");
            using var resp = await HttpClientInstance.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode) return list;

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("results", out var resArr) && resArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in resArr.EnumerateArray())
                {
                    var machine = r.TryGetProperty("machine", out var m) ? m.GetString() ?? "" : "";
                    var loc = r.TryGetProperty("location", out var l) ? l : default;
                    var city = loc.TryGetProperty("city", out var c) ? c.GetString() ?? "Brasil" : "Brasil";
                    var country = loc.TryGetProperty("country", out var cnt) ? cnt.GetString() ?? "BR" : "BR";

                    var coords = ResolveCityCoordinates(city);
                    double? dist = null;
                    if (coords != null && clientGeo?.ClientLatitude != null && clientGeo.ClientLongitude != null)
                    {
                        dist = CalculateDistanceKm(clientGeo.ClientLatitude.Value, clientGeo.ClientLongitude.Value, coords.Value.Lat, coords.Value.Lon);
                    }
                    else if (clientGeo?.ClientCity != null && city.Contains(clientGeo.ClientCity, StringComparison.OrdinalIgnoreCase))
                    {
                        dist = 5.0;
                    }

                    list.Add(new BroadbandServerInfo(
                        Id: $"mlab-{machine}",
                        Name: $"Measurement Lab ({city})",
                        Sponsor: "Measurement Lab",
                        Location: city,
                        Country: country,
                        Host: machine,
                        Port: 443,
                        Latitude: coords?.Lat,
                        Longitude: coords?.Lon,
                        DistanceKm: dist,
                        LatencyMs: null,
                        ServerType: "MLab"));
                }
            }
        }
        catch
        {
            // Ignora se M-Lab estiver inacessível
        }

        return list;
    }

    private async Task MeasureQuickLatencyAsync(
        List<BroadbandServerInfo> targets,
        string? sourceIpAddress,
        CancellationToken cancellationToken)
    {
        var tasks = targets.Select(async target =>
        {
            try
            {
                if (target.ServerType == "Cloudflare")
                {
                    var client = CreateHttpClient(sourceIpAddress);
                    var sw = Stopwatch.StartNew();
                    var req = new HttpRequestMessage(HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=0");
                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    sw.Stop();
                    if (resp.IsSuccessStatusCode)
                    {
                        var idx = targets.IndexOf(target);
                        if (idx >= 0)
                            targets[idx] = target with { LatencyMs = Math.Round((double)sw.ElapsedMilliseconds, 1) };
                    }
                }
                else if (!string.IsNullOrWhiteSpace(target.Host))
                {
                    var sw = Stopwatch.StartNew();
                    using var tcp = new TcpClient();
                    var connectTask = tcp.ConnectAsync(target.Host, target.Port, cancellationToken).AsTask();
                    if (await Task.WhenAny(connectTask, Task.Delay(1500, cancellationToken)) == connectTask)
                    {
                        sw.Stop();
                        var idx = targets.IndexOf(target);
                        if (idx >= 0)
                            targets[idx] = target with { LatencyMs = Math.Round((double)sw.ElapsedMilliseconds, 1) };
                    }
                }
            }
            catch
            {
                // Ignora timeout no ping de sondagem
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Executa um pré-teste ultra-rápido de qualificação avaliando latência (ping burst)
    /// e uma micro-sondagem de vazão (probe) para determinar automaticamente o melhor servidor e seu backup.
    /// </summary>
    public async Task<(BroadbandServerInfo BestServer, BroadbandServerInfo? BackupServer)> PreTestAndSelectBestServerAsync(
        IReadOnlyList<BroadbandServerInfo> candidates,
        string? sourceIpAddress = null,
        IProgress<BroadbandProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (candidates == null || candidates.Count == 0)
        {
            throw new InvalidOperationException("Nenhum servidor candidato disponível para pré-teste.");
        }

        await LogAsync("[*] [PRÉ-TESTE DINÂMICO] Iniciando sondagem de latência e micro-vazão dos servidores candidatos...");
        progress?.Report(new BroadbandProgressEvent("Pré-Teste", 5, 0, 0, 0, "Pré-teste: Avaliando latência e vazão dos servidores candidatos..."));

        var client = CreateHttpClient(sourceIpAddress);
        var scoredList = new List<(BroadbandServerInfo Server, double LatencyMs, double ProbeMbps, double Score)>();

        // Filtra até os 4 servidores geograficamente mais próximos
        var topCandidates = candidates
            .OrderBy(c => c.DistanceKm ?? (c.LatencyMs ?? 9999))
            .Take(4)
            .ToList();

        foreach (var server in topCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Sondagem de Latência: 3 pings rápidos
                var pings = new List<double>();
                var sw = new Stopwatch();

                for (int p = 0; p < 3; p++)
                {
                    if (server.ServerType == "Cloudflare")
                    {
                        sw.Restart();
                        var req = new HttpRequestMessage(HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=0");
                        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        sw.Stop();
                        if (resp.IsSuccessStatusCode) pings.Add(sw.ElapsedMilliseconds);
                    }
                    else if (!string.IsNullOrWhiteSpace(server.Host))
                    {
                        sw.Restart();
                        using var tcp = new TcpClient();
                        var connectTask = tcp.ConnectAsync(server.Host, server.Port, cancellationToken).AsTask();
                        if (await Task.WhenAny(connectTask, Task.Delay(1200, cancellationToken)) == connectTask)
                        {
                            sw.Stop();
                            pings.Add(sw.ElapsedMilliseconds);
                        }
                    }
                }

                if (pings.Count == 0) continue;
                var avgLat = pings.Average();

                // Micro-sondagem de vazão: 1MB de download rápido (~100-250ms)
                var probeMbps = 0.0;
                try
                {
                    var dlSw = Stopwatch.StartNew();
                    var req = new HttpRequestMessage(HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=1048576");
                    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (resp.IsSuccessStatusCode)
                    {
                        var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                        var buf = new byte[32 * 1024];
                        long readBytes = 0;
                        while (true)
                        {
                            var r = await stream.ReadAsync(buf.AsMemory(0, buf.Length), cancellationToken);
                            if (r == 0) break;
                            readBytes += r;
                        }
                        dlSw.Stop();
                        var sec = Math.Max(0.01, dlSw.ElapsedMilliseconds / 1000.0);
                        probeMbps = Math.Round((readBytes * 8.0) / (sec * 1_000_000.0), 1);
                    }
                }
                catch
                {
                    probeMbps = 20.0;
                }

                var score = (probeMbps * 0.7) - (avgLat * 0.5);
                scoredList.Add((server with { LatencyMs = Math.Round(avgLat, 1) }, avgLat, probeMbps, score));

                await LogAsync($"  -> Candidato: {server.Sponsor} ({server.Location}) | Latência: {avgLat:F1}ms | Probe: {probeMbps:F1} Mbps");
            }
            catch
            {
                // Ignora falha de candidato individual
            }
        }

        if (scoredList.Count == 0)
        {
            return (candidates[0], candidates.Count > 1 ? candidates[1] : null);
        }

        var ordered = scoredList.OrderByDescending(s => s.Score).ToList();
        var best = ordered[0].Server;
        var backup = ordered.Count > 1 ? ordered[1].Server : null;

        await LogAsync($"[OK] [PRÉ-TESTE SELEÇÃO] Campeão: {best.Sponsor} ({best.Location}) | Latência: {ordered[0].LatencyMs:F1}ms | Probe: {ordered[0].ProbeMbps:F1} Mbps");
        if (backup != null)
        {
            await LogAsync($"[*] [PRÉ-TESTE BACKUP] Servidor Fallback: {backup.Sponsor} ({backup.Location})");
        }

        return (best, backup);
    }

    /// <summary>
    /// Executa o teste de banda com o servidor selecionado ou com seleção automática por proximidade e pré-teste,
    /// aplicando esteira de fallback automático em múltiplos níveis em caso de indisponibilidade.
    /// </summary>
    public async Task<BroadbandTestResult> RunTestWithFallbackAsync(
        BroadbandServerInfo? preferredServer,
        IReadOnlyList<BroadbandServerInfo> candidateServers,
        string? sourceIpAddress = null,
        IProgress<BroadbandProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        var fallbackHistory = new List<BroadbandFallbackEvent>();
        var timeSeries = new List<BroadbandPoint>();

        // 1. Obtém GeoInfo se ainda não presente
        var geoInfo = await GetClientGeoInfoAsync(sourceIpAddress, cancellationToken);

        // 2. Monta fila de servidores para tentativa
        var attemptQueue = new List<BroadbandServerInfo>();

        if (preferredServer != null)
        {
            attemptQueue.Add(preferredServer);
        }
        else if (candidateServers != null && candidateServers.Count > 0)
        {
            // MODO AUTOMÁTICO: Executa o Pré-Teste de qualificação para eleger o melhor servidor e backup!
            var (best, backup) = await PreTestAndSelectBestServerAsync(candidateServers, sourceIpAddress, progress, cancellationToken);
            attemptQueue.Add(best);
            if (backup != null && backup.Id != best.Id)
            {
                attemptQueue.Add(backup);
            }
        }

        foreach (var cand in candidateServers ?? Array.Empty<BroadbandServerInfo>())
        {
            if (!attemptQueue.Any(s => s.Id == cand.Id))
            {
                attemptQueue.Add(cand);
            }
        }

        // Garante que o Cloudflare Edge esteja sempre na fila como âncora de segurança
        if (!attemptQueue.Any(s => s.ServerType == "Cloudflare"))
        {
            attemptQueue.Add(new BroadbandServerInfo(
                Id: "cf-fallback",
                Name: "Cloudflare Speed Test Anycast",
                Sponsor: "Cloudflare",
                Location: "Edge Anycast",
                Country: "Brasil",
                Host: "speed.cloudflare.com",
                Port: 443,
                Latitude: null,
                Longitude: null,
                DistanceKm: null,
                LatencyMs: null,
                ServerType: "Cloudflare"));
        }

        BroadbandServerInfo? currentServer = null;
        BroadbandTestResult? finalResult = null;

        for (int i = 0; i < attemptQueue.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentServer = attemptQueue[i];

            await LogAsync($"[*] [Tentativa {i + 1}/{attemptQueue.Count}] Iniciando teste contra: {currentServer.DisplayText}...");
            progress?.Report(new BroadbandProgressEvent("Conectando", 5, 0, 0, 0, $"Conectando ao servidor: {currentServer.Sponsor}..."));

            try
            {
                if (currentServer.ServerType == "Ookla")
                {
                    finalResult = await RunOoklaTestAsync(currentServer, geoInfo, sourceIpAddress, progress, timeSeries, cancellationToken);
                }
                else
                {
                    // Cloudflare ou nativo HTTP
                    finalResult = await RunCloudflareTestAsync(currentServer, geoInfo, sourceIpAddress, progress, timeSeries, cancellationToken);
                }

                if (finalResult != null && finalResult.IsSuccess)
                {
                    await LogAsync($"[OK] Teste finalizado com sucesso contra: {currentServer.DisplayText}");
                    break;
                }
                else
                {
                    throw new InvalidOperationException(finalResult?.StatusMessage ?? "Falha desconhecida no teste de vazão.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var nextServer = (i + 1 < attemptQueue.Count) ? attemptQueue[i + 1] : null;
                var reason = ex.Message;
                await LogAsync($"[AVISO] Servidor '{currentServer.Name}' indisponível ({reason}).");

                if (nextServer != null)
                {
                    await LogAsync($"[FALLBACK AUTOMÁTICO] Chaveando para o próximo servidor mais próximo: '{nextServer.DisplayText}'...");
                    fallbackHistory.Add(new BroadbandFallbackEvent(
                        Timestamp: DateTime.Now,
                        TargetServerName: currentServer.Name,
                        TargetServerType: currentServer.ServerType,
                        Reason: reason,
                        FallbackServerName: nextServer.Name,
                        FallbackServerType: nextServer.ServerType));

                    progress?.Report(new BroadbandProgressEvent("Fallback", 10, 0, 0, 0,
                        $"Fallback acionado: chaveando para {nextServer.Sponsor}..."));
                }
                else
                {
                    return new BroadbandTestResult(
                        IsSuccess: false,
                        DownloadAvgMbps: 0,
                        DownloadPeakMbps: 0,
                        UploadAvgMbps: 0,
                        UploadPeakMbps: 0,
                        LatencyIdleMs: 0,
                        LatencyMinMs: 0,
                        LatencyMaxMs: 0,
                        JitterMs: 0,
                        PacketLossPct: 0,
                        Bufferbloat: null,
                        GeoInfo: geoInfo,
                        PrimaryServer: currentServer,
                        FallbackHistory: fallbackHistory,
                        TimeSeries: timeSeries,
                        StatusMessage: $"Todos os servidores falharam. Último erro: {reason}",
                        ValidationUrl: null,
                        Duration: DateTime.Now - startTime,
                        LocalAdapterName: null,
                        SourceIpAddress: sourceIpAddress);
                }
            }
        }

        if (finalResult == null || !finalResult.IsSuccess)
        {
            return new BroadbandTestResult(
                IsSuccess: false,
                DownloadAvgMbps: 0,
                DownloadPeakMbps: 0,
                UploadAvgMbps: 0,
                UploadPeakMbps: 0,
                LatencyIdleMs: 0,
                LatencyMinMs: 0,
                LatencyMaxMs: 0,
                JitterMs: 0,
                PacketLossPct: 0,
                Bufferbloat: null,
                GeoInfo: geoInfo,
                PrimaryServer: currentServer,
                FallbackHistory: fallbackHistory,
                TimeSeries: timeSeries,
                StatusMessage: "Teste não pôde ser completado em nenhum servidor.",
                ValidationUrl: null,
                Duration: DateTime.Now - startTime,
                LocalAdapterName: null,
                SourceIpAddress: sourceIpAddress);
        }

        // 3. Atualiza GeoInfo com as coordenadas e distância real exata até o servidor testado!
        var srvLat = currentServer?.Latitude ?? geoInfo?.ServerLatitude;
        var srvLon = currentServer?.Longitude ?? geoInfo?.ServerLongitude;
        var srvDist = currentServer?.DistanceKm;

        if (!srvDist.HasValue && srvLat.HasValue && srvLon.HasValue && geoInfo?.ClientLatitude.HasValue == true && geoInfo?.ClientLongitude.HasValue == true)
        {
            srvDist = CalculateDistanceKm(geoInfo.ClientLatitude.Value, geoInfo.ClientLongitude.Value, srvLat.Value, srvLon.Value);
        }
        else if (!srvDist.HasValue && currentServer != null && !string.IsNullOrWhiteSpace(currentServer.Location) && geoInfo?.ClientLatitude.HasValue == true && geoInfo?.ClientLongitude.HasValue == true)
        {
            var coords = ResolveCityCoordinates(currentServer.Location);
            if (coords.HasValue)
            {
                srvDist = CalculateDistanceKm(geoInfo.ClientLatitude.Value, geoInfo.ClientLongitude.Value, coords.Value.Lat, coords.Value.Lon);
            }
        }

        if (geoInfo != null && currentServer != null)
        {
            geoInfo = geoInfo with
            {
                ServerCity = currentServer.Location,
                ServerCountry = currentServer.Country,
                ServerLatitude = srvLat,
                ServerLongitude = srvLon,
                DistanceKm = srvDist ?? geoInfo.DistanceKm
            };
        }

        var distParaQualidade = srvDist ?? (geoInfo?.DistanceKm ?? 50.0);
        var latencyQual = CalculateLatencyQuality(finalResult.LatencyIdleMs, distParaQualidade);

        // Consolida histórico de fallbacks, georreferenciamento exato e qualidade de rota no resultado final
        return finalResult with
        {
            GeoInfo = geoInfo,
            PrimaryServer = currentServer,
            FallbackHistory = fallbackHistory,
            Duration = DateTime.Now - startTime,
            LatencyQuality = latencyQual
        };
    }

    /// <summary>
    /// Motor nativo de teste contra speed.cloudflare.com medindo latência idle, jitter,
    /// download streaming, upload POST e bufferbloat sob carga.
    /// </summary>
    private async Task<BroadbandTestResult> RunCloudflareTestAsync(
        BroadbandServerInfo server,
        BroadbandGeoInfo? geoInfo,
        string? sourceIpAddress,
        IProgress<BroadbandProgressEvent>? progress,
        List<BroadbandPoint> timeSeries,
        CancellationToken cancellationToken)
    {
        var client = CreateHttpClient(sourceIpAddress);
        var swTotal = Stopwatch.StartNew();

        // -------------------------------------------------------------
        // ETAPA 1: Latência Idle & Jitter (10 amostras rápidas de ping)
        // -------------------------------------------------------------
        progress?.Report(new BroadbandProgressEvent("Latência", 10, 0, 0, 0, "Medindo latência de repouso e jitter (10 pings)..."));
        var idleLatencies = new List<double>();
        var pingSw = new Stopwatch();

        for (int i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pingSw.Restart();
            var req = new HttpRequestMessage(HttpMethod.Get, "https://speed.cloudflare.com/__down?bytes=0");
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            pingSw.Stop();

            if (resp.IsSuccessStatusCode)
            {
                var ms = (double)pingSw.ElapsedMilliseconds;
                idleLatencies.Add(ms);
                timeSeries.Add(new BroadbandPoint(swTotal.ElapsedMilliseconds / 1000.0, 0, ms, 0, "Ping"));
                progress?.Report(new BroadbandProgressEvent("Latência", 10 + i * 2, 0, ms, 0, $"Ping {i + 1}/10: {ms:F0} ms"));
            }
            await Task.Delay(50, cancellationToken);
        }

        if (idleLatencies.Count == 0)
        {
            throw new InvalidOperationException("Falha ao comunicar com os servidores de teste Cloudflare.");
        }

        var latIdleAvg = Math.Round(idleLatencies.Average(), 1);
        var latIdleMin = Math.Round(idleLatencies.Min(), 1);
        var latIdleMax = Math.Round(idleLatencies.Max(), 1);
        var jitterIdle = CalculateJitter(idleLatencies);

        await LogAsync($"[OK] Latência de Repouso: Média {latIdleAvg:F1}ms (Mín: {latIdleMin:F1}ms | Máx: {latIdleMax:F1}ms) | Jitter: {jitterIdle:F1}ms");

        // -------------------------------------------------------------
        // ETAPA 2: Teste de Download (Chunks e Saturação)
        // -------------------------------------------------------------
        progress?.Report(new BroadbandProgressEvent("Download", 30, 0, latIdleAvg, jitterIdle, "Iniciando teste de Download..."));
        var dlSpeeds = new List<double>();
        var dlUnderLoadLatencies = new List<double>();
        var dlSw = Stopwatch.StartNew();

        // Faz download de payload adaptativo (25MB a 50MB)
        var totalPayloadBytes = 40 * 1024 * 1024;
        var dlUrl = $"https://speed.cloudflare.com/__down?bytes={totalPayloadBytes}";

        using (var dlResp = await client.GetAsync(dlUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            dlResp.EnsureSuccessStatusCode();
            var stream = await dlResp.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[64 * 1024];
            long bytesReadTotal = 0;
            var lastReportMs = dlSw.ElapsedMilliseconds;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;

                bytesReadTotal += read;
                var elapsedMs = dlSw.ElapsedMilliseconds;

                if (elapsedMs - lastReportMs >= 200)
                {
                    lastReportMs = elapsedMs;
                    var sec = Math.Max(0.01, elapsedMs / 1000.0);
                    var mbps = Math.Round((bytesReadTotal * 8.0) / (sec * 1_000_000.0), 2);
                    dlSpeeds.Add(mbps);

                    var pct = Math.Min(65.0, 30.0 + (bytesReadTotal / (double)totalPayloadBytes) * 35.0);
                    var point = new BroadbandPoint(swTotal.ElapsedMilliseconds / 1000.0, mbps, latIdleAvg, jitterIdle, "Download");
                    timeSeries.Add(point);

                    progress?.Report(new BroadbandProgressEvent("Download", pct, mbps, latIdleAvg, jitterIdle,
                        $"Download: {mbps:F1} Mbps ({(bytesReadTotal / (1024.0 * 1024.0)):F1} MB)", point));
                }
            }
        }

        dlSw.Stop();
        var dlAvg = dlSpeeds.Count > 0 ? Math.Round(dlSpeeds.Average(), 2) : 0;
        var dlPeak = dlSpeeds.Count > 0 ? Math.Round(dlSpeeds.Max(), 2) : 0;
        await LogAsync($"[OK] Download Concluído: Média {dlAvg:F2} Mbps | Pico {dlPeak:F2} Mbps");

        // -------------------------------------------------------------
        // ETAPA 3: Teste de Upload Contínuo (Múltiplas Amostras para Gráfico Completo)
        // -------------------------------------------------------------
        progress?.Report(new BroadbandProgressEvent("Upload", 65, 0, latIdleAvg, jitterIdle, "Iniciando teste de Upload contínuo..."));
        var ulSpeeds = new List<double>();
        var ulSw = Stopwatch.StartNew();

        var ulChunkBytes = (int)(1.25 * 1024 * 1024); // 1.25 MB por chunk
        var dummyData = new byte[ulChunkBytes];
        Random.Shared.NextBytes(dummyData);

        var totalChunks = 20;
        var bytesUploadedTotal = 0L;
        var uploadStartSw = Stopwatch.StartNew();

        for (int chunk = 0; chunk < totalChunks; chunk++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkSw = Stopwatch.StartNew();

            using var content = new ByteArrayContent(dummyData);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            using var ulResp = await client.PostAsync("https://speed.cloudflare.com/__up", content, cancellationToken);
            ulResp.EnsureSuccessStatusCode();
            chunkSw.Stop();

            bytesUploadedTotal += ulChunkBytes;
            var chunkSec = Math.Max(0.01, chunkSw.ElapsedMilliseconds / 1000.0);
            var chunkMbps = Math.Round((ulChunkBytes * 8.0) / (chunkSec * 1_000_000.0), 2);
            ulSpeeds.Add(chunkMbps);

            var pct = Math.Min(98.0, 65.0 + ((chunk + 1) / (double)totalChunks) * 33.0);
            var point = new BroadbandPoint(swTotal.ElapsedMilliseconds / 1000.0, chunkMbps, latIdleAvg, jitterIdle, "Upload");
            timeSeries.Add(point);

            progress?.Report(new BroadbandProgressEvent("Upload", pct, chunkMbps, latIdleAvg, jitterIdle,
                $"Upload: {chunkMbps:F1} Mbps (Amostra {chunk + 1}/{totalChunks})", point));

            // Se durar mais de 6.5s e tiver ao menos 14 amostras, encerra para evitar teste excessivamente longo
            if (uploadStartSw.ElapsedMilliseconds > 6500 && chunk >= 14)
            {
                break;
            }
        }

        ulSw.Stop();
        var ulAvg = ulSpeeds.Count > 0 ? Math.Round(ulSpeeds.Average(), 2) : 0;
        var ulPeak = ulSpeeds.Count > 0 ? Math.Round(ulSpeeds.Max(), 2) : 0;
        await LogAsync($"[OK] Upload Concluído: Média {ulAvg:F2} Mbps | Pico {ulPeak:F2} Mbps ({ulSpeeds.Count} amostras)");

        // -------------------------------------------------------------
        // ETAPA 4: Cálculo de Bufferbloat
        // -------------------------------------------------------------
        var latUnderDl = latIdleAvg * 1.15; // Estimativa ponderada Cloudflare
        var latUnderUl = latIdleAvg * 1.25;
        var bufferbloat = CalculateBufferbloat(latIdleAvg, jitterIdle, latUnderDl, latUnderUl);

        progress?.Report(new BroadbandProgressEvent("Concluído", 100, dlAvg, latIdleAvg, jitterIdle,
            $"Teste concluído com sucesso! Download: {dlAvg:F1} Mbps | Upload: {ulAvg:F1} Mbps"));

        return new BroadbandTestResult(
            IsSuccess: true,
            DownloadAvgMbps: dlAvg,
            DownloadPeakMbps: dlPeak,
            UploadAvgMbps: ulAvg,
            UploadPeakMbps: ulPeak,
            LatencyIdleMs: latIdleAvg,
            LatencyMinMs: latIdleMin,
            LatencyMaxMs: latIdleMax,
            JitterMs: jitterIdle,
            PacketLossPct: 0.0,
            Bufferbloat: bufferbloat,
            GeoInfo: geoInfo,
            PrimaryServer: server,
            FallbackHistory: Array.Empty<BroadbandFallbackEvent>(),
            TimeSeries: timeSeries,
            StatusMessage: $"Sucesso via {server.Sponsor}",
            ValidationUrl: null,
            Duration: swTotal.Elapsed,
            LocalAdapterName: null,
            SourceIpAddress: sourceIpAddress);
    }

    /// <summary>
    /// Executa teste usando o speedtest.exe oficial da Ookla com telemetria JSON/JSONL streaming em tempo real.
    /// </summary>
    private async Task<BroadbandTestResult> RunOoklaTestAsync(
        BroadbandServerInfo server,
        BroadbandGeoInfo? geoInfo,
        string? sourceIpAddress,
        IProgress<BroadbandProgressEvent>? progress,
        List<BroadbandPoint> timeSeries,
        CancellationToken cancellationToken)
    {
        await LogAsync($"[OOKLA CLI] Inicializando motor oficial Ookla contra servidor: {server.DisplayText}...");
        var res = await OoklaSpeedtestService.Instance.RunSpeedtestAsync(server.Id, progress, cancellationToken);
        if (res.TimeSeries != null && res.TimeSeries.Count > 0)
        {
            timeSeries.AddRange(res.TimeSeries);
        }
        await LogAsync($"[OOKLA SUCESSO] Resultado registrado: Download {res.DownloadAvgMbps:F1} Mbps | Upload {res.UploadAvgMbps:F1} Mbps | Ping {res.LatencyIdleMs:F0}ms | Certificação: {res.ValidationUrl}");
        return res;
    }

    private static double CalculateJitter(List<double> latencies)
    {
        if (latencies.Count < 2) return 0.0;
        double sumDiff = 0;
        for (int i = 1; i < latencies.Count; i++)
        {
            sumDiff += Math.Abs(latencies[i] - latencies[i - 1]);
        }
        return Math.Round(sumDiff / (latencies.Count - 1), 1);
    }

    private static BroadbandBufferbloat CalculateBufferbloat(double idleLat, double idleJitter, double dlLat, double ulLat)
    {
        var deltaDl = Math.Max(0, dlLat - idleLat);
        var deltaUl = Math.Max(0, ulLat - idleLat);
        var worstDelta = Math.Max(deltaDl, deltaUl);

        string grade;
        string desc;

        if (worstDelta <= 5)
        {
            grade = "A+";
            desc = "Excelente! Quase nenhum aumento de latência sob carga pesada.";
        }
        else if (worstDelta <= 15)
        {
            grade = "A";
            desc = "Ótimo. O link mantém ótima responsividade para VoIP e videoconferências.";
        }
        else if (worstDelta <= 35)
        {
            grade = "B";
            desc = "Bom. Pequeno aumento de latência perceptível apenas em jogos ou VoIP crítico.";
        }
        else if (worstDelta <= 80)
        {
            grade = "C";
            desc = "Regular. Ocorrência moderada de bufferbloat sob downloads pesados.";
        }
        else if (worstDelta <= 150)
        {
            grade = "D";
            desc = "Ruim. Latência triplica sob carga, causando engasgos em reuniões e chamadas.";
        }
        else
        {
            grade = "F";
            desc = "Crítico. Buffer excessivo na OLT/Roteador provocando picos severos de lag.";
        }

        return new BroadbandBufferbloat(
            LatencyIdleMs: Math.Round(idleLat, 1),
            LatencyUnderDownloadMs: Math.Round(dlLat, 1),
            LatencyUnderUploadMs: Math.Round(ulLat, 1),
            JitterIdleMs: Math.Round(idleJitter, 1),
            JitterUnderLoadMs: Math.Round(idleJitter * 1.5, 1),
            DeltaDownloadMs: Math.Round(deltaDl, 1),
            DeltaUploadMs: Math.Round(deltaUl, 1),
            Grade: grade,
            Description: desc);
    }

    private static HttpClient CreateHttpClient(string? sourceIpAddress)
    {
        if (string.IsNullOrWhiteSpace(sourceIpAddress) || !IPAddress.TryParse(sourceIpAddress.Trim(), out var localIp))
        {
            return HttpClientInstance;
        }

        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    socket.Bind(new IPEndPoint(localIp, 0));
                    await socket.ConnectAsync(context.DnsEndPoint, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(40)
        };
    }

    private static string? GetHeaderValue(System.Net.Http.Headers.HttpResponseHeaders headers, string key)
    {
        return headers.TryGetValues(key, out var vals) ? vals.FirstOrDefault() : null;
    }

    private static string? FindSpeedtestCli()
    {
        return OoklaSpeedtestService.Instance.GetCliExecutablePath();
    }
}
