using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using NetworkDevice.Core.Installation;

namespace NetworkDevice.Core.Logging;

public static class TechnicianProfileService
{
    private static TechnicianProfile? _cachedProfile;
    private static readonly object _syncLock = new();

    public static TechnicianProfile LoadCurrentProfile(bool forceReload = false)
    {
        lock (_syncLock)
        {
            if (_cachedProfile != null && !forceReload)
            {
                return _cachedProfile;
            }

            var profile = new TechnicianProfile();

            var candidatePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SPARC-Beta", "user_profile.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPARC", "user_profile.json"),
                Path.Combine(WorkspaceManager.GetDefaultWorkspaceRoot(), "beta", "user_profile.json"),
                Path.Combine(WorkspaceManager.GetDefaultWorkspaceRoot(), "user_profile.json"),
                @"C:\SPARC\beta\user_profile.json"
            };

            foreach (var path in candidatePaths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("fn", out var fn)) profile.FirstName = fn.GetString() ?? "";
                        if (root.TryGetProperty("ln", out var ln)) profile.LastName = ln.GetString() ?? "";
                        if (root.TryGetProperty("cmp", out var cmp)) profile.Company = cmp.GetString() ?? "";
                        if (root.TryGetProperty("mat", out var mat)) profile.EmployeeId = mat.GetString() ?? "";
                        if (root.TryGetProperty("ph", out var ph)) profile.Phone = ph.GetString() ?? "";
                        if (root.TryGetProperty("em", out var em)) profile.Email = em.GetString() ?? "";
                        if (root.TryGetProperty("cl", out var cl)) profile.Cluster = cl.GetString() ?? "";
                        if (root.TryGetProperty("uf", out var uf)) profile.Uf = uf.GetString() ?? "";

                        // Se encontrou dados válidos de nome, interrompe a busca
                        if (!string.IsNullOrWhiteSpace(profile.FirstName))
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    // Tenta o próximo candidato silenciosamente
                }
            }

            // Identificação de hardware (MachineGuid e Display)
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                    var v = k?.GetValue("MachineGuid")?.ToString();
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        profile.MachineGuid = v.Trim();
                        profile.MachineDisplayId = profile.MachineGuid.Length >= 8 
                            ? profile.MachineGuid[..8].ToUpperInvariant() 
                            : profile.MachineGuid.ToUpperInvariant();
                    }
                }
            }
            catch
            {
                // Fallback seguro
            }

            if (string.IsNullOrWhiteSpace(profile.MachineGuid))
            {
                profile.MachineGuid = Environment.MachineName;
                profile.MachineDisplayId = Environment.MachineName;
            }

            // Fallbacks caso não haja cadastro
            if (string.IsNullOrWhiteSpace(profile.FirstName))
            {
                profile.FirstName = Environment.UserName;
            }

            if (string.IsNullOrWhiteSpace(profile.Company))
            {
                profile.Company = "Claro S.A.";
            }

            _cachedProfile = profile;
            return profile;
        }
    }

    public static void SaveProfile(TechnicianProfile profile, string? customPath = null)
    {
        lock (_syncLock)
        {
            var targetPath = customPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SPARC-Beta", "user_profile.json");

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var obj = new
            {
                fn = profile.FirstName,
                ln = profile.LastName,
                cmp = profile.Company,
                mat = profile.EmployeeId,
                ph = profile.Phone,
                em = profile.Email,
                cl = profile.Cluster,
                uf = profile.Uf
            };

            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(targetPath, json);
            _cachedProfile = profile;
        }
    }
}

