using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace NetworkDevice.UI.Beta;

internal sealed record BetaLicenseInfo(string MachineGuid, string Fingerprint, DateTime ExpiresUtc);

internal static class LicenseCrypto
{
    public const string Prefix = "SPB1.";

    public static bool TryValidate(string token, out BetaLicenseInfo? info)
    {
        info = null;
        try
        {
            token = token.Trim();
            if (!token.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var parts = token.Split('.');
            if (parts.Length != 3) return false;
            var payload = Base64Url.Decode(parts[1]);
            var sig = Base64Url.Decode(parts[2]);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(BetaConfig.PublicKeyPem);
            if (!rsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return false;
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var g = root.GetProperty("g").GetString() ?? "";
            var f = root.GetProperty("f").GetString() ?? "";
            var exp = root.GetProperty("exp").GetDateTime().ToUniversalTime();
            if (string.IsNullOrWhiteSpace(g) || string.IsNullOrWhiteSpace(f)) return false;
            info = new BetaLicenseInfo(g, f, exp);
            return true;
        }
        catch { return false; }
    }

    public static bool IsAuthorizedForThisMachine(BetaLicenseInfo info, DateTime now, out string reason)
    {
        if (now.Date > info.ExpiresUtc.Date) { reason = "Chave expirada em " + info.ExpiresUtc.ToString("dd/MM/yyyy") + "."; return false; }
        var cur = MachineId.Current();
        if (string.Equals(cur.MachineGuid, info.MachineGuid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(cur.Fingerprint, info.Fingerprint, StringComparison.OrdinalIgnoreCase))
        { reason = ""; return true; }
        reason = "Chave emitida para outro computador.";
        return false;
    }
}

internal static class BetaLicenseStore
{
    public static string? Load()
    {
        try
        {
            if (File.Exists(BetaConfig.LicensePath))
            {
                var t = File.ReadAllText(BetaConfig.LicensePath).Trim();
                return string.IsNullOrWhiteSpace(t) ? null : t;
            }
        }
        catch { }
        return null;
    }
}
