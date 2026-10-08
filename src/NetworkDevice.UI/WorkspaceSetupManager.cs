using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using NetworkDevice.Core.Installation;

namespace NetworkDevice.UI;

public static class WorkspaceSetupManager
{
    /// <summary>
    /// Avalia a necessidade de instalação/transferência para a pasta de trabalho permanente.
    /// Retorna TRUE se a aplicação foi transferida e uma nova instância foi iniciada (o processo atual deve encerrar).
    /// Retorna FALSE se a aplicação deve continuar a execução normal neste processo.
    /// </summary>
    public static bool CheckAndOfferSetup(string[] args)
    {
        // 1. Pula se houver flags de supressão ou modo portátil forçado
        if (args.Any(a => a.Equals("--portable", StringComparison.OrdinalIgnoreCase) ||
                         a.Equals("--no-setup", StringComparison.OrdinalIgnoreCase) ||
                         a.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe)) return false;

        // 2. Não interfere em ambiente de desenvolvimento local
        if (WorkspaceManager.IsDevelopmentOrTestEnvironment(currentExe))
        {
            return false;
        }

        var currentDir = Path.GetDirectoryName(currentExe) ?? "";
        var configuredWorkspace = WorkspaceManager.GetDefaultWorkspaceRoot();

        // 3. Se já estiver executando dentro da pasta de trabalho configurada (ex: C:\SPARC)
        if (WorkspaceManager.IsRunningFromWorkspace(currentExe, configuredWorkspace) ||
            string.Equals(Path.GetFileName(currentDir), "SPARC", StringComparison.OrdinalIgnoreCase))
        {
            // Garante que o atalho exista na Área de Trabalho
            var officialExe = Path.Combine(currentDir, "SPARC.exe");
            if (!File.Exists(officialExe)) officialExe = currentExe;
            WorkspaceManager.CreateOrUpdateDesktopShortcut(officialExe, currentDir);
            return false;
        }

        // 4. Executando de fora (Downloads, Desktop, Pendrive, etc.): Exibe o assistente
        var dialog = new SetupWorkspaceDialog(configuredWorkspace);
        var result = dialog.ShowDialog();

        if (result == true && dialog.InstallConfirmed && !string.IsNullOrWhiteSpace(dialog.SelectedWorkspacePath))
        {
            var targetDir = Path.GetFullPath(dialog.SelectedWorkspacePath);

            try
            {
                var targetExe = WorkspaceManager.InstallToWorkspace(currentExe, targetDir, dialog.CreateDesktopShortcut);

                // Inicia a nova instância a partir da pasta definitiva
                var forwardArgs = args.Length > 1 ? string.Join(" ", args.Skip(1)) : string.Empty;
                var psi = new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = targetDir,
                    Arguments = forwardArgs,
                    UseShellExecute = true
                };

                Process.Start(psi);
                return true; // Sinaliza para o processo temporário atual ser finalizado
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível concluir a instalação em '{targetDir}':\n{ex.Message}\n\nO aplicativo continuará em modo portátil.",
                                "Aviso de Instalação", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        // Modo Portátil escolhido pelo operador
        return false;
    }
}
