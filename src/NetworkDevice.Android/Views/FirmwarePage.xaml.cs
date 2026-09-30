using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using NetworkDevice.Android.Services;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;

namespace NetworkDevice.Android.Views;

public partial class FirmwarePage : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;
    private readonly FirmwareRepositoryService _firmwareRepo = new();
    private RemoteFirmwareInfo? _cachedOfficialRemote;

    public FirmwarePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateDeviceDisplay();
    }

    private void UpdateDeviceDisplay()
    {
        var detected = _connManager.LastDetectionResult;
        if (detected != null && detected.Series != DeviceSeries.Unknown)
        {
            AuditModelLabel.Text = $"{detected.Manufacturer} {detected.Series}";
        }
        else
        {
            AuditModelLabel.Text = "Nenhum equipamento identificado no console.";
        }
    }

    private async void OnAuditarFwClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB na aba Provisionamento antes de auditar.", "OK");
            return;
        }

        AuditarFwBtn.IsEnabled = false;
        AuditStatusMsgLabel.Text = "Auditando firmware e consultando repositório central...";
        AppendLog("[*] Iniciando auditoria de firmware...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // 1. Identifica o equipamento se ainda não identificado
            if (_connManager.LastDetectionResult == null || _connManager.LastDetectionResult.Series == DeviceSeries.Unknown)
            {
                AppendLog("[*] Identificando série do roteador na serial...");
                await _connManager.IdentifyDeviceAsync(cts.Token);
            }

            var detected = _connManager.LastDetectionResult;
            if (detected == null || detected.Series == DeviceSeries.Unknown)
            {
                AppendLog("[!] Não foi possível identificar o modelo do roteador.");
                await DisplayAlert("Erro", "Não foi possível identificar o modelo do roteador.", "OK");
                return;
            }

            AuditModelLabel.Text = $"{detected.Manufacturer} {detected.Series}";

            // 2. Consulta o repositório central de firmwares homologados
            AppendLog($"[*] Consultando repositório central para '{detected.Series}'...");
            try
            {
                _cachedOfficialRemote = await _firmwareRepo.QueryRemoteForSeriesAsync(detected.Series, cts.Token);
            }
            catch (Exception ex)
            {
                AppendLog($"[AVISO] Consulta ao repositório central falhou ({ex.Message}).");
            }

            if (_cachedOfficialRemote != null)
            {
                AuditOfficialLabel.Text = $"{_cachedOfficialRemote.FileName} ({_cachedOfficialRemote.DisplaySize})";
                FirmwareFileNameEntry.Text = _cachedOfficialRemote.FileName;
                FirmwareUrlEntry.Text = _cachedOfficialRemote.DownloadUrl;
            }
            else
            {
                AuditOfficialLabel.Text = "Nenhum arquivo homologado cadastrado.";
            }

            // 3. Executa a auditoria no roteador
            if (_connManager.CurrentSession == null)
            {
                AppendLog("[!] Sessão serial interativa não disponível. Conecte na aba Provisionamento.");
                return;
            }

            var updater = new RouterDirectFirmwareUpdater(msg =>
            {
                MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
                return Task.CompletedTask;
            });

            var result = await updater.AuditComplianceAsync(_connManager.CurrentSession, detected.Series, _cachedOfficialRemote, cts.Token);

            AuditCurrentVersionLabel.Text = result.CurrentVersion;
            AuditStatusMsgLabel.Text = result.Message;

            if (result.IsCompliant)
            {
                ComplianceBadge.Text = "CONFORME";
                ComplianceBadge.TextColor = Color.FromArgb("#4ADE80");
            }
            else
            {
                ComplianceBadge.Text = "ATUALIZAÇÃO RECOMENDADA";
                ComplianceBadge.TextColor = Color.FromArgb("#F87171");
            }

            AppendLog($"[✓] Auditoria concluída: {result.Message}");
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Erro durante auditoria: {ex.Message}");
            await DisplayAlert("Falha na Auditoria", ex.Message, "OK");
        }
        finally
        {
            AuditarFwBtn.IsEnabled = true;
        }
    }

    private async void OnTriggerRouterDownloadClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB primeiro.", "OK");
            return;
        }

        var url = FirmwareUrlEntry.Text?.Trim();
        var fileName = FirmwareFileNameEntry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(fileName))
        {
            await DisplayAlert("Campos Obrigatórios", "Informe a URL do pacote e o nome do arquivo na flash.", "OK");
            return;
        }

        var confirm = await DisplayAlert("Confirmar Transferência",
            $"Deseja instruir o roteador a baixar '{fileName}' diretamente pelo link WAN?\n\nEsta operação transfere o arquivo direto para a flash e atualiza o comando de boot.",
            "SIM, EXECUTAR", "CANCELAR");

        if (!confirm) return;

        TriggerRouterDownloadBtn.IsEnabled = false;
        AppendLog("[*] Iniciando processo de download no roteador...");

        try
        {
            var detected = _connManager.LastDetectionResult;
            var series = detected?.Series ?? DeviceSeries.Isr841;

            if (_connManager.CurrentSession == null)
            {
                await DisplayAlert("Aviso", "Sessão serial interativa não disponível.", "OK");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var updater = new RouterDirectFirmwareUpdater(msg =>
            {
                MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
                return Task.CompletedTask;
            });

            var ok = await updater.TriggerDownloadOnRouterAsync(_connManager.CurrentSession, series, url, fileName, cts.Token);
            if (ok)
            {
                AppendLog("\n[✓] PROCESSO DE FIRMWARE FINALIZADO COM SUCESSO NO ROTEADOR!");
                await DisplayAlert("Sucesso", "Download concluído e gravado na flash do roteador!\nO boot system foi atualizado.", "OK");
            }
            else
            {
                AppendLog("\n[!] Transferência não confirmada pelo roteador. Verifique o log.");
                await DisplayAlert("Aviso", "O roteador não confirmou o término da transferência. Confira as mensagens no registro.", "OK");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"\n[X] Falha na operação: {ex.Message}");
            await DisplayAlert("Erro", ex.Message, "OK");
        }
        finally
        {
            TriggerRouterDownloadBtn.IsEnabled = true;
        }
    }

    private void AppendLog(string message)
    {
        FwLogEditor.Text += message + Environment.NewLine;
    }
}
