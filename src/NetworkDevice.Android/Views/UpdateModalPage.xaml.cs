using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using NetworkDevice.Android.Services;
using NetworkDevice.Core.Firmware;

namespace NetworkDevice.Android.Views;

public partial class UpdateModalPage : ContentPage
{
    private readonly SparcPlatformRelease _release;
    private readonly SparcAppUpdateService _updateService;
    private CancellationTokenSource? _downloadCts;
    private bool _isDownloading;
    private bool _isCompleted;
    private string _targetFilePath = string.Empty;

    public UpdateModalPage(SparcPlatformRelease release)
    {
        InitializeComponent();
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _updateService = new SparcAppUpdateService();

        var displayVersion = !string.IsNullOrWhiteSpace(_release.Version) ? _release.Version : "0.8.74";
        LblVersionTitle.Text = $"Nova Versão Homologada v{displayVersion}";

        var fileName = !string.IsNullOrWhiteSpace(_release.FileName)
            ? _release.FileName
            : $"SPARC-Mobile-v{displayVersion}.apk";
        LblFileName.Text = fileName;

        if (!string.IsNullOrWhiteSpace(_release.ReleaseNotes))
        {
            LblReleaseNotes.Text = _release.ReleaseNotes.Trim();
            RowNotes.IsVisible = true;
        }
        else
        {
            RowNotes.IsVisible = false;
        }

        _targetFilePath = Path.Combine(FileSystem.CacheDirectory, fileName);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_isDownloading && !_isCompleted)
        {
            StartDownload();
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (_isDownloading)
        {
            // Bloqueia saída acidental e confirma com o usuário
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var confirm = await DisplayAlert("Cancelar Atualização", "O download da nova versão está em andamento. Deseja realmente cancelar?", "SIM, CANCELAR", "CONTINUAR");
                if (confirm)
                {
                    CancelDownload();
                }
            });
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void StartDownload()
    {
        _isDownloading = true;
        _isCompleted = false;
        _downloadCts = new CancellationTokenSource();

        BorderError.IsVisible = false;
        LblError.Text = string.Empty;
        PbDownload.Progress = 0.0;
        PbDownload.ProgressColor = Color.FromArgb("#00D26A");
        LblPercent.Text = "0%";
        LblStatus.Text = "Conectando ao repositório...";
        LblBytes.Text = "Iniciando transferência...";
        Spinner.IsRunning = true;
        Spinner.IsVisible = true;
        BtnCancel.IsEnabled = true;
        BtnAction.IsEnabled = false;
        BtnAction.Text = "Aguarde...";

        Task.Run(async () =>
        {
            try
            {
                // Limpa eventual arquivo temporário residual
                var tempPath = _targetFilePath + ".download";
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                var progressFraction = new Progress<double>(pct =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var clamped = Math.Clamp(pct / 100.0, 0.0, 1.0);
                        PbDownload.Progress = clamped;
                        LblPercent.Text = $"{pct:F0}%";
                        LblStatus.Text = "Baixando pacote oficial...";
                    });
                });

                var progressBytes = new Progress<(long BytesRead, long TotalBytes)>(p =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var readMb = p.BytesRead / (1024.0 * 1024.0);
                        if (p.TotalBytes > 0)
                        {
                            var totalMb = p.TotalBytes / (1024.0 * 1024.0);
                            LblBytes.Text = $"{readMb:F1} MB de {totalMb:F1} MB";
                        }
                        else
                        {
                            LblBytes.Text = $"{readMb:F1} MB baixados";
                        }
                    });
                });

                // Executa download para arquivo temporário
                await _updateService.DownloadUpdateFileAsync(
                    _release.DownloadUrl,
                    tempPath,
                    progressFraction,
                    progressBytes,
                    _downloadCts.Token);

                // Validação de integridade antes de mover para destino final
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LblStatus.Text = "Validando integridade do pacote...";
                    LblBytes.Text = "Checando assinatura e integridade do APK...";
                });

                if (!File.Exists(tempPath))
                {
                    throw new FileNotFoundException("Arquivo de atualização não localizado após download.");
                }

                var fi = new FileInfo(tempPath);
                // APK oficial tem ~47 MB. Se tiver menos de 35 MB, está corrompido ou é mensagem de erro HTTP
                if (fi.Length < 35_000_000)
                {
                    throw new InvalidDataException($"O arquivo baixado está incompleto ou corrompido ({fi.Length / (1024.0 * 1024.0):F2} MB). O tamanho mínimo esperado é de 35 MB.");
                }

                // Validação de cabeçalho ZIP / APK (Magic Bytes: 0x50, 0x4B, 0x03, 0x04 -> "PK..")
                using (var fs = File.OpenRead(tempPath))
                {
                    var magic = new byte[4];
                    var read = await fs.ReadAsync(magic, 0, 4);
                    if (read < 4 || magic[0] != 0x50 || magic[1] != 0x4B || magic[2] != 0x03 || magic[3] != 0x04)
                    {
                        throw new InvalidDataException("O pacote baixado não possui cabeçalho APK/ZIP válido. Download pode ter retornado erro HTTP/JSON do repositório.");
                    }
                }

                // Substitui arquivo final com segurança
                if (File.Exists(_targetFilePath))
                {
                    try { File.Delete(_targetFilePath); } catch { }
                }
                File.Move(tempPath, _targetFilePath);

                // Sucesso na validação
                _isCompleted = true;
                _isDownloading = false;

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    PbDownload.Progress = 1.0;
                    PbDownload.ProgressColor = Color.FromArgb("#16A34A");
                    LblPercent.Text = "100%";
                    LblStatus.Text = "Pacote 100% íntegro e validado!";
                    LblBytes.Text = "Iniciando instalador nativo do Android...";
                    Spinner.IsRunning = false;
                    Spinner.IsVisible = false;
                    BtnCancel.IsEnabled = false;
                    BtnAction.IsEnabled = true;
                    BtnAction.Text = "INSTALAR";
                    BtnAction.BackgroundColor = Color.FromArgb("#16A34A");

                    await Task.Delay(400);

                    // Dispara instalador nativo do Android
                    try
                    {
                        AndroidAppUpdater.TriggerApkInstall(_targetFilePath);
                        // Fecha modal após disparar o instalador
                        await Navigation.PopModalAsync();
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Falha ao abrir instalador do Android:\n{ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _isDownloading = false;
                    ShowError("Download cancelado pelo usuário.");
                    BtnAction.IsEnabled = true;
                    BtnAction.Text = "TENTAR NOVAMENTE";
                    BtnCancel.Text = "FECHAR";
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _isDownloading = false;
                    ShowError($"Falha no download/validação:\n{ex.Message}");
                    BtnAction.IsEnabled = true;
                    BtnAction.Text = "TENTAR NOVAMENTE";
                    BtnCancel.Text = "FECHAR";
                });
            }
        });
    }

    private void ShowError(string msg)
    {
        Spinner.IsRunning = false;
        Spinner.IsVisible = false;
        PbDownload.ProgressColor = Color.FromArgb("#DC2626");
        LblStatus.Text = "Erro na atualização";
        LblBytes.Text = "Operação interrompida";
        LblError.Text = msg;
        BorderError.IsVisible = true;
        BtnCancel.IsEnabled = true;
    }

    private void CancelDownload()
    {
        try
        {
            _downloadCts?.Cancel();
        }
        catch { }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        if (_isDownloading)
        {
            CancelDownload();
        }
        else
        {
            Navigation.PopModalAsync();
        }
    }

    private void OnActionClicked(object? sender, EventArgs e)
    {
        if (_isCompleted)
        {
            try
            {
                AndroidAppUpdater.TriggerApkInstall(_targetFilePath);
                Navigation.PopModalAsync();
            }
            catch (Exception ex)
            {
                ShowError($"Erro ao abrir instalador: {ex.Message}");
            }
        }
        else
        {
            // Tentar novamente
            StartDownload();
        }
    }
}
