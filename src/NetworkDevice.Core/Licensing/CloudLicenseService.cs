using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Core.Licensing;

public sealed class CloudLicenseService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public string RemoteRepoOwner { get; } = "Calcmann";
    public string RemoteRepoName { get; } = "repo";
    public string DevicesFileName { get; } = "devices.json";
    public string? GitHubToken { get; set; }
    public bool ForceLocalOnly { get; set; } = false;

    private readonly string _localFilePath;

    public string LocalFilePath => _localFilePath;

    public CloudLicenseService(string? customFilePath = null, string? customToken = null)
    {
        _localFilePath = customFilePath ?? ResolveLocalDevicesPath();
        GitHubToken = customToken ?? ResolveGitHubToken();
    }

    private static string? ResolveGitHubToken()
    {
        // 1. Token embutido e criptografado de fábrica no binário
        var vaultToken = Security.EmbeddedTokenVault.ResolveEmbeddedToken();
        if (!string.IsNullOrWhiteSpace(vaultToken)) return vaultToken;

        var envToken = Environment.GetEnvironmentVariable("SPARC_GITHUB_TOKEN") ??
                       Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken)) return envToken.Trim();

        var betaTokenPath = @"C:\SPARC\beta\github_token.txt";
        if (File.Exists(betaTokenPath))
        {
            var t = File.ReadAllText(betaTokenPath).Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }

        var fwTokenPath = @"C:\SPARC\firmwares\github_token.txt";
        if (File.Exists(fwTokenPath))
        {
            var t = File.ReadAllText(fwTokenPath).Trim();
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }

        return null;
    }

    private static string ResolveLocalDevicesPath()
    {
        // 1. C:\SPARC\beta\devices.json
        var betaPath = @"C:\SPARC\beta\devices.json";
        var betaDir = Path.GetDirectoryName(betaPath);
        if (Directory.Exists(betaDir)) return betaPath;

        // 2. C:\SPARC\firmwares\devices.json
        var fwPath = @"C:\SPARC\firmwares\devices.json";
        var fwDir = Path.GetDirectoryName(fwPath);
        if (Directory.Exists(fwDir)) return fwPath;

        // 3. Fallback no AppData
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPARC", "beta");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "devices.json");
    }

    public string DeletedDevicesPath => Path.Combine(Path.GetDirectoryName(_localFilePath) ?? @"C:\SPARC\beta", "deleted_devices.json");
    public string DeletedRequestsPath => Path.Combine(LocalRequestsDirectory, "deleted_requests.json");

    public HashSet<string> LoadDeletedDeviceGuids()
    {
        try
        {
            if (File.Exists(DeletedDevicesPath))
            {
                var json = File.ReadAllText(DeletedDevicesPath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null) return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public void RecordDeletedDeviceGuid(string machineGuid)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return;
        try
        {
            var set = LoadDeletedDeviceGuids();
            if (set.Add(machineGuid.Trim()))
            {
                var dir = Path.GetDirectoryName(DeletedDevicesPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(DeletedDevicesPath, JsonSerializer.Serialize(set.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }
    }

    public void UnrecordDeletedDeviceGuid(string machineGuid)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return;
        try
        {
            var set = LoadDeletedDeviceGuids();
            if (set.Remove(machineGuid.Trim()))
            {
                var dir = Path.GetDirectoryName(DeletedDevicesPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(DeletedDevicesPath, JsonSerializer.Serialize(set.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }
    }

    public HashSet<string> LoadDeletedRequestGuids()
    {
        try
        {
            if (File.Exists(DeletedRequestsPath))
            {
                var json = File.ReadAllText(DeletedRequestsPath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null) return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public void RecordDeletedRequestGuid(string machineGuid)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return;
        try
        {
            var set = LoadDeletedRequestGuids();
            if (set.Add(machineGuid.Trim()))
            {
                var dir = Path.GetDirectoryName(DeletedRequestsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(DeletedRequestsPath, JsonSerializer.Serialize(set.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }
    }

    public void UnrecordDeletedRequestGuid(string machineGuid)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return;
        try
        {
            var set = LoadDeletedRequestGuids();
            if (set.Remove(machineGuid.Trim()))
            {
                var dir = Path.GetDirectoryName(DeletedRequestsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(DeletedRequestsPath, JsonSerializer.Serialize(set.ToList(), new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch { }
    }

    /// <summary>
    /// Carrega os registros de dispositivos salvos localmente no painel do administrador,
    /// filtrando quaisquer dispositivos excluídos para evitar reaparecimento.
    /// </summary>
    public List<OnlineDeviceRecord> LoadLocalDevices()
    {
        if (!File.Exists(_localFilePath)) return new List<OnlineDeviceRecord>();

        try
        {
            var json = File.ReadAllText(_localFilePath);
            var items = JsonSerializer.Deserialize<List<OnlineDeviceRecord>>(json);
            if (items == null) return new List<OnlineDeviceRecord>();

            var deleted = LoadDeletedDeviceGuids();
            if (deleted.Count > 0)
            {
                var countBefore = items.Count;
                items.RemoveAll(d => deleted.Contains(d.MachineGuid));
                if (items.Count != countBefore)
                {
                    SaveLocalDevices(items);
                }
            }
            return items;
        }
        catch
        {
            return new List<OnlineDeviceRecord>();
        }
    }

    /// <summary>
    /// Salva os registros atualizados de dispositivos no arquivo local do administrador.
    /// </summary>
    public void SaveLocalDevices(IEnumerable<OnlineDeviceRecord> devices)
    {
        try
        {
            var dir = Path.GetDirectoryName(_localFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_localFilePath, json);
        }
        catch { }
    }

    /// <summary>
    /// Registra ou atualiza o cadastro de um técnico/máquina a partir de uma solicitação de ativação gerada.
    /// </summary>
    public OnlineDeviceRecord RegisterOrUpdateDevice(
        ActivationRequestData req,
        GeneratedLicenseResult lic,
        string? notes = null)
    {
        if (!string.IsNullOrWhiteSpace(req.MachineGuid))
        {
            UnrecordDeletedDeviceGuid(req.MachineGuid);
        }

        var devices = LoadLocalDevices();
        var existing = devices.FirstOrDefault(d => 
            (!string.IsNullOrEmpty(req.MachineGuid) && d.MachineGuid.Equals(req.MachineGuid, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(req.MachineFingerprint) && d.MachineFingerprint.Equals(req.MachineFingerprint, StringComparison.OrdinalIgnoreCase)));

        if (existing == null)
        {
            existing = new OnlineDeviceRecord
            {
                MachineGuid = req.MachineGuid,
                MachineFingerprint = req.MachineFingerprint,
                FirstName = SparcTextSanitizer.FormatPersonOrCompanyName(req.FirstName),
                LastName = SparcTextSanitizer.FormatPersonOrCompanyName(req.LastName),
                Company = SparcTextSanitizer.FormatPersonOrCompanyName(req.Company),
                EmployeeId = SparcTextSanitizer.FormatEmployeeId(req.EmployeeId),
                Phone = SparcTextSanitizer.FormatPhone(req.Phone),
                Email = SparcTextSanitizer.FormatEmail(req.Email),
                Cluster = SparcTextSanitizer.FormatCluster(req.Cluster),
                Uf = SparcTextSanitizer.FormatUf(req.Uf),
                ClientVersion = req.ClientVersion ?? "0.8",
                FirstRegisteredUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                ExpirationDateIso = lic.ExpirationDateIso,
                ValidDays = lic.ValidDays,
                Status = "Active",
                AuthorizedToken = lic.LicenseToken,
                Notes = notes ?? string.Empty,
                Platform = req.Platform ?? "Windows"
            };
            devices.Insert(0, existing);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(req.FirstName)) existing.FirstName = SparcTextSanitizer.FormatPersonOrCompanyName(req.FirstName);
            if (!string.IsNullOrWhiteSpace(req.LastName)) existing.LastName = SparcTextSanitizer.FormatPersonOrCompanyName(req.LastName);
            if (!string.IsNullOrWhiteSpace(req.Company)) existing.Company = SparcTextSanitizer.FormatPersonOrCompanyName(req.Company);
            if (!string.IsNullOrWhiteSpace(req.EmployeeId)) existing.EmployeeId = SparcTextSanitizer.FormatEmployeeId(req.EmployeeId);
            if (!string.IsNullOrWhiteSpace(req.Phone)) existing.Phone = SparcTextSanitizer.FormatPhone(req.Phone);
            if (!string.IsNullOrWhiteSpace(req.Email)) existing.Email = SparcTextSanitizer.FormatEmail(req.Email);
            if (!string.IsNullOrWhiteSpace(req.Cluster)) existing.Cluster = SparcTextSanitizer.FormatCluster(req.Cluster);
            if (!string.IsNullOrWhiteSpace(req.Uf)) existing.Uf = SparcTextSanitizer.FormatUf(req.Uf);
            if (!string.IsNullOrWhiteSpace(req.ClientVersion)) existing.ClientVersion = req.ClientVersion;
            if (!string.IsNullOrWhiteSpace(req.Platform)) existing.Platform = req.Platform;

            existing.LastSeenUtc = DateTime.UtcNow;
            existing.ExpirationDateIso = lic.ExpirationDateIso;
            existing.ValidDays = lic.ValidDays;
            existing.Status = "Active";
            existing.AuthorizedToken = lic.LicenseToken;
            if (!string.IsNullOrWhiteSpace(notes)) existing.Notes = notes;
        }

        SaveLocalDevices(devices);
        return existing;
    }

    /// <summary>
    /// <summary>
    /// Concede tempo adicional de licença para uma máquina diretamente no controle administrativo.
    /// Gera novo token assinado com a chave privada RSA do gestor.
    /// Se a máquina estiver revogada, ela é automaticamente reativada com status Active.
    /// </summary>
    public (bool Success, string Message, OnlineDeviceRecord? Device) ExtendLicense(
        string machineGuid,
        int additionalDays,
        LicenseSignerService signer,
        string? adminNotes = null)
    {
        var devices = LoadLocalDevices();
        var dev = devices.FirstOrDefault(d => d.MachineGuid.Equals(machineGuid, StringComparison.OrdinalIgnoreCase));
        if (dev == null)
        {
            return (false, "Dispositivo não encontrado no registro local.", null);
        }

        // Calcula a nova data base (a partir da data atual ou da data de expiração anterior se ainda válida e não revogada)
        DateTime baseDate = DateTime.UtcNow;
        if (DateTime.TryParse(dev.ExpirationDateIso, out var currentExp) && 
            currentExp > DateTime.UtcNow && 
            !string.Equals(dev.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
        {
            baseDate = currentExp;
        }

        var totalDays = Math.Max(additionalDays, (int)(baseDate.AddDays(additionalDays) - DateTime.UtcNow).TotalDays);

        var fakeReq = new ActivationRequestData(
            RawRequest: string.Empty,
            MachineGuid: dev.MachineGuid,
            MachineFingerprint: dev.MachineFingerprint,
            IsValid: true,
            ErrorMessage: null,
            FirstName: dev.FirstName,
            LastName: dev.LastName,
            Phone: dev.Phone,
            Email: dev.Email,
            Cluster: dev.Cluster,
            Uf: dev.Uf,
            ClientVersion: dev.ClientVersion,
            Platform: dev.Platform,
            Company: dev.Company,
            EmployeeId: dev.EmployeeId);

        var result = signer.GenerateLicense(fakeReq, totalDays, dev.FullName, adminNotes ?? $"Renovação de +{additionalDays} dias via Admin");
        if (!result.Success || string.IsNullOrEmpty(result.LicenseToken))
        {
            return (false, $"Falha ao gerar assinatura criptográfica: {result.ErrorMessage}", dev);
        }

        dev.ExpirationDateIso = result.ExpirationDateIso;
        dev.ValidDays = totalDays;
        dev.Status = "Active";
        dev.AuthorizedToken = result.LicenseToken;
        dev.LastSeenUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(adminNotes))
        {
            dev.Notes = adminNotes;
        }
        else if (dev.Notes.Contains("[REVOGADO]"))
        {
            dev.Notes = $"[REATIVADO/ESTENDIDO]: Renovado por +{additionalDays} dias em {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC";
        }

        SaveLocalDevices(devices);
        return (true, $"Licença estendida com sucesso até {result.ExpirationDateIso} ({totalDays} dias).", dev);
    }

    /// <summary>
    /// Libera e reativa imediatamente o acesso de um dispositivo previamente revogado ou bloqueado.
    /// Gera novo token assinado com a chave privada RSA do gestor e restabelece status para Active.
    /// </summary>
    public (bool Success, string Message, OnlineDeviceRecord? Device) UnrevokeDevice(
        string machineGuid,
        LicenseSignerService signer,
        int additionalDays = 30,
        string? adminNotes = null)
    {
        var devices = LoadLocalDevices();
        var dev = devices.FirstOrDefault(d => d.MachineGuid.Equals(machineGuid, StringComparison.OrdinalIgnoreCase));
        if (dev == null) return (false, "Dispositivo não encontrado no registro local.", null);

        DateTime baseDate = DateTime.UtcNow;
        if (DateTime.TryParse(dev.ExpirationDateIso, out var currentExp) && 
            currentExp > DateTime.UtcNow &&
            !string.Equals(dev.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
        {
            baseDate = currentExp;
        }

        var totalDays = Math.Max(additionalDays, (int)(baseDate.AddDays(additionalDays) - DateTime.UtcNow).TotalDays);

        var reqData = new ActivationRequestData(
            RawRequest: string.Empty,
            MachineGuid: dev.MachineGuid,
            MachineFingerprint: dev.MachineFingerprint,
            IsValid: true,
            ErrorMessage: null,
            FirstName: dev.FirstName,
            LastName: dev.LastName,
            Phone: dev.Phone,
            Email: dev.Email,
            Cluster: dev.Cluster,
            Uf: dev.Uf,
            ClientVersion: dev.ClientVersion,
            Platform: dev.Platform,
            Company: dev.Company,
            EmployeeId: dev.EmployeeId);

        var result = signer.GenerateLicense(reqData, totalDays, dev.FullName, adminNotes ?? $"Acesso liberado/reativado pelo Administrador (+{totalDays} dias)");
        if (!result.Success || string.IsNullOrEmpty(result.LicenseToken))
        {
            return (false, $"Falha ao gerar nova chave criptográfica: {result.ErrorMessage}", dev);
        }

        dev.Status = "Active";
        dev.ExpirationDateIso = result.ExpirationDateIso;
        dev.ValidDays = totalDays;
        dev.AuthorizedToken = result.LicenseToken;
        dev.LastSeenUtc = DateTime.UtcNow;
        dev.Notes = !string.IsNullOrWhiteSpace(adminNotes) && adminNotes.Contains("[LIBERADO]")
            ? adminNotes
            : $"[LIBERADO]: {adminNotes ?? $"Reativado pelo administrador em {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC (+{totalDays} dias)"}";

        SaveLocalDevices(devices);
        return (true, $"Acesso liberado com sucesso para {dev.FullName}! Válido até {dev.ExpirationDateIso} ({totalDays} dias).", dev);
    }

    /// <summary>
    /// Libera e reativa o acesso de um dispositivo previamente revogado e sincroniza imediatamente com a nuvem (devices.json).
    /// </summary>
    public async Task<(bool Success, string Message, OnlineDeviceRecord? Device)> UnrevokeDeviceAsync(
        string machineGuid,
        LicenseSignerService signer,
        int additionalDays = 30,
        string? adminNotes = null,
        CancellationToken ct = default)
    {
        var (ok, msg, dev) = UnrevokeDevice(machineGuid, signer, additionalDays, adminNotes);
        if (ok)
        {
            await SyncDevicesToRemoteAsync(ct).ConfigureAwait(false);
        }
        return (ok, msg, dev);
    }

    /// <summary>
    /// Revoga imediatamente a licença de um dispositivo/técnico.
    /// </summary>
    public (bool Success, string Message) RevokeDevice(string machineGuid, string reason)
    {
        var devices = LoadLocalDevices();
        var dev = devices.FirstOrDefault(d => d.MachineGuid.Equals(machineGuid, StringComparison.OrdinalIgnoreCase));
        if (dev == null) return (false, "Dispositivo não encontrado.");

        dev.Status = "Revoked";
        dev.Notes = $"[REVOGADO]: {reason} (em {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC)";
        SaveLocalDevices(devices);
        return (true, $"Acesso revogado com sucesso para {dev.FullName}.");
    }

    /// <summary>
    /// Consulta o repositório online para obter a lista atualizada de autorizações e status de máquinas.
    /// Tenta primeiro via API do GitHub (sem atraso de cache CDN) e faz fallback para o raw content.
    /// </summary>
    public async Task<List<OnlineDeviceRecord>> FetchRemoteDevicesAsync(CancellationToken ct = default)
    {
        if (ForceLocalOnly)
        {
            return LoadLocalDevices();
        }

        if (string.IsNullOrWhiteSpace(GitHubToken))
        {
            GitHubToken = ResolveGitHubToken();
        }

        // 1. Tenta via GitHub API contents (resposta instantânea, sem atraso de cache CDN)
        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            try
            {
                var apiUrl = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/{DevicesFileName}";
                using var apiReq = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                apiReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                apiReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                apiReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var apiResp = await HttpClient.SendAsync(apiReq, ct).ConfigureAwait(false);
                if (apiResp.IsSuccessStatusCode)
                {
                    var respJson = await apiResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(respJson);
                    if (doc.RootElement.TryGetProperty("content", out var contentProp))
                    {
                        var b64 = contentProp.GetString()?.Replace("\n", "").Replace("\r", "");
                        if (!string.IsNullOrEmpty(b64))
                        {
                            var bytes = Convert.FromBase64String(b64);
                            var json = Encoding.UTF8.GetString(bytes);
                            var list = JsonSerializer.Deserialize<List<OnlineDeviceRecord>>(json);
                            if (list != null)
                            {
                                var deletedGuids = LoadDeletedDeviceGuids();
                                if (deletedGuids.Count > 0)
                                {
                                    list.RemoveAll(d => deletedGuids.Contains(d.MachineGuid));
                                }
                                return list;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Fallback via Raw URL com bypass de CDN cache
        try
        {
            var rawUrl = $"https://raw.githubusercontent.com/{RemoteRepoOwner}/{RemoteRepoName}/main/{DevicesFileName}?t={DateTime.UtcNow.Ticks}";
            using var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            if (!string.IsNullOrWhiteSpace(GitHubToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
            }

            using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new List<OnlineDeviceRecord>();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<List<OnlineDeviceRecord>>(json) ?? new List<OnlineDeviceRecord>();
            var deletedGuids = LoadDeletedDeviceGuids();
            if (deletedGuids.Count > 0)
            {
                parsed.RemoveAll(d => deletedGuids.Contains(d.MachineGuid));
            }
            return parsed;
        }
        catch
        {
            return new List<OnlineDeviceRecord>();
        }
    }

    /// <summary>
    /// Sincroniza a base de dispositivos local com a nuvem (devices.json no GitHub).
    /// </summary>
    public async Task<(bool Success, string Message)> SyncDevicesToRemoteAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(GitHubToken))
        {
            GitHubToken = ResolveGitHubToken();
        }

        if (string.IsNullOrWhiteSpace(GitHubToken))
        {
            return (false, "Token do GitHub não configurado.");
        }

        try
        {
            var url = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/{DevicesFileName}";

            // Busca SHA atual para evitar conflitos de concorrência
            string? currentSha = null;
            try
            {
                using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                getReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                getReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var getResp = await HttpClient.SendAsync(getReq, ct).ConfigureAwait(false);
                if (getResp.IsSuccessStatusCode)
                {
                    var respJson = await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(respJson);
                    if (doc.RootElement.TryGetProperty("sha", out var sp))
                    {
                        currentSha = sp.GetString();
                    }
                }
            }
            catch { }

            var devices = LoadLocalDevices();
            var json = JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true });
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

            var payload = new Dictionary<string, object>
            {
                ["message"] = $"Atualizacao sincronizada de licencas SPARC ({devices.Count} dispositivos)",
                ["content"] = base64
            };
            if (!string.IsNullOrWhiteSpace(currentSha))
            {
                payload["sha"] = currentSha;
            }

            using var putReq = new HttpRequestMessage(HttpMethod.Put, url);
            putReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
            putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
            putReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
            putReq.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var putResp = await HttpClient.SendAsync(putReq, ct).ConfigureAwait(false);
            if (putResp.IsSuccessStatusCode)
            {
                return (true, "Base de licenças sincronizada na nuvem com sucesso!");
            }

            var errBody = await putResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (false, $"Erro na nuvem ({putResp.StatusCode}): {errBody}");
        }
        catch (Exception ex)
        {
            return (false, $"Erro de conexão ao sincronizar: {ex.Message}");
        }
    }

    /// <summary>
    /// Baixa a versão mais recente de devices.json da nuvem e salva localmente.
    /// </summary>
    public async Task<List<OnlineDeviceRecord>> SyncDevicesFromRemoteAsync(CancellationToken ct = default)
    {
        try
        {
            var remoteList = await FetchRemoteDevicesAsync(ct).ConfigureAwait(false);
            if (remoteList != null && remoteList.Count > 0)
            {
                SaveLocalDevices(remoteList);
                return remoteList;
            }
        }
        catch { }

        return LoadLocalDevices();
    }

    /// <summary>
    /// Validação prioritária de licença na inicialização (Windows e Android):
    /// 1. Se online: consulta status em tempo real. Se revogado ou expirado, bloqueia; se tem novo token/prorrogação, atualiza localmente.
    /// 2. Se offline: valida chave local armazenada e permite uso se válida.
    /// </summary>
    public async Task<(bool Allowed, bool Revoked, string? NewToken, string Message)> VerifyLicenseStartupAsync(
        string machineGuid,
        string machineFingerprint,
        string? currentLocalToken,
        Func<string, bool> offlineValidator,
        CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var remoteList = await FetchRemoteDevicesAsync(timeoutCts.Token).ConfigureAwait(false);
            if (remoteList != null && remoteList.Count > 0)
            {
                var dev = remoteList.FirstOrDefault(d =>
                    (!string.IsNullOrEmpty(machineGuid) && d.MachineGuid.Equals(machineGuid, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(machineFingerprint) && d.MachineFingerprint.Equals(machineFingerprint, StringComparison.OrdinalIgnoreCase)));

                if (dev != null)
                {
                    if (string.Equals(dev.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, true, null, "Esta cópia do SPARC foi suspensa ou revogada pelo Administrador.");
                    }

                    var isExpired = dev.CalculatedStatus == DeviceLicenseStatus.Expired ||
                                    (DateTime.TryParse(dev.ExpirationDateIso, out var exp) && exp.Date < DateTime.UtcNow.Date);

                    if (!string.IsNullOrWhiteSpace(dev.AuthorizedToken))
                    {
                        if (!dev.AuthorizedToken.Equals(currentLocalToken, StringComparison.Ordinal))
                        {
                            if (offlineValidator(dev.AuthorizedToken))
                            {
                                return (true, false, dev.AuthorizedToken, $"Licença sincronizada com a nuvem (Válida até {dev.ExpirationDateIso}).");
                            }
                        }
                        else
                        {
                            if (!isExpired && offlineValidator(currentLocalToken))
                            {
                                return (true, false, null, $"Licença online verificada e ativa até {dev.ExpirationDateIso}.");
                            }
                            return (false, false, null, $"Licença expirada em {dev.ExpirationDateIso}. Solicite ampliação do prazo ao Administrador.");
                        }
                    }

                    if (isExpired)
                    {
                        return (false, false, null, $"Licença expirada em {dev.ExpirationDateIso}. Solicite ampliação do prazo ao Administrador.");
                    }
                }
            }
        }
        catch
        {
            // Timeout ou offline: cai no fallback abaixo
        }

        // Fallback Offline
        if (!string.IsNullOrWhiteSpace(currentLocalToken) && offlineValidator(currentLocalToken))
        {
            return (true, false, null, "Modo Offline: Chave de ativação local validada com sucesso.");
        }

        return (false, false, null, "Chave de ativação inválida, ausente ou expirada.");
    }

    /// <summary>
    /// Utilizado pelo SPARC do técnico para checar se o administrador concedeu mais tempo online,
    /// liberou acesso ou revogou a máquina.
    /// </summary>
    public async Task<(bool HasNewAuthorization, string? NewToken, bool IsRevoked, bool IsExpired, string? Message)> CheckOnlineStatusAsync(
        string machineGuid,
        string machineFingerprint,
        string? currentLocalToken,
        CancellationToken ct = default)
    {
        try
        {
            var remoteList = await FetchRemoteDevicesAsync(ct).ConfigureAwait(false);
            var dev = remoteList.FirstOrDefault(d =>
                (!string.IsNullOrEmpty(machineGuid) && d.MachineGuid.Equals(machineGuid, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(machineFingerprint) && d.MachineFingerprint.Equals(machineFingerprint, StringComparison.OrdinalIgnoreCase)));

            if (dev == null)
            {
                return (false, null, false, false, "Máquina não encontrada no cadastro online.");
            }

            if (string.Equals(dev.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, true, false, "Licença revogada pelo Administrador.");
            }

            var isExpired = dev.CalculatedStatus == DeviceLicenseStatus.Expired ||
                            (DateTime.TryParse(dev.ExpirationDateIso, out var exp) && exp.Date < DateTime.UtcNow.Date);

            if (!string.IsNullOrWhiteSpace(dev.AuthorizedToken) && !dev.AuthorizedToken.Equals(currentLocalToken, StringComparison.Ordinal))
            {
                return (true, dev.AuthorizedToken, false, isExpired, $"Nova autorização concedida até {dev.ExpirationDateIso}.");
            }

            if (isExpired)
            {
                return (false, null, false, true, $"Licença expirada em {dev.ExpirationDateIso}.");
            }

            return (false, null, false, false, "Licença atualizada.");
        }
        catch (Exception ex)
        {
            return (false, null, false, false, $"Falha de conexão com a nuvem: {ex.Message}");
        }
    }

    public string LocalRequestsDirectory
    {
        get
        {
            var dir = Path.Combine(Path.GetDirectoryName(_localFilePath) ?? @"C:\SPARC\beta", "requests");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }
    }

    /// <summary>
    /// Envia uma solicitação de ativação online para a nuvem (e/ou pasta local).
    /// </summary>
    public async Task<(bool Success, string Message)> SubmitActivationRequestAsync(
        OnlineActivationRequest req,
        CancellationToken ct = default)
    {
        if (req == null) return (false, "Dados de solicitação inválidos.");
        if (string.IsNullOrWhiteSpace(req.MachineGuid)) return (false, "Identificador de máquina ausente.");
        if (string.IsNullOrWhiteSpace(req.Status))
        {
            req.Status = "Pending";
        }
        if (req.RequestedAtUtc == default)
        {
            req.RequestedAtUtc = DateTime.UtcNow;
        }

        UnrecordDeletedRequestGuid(req.MachineGuid);

        var json = JsonSerializer.Serialize(req, new JsonSerializerOptions { WriteIndented = true });

        // 1. Salva localmente como backup imediato
        try
        {
            var localFile = Path.Combine(LocalRequestsDirectory, $"{req.MachineGuid}.json");
            await File.WriteAllTextAsync(localFile, json, ct).ConfigureAwait(false);
        }
        catch { }

        // 2. Se houver token GitHub configurado (ou obtido via cofre embutido), envia para a nuvem via API do GitHub
        if (!ForceLocalOnly)
        {
            if (string.IsNullOrWhiteSpace(GitHubToken))
            {
                GitHubToken = ResolveGitHubToken();
            }

            if (!string.IsNullOrWhiteSpace(GitHubToken) && GitHubToken != "none")
            {
            try
            {
                var fileName = $"requests/{req.MachineGuid}.json";
                var url = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/{fileName}";

                // Checa SHA pré-existente
                string? existingSha = req.RemoteSha;
                if (string.IsNullOrWhiteSpace(existingSha))
                {
                    try
                    {
                        using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                        getReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                        getReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                        using var getResp = await HttpClient.SendAsync(getReq, ct).ConfigureAwait(false);
                        if (getResp.IsSuccessStatusCode)
                        {
                            var respJson = await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            using var doc = JsonDocument.Parse(respJson);
                            if (doc.RootElement.TryGetProperty("sha", out var shaProp))
                            {
                                existingSha = shaProp.GetString();
                            }
                        }
                    }
                    catch { }
                }

                var base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                var payloadObj = new Dictionary<string, object>
                {
                    ["message"] = $"Solicitacao ativacao: {req.FullName} ({req.RegionInfo})",
                    ["content"] = base64Content
                };
                if (!string.IsNullOrWhiteSpace(existingSha))
                {
                    payloadObj["sha"] = existingSha;
                }

                using var putReq = new HttpRequestMessage(HttpMethod.Put, url);
                putReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                putReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
                putReq.Content = new StringContent(JsonSerializer.Serialize(payloadObj), Encoding.UTF8, "application/json");

                using var putResp = await HttpClient.SendAsync(putReq, ct).ConfigureAwait(false);
                if (putResp.IsSuccessStatusCode)
                {
                    return (true, "Solicitação de ativação enviada com sucesso para a nuvem!");
                }
                else
                {
                    var errBody = await putResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return (false, $"Nuvem retornou código {putResp.StatusCode}: {errBody}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erro ao conectar à nuvem: {ex.Message}");
            }
            }
            else
            {
                return (false, "Token da nuvem não configurado. Apenas cópia local foi salva.");
            }
        }

        return (true, "Solicitação salva localmente.");
    }

    /// <summary>
    /// Consulta o status de aprovação de uma solicitação de ativação específica.
    /// Utilizado pelo SPARC na máquina do técnico para ativação automática assim que liberado.
    /// </summary>
    public async Task<(bool Found, OnlineActivationRequest? Request, string Message)> CheckActivationRequestStatusAsync(
        string machineGuid,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return (false, null, "GUID não informado.");

        var deletedReqs = LoadDeletedRequestGuids();
        if (deletedReqs.Contains(machineGuid))
        {
            return (false, null, "Solicitação foi excluída.");
        }

        // 1. Tenta buscar da nuvem se token estiver configurado
        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            try
            {
                var fileName = $"requests/{machineGuid}.json";
                var url = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/{fileName}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var resp = await HttpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var respJson = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(respJson);
                    string? sha = doc.RootElement.TryGetProperty("sha", out var sp) ? sp.GetString() : null;
                    if (doc.RootElement.TryGetProperty("content", out var contentProp))
                    {
                        var b64 = contentProp.GetString()?.Replace("\n", "").Replace("\r", "");
                        if (!string.IsNullOrEmpty(b64))
                        {
                            var rawBytes = Convert.FromBase64String(b64);
                            var itemJson = Encoding.UTF8.GetString(rawBytes);
                            var obj = JsonSerializer.Deserialize<OnlineActivationRequest>(itemJson);
                            if (obj != null)
                            {
                                obj.RemoteSha = sha;
                                return (true, obj, "Solicitação localizada na nuvem.");
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // 2. Fallback para arquivo local
        try
        {
            var localFile = Path.Combine(LocalRequestsDirectory, $"{machineGuid}.json");
            if (File.Exists(localFile))
            {
                var json = await File.ReadAllTextAsync(localFile, ct).ConfigureAwait(false);
                var obj = JsonSerializer.Deserialize<OnlineActivationRequest>(json);
                if (obj != null)
                {
                    return (true, obj, "Solicitação localizada localmente.");
                }
            }
        }
        catch { }

        return (false, null, "Nenhuma solicitação encontrada para esta máquina.");
    }

    /// <summary>
    /// Lista todas as solicitações de ativação (pendentes, aprovadas e rejeitadas) para o painel Admin.
    /// </summary>
    public async Task<List<OnlineActivationRequest>> ListActivationRequestsAsync(CancellationToken ct = default)
    {
        var resultList = new Dictionary<string, OnlineActivationRequest>(StringComparer.OrdinalIgnoreCase);
        var deletedReqs = LoadDeletedRequestGuids();

        // 1. Carrega registros locais (purgando os que constam como excluídos)
        try
        {
            if (Directory.Exists(LocalRequestsDirectory))
            {
                foreach (var file in Directory.GetFiles(LocalRequestsDirectory, "*.json"))
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var req = JsonSerializer.Deserialize<OnlineActivationRequest>(json);
                        if (req != null && !string.IsNullOrWhiteSpace(req.MachineGuid))
                        {
                            if (deletedReqs.Contains(req.MachineGuid) || (!string.IsNullOrWhiteSpace(req.RemoteFileName) && deletedReqs.Contains(req.RemoteFileName)))
                            {
                                try { File.Delete(file); } catch { }
                                continue;
                            }
                            resultList[req.MachineGuid] = req;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 2. Consulta diretório remoto no GitHub se token estiver disponível
        if (string.IsNullOrWhiteSpace(GitHubToken))
        {
            GitHubToken = ResolveGitHubToken();
        }

        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            try
            {
                var url = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/requests";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var resp = await HttpClient.SendAsync(req, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var respJson = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(respJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in doc.RootElement.EnumerateArray())
                        {
                            var name = item.TryGetProperty("name", out var np) ? np.GetString() : null;
                            var downloadUrl = item.TryGetProperty("download_url", out var dp) ? dp.GetString() : null;
                            var sha = item.TryGetProperty("sha", out var sp) ? sp.GetString() : null;

                            if (!string.IsNullOrEmpty(name) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(downloadUrl))
                            {
                                if (deletedReqs.Contains(name) || deletedReqs.Contains(name.Replace(".json", "", StringComparison.OrdinalIgnoreCase)))
                                {
                                    // Se ainda existir na nuvem, dispara deleção para limpar repositório
                                    _ = DeleteRemoteFileByNameAsync($"requests/{name}", sha, ct);
                                    continue;
                                }

                                try
                                {
                                    using var dlReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                                    dlReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                                    dlReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);

                                    using var dlResp = await HttpClient.SendAsync(dlReq, ct).ConfigureAwait(false);
                                    if (dlResp.IsSuccessStatusCode)
                                    {
                                        var itemJson = await dlResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                                        var onlineReq = JsonSerializer.Deserialize<OnlineActivationRequest>(itemJson);
                                        if (onlineReq != null && !string.IsNullOrWhiteSpace(onlineReq.MachineGuid))
                                        {
                                            if (deletedReqs.Contains(onlineReq.MachineGuid))
                                            {
                                                _ = DeleteRemoteFileByNameAsync($"requests/{name}", sha, ct);
                                                continue;
                                            }

                                            onlineReq.RemoteFileName = name;
                                            onlineReq.RemoteSha = sha;
                                            resultList[onlineReq.MachineGuid] = onlineReq;

                                            // Atualiza cache local
                                            try
                                            {
                                                var localFile = Path.Combine(LocalRequestsDirectory, $"{onlineReq.MachineGuid}.json");
                                                File.WriteAllText(localFile, itemJson);
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        return resultList.Values.OrderByDescending(r => r.RequestedAtUtc).ToList();
    }

    /// <summary>
    /// Deleta um arquivo específico do repositório remoto via API do GitHub.
    /// </summary>
    private async Task<bool> DeleteRemoteFileByNameAsync(string filePath, string? knownSha, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        if (string.IsNullOrWhiteSpace(GitHubToken)) GitHubToken = ResolveGitHubToken();
        if (string.IsNullOrWhiteSpace(GitHubToken)) return false;

        try
        {
            var cleanPath = filePath.TrimStart('/');
            var url = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/{cleanPath}";

            var sha = knownSha;
            if (string.IsNullOrWhiteSpace(sha))
            {
                using var getReq = new HttpRequestMessage(HttpMethod.Get, url);
                getReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                getReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                using var getResp = await HttpClient.SendAsync(getReq, ct).ConfigureAwait(false);
                if (getResp.StatusCode == System.Net.HttpStatusCode.NotFound) return true;
                if (getResp.IsSuccessStatusCode)
                {
                    var respJson = await getResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(respJson);
                    if (doc.RootElement.TryGetProperty("sha", out var sp))
                    {
                        sha = sp.GetString();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(sha))
            {
                using var delReq = new HttpRequestMessage(HttpMethod.Delete, url);
                delReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                delReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                var delPayload = new Dictionary<string, string>
                {
                    ["message"] = $"Exclusao de solicitacao: {Path.GetFileName(cleanPath)}",
                    ["sha"] = sha
                };
                delReq.Content = new StringContent(JsonSerializer.Serialize(delPayload), Encoding.UTF8, "application/json");

                using var delResp = await HttpClient.SendAsync(delReq, ct).ConfigureAwait(false);
                return delResp.IsSuccessStatusCode || delResp.StatusCode == System.Net.HttpStatusCode.NotFound;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Aprova uma solicitação de ativação online:
    /// 1. Gera a assinatura criptográfica RSA com a chave privada do Administrador;
    /// 2. Atualiza a solicitação para "Approved" e embute o token de ativação na nuvem e localmente;
    /// 3. Cadastra a máquina automaticamente na lista de dispositivos monitorados (devices.json).
    /// </summary>
    public async Task<(bool Success, string Message, string? LicenseToken)> ApproveActivationRequestAsync(
        OnlineActivationRequest request,
        int validDays,
        LicenseSignerService signer,
        string? adminNotes = null,
        CancellationToken ct = default)
    {
        if (request == null) return (false, "Solicitação inválida.", null);

        // Constrói objeto de solicitação para o signer
        var reqData = new ActivationRequestData(
            RawRequest: request.RawRequestCode,
            MachineGuid: request.MachineGuid,
            MachineFingerprint: request.MachineFingerprint,
            IsValid: true,
            ErrorMessage: null,
            FirstName: request.FirstName,
            LastName: request.LastName,
            Phone: request.Phone,
            Email: request.Email,
            Cluster: request.Cluster,
            Uf: request.Uf,
            ClientVersion: request.ClientVersion,
            Platform: request.Platform,
            Company: request.Company,
            EmployeeId: request.EmployeeId);

        var licResult = signer.GenerateLicense(reqData, validDays, request.FullName, adminNotes ?? "Aprovado via SPARC Admin Online");
        if (!licResult.Success || string.IsNullOrEmpty(licResult.LicenseToken))
        {
            return (false, $"Falha ao gerar chave criptográfica: {licResult.ErrorMessage}", null);
        }

        request.Status = "Approved";
        request.ApprovedToken = licResult.LicenseToken;
        request.ExpirationDateIso = licResult.ExpirationDateIso;
        request.ValidDays = validDays;
        request.AdminNotes = adminNotes ?? request.AdminNotes;

        // 1. Atualiza a solicitação online/local
        await SubmitActivationRequestAsync(request, ct).ConfigureAwait(false);

        // 2. Insere/atualiza na lista de dispositivos ativos (devices.json)
        RegisterOrUpdateDevice(reqData, licResult, adminNotes);

        // 3. Sincroniza a lista de dispositivos atualizada na nuvem
        _ = SyncDevicesToRemoteAsync(ct);

        return (true, $"Solicitação de {request.FullName} aprovada com sucesso! Válida por {validDays} dias até {licResult.ExpirationDateIso}.", licResult.LicenseToken);
    }

    /// <summary>
    /// Rejeita uma solicitação de ativação online com motivo especificado.
    /// </summary>
    public async Task<(bool Success, string Message)> RejectActivationRequestAsync(
        OnlineActivationRequest request,
        string reason,
        CancellationToken ct = default)
    {
        if (request == null) return (false, "Solicitação inválida.");

        request.Status = "Rejected";
        request.RejectionReason = reason;

        await SubmitActivationRequestAsync(request, ct).ConfigureAwait(false);
        return (true, $"Solicitação de {request.FullName} rejeitada.");
    }

    /// <summary>
    /// Exclui uma solicitação de ativação online do disco local e do repositório remoto no GitHub,
    /// gravando no registro de exclusões para que nunca mais retorne na inicialização.
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteActivationRequestAsync(
        OnlineActivationRequest request,
        CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.MachineGuid))
            return (false, "Solicitação inválida.");

        // 1. Registra no tombstone de exclusões locais
        RecordDeletedRequestGuid(request.MachineGuid);
        if (!string.IsNullOrWhiteSpace(request.RemoteFileName))
        {
            RecordDeletedRequestGuid(request.RemoteFileName);
            RecordDeletedRequestGuid(request.RemoteFileName.Replace(".json", "", StringComparison.OrdinalIgnoreCase));
        }

        // 2. Remove do disco local
        try
        {
            var localFile = Path.Combine(LocalRequestsDirectory, $"{request.MachineGuid}.json");
            if (File.Exists(localFile))
            {
                File.Delete(localFile);
            }
        }
        catch { }

        // 3. Remove do repositório remoto via API DELETE
        if (string.IsNullOrWhiteSpace(GitHubToken))
        {
            GitHubToken = ResolveGitHubToken();
        }

        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            try
            {
                var cleanName = !string.IsNullOrWhiteSpace(request.RemoteFileName) 
                    ? request.RemoteFileName 
                    : $"{request.MachineGuid}.json";
                var relativePath = cleanName.StartsWith("requests/") ? cleanName : $"requests/{cleanName}";
                var deletedRemotely = await DeleteRemoteFileByNameAsync(relativePath, request.RemoteSha, ct).ConfigureAwait(false);
                if (deletedRemotely)
                {
                    return (true, "Solicitação excluída permanentemente da nuvem e do cache local!");
                }
            }
            catch (Exception ex)
            {
                return (true, $"Removido localmente, mas aviso nuvem: {ex.Message}");
            }
        }

        return (true, "Solicitação excluída permanentemente com sucesso.");
    }

    /// <summary>
    /// Exclui o registro de uma cópia/dispositivo de campo da base de dispositivos monitorados (devices.json) localmente
    /// e sincroniza imediatamente com a nuvem, gravando na lista de exclusões.
    /// </summary>
    public (bool Success, string Message) DeleteDevice(string machineGuid)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return (false, "GUID não informado.");

        var devices = LoadLocalDevices();
        var removed = devices.RemoveAll(d => d.MachineGuid.Equals(machineGuid, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            RecordDeletedDeviceGuid(machineGuid);
            SaveLocalDevices(devices);
            _ = SyncDevicesToRemoteAsync();
            return (true, "Dispositivo removido da base de cópias com sucesso.");
        }

        return (false, "Dispositivo não encontrado.");
    }

    /// <summary>
    /// Concede tempo adicional de licença para uma máquina diretamente no controle administrativo
    /// e sincroniza imediatamente com a nuvem (devices.json).
    /// </summary>
    public async Task<(bool Success, string Message, OnlineDeviceRecord? Device)> ExtendLicenseAsync(
        string machineGuid,
        int additionalDays,
        LicenseSignerService signer,
        string? adminNotes = null,
        CancellationToken ct = default)
    {
        var (ok, msg, dev) = ExtendLicense(machineGuid, additionalDays, signer, adminNotes);
        if (ok)
        {
            await SyncDevicesToRemoteAsync(ct).ConfigureAwait(false);
        }
        return (ok, msg, dev);
    }

    /// <summary>
    /// Revoga imediatamente a licença de um dispositivo/técnico e atualiza a nuvem.
    /// </summary>
    public async Task<(bool Success, string Message)> RevokeDeviceAsync(
        string machineGuid,
        string reason,
        CancellationToken ct = default)
    {
        var (ok, msg) = RevokeDevice(machineGuid, reason);
        if (ok)
        {
            await SyncDevicesToRemoteAsync(ct).ConfigureAwait(false);
        }
        return (ok, msg);
    }

    /// <summary>
    /// Atualiza os dados cadastrais de um dispositivo (Nome, Empresa, Matrícula, Contato, etc.)
    /// com padronização uniforme de texto e salva imediatamente na nuvem.
    /// </summary>
    public async Task<(bool Success, string Message, OnlineDeviceRecord? Device)> UpdateDeviceAsync(
        OnlineDeviceRecord updatedDevice,
        CancellationToken ct = default)
    {
        if (updatedDevice == null || string.IsNullOrWhiteSpace(updatedDevice.MachineGuid))
            return (false, "Dados do dispositivo inválidos.", null);

        var devices = LoadLocalDevices();
        var existing = devices.FirstOrDefault(d => d.MachineGuid.Equals(updatedDevice.MachineGuid, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
            return (false, "Dispositivo não encontrado no registro local.", null);

        existing.FirstName = SparcTextSanitizer.FormatPersonOrCompanyName(updatedDevice.FirstName);
        existing.LastName = SparcTextSanitizer.FormatPersonOrCompanyName(updatedDevice.LastName);
        existing.Company = SparcTextSanitizer.FormatPersonOrCompanyName(updatedDevice.Company);
        existing.EmployeeId = SparcTextSanitizer.FormatEmployeeId(updatedDevice.EmployeeId);
        existing.Phone = SparcTextSanitizer.FormatPhone(updatedDevice.Phone);
        existing.Email = SparcTextSanitizer.FormatEmail(updatedDevice.Email);
        existing.Cluster = SparcTextSanitizer.FormatCluster(updatedDevice.Cluster);
        existing.Uf = SparcTextSanitizer.FormatUf(updatedDevice.Uf);
        if (!string.IsNullOrWhiteSpace(updatedDevice.Notes)) existing.Notes = updatedDevice.Notes.Trim();

        SaveLocalDevices(devices);

        // Sincroniza na nuvem imediatamente
        await SyncDevicesToRemoteAsync(ct).ConfigureAwait(false);

        return (true, "Cadastro do técnico atualizado com sucesso na nuvem e localmente!", existing);
    }

    /// <summary>
    /// Exclui o registro de uma cópia da base de dispositivos monitorados (devices.json)
    /// localmente e na nuvem, evitando que o cadastro reapareça ao reiniciar.
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteDeviceAsync(
        string machineGuid,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return (false, "GUID não informado.");

        var devices = LoadLocalDevices();
        var removed = devices.RemoveAll(d => d.MachineGuid.Equals(machineGuid, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            RecordDeletedDeviceGuid(machineGuid);
            SaveLocalDevices(devices);
            var (remoteOk, remoteMsg) = await SyncDevicesToRemoteAsync(ct).ConfigureAwait(false);
            if (remoteOk)
            {
                return (true, "Dispositivo excluído com sucesso da nuvem e da base local!");
            }
            return (true, $"Dispositivo removido localmente (Aviso nuvem: {remoteMsg})");
        }

        return (false, "Dispositivo não encontrado.");
    }
}


