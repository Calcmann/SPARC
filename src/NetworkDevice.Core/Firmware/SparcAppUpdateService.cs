using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NetworkDevice.Core.Security;

namespace NetworkDevice.Core.Firmware;

public sealed class SparcPlatformRelease
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("publishedAtUtc")]
    public DateTime? PublishedAtUtc { get; set; }

    [JsonPropertyName("minCompatibleVersion")]
    public string? MinCompatibleVersion { get; set; }

    [JsonPropertyName("mandatory")]
    public bool Mandatory { get; set; }
}

public sealed class SparcVersionManifest
{
    [JsonPropertyName("windows")]
    public SparcPlatformRelease? Windows { get; set; }

    [JsonPropertyName("android")]
    public SparcPlatformRelease? Android { get; set; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SparcAppUpdateService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public string RemoteRepoOwner { get; } = "Calcmann";
    public string RemoteRepoName { get; } = "repo";
    public string ReleaseTag { get; } = "homologados";
    public string? GitHubToken { get; set; }

    public SparcAppUpdateService(string? customToken = null)
    {
        GitHubToken = customToken ?? EmbeddedTokenVault.ResolveEmbeddedToken();
    }

    private HttpRequestMessage CreateGitHubRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Updater", "1.0"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        var token = !string.IsNullOrWhiteSpace(GitHubToken) ? GitHubToken : EmbeddedTokenVault.ResolveEmbeddedToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return req;
    }

    /// <summary>
    /// Consulta o repositório oficial e verifica se existe versão homologada superior à instalada.
    /// </summary>
    public async Task<(bool HasUpdate, SparcPlatformRelease? Release, string Message)> CheckForUpdateAsync(
        string currentVersion,
        string platform = "windows",
        CancellationToken ct = default)
    {
        try
        {
            var manifest = await FetchVersionManifestAsync(ct);
            if (manifest == null)
            {
                return (false, null, "Não foi possível carregar o manifesto de versões do repositório.");
            }

            var release = platform.ToLowerInvariant().Contains("and") ? manifest.Android : manifest.Windows;
            if (release == null || string.IsNullOrWhiteSpace(release.Version))
            {
                return (false, null, $"Nenhuma versão homologada registrada para {platform}.");
            }

            if (IsNewerVersion(release.Version, currentVersion))
            {
                return (true, release, $"Nova versão {release.Version} disponível (versão atual: {currentVersion}).");
            }

            return (false, release, $"SPARC está atualizado na versão mais recente ({currentVersion}).");
        }
        catch (Exception ex)
        {
            return (false, null, $"Falha ao checar atualização: {ex.Message}");
        }
    }

