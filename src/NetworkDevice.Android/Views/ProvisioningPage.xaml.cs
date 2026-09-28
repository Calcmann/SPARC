using Android.Hardware.Usb;
using NetworkDevice.Android.Services;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Provisioning;

namespace NetworkDevice.Android.Views;

public partial class ProvisioningPage : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;
    private List<UsbDevice> _discoveredDevices = new();

    public ProvisioningPage()
    {
        InitializeComponent();
        _connManager.OnConnectionStateChanged += UpdateConnectionState;
        _connManager.OnDeviceIdentified += UpdateIdentifiedDevice;
        ScanUsb();
    }

    private void OnScanUsbClicked(object? sender, EventArgs e)
    {
        ScanUsb();
    }

    private void ScanUsb()
    {
        _discoveredDevices = _connManager.ScanUsbDevices().ToList();
        UsbDevicePicker.ItemsSource = null;

        if (_discoveredDevices.Count == 0)
        {
            UsbStatusLabel.Text = "Nenhum adaptador serial USB encontrado na porta OTG.";
            UsbDevicePicker.Title = "Nenhum dispositivo USB detectado";
            return;
        }

        var deviceNames = _discoveredDevices.Select(d =>
            $"{d.ManufacturerName ?? "USB Serial"} ({d.ProductName ?? "Console"}) - VID:0x{d.VendorId:X4}:PID:0x{d.ProductId:X4}").ToList();

        UsbDevicePicker.ItemsSource = deviceNames;
        UsbDevicePicker.SelectedIndex = 0;
        UsbStatusLabel.Text = $"{_discoveredDevices.Count} dispositivo(s) serial USB detectado(s).";
    }

    private async void OnConnectUsbClicked(object? sender, EventArgs e)
    {
        if (_connManager.IsConnected)
        {
            await _connManager.DisconnectAsync();
            return;
        }

        if (UsbDevicePicker.SelectedIndex < 0 || UsbDevicePicker.SelectedIndex >= _discoveredDevices.Count)
        {
            await DisplayAlert("Aviso", "Selecione um adaptador USB na lista ou clique em Escanear USB.", "OK");
            return;
        }

        var targetDevice = _discoveredDevices[UsbDevicePicker.SelectedIndex];
        ConnectUsbBtn.IsEnabled = false;

        try
        {
            UsbStatusLabel.Text = "Conectando ao console serial USB (9600 8N1)...";
            await _connManager.ConnectUsbAsync(targetDevice, baudRate: 9600);
            AppendLog("[✓] Console serial USB conectado com sucesso!");

            // Tenta identificação automática ao conectar
            await Task.Delay(500);
            await RunDeviceIdentificationAsync();
        }
        catch (Exception ex)
        {
            UsbStatusLabel.Text = $"Falha na conexão: {ex.Message}";
            await DisplayAlert("Erro de Conexão USB", ex.Message, "OK");
        }
        finally
        {
            ConnectUsbBtn.IsEnabled = true;
        }
    }

    private void UpdateConnectionState(bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isConnected)
            {
                ConnectionBadge.Text = "CONECTADO";
                ConnectionBadge.TextColor = Color.FromArgb("#4ADE80");
                ConnectUsbBtn.Text = "❌ Desconectar";
                ConnectUsbBtn.BackgroundColor = Color.FromArgb("#DC2626");
                UsbStatusLabel.Text = "Porta serial aberta a 9600 bps.";
            }
            else
            {
                ConnectionBadge.Text = "DESCONECTADO";
                ConnectionBadge.TextColor = Color.FromArgb("#EF4444");
                ConnectUsbBtn.Text = "🔌 Conectar";
                ConnectUsbBtn.BackgroundColor = Color.FromArgb("#059669");
                UsbStatusLabel.Text = "Console serial desconectado.";
            }
        });
    }

    private async void OnIdentifyClicked(object? sender, EventArgs e)
    {
        await RunDeviceIdentificationAsync();
    }

    private async Task RunDeviceIdentificationAsync()
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB antes de identificar o roteador.", "OK");
            return;
        }

        IdentifyBtn.IsEnabled = false;
        AppendLog("[*] Interrogando o equipamento na porta serial CLI...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var result = await _connManager.IdentifyDeviceAsync(cts.Token);
            UpdateIdentifiedDevice(result);
            AppendLog($"[✓] Identificado: {result.Manufacturer} {result.Series} (Estado: {result.OperatingState})");
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Falha ao interrogar roteador: {ex.Message}");
            await DisplayAlert("Identificação", ex.Message, "OK");
        }
        finally
        {
            IdentifyBtn.IsEnabled = true;
        }
    }

    private void UpdateIdentifiedDevice(DeviceDetectionResult res)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ManufacturerLabel.Text = res.Manufacturer.ToString();
            ModelLabel.Text = res.Series == DeviceSeries.Unknown ? "Não identificado (Genérico)" : res.Series.ToString();
            StateLabel.Text = res.OperatingState.ToString();

            if (res.OperatingState == DeviceOperatingState.Ready)
                StateLabel.TextColor = Color.FromArgb("#4ADE80");
            else if (res.OperatingState == DeviceOperatingState.PasswordProtected)
                StateLabel.TextColor = Color.FromArgb("#FBBF24");
            else
                StateLabel.TextColor = Color.FromArgb("#EF4444");
        });
    }

    private async void OnLoadPdfClicked(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Selecione o arquivo da Ficha SAIP (PDF ou TXT)",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/pdf", "text/plain" } }
                })
            });

            if (result != null)
            {
                AppendLog($"[*] Carregando ficha: {result.FileName}...");
                using var stream = await result.OpenReadAsync();
                var tempPath = Path.Combine(FileSystem.CacheDirectory, result.FileName);
                using (var fileStream = File.Create(tempPath))
                {
                    await stream.CopyToAsync(fileStream);
                }

                var saip = await SaipParser.ParseFileAsync(tempPath);
                SetSaipData(saip, result.FileName);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro na Ficha", ex.Message, "OK");
        }
    }

    private async void OnPasteSaipClicked(object? sender, EventArgs e)
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            await DisplayAlert("Área de Transferência Vazia", "Copie o texto da Ficha SAIP antes de colar.", "OK");
            return;
        }

        try
        {
            var saip = SaipParser.ParseText(text);
            SetSaipData(saip, "Área de Transferência");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro no Parser", ex.Message, "OK");
        }
    }

    private void SetSaipData(SaipCircuitData saip, string origem)
    {
        _connManager.LoadedCircuit = saip;
        CircuitDetailsLayout.IsVisible = true;

        SaipClientLabel.Text = $"Cliente: {saip.ClienteRazaoSocial ?? "Não identificado"}";
        SaipCircuitLabel.Text = $"Designação: {saip.DesignacaoIp ?? "-"} | OTS: {saip.NumeroOts ?? "-"}";
        SaipWanLabel.Text = $"WAN: {saip.WanIp}/{saip.WanCidr} (Gateway: {saip.WanGateway})";
        SaipLanLabel.Text = $"LAN: {saip.LanIp}/{saip.LanCidr}";
        SaipBandwidthLabel.Text = $"Banda: {(saip.BandaMbpsNominal.HasValue ? $"{saip.BandaMbpsNominal.Value} Mbps" : "N/D")}";

        AppendLog($"[✓] Ficha SAIP carregada ({origem}): Cliente '{saip.ClienteRazaoSocial}', WAN '{saip.WanIp}', LAN '{saip.LanIp}'");
    }

    private async void OnApplyConfigClicked(object? sender, EventArgs e)
    {
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Aviso", "Conecte o console serial USB do roteador primeiro.", "OK");
            return;
        }

        if (_connManager.LoadedCircuit == null)
        {
            await DisplayAlert("Aviso", "Carregue ou cole os dados da Ficha SAIP antes de aplicar a configuração.", "OK");
            return;
        }

        var confirm = await DisplayAlert("Confirmar Provisionamento",
            $"Deseja aplicar a configuração do circuito '{_connManager.LoadedCircuit.DesignacaoIp ?? _connManager.LoadedCircuit.NumeroOts}' no roteador agora?",
            "SIM, APLICAR", "CANCELAR");

        if (!confirm) return;

        ApplyConfigBtn.IsEnabled = false;
        ProgressLogEditor.Text = "";
        AppendLog("[*] =================================================================");
        AppendLog("[*]           INICIANDO ESTEIRA DE PROVISIONAMENTO SPARC              ");
        AppendLog("[*] =================================================================");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await _connManager.ApplyProvisioningAsync(_connManager.LoadedCircuit, OnProvisioningProgressAsync, cts.Token);
            AppendLog("\n[✓] PROVISIONAMENTO FINALIZADO COM SUCESSO!");
            await DisplayAlert("Sucesso", "Configuração aplicada e gravada permanentemente na NVRAM com sucesso!", "OK");
        }
        catch (Exception ex)
        {
            AppendLog($"\n[X] FALHA NO PROVISIONAMENTO: {ex.Message}");
            await DisplayAlert("Erro no Provisionamento", ex.Message, "OK");
        }
        finally
        {
            ApplyConfigBtn.IsEnabled = true;
        }
    }

    private Task OnProvisioningProgressAsync(string message)
    {
        MainThread.BeginInvokeOnMainThread(() => AppendLog(message));
        return Task.CompletedTask;
    }

    private void AppendLog(string message)
    {
        ProgressLogEditor.Text += message + Environment.NewLine;
    }
}
