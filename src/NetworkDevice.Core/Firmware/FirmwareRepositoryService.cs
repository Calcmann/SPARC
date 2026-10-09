using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NetworkDevice.Core.Domain;

namespace NetworkDevice.Core.Firmware;

public sealed record FirmwareDownloadProgress(
    long BytesReceived,
    long TotalBytes,
    double Percentage,
    string StatusText);

public sealed record GitHubReleaseDetails(long Id, string TagName, string UploadUrl, List<GitHubReleaseAssetInfo> Assets);
public sealed record GitHubReleaseAssetInfo(long Id, string Name, long SizeBytes);

/// <summary>
/// Serviço central de gerenciamento do repositório de firmwares (online no GitHub e cache local do técnico).
/// </summary>
public sealed class FirmwareRepositoryService
{
    private static readonly HttpClient HttpClient = CreateResilientHttpClient();

    public string? LastQueryError { get; private set; }

    private static HttpClient CreateResilientHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, ct) =>
            {
                // 1. Tenta a rota padrão primária do sistema operacional
                try
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    linkedCts.CancelAfter(TimeSpan.FromSeconds(3));
                    await socket.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, linkedCts.Token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch when (!ct.IsCancellationRequested)
                {
                    // Se falhar ou der timeout na rota primária (ex: bancada sem internet),
                    // tenta conectar vinculando aos outros adaptadores locais IPv4 (ex: Wi-Fi ativo)
                    var candidateIps = GetActiveLocalIpv4Addresses();
                    foreach (var ip in candidateIps)
                    {
                        try
                        {
                            var altSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                            altSocket.Bind(new IPEndPoint(ip, 0));
                            using var altCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            altCts.CancelAfter(TimeSpan.FromSeconds(4));
                            await altSocket.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, altCts.Token).ConfigureAwait(false);
                            return new NetworkStream(altSocket, ownsSocket: true);
                        }
                        catch
                        {
                            // Tenta próximo IP de interface
                        }
                    }
                    throw;
                }
            }
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    private static List<IPAddress> GetActiveLocalIpv4Addresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                var ipProps = ni.GetIPProperties();
                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ua.Address) &&
                        !ua.Address.ToString().StartsWith("169.254."))
                    {
                        list.Add(ua.Address);
                    }
                }
            }
        }
        catch { }
        return list;
    }

    public string RemoteRepoOwner { get; } = "Calcmann";
    public string RemoteRepoName { get; } = "repo";
    public string ReleaseTag { get; } = "homologados";
    public string ReleasesWebUrl => $"https://github.com/{RemoteRepoOwner}/{RemoteRepoName}/releases";
    public string NewReleaseWebUrl => $"https://github.com/{RemoteRepoOwner}/{RemoteRepoName}/releases/new";

    public string? GitHubToken { get; set; }

    private readonly string _localRepositoryRoot;

    public string LocalRepositoryRoot => _localRepositoryRoot;

    public FirmwareRepositoryService(string? customLocalRoot = null, string? customToken = null)
    {
        _localRepositoryRoot = ResolveLocalRoot(customLocalRoot);
        GitHubToken = customToken ?? ResolveGitHubToken();
        EnsureLocalRepositoryStructure();
    }

    private static string? ResolveGitHubToken()
    {
        // 1. Token embutido e criptografado de fábrica no binário
        var vaultToken = Security.EmbeddedTokenVault.ResolveEmbeddedToken();
        if (!string.IsNullOrWhiteSpace(vaultToken)) return vaultToken;

        // 2. Variável de ambiente
        var envToken = Environment.GetEnvironmentVariable("SPARC_GITHUB_TOKEN") ??
                       Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken)) return envToken.Trim();

        // 2. Arquivo em C:\SPARC\beta\github_token.txt
        var betaTokenPath = @"C:\SPARC\beta\github_token.txt";
        if (File.Exists(betaTokenPath))
        {
            var t = File.ReadAllText(betaTokenPath).Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }

        // 3. Arquivo em C:\SPARC\firmwares\github_token.txt
        var fwTokenPath = @"C:\SPARC\firmwares\github_token.txt";
        if (File.Exists(fwTokenPath))
        {
            var t = File.ReadAllText(fwTokenPath).Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }

        return null;
    }

    private HttpRequestMessage CreateGitHubRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
        }

        return req;
    }

    private HttpRequestMessage CreateDownloadRequest(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));

        if (url.Contains("api.github.com/repos/", StringComparison.OrdinalIgnoreCase) &&
            url.Contains("/releases/assets/", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        }

        if (!string.IsNullOrWhiteSpace(GitHubToken) &&
            (url.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
             url.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
        }

        return req;
    }

    public static string? DefaultLocalRootOverride { get; set; }

    private static string ResolveLocalRoot(string? customLocalRoot)
    {
        var targetRoot = customLocalRoot ?? DefaultLocalRootOverride;
        if (!string.IsNullOrWhiteSpace(targetRoot))
        {
            try
            {
                Directory.CreateDirectory(targetRoot);
                return targetRoot;
            }
            catch
            {
                // Fallback caso não seja possível criar o custom
            }
        }

        // 1. Tenta a subpasta 'firmwares' na pasta base do executável (ex: C:\SPARC\firmwares ou D:\SPARC\firmwares)
        try
        {
            var baseFirmwares = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firmwares");
            Directory.CreateDirectory(baseFirmwares);
            return baseFirmwares;
        }
        catch { }

        // 2. Tenta C:\SPARC\firmwares
        try
        {
            var sparcFirmwares = @"C:\SPARC\firmwares";
            Directory.CreateDirectory(sparcFirmwares);
            return sparcFirmwares;
        }
        catch
        {
            // 3. Fallback para %LocalAppData%\SPARC\firmwares
            var localAppData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SPARC",
                "firmwares");
            Directory.CreateDirectory(localAppData);
            return localAppData;
        }
    }

    /// <summary>
    /// Garante que o repositório local possua a árvore de pastas para cada modelo de equipamento suportado.
    /// </summary>
    public void EnsureLocalRepositoryStructure()
    {
        foreach (var def in FirmwareModelMap.AllDefinitions)
        {
            var folder = Path.Combine(_localRepositoryRoot, def.FolderName);
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }
    }

    /// <summary>
    /// Retorna o diretório local específico de um modelo/série.
    /// </summary>
    public string GetLocalFolderForSeries(DeviceSeries series)
    {
        var def = FirmwareModelMap.GetDefinition(series);
        var folderName = def?.FolderName ?? "generic";
        var path = Path.Combine(_localRepositoryRoot, folderName);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return path;
    }

    /// <summary>
    /// Localiza o firmware atualmente disponível no repositório local para o modelo.
    /// </summary>
    public LocalFirmwareInfo? GetLocalFirmware(DeviceSeries series)
    {
        var folder = GetLocalFolderForSeries(series);
        if (!Directory.Exists(folder)) return null;

        var def = FirmwareModelMap.GetDefinition(series);
        var di = new DirectoryInfo(folder);
        var validFiles = di.GetFiles("*.*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Name.StartsWith(".") && !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Where(f => IsFirmwareExtension(f.Extension))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        if (validFiles.Count == 0) return null;

        var file = validFiles[0];
        return new LocalFirmwareInfo(
            series,
            def?.FolderName ?? Path.GetFileName(folder),
            file.FullName,
            file.Name,
            file.Length,
            file.LastWriteTimeUtc,
            ExtractVersionString(file.Name));
    }

    /// <summary>
    /// Consulta os firmwares homologados publicados na Release do GitHub ou nas pastas do repositório (Calcmann/repo).
    /// Suporta repositórios privados com autenticação via GitHub Token.
    /// </summary>
    public async Task<IReadOnlyList<RemoteFirmwareInfo>> QueryRemoteFirmwaresAsync(CancellationToken ct = default)
    {
        var result = new List<RemoteFirmwareInfo>();

        try
        {
            // 1. Tenta consulta direta pela Release da tag homologada
            long? targetReleaseId = null;
            JsonDocument? docAssets = null;

            var tagUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases/tags/{ReleaseTag}";
            using var reqTag = CreateGitHubRequest(HttpMethod.Get, tagUrl);
            using var respTag = await HttpClient.SendAsync(reqTag, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (respTag.IsSuccessStatusCode)
            {
                var jsonTag = await respTag.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var docTag = JsonDocument.Parse(jsonTag);
                if (docTag.RootElement.TryGetProperty("id", out var idProp))
                {
                    targetReleaseId = idProp.GetInt64();
                }
            }

            // Se obteve o ID da release, busca os assets diretamente com per_page=100 para evitar truncamento de 30 itens
            if (targetReleaseId.HasValue)
            {
                var assetsUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases/{targetReleaseId.Value}/assets?per_page=100";
                using var reqAssets = CreateGitHubRequest(HttpMethod.Get, assetsUrl);
                using var respAssets = await HttpClient.SendAsync(reqAssets, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (respAssets.IsSuccessStatusCode)
                {
                    var jsonAssets = await respAssets.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    docAssets = JsonDocument.Parse(jsonAssets);
                }
            }

            // Fallback: se não conseguiu pela tag direta, consulta a lista geral de releases
            if (docAssets == null)
            {
                var apiUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases";
                using var request = CreateGitHubRequest(HttpMethod.Get, apiUrl);
                using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        JsonElement targetRelease = default;
                        bool foundRelease = false;

                        foreach (var rel in doc.RootElement.EnumerateArray())
                        {
                            var tag = rel.TryGetProperty("tag_name", out var tg) ? tg.GetString() : null;
                            if (string.Equals(tag, ReleaseTag, StringComparison.OrdinalIgnoreCase))
                            {
                                targetRelease = rel;
                                foundRelease = true;
                                break;
                            }
                        }

                        if (!foundRelease) targetRelease = doc.RootElement[0];

                        if (targetRelease.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                        {
                            docAssets = JsonDocument.Parse(assets.GetRawText());
                        }
                    }
                }
            }

            if (docAssets != null)
            {
                using (docAssets)
                {
                    var processedSeries = new HashSet<DeviceSeries>();
                    var assetsArray = docAssets.RootElement.ValueKind == JsonValueKind.Array
                        ? docAssets.RootElement
                        : default;

                    if (assetsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assetsArray.EnumerateArray())
                        {
                            var name = asset.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                            if (string.IsNullOrWhiteSpace(name) || name.StartsWith(".")) continue;

                            var ext = Path.GetExtension(name).ToLowerInvariant();
                            if (!IsFirmwareExtension(ext)) continue;

                            var series = FirmwareModelMap.MatchSeriesFromFileName(name);
                            if (series == DeviceSeries.Unknown) continue;

                            if (!processedSeries.Add(series)) continue;

                            var def = FirmwareModelMap.GetDefinition(series);
                            var folderName = def?.FolderName ?? "generic";
                            var size = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0L;
                            var assetApiUrl = asset.TryGetProperty("url", out var u) ? u.GetString() : null;
                            var browserDownloadUrl = asset.TryGetProperty("browser_download_url", out var dl) ? dl.GetString() : null;

                            var downloadUrl = (!string.IsNullOrWhiteSpace(GitHubToken) && !string.IsNullOrWhiteSpace(assetApiUrl))
                                ? assetApiUrl
                                : (browserDownloadUrl ?? assetApiUrl);

                            if (string.IsNullOrWhiteSpace(downloadUrl)) continue;

                            result.Add(new RemoteFirmwareInfo(
                                series,
                                folderName,
                                name,
                                downloadUrl,
                                size,
                                null,
                                ExtractVersionString(name)));
                        }
                    }
                }
            }

            // Fallback: se não houver assets na Release, consulta a árvore de pastas dos modelos (contents/{folder})
            if (result.Count == 0)
            {
                foreach (var def in FirmwareModelMap.AllDefinitions)
                {
                    ct.ThrowIfCancellationRequested();
                    var contentsUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/{def.FolderName}";
                    using var contentReq = CreateGitHubRequest(HttpMethod.Get, contentsUrl);
                    using var contentResp = await HttpClient.SendAsync(contentReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    if (!contentResp.IsSuccessStatusCode) continue;

                    var cJson = await contentResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var cDoc = JsonDocument.Parse(cJson);
                    if (cDoc.RootElement.ValueKind != JsonValueKind.Array) continue;

                    foreach (var elem in cDoc.RootElement.EnumerateArray())
                    {
                        var type = elem.TryGetProperty("type", out var tp) ? tp.GetString() : null;
                        if (type != "file") continue;

                        var name = elem.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                        if (string.IsNullOrWhiteSpace(name) || name.StartsWith(".")) continue;

                        var ext = Path.GetExtension(name).ToLowerInvariant();
                        if (!IsFirmwareExtension(ext)) continue;

                        var size = elem.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0L;
                        var downloadUrl = elem.TryGetProperty("download_url", out var dl) ? dl.GetString() : null;
                        if (string.IsNullOrWhiteSpace(downloadUrl))
                        {
                            downloadUrl = $"https://raw.githubusercontent.com/{RemoteRepoOwner}/{RemoteRepoName}/main/{def.FolderName}/{name}";
                        }

                        result.Add(new RemoteFirmwareInfo(
                            def.Series,
                            def.FolderName,
                            name,
                            downloadUrl,
                            size,
                            null,
                            ExtractVersionString(name)));
                        break;
                    }
                }
            }

            LastQueryError = null;
        }
        catch (HttpRequestException ex)
        {
            LastQueryError = $"Falha de conexão com a base online do GitHub: {ex.Message}";
        }
        catch (Exception ex)
        {
            LastQueryError = $"Erro ao acessar base online do GitHub: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Consulta o arquivo de firmware remoto para um modelo específico a partir dos assets da Release.
    /// </summary>
    public async Task<RemoteFirmwareInfo?> QueryRemoteForSeriesAsync(DeviceSeries series, CancellationToken ct = default)
    {
        var all = await QueryRemoteFirmwaresAsync(ct).ConfigureAwait(false);
        return all.FirstOrDefault(r => r.Series == series);
    }

    /// <summary>
    /// Compara os firmwares locais com os remotos publicados na Release do GitHub e gera o relatório de atualização.
    /// </summary>
    public async Task<FirmwareSyncReport> CheckUpdatesAsync(CancellationToken ct = default)
    {
        var items = new List<FirmwareSyncItem>();
        bool hasInternet = true;
        string? message = null;

        try
        {
            var remotes = await QueryRemoteFirmwaresAsync(ct).ConfigureAwait(false);
            var remoteDict = remotes.ToDictionary(r => r.Series);

            foreach (var def in FirmwareModelMap.AllDefinitions)
            {
                ct.ThrowIfCancellationRequested();

                var local = GetLocalFirmware(def.Series);
                remoteDict.TryGetValue(def.Series, out var remote);

                var (status, desc) = EvaluateStatus(local, remote);
                items.Add(new FirmwareSyncItem(def, status, local, remote, desc));
            }
        }
        catch (Exception ex)
        {
            hasInternet = false;
            message = $"Erro ao verificar repositório remoto: {ex.Message}";
        }

        return new FirmwareSyncReport(items, DateTime.UtcNow, hasInternet, message);
    }

    private static (FirmwareComparisonStatus Status, string Description) EvaluateStatus(
        LocalFirmwareInfo? local,
        RemoteFirmwareInfo? remote)
    {
        if (remote == null)
        {
            if (local == null)
                return (FirmwareComparisonStatus.MissingLocally, "Sem firmware local ou remoto disponível.");
            return (FirmwareComparisonStatus.LocalOnly, $"Presente apenas no cache local ({local.FileName}).");
        }

        if (local == null)
        {
            return (FirmwareComparisonStatus.MissingLocally, $"Novo firmware disponível no repositório online: {remote.FileName} ({remote.DisplaySize}).");
        }

        // Se o nome do arquivo difere (ex: nova versão homologada)
        if (!string.Equals(local.FileName, remote.FileName, StringComparison.OrdinalIgnoreCase))
        {
            return (FirmwareComparisonStatus.UpdateAvailable, $"Nova versão homologada online: {remote.FileName} (atual local: {local.FileName}).");
        }

        // Se o nome é o mesmo mas o tamanho mudou significativamente
        if (remote.SizeBytes > 0 && Math.Abs(local.SizeBytes - remote.SizeBytes) > 1024)
        {
            return (FirmwareComparisonStatus.UpdateAvailable, $"Arquivo atualizado no repositório online ({remote.DisplaySize}).");
        }

        return (FirmwareComparisonStatus.UpToDate, $"Firmware atualizado ({local.FileName}).");
    }

    /// <summary>
    /// Realiza o download seguro do firmware remoto para a pasta correspondente no repositório local.
    /// Mantém apenas o arquivo mais recente na pasta do modelo.
    /// </summary>
    public async Task<string> DownloadFirmwareAsync(
        RemoteFirmwareInfo remote,
        IProgress<FirmwareDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var folder = GetLocalFolderForSeries(remote.Series);
        var targetPath = Path.Combine(folder, remote.FileName);
        var tempPath = Path.Combine(folder, $"{remote.FileName}.tmp.{Guid.NewGuid():N}");

        try
        {
            using var request = CreateDownloadRequest(remote.DownloadUrl);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? remote.SizeBytes;
            var canReportProgress = totalBytes > 0;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    totalRead += read;

                    if (canReportProgress && progress != null)
                    {
                        var pct = Math.Min(100.0, (double)totalRead / totalBytes * 100.0);
                        var mbRead = totalRead / (1024.0 * 1024.0);
                        var mbTotal = totalBytes / (1024.0 * 1024.0);
                        progress.Report(new FirmwareDownloadProgress(
                            totalRead,
                            totalBytes,
                            pct,
                            $"Baixando {remote.FileName}: {pct:F0}% ({mbRead:F1}/{mbTotal:F1} MB)"));
                    }
                }
            }

            // Limpa arquivos de firmware antigos na pasta desse modelo para manter o repositório organizado com arquivo único
            try
            {
                var di = new DirectoryInfo(folder);
                foreach (var oldFile in di.GetFiles("*.*", SearchOption.TopDirectoryOnly))
                {
                    if (oldFile.FullName.Equals(targetPath, StringComparison.OrdinalIgnoreCase) ||
                        oldFile.FullName.Equals(tempPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IsFirmwareExtension(oldFile.Extension))
                    {
                        oldFile.Delete();
                    }
                }
            }
            catch
            {
                // Ignora falha de exclusão de versões obsoletas
            }

            // Substituição atômica/segura
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);

            progress?.Report(new FirmwareDownloadProgress(
                totalBytes,
                totalBytes,
                100.0,
                $"Download concluído: {remote.FileName} ({remote.DisplaySize})"));

            return targetPath;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static bool IsFirmwareExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return false;
        var clean = ext.Trim().ToLowerInvariant();
        return clean is ".bin" or ".out" or ".ipe" or ".pkg";
    }

    private static string? ExtractVersionString(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // Exemplo: c900-universalk9-mz.SPA.159-3.M4.bin -> 159-3.M4
        // Exemplo: FGT_40F-v7.2.6.F-build1575-FORTINET.out -> 7.2.6
        // Exemplo: msr954-cmw710-r0413p03.ipe -> r0413p03
        var verMatch = System.Text.RegularExpressions.Regex.Match(
            fileName,
            @"(?i)(?:v|ver|spa\.|cmw\d+-)?(?<ver>\d+(?:\.\d+)+(?:[-_]build\d+)?|\d{2,3}-\d+\.[A-Za-z0-9]+|r\d+p\d+)");

        return verMatch.Success ? verMatch.Groups["ver"].Value : null;
    }

    public async Task<GitHubReleaseDetails> GetOrCreateReleaseAsync(string tagName = "homologados", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(GitHubToken))
        {
            throw new InvalidOperationException("O Token de Acesso (PAT) do GitHub não está configurado. Salve o token com permissão 'Contents: Read and write' no SPARC Admin.");
        }

        var getTagUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases/tags/{tagName}";
        using var getReq = CreateGitHubRequest(HttpMethod.Get, getTagUrl);
        using var getResp = await HttpClient.SendAsync(getReq, ct).ConfigureAwait(false);

        if (getResp.IsSuccessStatusCode)
        {
            var json = await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseReleaseDetails(json);
        }

        if (getResp.StatusCode == HttpStatusCode.NotFound)
        {
            // Cria a Release se ainda não existir
            var createUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases";
            using var createReq = CreateGitHubRequest(HttpMethod.Post, createUrl);
            var bodyObj = new
            {
                tag_name = tagName,
                name = "Firmwares Homologados SPARC",
                body = "Distribuição oficial de firmwares homologados pelo SPARC Admin.",
                draft = false,
                prerelease = false
            };
            createReq.Content = new StringContent(JsonSerializer.Serialize(bodyObj), System.Text.Encoding.UTF8, "application/json");

            using var createResp = await HttpClient.SendAsync(createReq, ct).ConfigureAwait(false);
            if (!createResp.IsSuccessStatusCode)
            {
                var errJson = await createResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new InvalidOperationException($"Falha ao criar Release '{tagName}' no GitHub ({createResp.StatusCode}): {errJson}");
            }

            var createJson = await createResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseReleaseDetails(createJson);
        }

        if (getResp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Acesso negado (401/403). Certifique-se de que o Token PAT possui permissão 'Contents: Read and write' no repositório.");
        }

        throw new InvalidOperationException($"Erro ao verificar Release no GitHub: {getResp.StatusCode}");
    }

    private static GitHubReleaseDetails ParseReleaseDetails(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt64();
        var tag = root.TryGetProperty("tag_name", out var tp) ? tp.GetString() ?? "homologados" : "homologados";
        var rawUploadUrl = root.GetProperty("upload_url").GetString() ?? "";

        var cleanUploadUrl = rawUploadUrl.Contains('{') ? rawUploadUrl.Substring(0, rawUploadUrl.IndexOf('{')) : rawUploadUrl;

        var assets = new List<GitHubReleaseAssetInfo>();
        if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assetsElem.EnumerateArray())
            {
                var aId = a.GetProperty("id").GetInt64();
                var aName = a.GetProperty("name").GetString() ?? "";
                var aSize = a.TryGetProperty("size", out var sp) ? sp.GetInt64() : 0L;
                assets.Add(new GitHubReleaseAssetInfo(aId, aName, aSize));
            }
        }

        return new GitHubReleaseDetails(id, tag, cleanUploadUrl, assets);
    }

    /// <summary>
    /// Envia um arquivo de firmware local diretamente para a Release 'homologados' no GitHub.
    /// Se um asset de mesmo nome já existir, ele é substituído de forma atômica.
    /// </summary>
    public async Task<bool> UploadFirmwareAssetAsync(
        string localFilePath,
        IProgress<FirmwareDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException($"Arquivo local não encontrado: {localFilePath}");
        }

        var fileName = Path.GetFileName(localFilePath);
        var release = await GetOrCreateReleaseAsync(ReleaseTag, ct).ConfigureAwait(false);

        // Se já existe um asset com esse nome, exclui para substituir pela nova versão homologada
        var existing = release.Assets.FirstOrDefault(a => a.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            var deleteUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases/assets/{existing.Id}";
            using var delReq = CreateGitHubRequest(HttpMethod.Delete, deleteUrl);
            using var delResp = await HttpClient.SendAsync(delReq, ct).ConfigureAwait(false);
        }

        // Upload do novo arquivo
        var uploadTargetUrl = $"{release.UploadUrl}?name={Uri.EscapeDataString(fileName)}";
        using var uploadReq = new HttpRequestMessage(HttpMethod.Post, uploadTargetUrl);
        uploadReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            uploadReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
        }

        using var content = new ProgressFileStreamContent(localFilePath, progress);
        uploadReq.Content = content;

        using var uploadResp = await HttpClient.SendAsync(uploadReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!uploadResp.IsSuccessStatusCode)
        {
            var err = await uploadResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Falha no upload de '{fileName}' para o GitHub ({uploadResp.StatusCode}): {err}");
        }

        return true;
    }
}

