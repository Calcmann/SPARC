using NetworkDevice.Android.Services;
using NetworkDevice.Core.Validation;

namespace NetworkDevice.Android.Views;

public partial class FirmwarePage : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;
    private string? _firmwarePath;

    public FirmwarePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshLocalIps();

        if (VendorPicker.ItemsSource == null)
            VendorPicker.ItemsSource = new List<string> { "Cisco (HTTP :8080)", "HPE Comware (FTP :2121)", "Fortinet FortiGate (FTP :2121)" };

        var detected = _connManager.LastDetectionResult;
        if (detected != null)
        {
            VendorPicker.SelectedIndex = detected.Manufacturer switch
            {
                NetworkDevice.Core.Domain.DeviceManufacturer.Hpe => 1,
                NetworkDevice.Core.Domain.DeviceManufacturer.Fortinet => 2,
                _ => 0,
            };
        }
        else if (VendorPicker.SelectedIndex < 0)
        {
            VendorPicker.SelectedIndex = 0;
        }
    }

    private void OnVendorChanged(object? sender, EventArgs e)
    {
        UpgradeBtn.Text = VendorPicker.SelectedIndex switch
        {
            1 => "⬆️ ATUALIZAR COMWARE VIA FTP",
            2 => "⬆️ RESTAURAR FORTIOS VIA FTP",
            _ => "⬆️ ATUALIZAR IOS VIA HTTP",
        };
    }

    private void RefreshLocalIps()
    {
        var ips = DeviceConnectionManager.GetLocalIpv4Addresses().ToList();
        PhoneIpPicker.ItemsSource = ips;
        if (ips.Count == 0)
        {
            PhoneIpPicker.Title = "Sem rede ativa (conecte o Ethernet OTG)";
        }
        else
        {
            // Prefere eth (OTG) a wlan
            var eth = ips.FindIndex(s => s.StartsWith("eth", StringComparison.OrdinalIgnoreCase) || s.Contains("eth"));
            PhoneIpPicker.SelectedIndex = eth >= 0 ? eth : 0;
        }
    }

    private async void OnPickFirmwareClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione a imagem IOS (.bin)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/octet-stream" } }
                })
            });

            if (result == null)
                return;

            var dest = Path.Combine(FileSystem.CacheDirectory, result.FileName);
            using (var src = await result.OpenReadAsync())
            using (var dst = File.Create(dest))
            {
                await src.CopyToAsync(dst);
            }

            _firmwarePath = dest;
            var sizeMb = (new FileInfo(dest).Length / (1024.0 * 1024.0)).ToString("N1");
            FwInfoLabel.Text = $"{result.FileName} ({sizeMb} MB)";
            AppendLog($"[*] Firmware: {result.FileName} ({sizeMb} MB)");

            // Validação de compatibilidade com o modelo detectado (se houver)
            var detected = _connManager.LastDetectionResult;
            if (detected != null)
            {
                var validation = FirmwareCompatibilityValidator.Validate(detected.Series, result.FileName);
                FwCompatLabel.Text = validation.IsCompatible
                    ? $"✓ Compatível com {detected.Series}"
                    : $"✗ {validation.ErrorMessage}";
                FwCompatLabel.TextColor = validation.IsCompatible
                    ? Color.FromArgb("#4ADE80")
                    : Color.FromArgb("#EF4444");
                if (!validation.IsCompatible)
                    AppendLog($"[!] INCOMPATÍVEL: {validation.ErrorMessage} ({validation.ExpectedFormatDescription})");
            }
            else
            {
                FwCompatLabel.Text = "Modelo não identificado ainda (verifique na aba Provisionamento).";
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro", ex.Message, "OK");
        }
    }

    private async void OnUpgradeClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB do roteador primeiro (aba Provisionamento).", "OK");
            return;
        }
        if (string.IsNullOrWhiteSpace(_firmwarePath) || !File.Exists(_firmwarePath))
        {
            await DisplayAlert("Aviso", "Selecione a imagem IOS (.bin) primeiro.", "OK");
            return;
        }

        var phoneIpRaw = PhoneIpPicker.SelectedItem as string;
        var phoneIp = phoneIpRaw?.Split(' ')[0].Trim();
        if (string.IsNullOrWhiteSpace(phoneIp))
        {
            await DisplayAlert("Sem rede",
                "Nenhum IP ativo no celular. Conecte o adaptador Ethernet OTG à LAN do roteador (ou configure IP estático nas configurações do Android).",
                "OK");
            return;
        }

        var routerIp = RouterIpEntry.Text?.Trim() ?? string.Empty;
        var vendor = VendorPicker.SelectedIndex; // 0 Cisco • 1 HPE • 2 Fortinet

        var confirm = await DisplayAlert("Confirmar Upgrade",
            vendor switch
            {
                1 => $"Baixar '{Path.GetFileName(_firmwarePath)}' via FTP ({phoneIp}), aplicar boot-loader + reboot?\n\nMantenha a TELA LIGADA.",
                2 => $"Restaurar '{Path.GetFileName(_firmwarePath)}' via FTP ({phoneIp})? O FortiGate GRAVA e REINICIA imediatamente.\n\nMantenha a TELA LIGADA.",
                _ => $"Transferir '{Path.GetFileName(_firmwarePath)}' via HTTP ({phoneIp}) e aplicar boot + reload?\n\nMantenha a TELA LIGADA — leva vários minutos.",
            },
            "SIM, ATUALIZAR", "CANCELAR");
        if (!confirm) return;

        UpgradeBtn.IsEnabled = false;
        AppendLog("[*] =================================================================");
        AppendLog("[*]           ATUALIZAÇÃO DE FIRMWARE" + vendor switch { 1 => " HPE VIA FTP", 2 => " FORTIOS VIA FTP", _ => " CISCO VIA HTTP" });
        AppendLog("[*] =================================================================");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            bool ok;
            if (vendor == 1)
            {
                ok = await _connManager.UpgradeFirmwareHpeFtpAsync(
                    _firmwarePath, phoneIp,
                    string.IsNullOrWhiteSpace(FtpUserEntry.Text) ? "sparc" : FtpUserEntry.Text.Trim(),
                    FtpPassEntry.Text ?? string.Empty,
                    OnFwProgressAsync, cts.Token);
            }
            else if (vendor == 2)
            {
                ok = await _connManager.RestoreFirmwareFortiFtpAsync(
                    _firmwarePath, phoneIp,
                    string.IsNullOrWhiteSpace(FtpUserEntry.Text) ? null : FtpUserEntry.Text.Trim(),
                    FtpPassEntry.Text,
                    OnFwProgressAsync, cts.Token);
            }
            else
            {
                ok = await _connManager.UpgradeFirmwareCiscoHttpAsync(
                    _firmwarePath,
                    phoneIp,
                    routerIp,
                    string.IsNullOrWhiteSpace(TelnetUserEntry.Text) ? null : TelnetUserEntry.Text.Trim(),
                    TelnetPassEntry.Text ?? string.Empty,
                    string.IsNullOrWhiteSpace(TempIpEntry.Text) ? null : TempIpEntry.Text.Trim(),
                    string.IsNullOrWhiteSpace(TempMaskEntry.Text) ? null : TempMaskEntry.Text.Trim(),
                    lanInterface: null,
                    expectedMd5: string.IsNullOrWhiteSpace(Md5Entry.Text) ? null : Md5Entry.Text.Trim(),
                    OnFwProgressAsync,
                    cts.Token);
            }

            AppendLog(ok ? "\n[✓] FIRMWARE ATUALIZADO E VERIFICADO!" : "\n[!] Upgrade sem verificação final — confira 'show version'.");
            await DisplayAlert(ok ? "Sucesso" : "Atenção",
                ok ? "Firmware atualizado e versão confirmada pós-reload!" : "Upgrade executado, mas valide 'show version' manualmente.",
                "OK");
        }
        catch (Exception ex)
        {
            AppendLog($"\n[X] FALHA NO UPGRADE: {ex.Message}");
            await DisplayAlert("Erro no Upgrade", ex.Message, "OK");
        }
        finally
        {
            UpgradeBtn.IsEnabled = true;
        }
    }

    private Task OnFwProgressAsync(string message)
    {
        MainThread.BeginInvokeOnMainThread(() => AppendLog(message));
        return Task.CompletedTask;
    }

    private void AppendLog(string message)
    {
        FwLogEditor.Text += message + Environment.NewLine;
    }
}
