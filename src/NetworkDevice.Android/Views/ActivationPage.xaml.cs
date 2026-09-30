using System;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using NetworkDevice.Android.Services;

namespace NetworkDevice.Android.Views;

public partial class ActivationPage : ContentPage
{
    private readonly AndroidLicenseManager _licenseManager = AndroidLicenseManager.Instance;

    public ActivationPage()
    {
        InitializeComponent();
        LoadExistingData();
    }

    private void LoadExistingData()
    {
        var profile = _licenseManager.GetProfile();
        FirstNameEntry.Text = profile.FirstName;
        LastNameEntry.Text = profile.LastName;
        PhoneEntry.Text = profile.Phone;
        EmailEntry.Text = profile.Email;
        ClusterEntry.Text = profile.Cluster;
        UfEntry.Text = profile.Uf;

        DeviceDisplayIdLabel.Text = _licenseManager.GetDisplayId();
        AppVersionLabel.Text = $"Versão: {AppInfo.VersionString} Beta • ID: {_licenseManager.GetDisplayId()}";

        if (_licenseManager.IsActivated(out var statusMsg))
        {
            OnlineStatusLabel.Text = $"✅ {statusMsg}";
            OnlineStatusLabel.TextColor = Color.FromArgb("#4ADE80");
        }
    }

    private void OnSaveProfileClicked(object? sender, EventArgs e)
    {
        SaveCurrentProfile();
        DisplayAlert("Identificação", "Dados do técnico salvos com sucesso.", "OK");
    }

    private TechnicianProfile SaveCurrentProfile()
    {
        var p = new TechnicianProfile
        {
            FirstName = FirstNameEntry.Text?.Trim() ?? string.Empty,
            LastName = LastNameEntry.Text?.Trim() ?? string.Empty,
            Phone = PhoneEntry.Text?.Trim() ?? string.Empty,
            Email = EmailEntry.Text?.Trim() ?? string.Empty,
            Cluster = ClusterEntry.Text?.Trim().ToUpperInvariant() ?? string.Empty,
            Uf = UfEntry.Text?.Trim().ToUpperInvariant() ?? string.Empty
        };
        _licenseManager.SaveProfile(p);
        return p;
    }

    private async void OnSolicitarOnlineClicked(object? sender, EventArgs e)
    {
        var p = SaveCurrentProfile();
        if (string.IsNullOrWhiteSpace(p.FirstName))
        {
            await DisplayAlert("Campo Obrigatório", "Informe seu Nome para identificação.", "OK");
            return;
        }
        if (string.IsNullOrWhiteSpace(p.Cluster))
        {
            await DisplayAlert("Campo Obrigatório", "Informe o Cluster de atuação (ex: SP-INTERIOR).", "OK");
            return;
        }
        if (string.IsNullOrWhiteSpace(p.Uf))
        {
            await DisplayAlert("Campo Obrigatório", "Informe a UF do estado de atuação.", "OK");
            return;
        }

        SolicitarOnlineBtn.IsEnabled = false;
        OnlineStatusLabel.Text = "Enviando solicitação para o servidor central...";
        OnlineStatusLabel.TextColor = Color.FromArgb("#FBBF24");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var (ok, msg) = await _licenseManager.SubmitOnlineActivationAsync(cts.Token);
            if (ok)
            {
                OnlineStatusLabel.Text = "✅ Solicitação enviada! Avise seu gestor para aprovar no SPARC Admin.";
                OnlineStatusLabel.TextColor = Color.FromArgb("#4ADE80");
                await DisplayAlert("Solicitação Enviada",
                    "Sua solicitação de ativação foi encaminhada com sucesso para o SPARC Admin!\n\nAssim que o administrador aprovar, clique em 'Verificar Aprovação' para liberar o acesso.",
                    "OK");
            }
            else
            {
                OnlineStatusLabel.Text = $"Falha no envio: {msg}";
                OnlineStatusLabel.TextColor = Color.FromArgb("#EF4444");
                await DisplayAlert("Falha na Solicitação", msg, "OK");
            }
        }
        catch (Exception ex)
        {
            OnlineStatusLabel.Text = $"Erro de conexão: {ex.Message}";
            OnlineStatusLabel.TextColor = Color.FromArgb("#EF4444");
            await DisplayAlert("Erro de Conexão", ex.Message, "OK");
        }
        finally
        {
            SolicitarOnlineBtn.IsEnabled = true;
        }
    }

    private async void OnChecarAprovacaoClicked(object? sender, EventArgs e)
    {
        ChecarAprovacaoBtn.IsEnabled = false;
        OnlineStatusLabel.Text = "Consultando status no servidor central...";
        OnlineStatusLabel.TextColor = Color.FromArgb("#38BDF8");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var (approved, msg) = await _licenseManager.CheckOnlineApprovalAsync(cts.Token);
            if (approved)
            {
                OnlineStatusLabel.Text = "✅ Aprovado e Ativado com Sucesso!";
                OnlineStatusLabel.TextColor = Color.FromArgb("#4ADE80");
                await DisplayAlert("Ativado!", msg, "OK");
                NavigateToMainApp();
            }
            else
            {
                OnlineStatusLabel.Text = msg;
                OnlineStatusLabel.TextColor = Color.FromArgb("#FBBF24");
                await DisplayAlert("Status da Aprovação", msg, "OK");
            }
        }
        catch (Exception ex)
        {
            OnlineStatusLabel.Text = $"Erro ao verificar: {ex.Message}";
            OnlineStatusLabel.TextColor = Color.FromArgb("#EF4444");
            await DisplayAlert("Erro", ex.Message, "OK");
        }
        finally
        {
            ChecarAprovacaoBtn.IsEnabled = true;
        }
    }

    private async void OnCopyDeviceIdClicked(object? sender, EventArgs e)
    {
        var id = _licenseManager.GetDisplayId();
        await Clipboard.Default.SetTextAsync(id);
        await DisplayAlert("Copiado", $"ID do dispositivo copiado:\n{id}", "OK");
    }

    private async void OnCopyFullRequestClicked(object? sender, EventArgs e)
    {
        SaveCurrentProfile();
        var fullReq = _licenseManager.BuildActivationRequestString();
        await Clipboard.Default.SetTextAsync(fullReq);
        await DisplayAlert("Copiado", "Código de solicitação completo copiado para a área de transferência.", "OK");
    }

    private async void OnAtivarManualClicked(object? sender, EventArgs e)
    {
        var key = LicenseKeyEditor.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            await DisplayAlert("Aviso", "Cole a chave de liberação SPB1... fornecida pelo gestor.", "OK");
            return;
        }

        SaveCurrentProfile();
        var ok = _licenseManager.ActivateWithKey(key, out var msg);
        if (ok)
        {
            await DisplayAlert("Sucesso", msg, "INICIAR SPARC");
            NavigateToMainApp();
        }
        else
        {
            await DisplayAlert("Chave Inválida", msg, "OK");
        }
    }

    private void NavigateToMainApp()
    {
        if (Application.Current != null)
        {
            Application.Current.MainPage = new AppShell();
        }
    }
}
