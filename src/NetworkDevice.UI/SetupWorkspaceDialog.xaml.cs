using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace NetworkDevice.UI;

public partial class SetupWorkspaceDialog : Window
{
    public string SelectedWorkspacePath { get; private set; } = string.Empty;
    public bool CreateDesktopShortcut { get; private set; } = true;
    public bool InstallConfirmed { get; private set; }

    public SetupWorkspaceDialog(string suggestedPath)
    {
        InitializeComponent();
        TxtWorkspacePath.Text = suggestedPath;
    }

    private void BtnProcurarPasta_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Selecione a Pasta de Trabalho / Repositório do SPARC",
                InitialDirectory = Directory.Exists(TxtWorkspacePath.Text)
                    ? TxtWorkspacePath.Text
                    : (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\")
            };

            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                var folder = dialog.FolderName.Trim();
                // Se o usuário selecionou uma raiz de disco ou pasta sem subpasta SPARC, sugere SPARC dentro dela
                if (!folder.EndsWith("SPARC", StringComparison.OrdinalIgnoreCase))
                {
                    folder = Path.Combine(folder, "SPARC");
                }
                TxtWorkspacePath.Text = folder;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir o seletor de pastas: {ex.Message}", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnInstalarEIniciar_Click(object sender, RoutedEventArgs e)
    {
        var path = TxtWorkspacePath.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show(this, "Por favor, informe um caminho de pasta válido.", "Caminho Inválido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            SelectedWorkspacePath = fullPath;
            CreateDesktopShortcut = ChkCriarAtalhoDesktop.IsChecked == true;
            InstallConfirmed = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"O caminho informado é inválido:\n{ex.Message}", "Caminho Inválido", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnModoPortatil_Click(object sender, RoutedEventArgs e)
    {
        InstallConfirmed = false;
        DialogResult = false;
        Close();
    }
}
