using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetworkDevice.Core.Licensing;

public sealed record SparcValidatedLicense(string MachineGuid, string Fingerprint, DateTime ExpiresUtc);

/// <summary>
/// Validador e gerador de requisições de licenças SPARC baseado em assinatura digital RSA-256 (Node-locking).
/// Compartilhado entre todas as plataformas (Windows, Android, Linux).
/// </summary>
public static class SparcLicenseValidator
{
    public const string Prefix = "SPB1.";
    public const string RequestPrefix = "SPBREQ.";

    public const string PublicKeyPem =
@"-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAuyYqMZ9dT+ghjOg5CQ5L
uUg7eHMjBUCgdcUgSNzBViHHfUR59jUlaLs38KpeSBRKwL80UYC2tJEjZSjBHOUt
RcaB0tny0xbtMVir/09j4Ei6vn7lw3tLlLkmL2OAADHC2RHZ6hV/Q2qf+XcscfC1
HvGwsZfmKxnFcVmGp3lX3VV2SFZ9sqn39wXrt9t8THmAvwZ7j0A2YSBcT/YTObUS
/5OxFGEoBBLLtqfCV2sT+Gy1DegkCMUps56o0OwFfYT6vjA6IqZFmWoYYWZtxxdQ
GL995oBVY1eKVWhtCh3nWDfNQn5sDDBRhx7DuF0guM5K6JnkjBq76TYjzojxkNTW
QQIDAQAB
-----END PUBLIC KEY-----";

    public static string BuildRequest(
        string machineGuid,
        string fingerprint,
        string? firstName = null,
        string? lastName = null,
        string? phone = null,
        string? email = null,
        string? cluster = null,
        string? uf = null,
        string? clientVersion = null,
        string? platform = null,
        string? company = null,
        string? employeeId = null)
    {
        var fn = EscapeJson(SparcTextSanitizer.FormatPersonOrCompanyName(firstName));
        var ln = EscapeJson(SparcTextSanitizer.FormatPersonOrCompanyName(lastName));
        var ph = EscapeJson(SparcTextSanitizer.FormatPhone(phone));
        var em = EscapeJson(SparcTextSanitizer.FormatEmail(email));
        var cl = EscapeJson(SparcTextSanitizer.FormatCluster(cluster));
        var u = EscapeJson(SparcTextSanitizer.FormatUf(uf));
        var ver = EscapeJson(clientVersion ?? "0.8");
        var plt = EscapeJson(platform ?? "Windows");
        var cmp = EscapeJson(SparcTextSanitizer.FormatPersonOrCompanyName(company));
        var mat = EscapeJson(SparcTextSanitizer.FormatEmployeeId(employeeId));
        var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var json = "{\"g\":\"" + machineGuid + "\",\"f\":\"" + fingerprint + "\""
                 + ",\"n\":\"" + fn + "\""
                 + ",\"s\":\"" + ln + "\""
                 + ",\"p\":\"" + ph + "\""
                 + ",\"e\":\"" + em + "\""
                 + ",\"c\":\"" + cl + "\""
                 + ",\"u\":\"" + u + "\""
                 + ",\"v\":\"" + ver + "\""
                 + ",\"plt\":\"" + plt + "\""
                 + ",\"cmp\":\"" + cmp + "\""
                 + ",\"mat\":\"" + mat + "\""
                 + ",\"t\":\"" + ts + "\"}";

        return RequestPrefix + Base64UrlEncode(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryValidate(string token, out SparcValidatedLicense? info)
    {
        info = null;
        try
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            token = token.Trim();
            if (!token.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            var parts = token.Split('.');
            if (parts.Length != 3) return false;

            var payload = Base64UrlDecode(parts[1]);
            var sig = Base64UrlDecode(parts[2]);

            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            if (!rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return false;

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var g = root.GetProperty("g").GetString() ?? "";
            var f = root.GetProperty("f").GetString() ?? "";
            var exp = root.GetProperty("exp").GetDateTime().ToUniversalTime();

            if (string.IsNullOrWhiteSpace(g) || string.IsNullOrWhiteSpace(f)) return false;

            info = new SparcValidatedLicense(g, f, exp);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAuthorized(SparcValidatedLicense info, string machineGuid, string fingerprint, DateTime nowUtc, out string reason)
    {
        if (nowUtc.Date > info.ExpiresUtc.Date)
        {
            reason = $"Chave expirada em {info.ExpiresUtc:dd/MM/yyyy}.";
            return false;
        }

        if (string.Equals(machineGuid, info.MachineGuid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fingerprint, info.Fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            reason = "";
            return true;
        }

        reason = "Chave de licença emitida para outro dispositivo.";
        return false;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "");

    public static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2: output += "=="; break;
            case 3: output += "="; break;
        }
        return Convert.FromBase64String(output);
    }

    public static string Base64UrlEncode(byte[] input)
    {
        var output = Convert.ToBase64String(input);
        return output.Split('=')[0].Replace('+', '-').Replace('/', '_');
    }
}
