using System;
using System.IO;

namespace NetworkDevice.UI.Beta;

// Valores injetados por beta/Build-Beta.ps1 na COPIA isolada. Nao editar na base.
internal static class BetaConfig
{
    public const string Tag = "%%BETA_TAG%%";
    public const string ExpiresUtcIso = "%%BETA_EXPIRES_UTC%%"; // yyyy-MM-ddTHH:mm:ssZ
    public const string PublicKeyPem = @"%%BETA_PUBLIC_KEY_PEM%%";

    public static DateTime ExpiresUtc
    {
        get
        {
            try { return DateTime.Parse(ExpiresUtcIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal).ToUniversalTime(); }
            catch { return DateTime.UtcNow.AddDays(30); }
        }
    }

    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SPARC-Beta");
    public static string LicensePath => Path.Combine(DataDir, "license.key");
    public static string LastSeenPath => Path.Combine(DataDir, "lastseen.dat");
}
