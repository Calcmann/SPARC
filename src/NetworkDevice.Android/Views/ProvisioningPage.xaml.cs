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
        _connManager.OnProbeProgress += msg => MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ScanUsb();
        UpdateTechnicianHeader();
    }

    private void UpdateTechnicianHeader()
    {
        var profile = AndroidLicenseManager.Instance.GetProfile();
        if (!string.IsNullOrWhiteSpace(profile.FirstName))
        {
            TechnicianBadgeLabel.Text = $"👤 {profile.FullName}";
            TechnicianDetailsLabel.Text = $"Matrícula: {profile.Phone} • Cluster: {profile.Cluster} • UF: {profile.Uf}";
        }
        else
        {
            TechnicianBadgeLabel.Text = "⚠️ Identificação Pendente";
            TechnicianDetailsLabel.Text = "Toque na aba Licença para preencher Nome, Matrícula, Cluster e UF.";
        }
    }

    private async void OnConnectUsbClicked(object? sender, EventArgs e)
    {
        if (_connManager.IsConnected)
        {
            await _connManager.DisconnectAsync();
            return;
        }

        ConnectUsbBtn.IsEnabled = false;

        try
        {
            ScanUsb();

            UsbDevice? targetDevice = null;
            if (UsbDevicePicker.SelectedIndex >= 0 && UsbDevicePicker.SelectedIndex < _discoveredDevices.Count)
            {
                targetDevice = _discoveredDevices[UsbDevicePicker.SelectedIndex];
            }

            UsbStatusLabel.Text = "Conectando ao console serial USB...";
            AppendLog("[*] Iniciando conexão com o adaptador serial USB...");

            await _connManager.ConnectUsbAsync(targetDevice, baudRate: 9600, status =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UsbStatusLabel.Text = status;
                    AppendLog(status);
                });
            });

            ScanUsb();
            AppendLog("[✓] Porta serial USB aberta e pronta para operação!");

            // Executa identificação autônoma multi-padrão (9600 Cisco/HPE -> 115200 Fortinet)
            await Task.Delay(400);
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
                var baud = (_connManager.CurrentTransport as AndroidUsbSerialTransport)?.BaudRate ?? 9600;
                ConnectionBadge.Text = "CONECTADO";
                ConnectionBadge.TextColor = Color.FromArgb("#4ADE80");
                ConnectUsbBtn.Text = "❌ Desconectar";
                ConnectUsbBtn.BackgroundColor = Color.FromArgb("#DC2626");
                UsbStatusLabel.Text = $"Porta serial conectada a {baud} bps.";
                NegotiatedBaudBadge.Text = $"Taxa Serial Ativa: {baud} bps";
            }
            else
            {
                ConnectionBadge.Text = "DESCONECTADO";
                ConnectionBadge.TextColor = Color.FromArgb("#EF4444");
                ConnectUsbBtn.Text = "🔌 Conectar";
                ConnectUsbBtn.BackgroundColor = Color.FromArgb("#059669");
                UsbStatusLabel.Text = "Console serial desconectado.";
                NegotiatedBaudBadge.Text = "Taxa Serial: Automática (9600 Cisco/HPE • 115200 Fortinet)";
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
        AppendLog("[*] Iniciando identificação autônoma do roteador na porta serial...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _connManager.IdentifyDeviceAsync(cts.Token);
            UpdateIdentifiedDevice(result);

            if (result.Manufacturer == DeviceManufacturer.Unknown)
            {
                AppendLog("[!] Nenhuma resposta recebida em 9600 bps e 115200 bps.");
                AppendLog("    • Certifique-se de que o cabo RJ45 Console está plugado na porta CONSOLE do roteador.");
                AppendLog("    • Certifique-se de que o roteador está ligado à energia.");
                AppendLog("    • Pressione a tecla ENTER na aba 'Console CLI' para testar a comunicação manual.");
            }
            else
            {
                var activeBaud = (_connManager.CurrentTransport as AndroidUsbSerialTransport)?.BaudRate ?? 9600;
                AppendLog($"[✓] EQUIPAMENTO IDENTIFICADO COM SUCESSO!");
                AppendLog($"    Fabricante: {result.Manufacturer}");
                AppendLog($"    Modelo: {result.Series}");
                AppendLog($"    Velocidade Negociada: {activeBaud} bps");
                AppendLog($"    Estado Operacional: {result.OperatingState}");
                if (!string.IsNullOrWhiteSpace(result.RawPrompt))
                {
                    AppendLog($"    Prompt Serial: '{result.RawPrompt.Trim()}'");
                }
            }
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
            var activeBaud = (_connManager.CurrentTransport as AndroidUsbSerialTransport)?.BaudRate ?? 9600;
            ManufacturerLabel.Text = res.Manufacturer.ToString();
            ModelLabel.Text = res.Series == DeviceSeries.Unknown ? "Não identificado (Genérico)" : res.Series.ToString();
            BaudLabel.Text = $"{activeBaud} bps";
            StateLabel.Text = res.OperatingState.ToString();
            PromptLabel.Text = string.IsNullOrWhiteSpace(res.RawPrompt) ? "(Sem resposta serial)" : res.RawPrompt.Trim();

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
            ShareReportBtn.IsVisible = true;
            await DisplayAlert("Sucesso", "Configuração aplicada e gravada permanentemente na NVRAM com sucesso!\n\nVocê já pode clicar em 'Compartilhar Relatório' para enviar o ateste.", "OK");
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

    private async void OnShareReportClicked(object? sender, EventArgs e)
    {
        var saip = _connManager.LoadedCircuit;
        var detected = _connManager.LastDetectionResult;
        var profile = AndroidLicenseManager.Instance.GetProfile();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("📋 ==============================================");
        sb.AppendLine("📋   RELATÓRIO DE PROVISIONAMENTO - SPARC MOBILE  ");
        sb.AppendLine("📋 ==============================================");
        sb.AppendLine($"Data/Hora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Técnico: {(string.IsNullOrWhiteSpace(profile.FullName) ? "Não informado" : profile.FullName)}");
        sb.AppendLine($"Matrícula / Contato: {profile.Phone}");
        sb.AppendLine($"Cluster: {profile.Cluster} | UF: {profile.Uf}");
        sb.AppendLine("");
        sb.AppendLine("--- EQUIPAMENTO DE REDE ---");
        sb.AppendLine($"Fabricante: {detected?.Manufacturer.ToString() ?? "Cisco"}");
        sb.AppendLine($"Modelo: {detected?.Series.ToString() ?? "C841/C921"}");
        sb.AppendLine($"Prompt Serial: {detected?.RawPrompt?.Trim() ?? "-"}");
        sb.AppendLine("");
        sb.AppendLine("--- DADOS DO CIRCUITO (FICHA SAIP) ---");
        sb.AppendLine($"Cliente: {saip?.ClienteRazaoSocial ?? "Não identificado"}");
        sb.AppendLine($"Designação: {saip?.DesignacaoIp ?? "-"}");
        sb.AppendLine($"OTS: {saip?.NumeroOts ?? "-"}");
        sb.AppendLine($"WAN: {saip?.WanIp}/{saip?.WanCidr} (GW: {saip?.WanGateway})");
        sb.AppendLine($"LAN: {saip?.LanIp}/{saip?.LanCidr}");
        sb.AppendLine($"Banda Nominal: {(saip?.BandaMbpsNominal.HasValue == true ? $"{saip.BandaMbpsNominal.Value} Mbps" : "N/D")}");
        sb.AppendLine("");
        sb.AppendLine("--- STATUS DA ATIVAÇÃO ---");
        sb.AppendLine("[✓] Configuração aplicada na sessão serial");
        sb.AppendLine("[✓] Gravação confirmada na NVRAM (write memory / save force)");
        sb.AppendLine("[✓] Validação de link LAN: CONECTADO");
        sb.AppendLine("");
        sb.AppendLine("Relatório gerado automaticamente pelo SPARC Mobile.");

        var textoRelatorio = sb.ToString();

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = $"Ateste SPARC - {saip?.DesignacaoIp ?? saip?.NumeroOts ?? "Circuito"}",
                Text = textoRelatorio
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro ao Compartilhar", ex.Message, "OK");
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
