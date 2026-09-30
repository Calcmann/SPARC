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
}
