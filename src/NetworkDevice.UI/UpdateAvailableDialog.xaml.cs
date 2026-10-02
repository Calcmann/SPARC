using System;
using System.IO;
using System.Threading;
using System.Windows;
using NetworkDevice.Core.Firmware;

namespace NetworkDevice.UI;

public partial class UpdateAvailableDialog : Window
{
    private readonly SparcPlatformRelease _release;
    private readonly string _currentVersion;
    private readonly SparcAppUpdateService _updateService;
    private CancellationTokenSource? _cts;

    public UpdateAvailableDialog(SparcPlatformRelease release, string currentVersion, SparcAppUpdateService updateService)
    {
        InitializeComponent();
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _currentVersion = currentVersion;
        _updateService = updateService ?? new SparcAppUpdateService();

        TxtVersaoAtual.Text = $"v{currentVersion}";
        TxtVersaoNova.Text = $"v{_release.Version}";
        TxtReleaseNotes.Text = string.IsNullOrWhiteSpace(_release.ReleaseNotes)
            ? "Atualização recomendada com melhorias de estabilidade, novas funções e compatibilidade."
            : _release.ReleaseNotes;
    }

    private void BtnLembrarMaisTarde_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    private async void BtnAtualizarAgora_Click(object sender, RoutedEventArgs e)
    {
        BtnAtualizarAgora.IsEnabled = false;
        PanelDownloadProgress.Visibility = Visibility.Visible;
        TxtDownloadStatus.Text = "Conectando ao repositório homologado...";

        _cts = new CancellationTokenSource();
        var tempFile = Path.Combine(Path.GetTempPath(), $"SPARC_Update_{_release.Version}_{Guid.NewGuid():N}.exe");

        var progress = new Progress<double>(pct =>
        {
            PbDownload.Value = pct;
            TxtDownloadPercent.Text = $"{pct:F0}%";
            TxtDownloadStatus.Text = $"Baixando atualização... {pct:F0}%";
        });

        try
        {
            await _updateService.DownloadUpdateFileAsync(_release.DownloadUrl, tempFile, progress, _cts.Token);
            TxtDownloadStatus.Text = "Download concluído! Aplicando e reiniciando o SPARC...";
            TxtDownloadPercent.Text = "100%";

            await System.Threading.Tasks.Task.Delay(1000);
            SparcAppUpdateService.ApplyWindowsUpdateAndRestart(tempFile);
        }
        catch (OperationCanceledException)
        {
            PanelDownloadProgress.Visibility = Visibility.Collapsed;
            BtnAtualizarAgora.IsEnabled = true;
        }
        catch (Exception ex)
        {
            PanelDownloadProgress.Visibility = Visibility.Collapsed;
            BtnAtualizarAgora.IsEnabled = true;
            MessageBox.Show($"Falha ao atualizar o SPARC: {ex.Message}", "Erro na Atualização", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
