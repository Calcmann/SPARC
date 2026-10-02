using Microsoft.Maui.Controls;
using NetworkDevice.Android.Services;
using NetworkDevice.Core.Diagnostics;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Android.Views;

public partial class Y1564Page : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;
    private CancellationTokenSource? _testCts;
    private Y1564Result? _lastResult;
    private DigitalLoopbackService? _localReflector;

    public Y1564Page()
    {
        InitializeComponent();
        ModePicker.SelectedIndex = 0; // Gateway Traffic Load
        FrameSizePicker.SelectedIndex = 0; // 1500
        DurationPicker.SelectedIndex = 0; // 15s
        TierPicker.SelectedIndex = 1; // <= 1200Km
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadCircuitInputs();
        CheckEthernetStatus();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopLocalReflector();
    }

    private void LoadCircuitInputs()
    {
        var saip = _connManager.LoadedCircuit;
        if (saip != null)
        {
            CircuitoInfoLabel.Text = $"Circuito: {saip.DesignacaoIp ?? saip.NumeroOts ?? "N/D"} — {saip.ClienteRazaoSocial ?? "Claro S.A."}";
            if (string.IsNullOrWhiteSpace(RemoteIpEntry.Text))
            {
                RemoteIpEntry.Text = saip.WanGateway ?? saip.WanIp ?? "201.90.204.21";
            }
            if (saip.BandaMbpsNominal.HasValue && saip.BandaMbpsNominal.Value > 0)
            {
                BandwidthEntry.Text = saip.BandaMbpsNominal.Value.ToString("F0");
            }
        }
        else
        {
            CircuitoInfoLabel.Text = "Circuito: Nenhum insumo carregado (pode ser informado manualmente abaixo).";
        }
    }

    private void CheckEthernetStatus()
    {
        var ethManager = AndroidEthernetManager.Instance;
        var hasEth = ethManager.IsEthernetConnected();
        var ethIp = ethManager.GetEthernetIpAddress();

        if (hasEth)
        {
            EthStatusBadge.Text = string.IsNullOrWhiteSpace(ethIp) ? "🔌 ETH UP (Sem IP)" : $"🔌 ETH UP ({ethIp})";
            EthStatusBadge.TextColor = Color.FromArgb("#4ADE80");
            EthDetailsLabel.Text = "Adaptador OTG cabeado pronto para isolamento do teste Y.1564.";
        }
        else
        {
            EthStatusBadge.Text = "❌ ETH Desconectada";
            EthStatusBadge.TextColor = Color.FromArgb("#EF4444");
            EthDetailsLabel.Text = "Recomendado conectar o cabo Ethernet OTG ao roteador antes de testar.";
        }
    }

    private async void OnStartTestClicked(object? sender, EventArgs e)
    {
        var targetIp = RemoteIpEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(targetIp))
        {
            await DisplayAlert("IP Obrigatório", "Informe o endereço IP remoto (Gateway Claro ou Refletor).", "OK");
            return;
        }

        if (!double.TryParse(BandwidthEntry.Text?.Trim(), out var targetMbps) || targetMbps <= 0)
        {
            await DisplayAlert("Banda Inválida", "Informe a banda contratada (CIR) em Mbps.", "OK");
            return;
        }

        // Duração selecionada
        var durationSeconds = DurationPicker.SelectedIndex switch
        {
            0 => 15,
            1 => 30,
            2 => 60,
            3 => 180,
            _ => 15
        };

        // Tamanho de quadro
        var isVariable = FrameSizePicker.SelectedIndex == 5;
        var frameSize = FrameSizePicker.SelectedIndex switch
        {
            0 => 1500,
            1 => 1024,
            2 => 512,
            3 => 128,
            4 => 64,
            _ => 1500
        };

        // Modo de Teste
        var testMode = ModePicker.SelectedIndex == 1
            ? Y1564TestMode.LoopbackBidirectional
            : Y1564TestMode.GatewayTrafficLoad;

        // Perfil MEF
        var (tierLabel, delayMax, jitterMax, lossMax) = TierPicker.SelectedIndex switch
        {
            0 => ("≤ 250Km (Metropolitano)", 74.0, 28.0, 0.02),
            2 => ("≤ 7000 Km (Nacional Extremo)", 460.0, 260.0, 0.02),
            _ => ("≤ 1200 Km (Interestadual)", 250.0, 160.0, 0.02)
        };

        var saip = _connManager.LoadedCircuit;
        var config = new Y1564TestConfig(
            RemoteIp: targetIp,
            RemotePort: 5001,
            TargetBandwidthMbps: targetMbps,
            FrameSizeBytes: frameSize,
            IsVariableFrame: isVariable,
            VariableFrameSizes: isVariable ? new[] { 64, 512, 1500 } : null,
            FrameSizeLabel: isVariable ? "EMIX" : frameSize.ToString(),
            Duration: TimeSpan.FromSeconds(durationSeconds),
            ClientName: saip?.ClienteRazaoSocial ?? "Claro S.A.",
            Designation: saip?.DesignacaoIp ?? saip?.NumeroOts ?? "Circuito",
            SlaLossPercent: lossMax,
            SlaDelayMs: delayMax,
            SlaJitterMs: jitterMax,
            DistanceTier: tierLabel,
            SourceIpAddress: null,
            Mode: testMode);

        StartTestBtn.IsEnabled = false;
        CancelTestBtn.IsEnabled = true;
        ShareCertidaoBtn.IsVisible = false;

        VerdictBorder.BackgroundColor = Color.FromArgb("#D97706");
        VerdictLabel.Text = "⏳ EM EXECUÇÃO";
        VerdictLabel.TextColor = Color.FromArgb("#FFFFFF");

        AppendLog("=================================================================");
        AppendLog("       INICIANDO CERTIFICAÇÃO E-SAM ITU-T Y.1564 (SPARC)         ");
        AppendLog("=================================================================");
        AppendLog($"[*] Destino Remoto : {targetIp}");
        AppendLog($"[*] Modo de Teste  : {testMode}");
        AppendLog($"[*] Banda Alvo CIR : {targetMbps:F1} Mbps");
        AppendLog($"[*] Duração        : {durationSeconds} segundos");
        AppendLog($"[*] Perfil MEF SLA : {tierLabel} (FLR<={lossMax:F2}%, FTD<={delayMax:F0}ms, IFDV<={jitterMax:F0}ms)");

        _testCts = new CancellationTokenSource();
        var ct = _testCts.Token;

        var ethBound = AndroidEthernetManager.Instance.BindProcessToEthernet(AppendLog);
        DeviceDisplay.Current.KeepScreenOn = true;

        try
        {
            var service = new Y1564TestService();
            var progressHandler = new Progress<Y1564LiveProgress>(OnLiveProgress);
            _lastResult = await service.RunTestAsync(config, progressHandler, AppendLog, ct);

            VerdictBorder.BackgroundColor = Color.FromArgb(_lastResult.IsPass ? "#16A34A" : "#DC2626");
            VerdictLabel.Text = _lastResult.IsPass ? "✅ APROVADO MEF" : "❌ REPROVADO SLA";

            AppendLog("\n=================================================================");
            AppendLog($"   VEREDITO FINAL: {(_lastResult.IsPass ? "APROVADO CONFORME SLA" : "REPROVADO POR VIOLAÇÃO")}");
            AppendLog($"   Vazão Medida : {_lastResult.RxThroughputMbps:F1} Mbps");
            AppendLog($"   Perda FLR    : {_lastResult.LossPercentage:F2}% (Limite: {lossMax:F2}%)");
            AppendLog($"   Delay FTD    : {_lastResult.DelayAvgMs:F1} ms (Limite: {delayMax:F0} ms)");
            AppendLog($"   Jitter IFDV  : {_lastResult.JitterAvgMs:F1} ms (Limite: {jitterMax:F0} ms)");
            AppendLog("=================================================================");

            ShareCertidaoBtn.IsVisible = true;
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(_lastResult.IsPass ? 500 : 250)); } catch { }
            await DisplayAlert("Teste Finalizado",
                $"Certificação Y.1564 concluída!\n\n" +
                $"• Veredito: {(_lastResult.IsPass ? "APROVADO CONFORME SLA MEF" : "REPROVADO")}\n" +
                $"• Vazão: {_lastResult.RxThroughputMbps:F1} Mbps\n" +
                $"• Perda: {_lastResult.LossPercentage:F2}%\n" +
                $"• Delay: {_lastResult.DelayAvgMs:F1} ms\n\n" +
                $"Toque em 'Gerar e Compartilhar Certidão' para emitir o relatório técnico.", "OK");
        }
        catch (OperationCanceledException)
        {
            VerdictBorder.BackgroundColor = Color.FromArgb("#64748B");
            VerdictLabel.Text = "CANCELADO";
            AppendLog("\n[!] Teste Y.1564 cancelado pelo operador.");
        }
        catch (Exception ex)
        {
            VerdictBorder.BackgroundColor = Color.FromArgb("#DC2626");
            VerdictLabel.Text = "ERRO";
            AppendLog($"\n[X] Falha no teste: {ex.Message}");
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(250)); } catch { }
            await DisplayAlert("Erro no Teste Y.1564", ex.Message, "OK");
        }
        finally
        {
            DeviceDisplay.Current.KeepScreenOn = false;
            if (ethBound)
            {
                AndroidEthernetManager.Instance.UnbindProcessFromNetwork(AppendLog);
            }
            StartTestBtn.IsEnabled = true;
            CancelTestBtn.IsEnabled = false;
        }
    }

    private void OnLiveProgress(Y1564LiveProgress p)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TestProgressBar.Progress = p.PercentComplete / 100.0;
            ProgressTimerLabel.Text = $"Tempo: {p.Elapsed:mm\\:ss} / {p.TotalDuration:mm\\:ss} ({p.PercentComplete:F0}%)";

            ThroughputLabel.Text = $"{p.CurrentTxMbps:F1} / {p.CurrentRxMbps:F1} Mbps";
            LossLabel.Text = $"{p.CurrentLossPercent:F2} %";
            LossLabel.TextColor = Color.FromArgb(p.CurrentLossPercent <= 0.05 ? "#4ADE80" : "#F87171");

            DelayLabel.Text = $"{p.CurrentDelayAvgMs:F1} ms";
            JitterLabel.Text = $"{p.CurrentJitterAvgMs:F1} ms";
        });
    }

    private void OnCancelTestClicked(object? sender, EventArgs e)
    {
        _testCts?.Cancel();
    }

    private async void OnShareCertidaoClicked(object? sender, EventArgs e)
    {
        if (_lastResult == null) return;

        try
        {
            var desig = string.IsNullOrWhiteSpace(_lastResult.Designation) ? "Circuito" : _lastResult.Designation;
            var html = Y1564PdfReportService.GenerateHtml(_lastResult);
            var filePath = await ReportsStorageService.Instance.SaveReportAsync($"Certidao_Y1564_{desig}", html, ".html", "Y.1564 SLA");

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Certidão de Nascimento Y.1564 - {desig}",
                File = new ShareFile(filePath)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro ao Compartilhar Certidão", ex.Message, "OK");
        }
    }

    #region Refletor Local VIAVI / JDSU

    private void OnToggleReflectorClicked(object? sender, EventArgs e)
    {
        if (_localReflector?.IsRunning == true)
        {
            StopLocalReflector();
        }
        else
        {
            StartLocalReflector();
        }
    }

    private void StartLocalReflector()
    {
        try
        {
            _localReflector = new DigitalLoopbackService();
            _localReflector.LogMessage += msg => MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
            _localReflector.Start(port: 5001);

            ToggleReflectorBtn.Text = "🛑 PARAR REFLETOR LOCAL (PORTA 5001 ATIVA)";
            ToggleReflectorBtn.BackgroundColor = Color.FromArgb("#DC2626");
            ToggleReflectorBtn.TextColor = Color.FromArgb("#FFFFFF");

            AppendLog("[✓] REFLETOR LOCAL ATIVO na porta UDP 5001! Respondendo a instrumentos VIAVI/JDSU.");
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Falha ao iniciar refletor local: {ex.Message}");
            DisplayAlert("Erro Refletor", ex.Message, "OK");
        }
    }

    private void StopLocalReflector()
    {
        if (_localReflector != null)
        {
            try
            {
                _localReflector.Dispose();
                _localReflector = null;
                ToggleReflectorBtn.Text = "🔄 ATIVAR REFLETOR LOCAL (RESPONDER VIAVI)";
                ToggleReflectorBtn.BackgroundColor = Color.FromArgb("#334155");
                ToggleReflectorBtn.TextColor = Color.FromArgb("#38BDF8");
                AppendLog("[*] Refletor local desativado.");
            }
            catch { }
        }
    }

    #endregion

    private void AppendLog(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LogEditor.Text += $"\n{message}";
            if (LogEditor.Text.Length > 20000)
            {
                LogEditor.Text = LogEditor.Text.Substring(LogEditor.Text.Length - 10000);
            }
        });
    }
}