internal sealed class ProgressFileStreamContent : HttpContent
{
    private readonly FileStream _fileStream;
    private readonly IProgress<FirmwareDownloadProgress>? _progress;
    private readonly string _fileName;
    private readonly long _totalBytes;

    public ProgressFileStreamContent(string filePath, IProgress<FirmwareDownloadProgress>? progress = null)
    {
        _fileStream = File.OpenRead(filePath);
        _progress = progress;
        _fileName = Path.GetFileName(filePath);
        _totalBytes = _fileStream.Length;
        Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        Headers.ContentLength = _totalBytes;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[81920];
        long totalUploaded = 0;
        int read;

        while ((read = await _fileStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            totalUploaded += read;

            if (_progress != null && _totalBytes > 0)
            {
                var pct = Math.Min(100.0, (double)totalUploaded / _totalBytes * 100.0);
                var mbUp = totalUploaded / (1024.0 * 1024.0);
                var mbTotal = _totalBytes / (1024.0 * 1024.0);
                _progress.Report(new FirmwareDownloadProgress(
                    totalUploaded,
                    _totalBytes,
                    pct,
                    $"Enviando {_fileName}: {pct:F0}% ({mbUp:F1}/{mbTotal:F1} MB)"));
            }
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _totalBytes;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fileStream.Dispose();
        }
        base.Dispose(disposing);
    }
}
