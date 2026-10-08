using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;

namespace NetworkDevice.Android.Views;

public partial class FirmwareRepoPage : ContentPage
{
    private readonly FirmwareRepositoryService _firmwareRepo = new();
    private readonly List<RemoteFirmwareInfo> _cachedRemote = new();
    private CancellationTokenSource? _activeDownloadCts;

    public FirmwareRepoPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RecarregarCatalogoAsync(consultarRemoto: false);
    }

    private async void OnVoltarClicked(object? sender, EventArgs e)
    {
        _activeDownloadCts?.Cancel();
        await Navigation.PopModalAsync();
    }

    private async void OnAtualizarCatalogoClicked(object? sender, EventArgs e)
    {
        await RecarregarCatalogoAsync(consultarRemoto: true);
    }

    private async Task RecarregarCatalogoAsync(bool consultarRemoto)
    {
        LoadingSpinner.IsVisible = true;
        LoadingSpinner.IsRunning = true;
        LoadingLabel.IsVisible = true;
        LoadingLabel.Text = consultarRemoto ? "Consultando repositório na nuvem..." : "Verificando arquivos locais...";

        TxtLocalPath.Text = $"Diretório: {_firmwareRepo.LocalRepositoryRoot}";

        if (consultarRemoto || _cachedRemote.Count == 0)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var remotos = await _firmwareRepo.QueryRemoteFirmwaresAsync(cts.Token);
                _cachedRemote.Clear();
                _cachedRemote.AddRange(remotos);
            }
            catch (Exception ex)
            {
                if (consultarRemoto)
                {
                    await DisplayAlert("Aviso", $"Não foi possível conectar ao repositório nuvem ({ex.Message}). Mostrando base local.", "OK");
                }
            }
        }

        RenderizarListaModelos();

        LoadingSpinner.IsVisible = false;
        LoadingSpinner.IsRunning = false;
        LoadingLabel.IsVisible = false;
    }

    private void RenderizarListaModelos()
    {
        FirmwaresContainer.Children.Clear();

        var seriesList = new[]
        {
            DeviceSeries.Series1900,
            DeviceSeries.Series2900,
            DeviceSeries.Isr841,
            DeviceSeries.Isr921,
            DeviceSeries.Msr954,
            DeviceSeries.Msr930,
            DeviceSeries.Msr1002,
            DeviceSeries.FortiGate40F
        };

        int totalOffline = 0;
        int totalModelos = seriesList.Length;

        foreach (var series in seriesList)
        {
            var def = FirmwareModelMap.GetDefinition(series);
            var localFw = _firmwareRepo.GetLocalFirmware(series);
            var remoteFw = _cachedRemote.FirstOrDefault(r => r.Series == series);

            var isOffline = localFw != null && File.Exists(localFw.LocalFilePath);
            if (isOffline) totalOffline++;

            var card = CriarCardModelo(series, def, localFw, remoteFw, isOffline);
            FirmwaresContainer.Children.Add(card);
        }

        BadgeTotalOffline.Text = $"{totalOffline}/{totalModelos} Offline";
        TxtStatusGeral.Text = totalOffline == totalModelos
            ? "✅ Todas as imagens homologadas estão prontas no celular!"
            : $"📦 {totalOffline} de {totalModelos} imagens armazenadas no celular";
        BtnBaixarTodos.IsEnabled = totalOffline < totalModelos;
    }

    private View CriarCardModelo(DeviceSeries series, FirmwareModelDefinition? def, LocalFirmwareInfo? localFw, RemoteFirmwareInfo? remoteFw, bool isOffline)
    {
        var seriesTitle = series switch
        {
            DeviceSeries.Series1900 => "Cisco Série 1900 / 1905 / 1921 / 1941",
            DeviceSeries.Series2900 => "Cisco Série 2900 / 2911 / 2921 / 2951",
            DeviceSeries.Isr841 => "Cisco Série 800 / C841M",
            DeviceSeries.Isr921 => "Cisco Série 900 / C921-4P",
            DeviceSeries.Msr954 => "HPE MSR 954 / 958 Comware 7",
            DeviceSeries.Msr930 => "HPE MSR 930 / 931 / 935",
            DeviceSeries.Msr1002 => "HPE MSR 1002 / 1003",
            DeviceSeries.FortiGate40F => "Fortinet FortiGate 40F (FortiOS)",
            _ => series.ToString()
        };

        var fileName = localFw?.FileName ?? remoteFw?.FileName ?? "Arquivo homologado oficial";
        var sizeDisplay = localFw?.DisplaySize ?? remoteFw?.DisplaySize ?? "Nuvem";

        var card = new Frame
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            BorderColor = isOffline ? Color.FromArgb("#059669") : Color.FromArgb("#334155"),
            CornerRadius = 8,
            Padding = new Thickness(12),
            HasShadow = false
        };

        var stack = new VerticalStackLayout { Spacing = 6 };

        var topGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        var titleLabel = new Label
        {
            Text = seriesTitle,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#F8FAFC"),
            VerticalOptions = LayoutOptions.Center
        };
        topGrid.Children.Add(titleLabel);
        Grid.SetColumn(titleLabel, 0);

        var badgeBorder = new Border
        {
            BackgroundColor = isOffline ? Color.FromArgb("#064E3B") : Color.FromArgb("#1E293B"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Padding = new Thickness(6, 2),
            VerticalOptions = LayoutOptions.Center
        };
        var badgeText = new Label
        {
            Text = isOffline ? "✅ NO CELULAR" : "☁️ APENAS NUVEM",
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            TextColor = isOffline ? Color.FromArgb("#4ADE80") : Color.FromArgb("#FBBF24")
        };
        badgeBorder.Content = badgeText;
        topGrid.Children.Add(badgeBorder);
        Grid.SetColumn(badgeBorder, 1);

        stack.Children.Add(topGrid);

        var fileInfoLabel = new Label
        {
            Text = $"Arquivo: {fileName} ({sizeDisplay})",
            FontSize = 11,
            TextColor = Color.FromArgb("#94A3B8"),
            LineBreakMode = LineBreakMode.MiddleTruncation
        };
        stack.Children.Add(fileInfoLabel);

        // Barra de progresso para download
        var progressBar = new ProgressBar
        {
            Progress = 0,
            ProgressColor = Color.FromArgb("#38BDF8"),
            IsVisible = false,
            HeightRequest = 4
        };
        stack.Children.Add(progressBar);

        var progLabel = new Label
        {
            Text = "",
            FontSize = 10,
            TextColor = Color.FromArgb("#38BDF8"),
            IsVisible = false
        };
        stack.Children.Add(progLabel);

        // Ações do card
        var btnAction = new Button
        {
            Text = isOffline ? "✅ Imagem Pronta (Toque para Rebaixar)" : "⬇️ Baixar Imagem no Celular",
            BackgroundColor = isOffline ? Color.FromArgb("#1E293B") : Color.FromArgb("#0284C7"),
            TextColor = isOffline ? Color.FromArgb("#94A3B8") : Color.FromArgb("#FFFFFF"),
            CornerRadius = 6,
            FontSize = 11,
            HeightRequest = 34,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(0, 4, 0, 0)
        };

        btnAction.Clicked += async (s, e) =>
        {
            if (remoteFw == null)
            {
                // Tenta consultar individual
                try
                {
                    btnAction.IsEnabled = false;
                    progLabel.IsVisible = true;
                    progLabel.Text = "Consultando nuvem...";
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    remoteFw = await _firmwareRepo.QueryRemoteForSeriesAsync(series, cts.Token);
                }
                catch { }
            }

            if (remoteFw == null)
            {
                await DisplayAlert("Erro", "Arquivo oficial não localizado no catálogo da nuvem.", "OK");
                btnAction.IsEnabled = true;
                progLabel.IsVisible = false;
                return;
            }

            await BaixarFirmwareIndividualAsync(remoteFw, progressBar, progLabel, btnAction);
        };

        stack.Children.Add(btnAction);
        card.Content = stack;
        return card;
    }

    private async Task BaixarFirmwareIndividualAsync(RemoteFirmwareInfo remoteFw, ProgressBar progressBar, Label progLabel, Button btnAction)
    {
        btnAction.IsEnabled = false;
        progressBar.IsVisible = true;
        progLabel.IsVisible = true;
        progressBar.Progress = 0;
        progLabel.Text = "Iniciando download...";

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var progress = new Progress<FirmwareDownloadProgress>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    progressBar.Progress = p.Percentage / 100.0;
                    progLabel.Text = $"{p.Percentage:F0}% ({p.BytesReceived / (1024 * 1024.0):F1} MB de {p.TotalBytes / (1024 * 1024.0):F1} MB)";
                });
            });

            await _firmwareRepo.DownloadFirmwareAsync(remoteFw, progress, cts.Token);

            progLabel.Text = "✅ Concluído com sucesso!";
            progLabel.TextColor = Color.FromArgb("#4ADE80");
            await Task.Delay(800);
            RenderizarListaModelos();
        }
        catch (Exception ex)
        {
            progLabel.Text = $"[!] Falha: {ex.Message}";
            progLabel.TextColor = Color.FromArgb("#EF4444");
            btnAction.IsEnabled = true;
        }
    }

    private async void OnBaixarTodosClicked(object? sender, EventArgs e)
    {
        BtnBaixarTodos.IsEnabled = false;

        var pendentes = new List<RemoteFirmwareInfo>();
        var seriesList = new[]
        {
            DeviceSeries.Series1900,
            DeviceSeries.Series2900,
            DeviceSeries.Isr841,
            DeviceSeries.Isr921,
            DeviceSeries.Msr954,
            DeviceSeries.Msr930,
            DeviceSeries.Msr1002,
            DeviceSeries.FortiGate40F
        };

        foreach (var s in seriesList)
        {
            var localFw = _firmwareRepo.GetLocalFirmware(s);
            if (localFw == null || !File.Exists(localFw.LocalFilePath))
            {
                var r = _cachedRemote.FirstOrDefault(x => x.Series == s);
                if (r != null) pendentes.Add(r);
            }
        }

        if (pendentes.Count == 0)
        {
            await DisplayAlert("Pronto", "Todos os firmwares já estão baixados ou é necessário atualizar o catálogo online primeiro.", "OK");
            BtnBaixarTodos.IsEnabled = true;
            return;
        }

        var confirm = await DisplayAlert("Baixar em Lote", $"Serão baixadas {pendentes.Count} imagens de firmware para o celular. Recomendado estar conectado ao Wi-Fi.\n\nDeseja iniciar?", "SIM, BAIXAR", "CANCELAR");
        if (!confirm)
        {
            BtnBaixarTodos.IsEnabled = true;
            return;
        }

        _activeDownloadCts = new CancellationTokenSource();
        int sucesso = 0;

        foreach (var item in pendentes)
        {
            if (_activeDownloadCts.IsCancellationRequested) break;
            try
            {
                TxtStatusGeral.Text = $"⬇️ Baixando {item.FileName}...";
                var prog = new Progress<FirmwareDownloadProgress>(p =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        TxtStatusGeral.Text = $"⬇️ Baixando {item.FileName} ({p.Percentage:F0}%)...";
                    });
                });
                await _firmwareRepo.DownloadFirmwareAsync(item, prog, _activeDownloadCts.Token);
                sucesso++;
            }
            catch (Exception ex)
            {
                TxtStatusGeral.Text = $"[!] Falha ao baixar {item.FileName}: {ex.Message}";
                await Task.Delay(1000);
            }
        }

        RenderizarListaModelos();
        await DisplayAlert("Download Concluído", $"{sucesso} de {pendentes.Count} imagens foram baixadas para o celular com sucesso!", "OK");
        BtnBaixarTodos.IsEnabled = true;
    }
}
