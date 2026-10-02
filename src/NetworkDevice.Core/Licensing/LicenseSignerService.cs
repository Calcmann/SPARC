using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetworkDevice.Core.Licensing;

public sealed record ActivationRequestData(
    string RawRequest,
    string MachineGuid,
    string MachineFingerprint,
    bool IsValid,
    string? ErrorMessage,
    string? FirstName = null,
    string? LastName = null,
    string? Phone = null,
    string? Email = null,
    string? Cluster = null,
    string? Uf = null,
    string? ClientVersion = null,
    DateTime? RequestTimeUtc = null,
    string? Platform = "Windows",
    string? Company = null,
    string? EmployeeId = null)
{
    public string FullName
    {
        get
        {
            var fn = FirstName?.Trim() ?? string.Empty;
            var ln = LastName?.Trim() ?? string.Empty;
            var combined = $"{fn} {ln}".Trim();
            return string.IsNullOrWhiteSpace(combined) ? "(Técnico não informado)" : combined;
        }
    }

    public string PlatformBadge =>
        (string.Equals(Platform, "Android", StringComparison.OrdinalIgnoreCase) ||
         ClientVersion?.Contains("Android", StringComparison.OrdinalIgnoreCase) == true)
        ? "📱 Android"
        : "🪟 Windows";
}

public sealed record GeneratedLicenseResult(
    bool Success,
    string LicenseToken,
    string ExpirationDateIso,
    DateTime ExpirationUtc,
    int ValidDays,
    string? ErrorMessage);

public sealed class ActivationKeyHistoryItem
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string Platform { get; set; } = "Windows";
    public string TechnicianName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string Uf { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string MachineGuid { get; set; } = string.Empty;
    public string MachineFingerprint { get; set; } = string.Empty;
    public int ValidDays { get; set; } = 30;
    public string ExpirationDate { get; set; } = string.Empty;
    public string LicenseToken { get; set; } = string.Empty;

    public string PlatformBadge =>
        (string.Equals(Platform, "Android", StringComparison.OrdinalIgnoreCase) ||
         Notes?.Contains("Android", StringComparison.OrdinalIgnoreCase) == true)
        ? "📱 Android"
        : "🪟 Windows";
}

/// <summary>
/// Serviço de gerenciamento, decodificação e assinatura de licenças Node-locking RSA do SPARC.
/// Compatível com o padrão do LicenseTool (SPBREQ -> SPB1).
/// </summary>
public sealed class LicenseSignerService
{
    private readonly string _privateKeyPath;
    private readonly string _publicKeyPath;
    private readonly string _historyFilePath;

    public LicenseSignerService(string? customBetaDir = null)
    {
        var betaDir = customBetaDir ?? ResolveBetaDirectory();
        _privateKeyPath = Path.Combine(betaDir, "keys", "private.pem");
        _publicKeyPath = Path.Combine(betaDir, "keys", "public.pem");
        _historyFilePath = Path.Combine(betaDir, "historico_chaves.json");
    }

    private static string ResolveBetaDirectory()
    {
        // 1. Tenta C:\SPARC\beta
        var direct = @"C:\SPARC\beta";
        if (Directory.Exists(direct)) return direct;

        // 2. Tenta diretório relativo à execução
        var rel = Path.Combine(AppContext.BaseDirectory, "beta");
        if (Directory.Exists(rel)) return rel;

        // 3. Fallback procurando subindo diretórios
        var cur = new DirectoryInfo(AppContext.BaseDirectory);
        while (cur != null)
        {
            var test = Path.Combine(cur.FullName, "beta");
            if (Directory.Exists(test)) return test;
            cur = cur.Parent;
        }

        return direct;
    }

    public bool HasPrivateKey => File.Exists(_privateKeyPath);

    public string PrivateKeyPath => _privateKeyPath;

