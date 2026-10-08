using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace NetworkDevice.Core.Installation;

public static class WorkspaceManager
{
    private sealed class WorkspaceConfig
    {
        public string? WorkspaceRoot { get; set; }
    }

    public static string GetDefaultWorkspaceRoot()
    {
        try
        {
            var configPath = GetGlobalConfigPath();
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var cfg = JsonSerializer.Deserialize<WorkspaceConfig>(json);
                if (!string.IsNullOrWhiteSpace(cfg?.WorkspaceRoot) && Directory.Exists(cfg.WorkspaceRoot))
                {
                    return cfg.WorkspaceRoot;
                }
            }
        }
        catch { }

        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        return Path.Combine(systemDrive, "SPARC");
    }

    public static string GetGlobalConfigPath()
    {
        var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPARC");
        return Path.Combine(localAppData, "workspace.json");
    }

    public static void SaveWorkspaceRoot(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPARC");
            Directory.CreateDirectory(localAppData);

            var cfg = new WorkspaceConfig { WorkspaceRoot = fullPath };
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(localAppData, "workspace.json"), json);

            // Salva também na raiz do próprio espaço de trabalho
            try
            {
                File.WriteAllText(Path.Combine(fullPath, "sparc_workspace.json"), json);
            }
            catch { }
        }
        catch { }
    }

    public static bool IsDevelopmentOrTestEnvironment(string currentExe)
    {
        if (string.IsNullOrWhiteSpace(currentExe)) return true;
        var dir = Path.GetDirectoryName(currentExe) ?? "";

        return dir.Contains(@"\src\", StringComparison.OrdinalIgnoreCase) ||
               dir.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
               dir.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunningFromWorkspace(string currentExe, string workspaceDir)
    {
        if (string.IsNullOrWhiteSpace(currentExe) || string.IsNullOrWhiteSpace(workspaceDir)) return false;
        var currentDir = Path.GetDirectoryName(currentExe) ?? "";
        return string.Equals(Path.GetFullPath(currentDir).TrimEnd('\\'), Path.GetFullPath(workspaceDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    }

    public static void CreateOrUpdateDesktopShortcut(string targetExe, string workingDir)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktop, "SPARC.lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Description = "SPARC - Sistema de Provisionamento Automatizado de Roteadores Claro";
            shortcut.IconLocation = $"{targetExe},0";
            shortcut.Save();
        }
        catch { }
    }

    public static string InstallToWorkspace(string sourceExe, string targetDir, bool createShortcut = true)
    {
        var targetFullPath = Path.GetFullPath(targetDir);
        var targetExe = Path.Combine(targetFullPath, "SPARC.exe");

        Directory.CreateDirectory(targetFullPath);
        Directory.CreateDirectory(Path.Combine(targetFullPath, "firmwares"));
        Directory.CreateDirectory(Path.Combine(targetFullPath, "backups"));
        Directory.CreateDirectory(Path.Combine(targetFullPath, "backups", "Relatorios"));
        Directory.CreateDirectory(Path.Combine(targetFullPath, "backups", "Certidoes_Y1564"));

        File.Copy(sourceExe, targetExe, overwrite: true);
        SaveWorkspaceRoot(targetFullPath);

        if (createShortcut)
        {
            CreateOrUpdateDesktopShortcut(targetExe, targetFullPath);
        }

        return targetExe;
    }
}
