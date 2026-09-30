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

    /// <summary>
    /// Carrega os registros de dispositivos salvos localmente no painel do administrador.
    /// </summary>
    public List<OnlineDeviceRecord> LoadLocalDevices()
    {
        if (!File.Exists(_localFilePath)) return new List<OnlineDeviceRecord>();

        try
        {
            var json = File.ReadAllText(_localFilePath);
            var items = JsonSerializer.Deserialize<List<OnlineDeviceRecord>>(json);
            return items ?? new List<OnlineDeviceRecord>();
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
                FirstName = req.FirstName ?? string.Empty,
                LastName = req.LastName ?? string.Empty,
                Phone = req.Phone ?? string.Empty,
                Email = req.Email ?? string.Empty,
                Cluster = req.Cluster ?? string.Empty,
                Uf = req.Uf ?? string.Empty,
                ClientVersion = req.ClientVersion ?? "0.8",
                FirstRegisteredUtc = DateTime.UtcNow,
                LastSeenUtc = DateTime.UtcNow,
                ExpirationDateIso = lic.ExpirationDateIso,
                ValidDays = lic.ValidDays,
                Status = "Active",
                AuthorizedToken = lic.LicenseToken,
                Notes = notes ?? string.Empty
            };
            devices.Insert(0, existing);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(req.FirstName)) existing.FirstName = req.FirstName;
            if (!string.IsNullOrWhiteSpace(req.LastName)) existing.LastName = req.LastName;
            if (!string.IsNullOrWhiteSpace(req.Phone)) existing.Phone = req.Phone;
            if (!string.IsNullOrWhiteSpace(req.Email)) existing.Email = req.Email;
            if (!string.IsNullOrWhiteSpace(req.Cluster)) existing.Cluster = req.Cluster;
            if (!string.IsNullOrWhiteSpace(req.Uf)) existing.Uf = req.Uf;
            if (!string.IsNullOrWhiteSpace(req.ClientVersion)) existing.ClientVersion = req.ClientVersion;

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
    /// Concede tempo adicional de licença para uma máquina diretamente no controle administrativo.
    /// Gera novo token assinado com a chave privada RSA do gestor.
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

        // Calcula a nova data base (a partir da data atual ou da data de expiração anterior se ainda válida)
        DateTime baseDate = DateTime.UtcNow;
        if (DateTime.TryParse(dev.ExpirationDateIso, out var currentExp) && currentExp > DateTime.UtcNow)
        {
            baseDate = currentExp;
        }

        var totalDays = Math.Max(1, (int)(baseDate.AddDays(additionalDays) - DateTime.UtcNow).TotalDays);

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
            ClientVersion: dev.ClientVersion);

        var result = signer.GenerateLicense(fakeReq, totalDays, dev.FullName, adminNotes ?? $"Renovação de +{additionalDays} dias via Admin");
        if (!result.Success)
        {
            return (false, $"Falha ao gerar assinatura criptográfica: {result.ErrorMessage}", dev);
        }

        dev.ExpirationDateIso = result.ExpirationDateIso;
        dev.ValidDays = totalDays;
        dev.Status = "Active";
        dev.AuthorizedToken = result.LicenseToken;
        dev.LastSeenUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(adminNotes)) dev.Notes = adminNotes;

        SaveLocalDevices(devices);
        return (true, $"Licença estendida com sucesso até {result.ExpirationDateIso} ({totalDays} dias).", dev);
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
    /// </summary>
    public async Task<List<OnlineDeviceRecord>> FetchRemoteDevicesAsync(CancellationToken ct = default)
    {
        try
        {
            var rawUrl = $"https://raw.githubusercontent.com/{RemoteRepoOwner}/{RemoteRepoName}/main/{DevicesFileName}";
            using var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
            if (!string.IsNullOrWhiteSpace(GitHubToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
            }

            using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new List<OnlineDeviceRecord>();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<OnlineDeviceRecord>>(json) ?? new List<OnlineDeviceRecord>();
        }
        catch
        {
            return new List<OnlineDeviceRecord>();
        }
    }

    /// <summary>
    /// Utilizado pelo SPARC do técnico para checar se o administrador concedeu mais tempo online
    /// ou revogou o acesso da máquina.
    /// </summary>
    public async Task<(bool HasNewAuthorization, string? NewToken, bool IsRevoked, string? Message)> CheckOnlineStatusAsync(
        string machineGuid,
        string machineFingerprint,
        string currentLocalToken,
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
                return (false, null, false, "Máquina não encontrada no cadastro online.");
            }

            if (string.Equals(dev.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
            {
                return (false, null, true, "Licença revogada pelo Administrador.");
            }

            if (!string.IsNullOrWhiteSpace(dev.AuthorizedToken) && !dev.AuthorizedToken.Equals(currentLocalToken, StringComparison.Ordinal))
            {
                return (true, dev.AuthorizedToken, false, $"Nova autorização concedida até {dev.ExpirationDateIso}.");
            }

            return (false, null, false, "Licença atualizada.");
        }
        catch (Exception ex)
        {
            return (false, null, false, $"Falha de conexão com a nuvem: {ex.Message}");
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

        var json = JsonSerializer.Serialize(req, new JsonSerializerOptions { WriteIndented = true });

        // 1. Salva localmente como backup imediato
        try
        {
            var localFile = Path.Combine(LocalRequestsDirectory, $"{req.MachineGuid}.json");
            await File.WriteAllTextAsync(localFile, json, ct).ConfigureAwait(false);
        }
        catch { }

        // 2. Se houver token GitHub configurado, envia para a nuvem via API do GitHub
        if (!string.IsNullOrWhiteSpace(GitHubToken))
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

        // 1. Carrega registros locais
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
                            resultList[req.MachineGuid] = req;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 2. Consulta diretório remoto no GitHub se token estiver disponível
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
            ClientVersion: request.ClientVersion);

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
    /// Exclui uma solicitação de ativação online do disco local e do repositório remoto no GitHub.
    /// Útil para limpeza e organização de testes.
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteActivationRequestAsync(
        OnlineActivationRequest request,
        CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.MachineGuid))
            return (false, "Solicitação inválida.");

        // 1. Remove do disco local
        try
        {
            var localFile = Path.Combine(LocalRequestsDirectory, $"{request.MachineGuid}.json");
            if (File.Exists(localFile))
            {
                File.Delete(localFile);
            }
        }
        catch { }

        // 2. Se houver token GitHub configurado, remove do repositório remoto via API DELETE
        if (!string.IsNullOrWhiteSpace(GitHubToken))
        {
            try
            {
                var fileName = $"requests/{request.MachineGuid}.json";
                var url = $"https://api.github.com/repos/{RemoteRepoOwner}/{RemoteRepoName}/contents/{fileName}";

                var sha = request.RemoteSha;
                if (string.IsNullOrWhiteSpace(sha))
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
                            if (doc.RootElement.TryGetProperty("sha", out var sp))
                            {
                                sha = sp.GetString();
                            }
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(sha))
                {
                    using var delReq = new HttpRequestMessage(HttpMethod.Delete, url);
                    delReq.Headers.UserAgent.Add(new ProductInfoHeaderValue("SPARC-Agent", "1.0"));
                    delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
                    delReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

                    var delPayload = new Dictionary<string, string>
                    {
                        ["message"] = $"Limpeza de teste: removida solicitacao de {request.FullName}",
                        ["sha"] = sha
                    };
                    delReq.Content = new StringContent(JsonSerializer.Serialize(delPayload), Encoding.UTF8, "application/json");

                    using var delResp = await HttpClient.SendAsync(delReq, ct).ConfigureAwait(false);
                    if (delResp.IsSuccessStatusCode)
                    {
                        return (true, "Solicitação excluída com sucesso da nuvem e do cache local!");
                    }
                }
            }
            catch (Exception ex)
            {
                return (true, $"Removido localmente, mas erro na nuvem: {ex.Message}");
            }
        }

        return (true, "Solicitação excluída com sucesso.");
    }

    /// <summary>
    /// Exclui o registro de uma cópia/dispositivo de campo da base de dispositivos monitorados (devices.json).
    /// </summary>
    public (bool Success, string Message) DeleteDevice(string machineGuid)
    {
        if (string.IsNullOrWhiteSpace(machineGuid)) return (false, "GUID não informado.");

        var devices = LoadLocalDevices();
        var removed = devices.RemoveAll(d => d.MachineGuid.Equals(machineGuid, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            SaveLocalDevices(devices);
            return (true, "Dispositivo removido da base de cópias com sucesso.");
        }

        return (false, "Dispositivo não encontrado.");
    }
}