    /// <summary>
    /// Baixa o manifesto de versão oficial (primeiro via API contents sem cache, com fallback para raw).
    /// </summary>
    public async Task<SparcVersionManifest?> FetchVersionManifestAsync(CancellationToken ct = default)
    {
        // 1. Tenta API do GitHub para dados em tempo real (evita cache de 300s)
        try
        {
            var apiUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/version.json";
            using var req = CreateGitHubRequest(HttpMethod.Get, apiUrl);
            using var res = await HttpClient.SendAsync(req, ct);

            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("content", out var contentProp))
                {
                    var base64 = contentProp.GetString()?.Replace("\n", "").Replace("\r", "") ?? "";
                    if (!string.IsNullOrWhiteSpace(base64))
                    {
                        var decodedBytes = Convert.FromBase64String(base64);
                        var manifestJson = Encoding.UTF8.GetString(decodedBytes);
                        return JsonSerializer.Deserialize<SparcVersionManifest>(manifestJson);
                    }
                }
            }
        }
        catch { }

        // 2. Fallback: raw.githubusercontent.com
        try
        {
            var rawUrl = $"https://raw.githubusercontent.com/{RemoteRepoOwner}/{RemoteRepoName}/main/version.json?t={DateTime.UtcNow.Ticks}";
            using var reqRaw = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            reqRaw.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Updater", "1.0"));
            using var resRaw = await HttpClient.SendAsync(reqRaw, ct);

            if (resRaw.IsSuccessStatusCode)
            {
                var json = await resRaw.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<SparcVersionManifest>(json);
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Compara se a versão remota é superior à versão local.
    /// Exemplo: "0.8.34" > "0.8.33" -> true.
    /// </summary>
    public static bool IsNewerVersion(string remoteVersionStr, string currentVersionStr)
    {
        if (string.IsNullOrWhiteSpace(remoteVersionStr) || string.IsNullOrWhiteSpace(currentVersionStr))
            return false;

        // Limpa sufixos como "v", " Beta", "-mobile"
        var cleanRemote = CleanVersionString(remoteVersionStr);
        var cleanCurrent = CleanVersionString(currentVersionStr);

        if (Version.TryParse(cleanRemote, out var vRemote) && Version.TryParse(cleanCurrent, out var vCurrent))
        {
            return vRemote > vCurrent;
        }

        // Fallback para divisão por pontos
        var remoteParts = cleanRemote.Split('.');
        var currentParts = cleanCurrent.Split('.');
        var maxLen = Math.Max(remoteParts.Length, currentParts.Length);

        for (int i = 0; i < maxLen; i++)
        {
            int rPart = i < remoteParts.Length && int.TryParse(remoteParts[i], out var rp) ? rp : 0;
            int cPart = i < currentParts.Length && int.TryParse(currentParts[i], out var cp) ? cp : 0;
            if (rPart > cPart) return true;
            if (rPart < cPart) return false;
        }

        return false;
    }

    private static string CleanVersionString(string raw)
    {
        var sb = new StringBuilder();
        bool foundNumber = false;
        foreach (var ch in raw)
        {
            if (char.IsDigit(ch) || ch == '.')
            {
                sb.Append(ch);
                foundNumber = true;
            }
            else if (foundNumber && ch != '.')
            {
                break;
            }
        }
        return sb.ToString().TrimEnd('.');
    }

    /// <summary>
    /// Faz o download do arquivo de atualização exibindo progresso.
    /// Suporta repositórios privados do GitHub resolvendo URLs de releases para a API oficial de assets.
    /// </summary>
    public async Task<string> DownloadUpdateFileAsync(
        string downloadUrl,
        string destinationFilePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var token = !string.IsNullOrWhiteSpace(GitHubToken) ? GitHubToken : EmbeddedTokenVault.ResolveEmbeddedToken();

        // Se for URL web pública de release em repositório privado do GitHub (/releases/download/...),
        // converte automaticamente para o endpoint da API de assets (/releases/assets/{id})
        var effectiveUrl = downloadUrl;
        if (!string.IsNullOrWhiteSpace(token) && effectiveUrl.Contains("github.com/") && effectiveUrl.Contains("/releases/download/"))
        {
            try
            {
                var resolved = await TryResolveReleaseAssetApiUrlAsync(effectiveUrl, token, ct);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    effectiveUrl = resolved;
                }
            }
            catch { }
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, effectiveUrl);
        req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Updater", "1.0"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await HttpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var sourceStream = await response.Content.ReadAsStreamAsync(ct);
        using var destStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                var pct = (double)totalRead / totalBytes * 100.0;
                progress.Report(pct);
            }
        }

        progress?.Report(100.0);
        return destinationFilePath;
    }

    /// <summary>
    /// Aplica a atualização no Windows substituindo o executável atual e reiniciando a aplicação.
    /// Utiliza PowerShell com EncodedCommand (UTF-16LE) e LiteralPath para suportar com total segurança
    /// caminhos com acentuação (ex: 'Área de Trabalho') e sincronizados pelo OneDrive.
    /// </summary>
    public static void ApplyWindowsUpdateAndRestart(string newExePath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe))
        {
            throw new InvalidOperationException("Não foi possível determinar o executável atual do SPARC.");
        }

        if (!File.Exists(newExePath))
        {
            throw new FileNotFoundException("O arquivo de atualização não foi encontrado no disco.", newExePath);
        }

        var fi = new FileInfo(newExePath);
        // O executável do SPARC possui ~76 MB. Se tiver menos de 10 MB, o arquivo baixado está incompleto ou corrompido (ex: erro HTML/JSON).
        if (fi.Length < 10_000_000)
        {
            throw new InvalidDataException($"O arquivo de atualização baixado está incompleto ou corrompido ({fi.Length} bytes). A atualização foi cancelada para proteger a instalação existente.");
        }

        // Validação de cabeçalho PE (Magic bytes MZ: 0x4D, 0x5A)
        using (var fs = File.OpenRead(newExePath))
        {
            var header = new byte[2];
            if (fs.Read(header, 0, 2) < 2 || header[0] != 0x4D || header[1] != 0x5A)
            {
                throw new InvalidDataException("O arquivo de atualização não é um executável Windows válido (cabeçalho PE corrompido). A atualização foi cancelada para proteger a instalação existente.");
            }
        }

        var escapedNew = newExePath.Replace("'", "''");
        var escapedCurrent = currentExe.Replace("'", "''");

        var psScript = $@"
