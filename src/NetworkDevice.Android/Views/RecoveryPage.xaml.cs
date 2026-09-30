using NetworkDevice.Android.Services;
using NetworkDevice.Core.Domain;

namespace NetworkDevice.Android.Views;

public partial class RecoveryPage : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;

    public RecoveryPage()
    {
        InitializeComponent();
        _connManager.OnDeviceIdentified += res =>
            MainThread.BeginInvokeOnMainThread(() => ShowDetection(res));
    }

    private async void OnCheckLockClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB na aba Provisionamento primeiro.", "OK");
            return;
        }

        CheckLockBtn.IsEnabled = false;
        AppendLog("[*] Verificando estado de bloqueio do equipamento...");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var result = await _connManager.IdentifyDeviceAsync(cts.Token);
            ShowDetection(result);

            if (result.OperatingState == DeviceOperatingState.PasswordProtected)
                AppendLog("[!] Equipamento BLOQUEADO por senha. Use a Opção 1 (login) ou Opção 2 (quebra).");
            else if (result.OperatingState == DeviceOperatingState.Ready)
                AppendLog("[✓] Equipamento SEM bloqueio — pode provisionar direto na aba Provisionamento.");
            else
                AppendLog($"[!] Estado: {result.OperatingState} — {result.Details}");
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Falha na verificação: {ex.Message}");
        }
        finally
        {
            CheckLockBtn.IsEnabled = true;
        }
    }

    private void ShowDetection(DeviceDetectionResult res)
    {
        DetectedInfoLabel.Text = $"{res.Manufacturer} | {res.Series} | {res.OperatingState}";
        if (res.OperatingState == DeviceOperatingState.PasswordProtected)
        {
            LockBadge.Text = "🔒 BLOQUEADO";
            LockBadge.TextColor = Color.FromArgb("#FBBF24");
        }
        else if (res.OperatingState == DeviceOperatingState.Ready)
        {
            LockBadge.Text = "🔓 LIBERADO";
            LockBadge.TextColor = Color.FromArgb("#4ADE80");
        }
        else
        {
            LockBadge.Text = res.OperatingState.ToString().ToUpperInvariant();
            LockBadge.TextColor = Color.FromArgb("#EF4444");
        }
    }

    // ---------- OPÇÃO 1: login direto ----------
    private async void OnTryLoginClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB na aba Provisionamento primeiro.", "OK");
            return;
        }

        TryLoginBtn.IsEnabled = false;
        AppendLog("[*] Tentando login direto com as credenciais informadas...");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var ok = await _connManager.TryLoginAsync(
                string.IsNullOrWhiteSpace(LoginUserEntry.Text) ? null : LoginUserEntry.Text.Trim(),
                LoginPassEntry.Text ?? string.Empty,
                msg => MainThread.BeginInvokeOnMainThread(() => AppendLog(msg)),
                cts.Token);

            if (!ok)
            {
                AppendLog("[X] Login recusado. Confira usuário/senha ou use a Opção 2 (quebra).");
                await DisplayAlert("Login recusado", "As credenciais não foram aceitas. Confira ou use a quebra de senha.", "OK");
                return;
            }

            AppendLog("[✓] LOGIN ACEITO — quebra de senha dispensada!");
            await ContinueProvisioningAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[X] Falha no login: {ex.Message}");
        }
        finally
        {
            TryLoginBtn.IsEnabled = true;
        }
    }

    // ---------- OPÇÃO 2: quebra + zeramento ----------
    private async void OnBreakClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB na aba Provisionamento primeiro.", "OK");
            return;
        }

        var confirm = await DisplayAlert("Confirmar Quebra de Senha",
            "Isso vai ZERAR o equipamento (apagar configuração/senha) via ROMMON, BootWare ou reset físico.\n\n" +
            "• Cisco: será preciso DESLIGAR e RELIGAR na tomada quando o app pedir.\n" +
            "• Mantenha a TELA LIGADA — o processo leva vários minutos.\n\nDeseja continuar?",
            "SIM, ZERAR", "CANCELAR");
        if (!confirm) return;

        BreakBtn.IsEnabled = false;
        AppendLog("[*] =================================================================");
        AppendLog("[*]           QUEBRA DE SENHA E RESET DE FÁBRICA                    ");
        AppendLog("[*] =================================================================");

        try
        {
            // Janela ampla: interrupção (2,5–3 min) + boot pós-reset (60–90s) + zeramento
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(12));
            var ok = await _connManager.RecoverPasswordAsync(OnRecoveryProgressAsync, InstructOperatorAsync, cts.Token);

            if (!ok)
            {
                AppendLog("[X] Recuperação não confirmada. Tente novamente.");
                await DisplayAlert("Não confirmado", "O reset não foi confirmado. Tente novamente seguindo as instruções.", "OK");
                return;
            }

            AppendLog("\n[✓] EQUIPAMENTO ZERADO E DESBLOQUEADO!");
            await ContinueProvisioningAsync();
        }
        catch (OperationCanceledException)
        {
            AppendLog("[!] Recuperação cancelada / tempo esgotado.");
        }
        catch (Exception ex)
        {
            AppendLog($"[X] FALHA NA RECUPERAÇÃO: {ex.Message}");
            await DisplayAlert("Erro na Recuperação", ex.Message, "OK");
        }
        finally
        {
            BreakBtn.IsEnabled = true;
        }
    }

    /// <summary>
    /// Emenda automática da esteira (paridade com o Windows): após login ou zeramento,
    /// aplica a Ficha SAIP já carregada; sem ficha, orienta a carregar.
    /// </summary>
    private async Task ContinueProvisioningAsync()
    {
        if (_connManager.LoadedCircuit == null)
        {
            AppendLog("[i] Nenhuma Ficha SAIP carregada — carregue na aba Provisionamento e toque em APLICAR.");
            await DisplayAlert("Próximo passo",
                "Equipamento liberado! Carregue a Ficha SAIP na aba Provisionamento e toque em APLICAR CONFIGURAÇÃO.",
                "OK");
            return;
        }

        AppendLog("[*] Ficha SAIP já carregada — emendando provisionamento automaticamente...");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await _connManager.ApplyProvisioningAsync(_connManager.LoadedCircuit, OnRecoveryProgressAsync, cts.Token);
            AppendLog("\n[✓] PROVISIONAMENTO PÓS-RECUPERAÇÃO CONCLUÍDO!");
            await DisplayAlert("Sucesso", "Senha quebrada/validada e configuração SAIP aplicada com sucesso!", "OK");
        }
        catch (Exception ex)
        {
            AppendLog($"[X] Falha no provisionamento pós-recuperação: {ex.Message}");
            await DisplayAlert("Atenção", $"Equipamento liberado, mas o provisionamento falhou: {ex.Message}\n\nAplique pela aba Provisionamento.", "OK");
        }
    }

    private Task OnRecoveryProgressAsync(string message)
    {
        MainThread.BeginInvokeOnMainThread(() => AppendLog(message));
        return Task.CompletedTask;
    }

    private async Task InstructOperatorAsync(string message, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await DisplayAlert("Ação necessária 🔧", message, "ENTENDI, CONTINUAR");
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            await tcs.Task;
        }
    }

    private void AppendLog(string message)
    {
        RecoveryLogEditor.Text += message + Environment.NewLine;
    }
}