    public static string Base64UrlEncode(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4)
        {
            case 2: t += "=="; break;
            case 3: t += "="; break;
        }
        return Convert.FromBase64String(t);
    }

    /// <summary>
    /// Decodifica e inspeciona um código de solicitação de ativação do técnico (SPBREQ...).
    /// Extrai identificadores de hardware e dados cadastrais (nome, telefone, email).
    /// </summary>
    public ActivationRequestData DecodeRequest(string? rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new ActivationRequestData(string.Empty, string.Empty, string.Empty, false, "Nenhum código fornecido.");
        }

        var clean = rawInput.Trim().Replace(" ", "").Replace("\r", "").Replace("\n", "");

        // Localiza SPBREQ caso tenha texto antes ou depois
        var match = System.Text.RegularExpressions.Regex.Match(clean, @"(SPBREQ\.[A-Za-z0-9\-_=]+)");
        if (match.Success)
        {
            clean = match.Groups[1].Value;
        }

        if (!clean.StartsWith("SPBREQ.", StringComparison.OrdinalIgnoreCase))
        {
            return new ActivationRequestData(rawInput, string.Empty, string.Empty, false, "Formato inválido. O código de ativação do SPARC deve iniciar com 'SPBREQ.'.");
        }

        try
        {
            var payloadPart = clean.Substring("SPBREQ.".Length);
            var payloadBytes = Base64UrlDecode(payloadPart);
            var json = Encoding.UTF8.GetString(payloadBytes);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var g = root.TryGetProperty("g", out var gProp) ? gProp.GetString() ?? string.Empty : string.Empty;
            var f = root.TryGetProperty("f", out var fProp) ? fProp.GetString() ?? string.Empty : string.Empty;

            if (string.IsNullOrWhiteSpace(g) && string.IsNullOrWhiteSpace(f))
            {
                return new ActivationRequestData(clean, string.Empty, string.Empty, false, "Código sem identificação de hardware da máquina.");
            }

            var n = root.TryGetProperty("n", out var nProp) ? SparcTextSanitizer.FormatPersonOrCompanyName(nProp.GetString()) : null;
            var s = root.TryGetProperty("s", out var sProp) ? SparcTextSanitizer.FormatPersonOrCompanyName(sProp.GetString()) : null;
            var p = root.TryGetProperty("p", out var pProp) ? SparcTextSanitizer.FormatPhone(pProp.GetString()) : null;
            var e = root.TryGetProperty("e", out var eProp) ? SparcTextSanitizer.FormatEmail(eProp.GetString()) : null;
            var c = root.TryGetProperty("c", out var cProp) ? SparcTextSanitizer.FormatCluster(cProp.GetString()) : null;
            var u = root.TryGetProperty("u", out var uProp) ? SparcTextSanitizer.FormatUf(uProp.GetString()) : null;
            var v = root.TryGetProperty("v", out var vProp) ? vProp.GetString() : null;
            var cmp = root.TryGetProperty("cmp", out var cmpProp) ? SparcTextSanitizer.FormatPersonOrCompanyName(cmpProp.GetString()) : null;
            var mat = root.TryGetProperty("mat", out var matProp) ? SparcTextSanitizer.FormatEmployeeId(matProp.GetString()) : null;
            var plt = root.TryGetProperty("plt", out var pltProp) ? pltProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(plt))
            {
                // Heurística de fallback: detecta Android via fingerprint ou versão
                if ((v?.Contains("Android", StringComparison.OrdinalIgnoreCase) == true) ||
                    clean.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
                    f.StartsWith("AND-", StringComparison.OrdinalIgnoreCase))
                {
                    plt = "Android";
                }
                else
                {
                    plt = "Windows";
                }
            }

            DateTime? reqTime = null;
            if (root.TryGetProperty("t", out var tProp) && DateTime.TryParse(tProp.GetString(), out var parsedTime))
            {
                reqTime = parsedTime;
            }

            return new ActivationRequestData(
                clean,
                g,
                f,
                true,
                null,
                FirstName: n,
                LastName: s,
                Phone: p,
                Email: e,
                Cluster: c,
                Uf: u,
                ClientVersion: v,
                RequestTimeUtc: reqTime,
                Platform: plt,
                Company: cmp,
                EmployeeId: mat);
        }
        catch (Exception ex)
        {
            return new ActivationRequestData(clean, string.Empty, string.Empty, false, $"Erro ao decodificar pedido: {ex.Message}");
        }
    }

    /// <summary>
    /// Gera e assina criptograficamente a chave de ativação para a máquina solicitante.
    /// </summary>
    public GeneratedLicenseResult GenerateLicense(
        ActivationRequestData req,
        int validDays = 30,
        string? technicianName = null,
        string? notes = null)
    {
        if (!req.IsValid)
        {
            return new GeneratedLicenseResult(false, string.Empty, string.Empty, DateTime.MinValue, 0, req.ErrorMessage ?? "Pedido inválido.");
        }

        if (!HasPrivateKey)
        {
            return new GeneratedLicenseResult(false, string.Empty, string.Empty, DateTime.MinValue, 0, $"Chave privada RSA não encontrada em '{_privateKeyPath}'.");
        }

        try
        {
            var expDate = DateTime.UtcNow.AddDays(validDays);
            var expIsoDate = expDate.ToString("yyyy-MM-dd");
            var expFullIso = $"{expIsoDate}T00:00:00Z";

            var payloadJson = $"{{\"g\":\"{req.MachineGuid}\",\"f\":\"{req.MachineFingerprint}\",\"exp\":\"{expFullIso}\"}}";
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(_privateKeyPath));
            var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var token = $"SPB1.{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";

            // Salva no histórico
            SaveToHistory(new ActivationKeyHistoryItem
            {
                GeneratedAtUtc = DateTime.UtcNow,
                Platform = req.Platform ?? "Windows",
                TechnicianName = technicianName?.Trim() ?? req.FullName,
                FirstName = req.FirstName ?? string.Empty,
                LastName = req.LastName ?? string.Empty,
                Phone = req.Phone ?? string.Empty,
                Email = req.Email ?? string.Empty,
                Company = req.Company ?? string.Empty,
                EmployeeId = req.EmployeeId ?? string.Empty,
                Cluster = req.Cluster ?? string.Empty,
                Uf = req.Uf ?? string.Empty,
                Notes = notes?.Trim() ?? string.Empty,
                MachineGuid = req.MachineGuid,
                MachineFingerprint = req.MachineFingerprint,
                ValidDays = validDays,
                ExpirationDate = expIsoDate,
                LicenseToken = token
            });

            return new GeneratedLicenseResult(true, token, expIsoDate, expDate, validDays, null);
        }
        catch (Exception ex)
        {
            return new GeneratedLicenseResult(false, string.Empty, string.Empty, DateTime.MinValue, 0, $"Falha na assinatura criptográfica: {ex.Message}");
        }
    }

    /// <summary>
    /// Valida uma chave gerada (SPB1...) para conferência.
    /// </summary>
    public bool VerifyLicense(string token, out string? decodedPayload)
    {
        decodedPayload = null;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var clean = token.Trim();
        var parts = clean.Split('.');
        if (parts.Length != 3 || parts[0] != "SPB1") return false;

        try
        {
            var payload = Base64UrlDecode(parts[1]);
            var sig = Base64UrlDecode(parts[2]);

            using var rsa = RSA.Create();
            if (File.Exists(_publicKeyPath))
            {
                rsa.ImportFromPem(File.ReadAllText(_publicKeyPath));
            }
            else if (HasPrivateKey)
            {
                rsa.ImportFromPem(File.ReadAllText(_privateKeyPath));
            }
            else
            {
                return false;
            }

            var ok = rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (ok)
            {
                decodedPayload = Encoding.UTF8.GetString(payload);
            }
            return ok;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<ActivationKeyHistoryItem> LoadHistory()
    {
        if (!File.Exists(_historyFilePath)) return Array.Empty<ActivationKeyHistoryItem>();

        try
        {
            var json = File.ReadAllText(_historyFilePath);
            return JsonSerializer.Deserialize<List<ActivationKeyHistoryItem>>(json) ?? new List<ActivationKeyHistoryItem>();
        }
        catch
        {
            return Array.Empty<ActivationKeyHistoryItem>();
        }
    }

    private void SaveToHistory(ActivationKeyHistoryItem item)
    {
        try
        {
            var list = LoadHistory().ToList();
            list.Insert(0, item);

            // Mantém os últimos 500 registros
            if (list.Count > 500)
            {
                list = list.Take(500).ToList();
            }

            var dir = Path.GetDirectoryName(_historyFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_historyFilePath, json);
        }
        catch
        {
            // Histórico é não-bloqueante
        }
    }

    /// <summary>
    /// Exclui uma chave específica do histórico de licenças geradas.
    /// </summary>
    public bool DeleteHistoryItem(string licenseToken)
    {
        if (string.IsNullOrWhiteSpace(licenseToken)) return false;

        try
        {
            var list = LoadHistory().ToList();
            var removed = list.RemoveAll(i => string.Equals(i.LicenseToken, licenseToken, StringComparison.Ordinal));
            if (removed > 0)
            {
                var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_historyFilePath, json);
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Limpa todo o histórico de chaves geradas.
    /// </summary>
    public bool ClearHistory()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                File.WriteAllText(_historyFilePath, "[]");
            }
            return true;
        }
        catch
        {
            return false;
        }
    }
}