Start-Sleep -Seconds 2
$backup = '{escapedCurrent}.bak'
for ($i = 0; $i -lt 15; $i++) {{
    try {{
        Copy-Item -LiteralPath '{escapedCurrent}' -Destination $backup -Force -ErrorAction SilentlyContinue
        Copy-Item -LiteralPath '{escapedNew}' -Destination '{escapedCurrent}' -Force -ErrorAction Stop
        break
    }} catch {{
        Start-Sleep -Seconds 1
    }}
}}
try {{
    Start-Process -FilePath '{escapedCurrent}' -ErrorAction Stop
    Start-Sleep -Seconds 1
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath '{escapedNew}' -Force -ErrorAction SilentlyContinue
}} catch {{
    if (Test-Path $backup) {{
        Copy-Item -LiteralPath $backup -Destination '{escapedCurrent}' -Force
        Start-Process -FilePath '{escapedCurrent}'
    }}
}}
";

        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encodedCommand}",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        Process.Start(psi);
        Environment.Exit(0);
    }

    /// <summary>
    /// Publica uma nova versão homologada no repositório do GitHub (Release Asset + version.json).
    /// </summary>
    public async Task<(bool Success, string Message, string? DownloadUrl)> PublishReleaseAsync(
        string platform,
        string version,
        string sourceFilePath,
        string releaseNotes,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
        {
            return (false, $"Arquivo fonte não encontrado: {sourceFilePath}", null);
        }

        var token = !string.IsNullOrWhiteSpace(GitHubToken) ? GitHubToken : EmbeddedTokenVault.ResolveEmbeddedToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Token do GitHub não configurado para envio de releases.", null);
        }

        var isAndroid = platform.ToLowerInvariant().Contains("and");
        var fileName = isAndroid ? $"SPARC-Mobile-v{version}.apk" : $"SPARC-Beta-Testes-{version}.exe";

        // 1. Obter ou criar a release oficial 'homologados'
        var (relOk, releaseDetails, relMsg) = await EnsureReleaseExistsAsync(token, ct);
        if (!relOk || releaseDetails == null)
        {
            return (false, $"Falha ao obter release no GitHub: {relMsg}", null);
        }

        // 2. Se já existir asset com mesmo nome, remove para substituir
        var existingAsset = releaseDetails.Assets.Find(a => string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (existingAsset != null)
        {
            await DeleteAssetAsync(existingAsset.Id, token, ct);
        }

        // 3. Fazer upload do binário
        var (uploadOk, downloadUrl, uploadMsg) = await UploadAssetAsync(releaseDetails.UploadUrl, sourceFilePath, fileName, token, ct);
        if (!uploadOk || string.IsNullOrWhiteSpace(downloadUrl))
        {
            return (false, $"Falha no upload do arquivo homologado: {uploadMsg}", null);
        }

        // 4. Atualizar o manifesto version.json no repositório
        var manifest = await FetchVersionManifestAsync(ct) ?? new SparcVersionManifest();
        var releaseInfo = new SparcPlatformRelease
        {
            Version = version.Trim().TrimStart('v', 'V'),
            FileName = fileName,
            DownloadUrl = downloadUrl,
            ReleaseNotes = releaseNotes.Trim(),
            PublishedAtUtc = DateTime.UtcNow,
            Mandatory = false
        };

        if (isAndroid)
        {
            manifest.Android = releaseInfo;
        }
        else
        {
            manifest.Windows = releaseInfo;
        }
        manifest.UpdatedAtUtc = DateTime.UtcNow;

        var saveOk = await SaveVersionManifestToRepoAsync(manifest, token, ct);
        if (!saveOk)
        {
            return (true, $"Binário homologado publicado no GitHub Releases com sucesso, mas houve aviso ao atualizar version.json: {downloadUrl}", downloadUrl);
        }

        return (true, $"Versão {version} para {(isAndroid ? "Android" : "Windows")} publicada com sucesso no repositório oficial!", downloadUrl);
    }

    private async Task<(bool Success, GitHubReleaseDetails? Details, string Message)> EnsureReleaseExistsAsync(string token, CancellationToken ct)
    {
        try
        {
            // Busca release existente pela tag
            var url = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases/tags/{ReleaseTag}";
            using var req = CreateGitHubRequest(HttpMethod.Get, url);
            using var res = await HttpClient.SendAsync(req, ct);

            if (res.IsSuccessStatusCode)
            {
                var json = await res.Content.ReadAsStringAsync(ct);
                var details = ParseReleaseDetails(json);
                return (true, details, "Release encontrada.");
            }

            // Se não existe, cria a release
            var createUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases";
            var payload = new
            {
                tag_name = ReleaseTag,
                name = "SPARC — Versões Homologadas em Campo",
                body = "Repositório central de binários e executáveis oficiais do SPARC para Windows e Android.",
                draft = false,
                prerelease = false
            };

            using var createReq = CreateGitHubRequest(HttpMethod.Post, createUrl);
            createReq.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var createRes = await HttpClient.SendAsync(createReq, ct);

            if (createRes.IsSuccessStatusCode)
            {
                var json = await createRes.Content.ReadAsStringAsync(ct);
                var details = ParseReleaseDetails(json);
                return (true, details, "Release criada com sucesso.");
            }

            return (false, null, $"Status HTTP {createRes.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private static GitHubReleaseDetails ParseReleaseDetails(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt64();
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var uploadUrl = root.GetProperty("upload_url").GetString() ?? "";
        var assets = new System.Collections.Generic.List<GitHubReleaseAssetInfo>();

        if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assetsElem.EnumerateArray())
            {
                var aId = a.GetProperty("id").GetInt64();
                var aName = a.GetProperty("name").GetString() ?? "";
                var aSize = a.GetProperty("size").GetInt64();
                assets.Add(new GitHubReleaseAssetInfo(aId, aName, aSize));
            }
        }

        return new GitHubReleaseDetails(id, tag, uploadUrl, assets);
    }

    private async Task DeleteAssetAsync(long assetId, string token, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases/assets/{assetId}";
            using var req = CreateGitHubRequest(HttpMethod.Delete, url);
            await HttpClient.SendAsync(req, ct);
        }
        catch { }
    }

    private async Task<(bool Success, string? DownloadUrl, string Message)> UploadAssetAsync(
        string rawUploadUrl,
        string filePath,
        string fileName,
        string token,
        CancellationToken ct)
    {
        try
        {
            var cleanUploadUrl = rawUploadUrl.Contains('{') ? rawUploadUrl.Substring(0, rawUploadUrl.IndexOf('{')) : rawUploadUrl;
            var targetUrl = $"{cleanUploadUrl}?name={Uri.EscapeDataString(fileName)}";

            using var req = new HttpRequestMessage(HttpMethod.Post, targetUrl);
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Updater", "1.0"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var contentType = fileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.android.package-archive"
                : "application/octet-stream";

            using var fileStream = File.OpenRead(filePath);
            req.Content = new StreamContent(fileStream);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var res = await HttpClient.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(ct);
                return (false, null, $"Erro HTTP {res.StatusCode}: {err}");
            }

            var resJson = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(resJson);
            var assetApiUrl = doc.RootElement.TryGetProperty("url", out var urlProp) ? urlProp.GetString() : null;
            var browserDownloadUrl = doc.RootElement.TryGetProperty("browser_download_url", out var bProp) ? bProp.GetString() : null;
            var downloadUrl = assetApiUrl ?? browserDownloadUrl;
            return (true, downloadUrl, "Upload concluído.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private async Task<string?> TryResolveReleaseAssetApiUrlAsync(string webDownloadUrl, string token, CancellationToken ct)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(webDownloadUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName)) return null;

            var (ok, release, _) = await EnsureReleaseExistsAsync(token, ct);
            if (!ok || release == null) return null;

            var asset = release.Assets.Find(a => string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase));
            if (asset != null)
            {
                return $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/releases/assets/{asset.Id}";
            }
        }
        catch { }
        return null;
    }

    private async Task<bool> SaveVersionManifestToRepoAsync(SparcVersionManifest manifest, string token, CancellationToken ct)
    {
        try
        {
            var contentUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/version.json";
            string? sha = null;

            // Busca SHA atual se existir
            using var checkReq = CreateGitHubRequest(HttpMethod.Get, contentUrl);
            using var checkRes = await HttpClient.SendAsync(checkReq, ct);
            if (checkRes.IsSuccessStatusCode)
            {
                var checkJson = await checkRes.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(checkJson);
                if (doc.RootElement.TryGetProperty("sha", out var shaProp))
                {
                    sha = shaProp.GetString();
                }
            }

            var jsonContent = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonContent));

            object payload = sha != null
                ? new { message = "Update SPARC version manifest", content = base64, sha = sha }
                : new { message = "Create SPARC version manifest", content = base64 };

            using var putReq = CreateGitHubRequest(HttpMethod.Put, contentUrl);
            putReq.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var putRes = await HttpClient.SendAsync(putReq, ct);
            return putRes.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
