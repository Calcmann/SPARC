using Android.Hardware.Usb;
using Microsoft.Maui.Controls;
using NetworkDevice.Android.Services;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Android.Views;

public partial class ProvisioningPage : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;
    private readonly FirmwareRepositoryService _firmwareRepo = new();
    private List<UsbDevice> _discoveredDevices = new();

    private CancellationTokenSource? _autoCts;
    private TripleIcmpData? _lastIcmpResult;
    private ConnectivityService.TelnetTestResult? _lastTelnetResult;
    private BandwidthTestResult? _lastBandResult;

    private int _sparcTapCount = 0;
    private DateTime _lastTapTime = DateTime.MinValue;

    private bool _firmwarePostergado = false;
    private DeviceSeries _postergadoSeries = DeviceSeries.Unknown;
    private RemoteFirmwareInfo? _postergadoRemote = null;

    public ProvisioningPage()
    {
        InitializeComponent();
        _connManager.OnConnectionStateChanged += UpdateConnectionState;
        _connManager.OnDeviceIdentified += UpdateIdentifiedDevice;
        _connManager.OnProbeProgress += msg => MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
        _connManager.OnFirmwareProgress += state => MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateFirmwareProgress(state.Percentage, state.Stage, state.Details, $"{state.BytesTransferred / 1048576.0:F1} / {state.TotalBytes / 1048576.0:F1} MB");
        });
        ScanUsb();
        UpdateSpecialFunctionsVisibility();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ScanUsb();
        UpdateTechnicianHeader();
        UpdateSpecialFunctionsVisibility();
        UpdateFirmwareRepoSummary();
    }

    private void UpdateFirmwareRepoSummary()
    {
        try
        {
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

            int offlineCount = 0;
            foreach (var s in seriesList)
            {
                var localFw = _firmwareRepo.GetLocalFirmware(s);
                if (localFw != null && File.Exists(localFw.LocalFilePath))
                    offlineCount++;
            }

            TxtRepoFirmwareSummary.Text = offlineCount == seriesList.Length
                ? $"✅ Todas as {offlineCount} imagens estão salvas no celular (100% offline)"
                : $"📦 {offlineCount} de {seriesList.Length} imagens homologadas salvas no celular";
        }
        catch
        {
            TxtRepoFirmwareSummary.Text = "Repositório local pronto para uso.";
        }
    }

    private async void OnOpenFirmwareRepoClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new NavigationPage(new FirmwareRepoPage()));
    }

    private void UpdateSpecialFunctionsVisibility()
    {
        var enabled = Preferences.Default.Get("sparc_special_functions", false);
        LayoutNatLab.IsVisible = enabled;
        BtnAnalisadorDados.IsVisible = enabled;
        (Shell.Current as AppShell)?.SetSpecialFunctionsVisibility(enabled);
    }

    private async void OnTitleSparcMobileTapped(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTapTime).TotalSeconds > 2.5)
        {
            _sparcTapCount = 0;
        }

        _lastTapTime = now;
        _sparcTapCount++;

        if (_sparcTapCount >= 7)
        {
            _sparcTapCount = 0;
            var current = Preferences.Default.Get("sparc_special_functions", false);
            var newState = !current;
            Preferences.Default.Set("sparc_special_functions", newState);

            UpdateSpecialFunctionsVisibility();
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(400)); } catch { }

            if (newState)
            {
                await DisplayAlert("🔓 Funções Especiais Ativadas",
                    "Modo Especial liberado com sucesso!\n\n" +
                    "• Habilitar NAT (LAB) — bancada\n" +
                    "• Analisador de Dados (ITU-T Y.1564 / Loop VIAVI)\n\n" +
                    "As opções foram disponibilizadas na tela inicial e no menu inferior.", "OK");
            }
            else
            {
                await DisplayAlert("🔒 Funções Especiais Ocultadas",
                    "Modo padrão restabelecido. As opções de NAT LAB e Analisador de Dados foram ocultadas da interface.", "OK");
            }
        }
    }

    private void UpdateTechnicianHeader()
    {
        var profile = AndroidLicenseManager.Instance.GetProfile();
        if (!string.IsNullOrWhiteSpace(profile.FirstName))
        {
            TechnicianBadgeLabel.Text = $"👤 {profile.FullName}";
            var mat = string.IsNullOrWhiteSpace(profile.EmployeeId) ? "Não informada" : profile.EmployeeId;
            TechnicianDetailsLabel.Text = $"Matrícula: {mat} • Cluster: {profile.Cluster} • UF: {profile.Uf}";
        }
        else
        {
            TechnicianBadgeLabel.Text = "⚠️ Identificação Pendente";
            TechnicianDetailsLabel.Text = "Toque em Perfil para preencher Nome e Matrícula.";
        }
    }

    #region Passo 1: USB & Hardware

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
            UsbStatusLabel.Text = "Nenhum adaptador serial USB encontrado (placas USB/ETH são reservadas para tráfego de rede).";
            UsbDevicePicker.Title = "Nenhum dispositivo serial detectado";
            return;
        }

        var deviceNames = _discoveredDevices.Select(d =>
        {
            var isSerial = DeviceConnectionManager.IsSupportedSerialDevice(d);
            var isNet = DeviceConnectionManager.IsNetworkOrHubDevice(d);
            var tag = isSerial ? "🔌 [SERIAL CONSOLE]" : (isNet ? "🌐 [REDE ETHERNET - NÃO É SERIAL]" : "❓ [DISPOSITIVO USB]");
            var m = string.IsNullOrWhiteSpace(d.ManufacturerName) ? "USB" : d.ManufacturerName;
            var p = string.IsNullOrWhiteSpace(d.ProductName) ? "Device" : d.ProductName;
            return $"{tag} {m} {p} (VID:0x{d.VendorId:X4}:PID:0x{d.ProductId:X4})";
        }).ToList();

        UsbDevicePicker.ItemsSource = deviceNames;

        // GARANTIA ESTRITA: O primeiro item preferencialmente tem que ser o adaptador serial homologado,
        // evitando que o adaptador Ethernet assuma a posição e impeça o acesso CLI inicial.
        int targetIndex = _discoveredDevices.FindIndex(DeviceConnectionManager.IsSupportedSerialDevice);
        if (targetIndex < 0)
        {
            targetIndex = _discoveredDevices.FindIndex(d => !DeviceConnectionManager.IsNetworkOrHubDevice(d));
        }
        if (targetIndex < 0)
        {
            targetIndex = 0;
        }

        UsbDevicePicker.SelectedIndex = targetIndex;

        var serialCount = _discoveredDevices.Count(DeviceConnectionManager.IsSupportedSerialDevice);
        if (serialCount > 0)
        {
            UsbStatusLabel.Text = $"🔌 {serialCount} adaptador(es) serial USB priorizado(s) no topo.";
        }
        else
        {
            UsbStatusLabel.Text = $"{_discoveredDevices.Count} dispositivo(s) USB detectado(s).";
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

            // Alerta preventivo se o usuário tentar abrir um adaptador de rede como console serial CLI
            if (targetDevice != null && DeviceConnectionManager.IsNetworkOrHubDevice(targetDevice))
            {
                var serialExistente = _discoveredDevices.FirstOrDefault(DeviceConnectionManager.IsSupportedSerialDevice);
                if (serialExistente != null)
                {
                    var trocar = await DisplayAlert(
                        "Adaptador de Rede Selecionado",
                        $"O dispositivo selecionado ({targetDevice.ProductName ?? "Ethernet"}) é um adaptador de rede RJ45 e não uma porta serial CLI.\n\n" +
                        $"Deseja alternar automaticamente para o adaptador serial ({serialExistente.ProductName ?? "Console Serial"}) para acessar a CLI?",
                        "SIM, USAR SERIAL", "CANCELAR");

                    if (trocar)
                    {
                        var idx = _discoveredDevices.IndexOf(serialExistente);
                        if (idx >= 0)
                        {
                            UsbDevicePicker.SelectedIndex = idx;
                            targetDevice = serialExistente;
                        }
                    }
                    else
                    {
                        ConnectUsbBtn.IsEnabled = true;
                        return;
                    }
                }
                else
                {
                    await DisplayAlert(
                        "Adaptador Ethernet RJ45 Detectado",
                        "O dispositivo selecionado é um adaptador de rede USB/Ethernet e não suporta comandos CLI serial.\n\n" +
                        "Conecte o cabo de console serial USB ao smartphone/HUB para prosseguir com a identificação e configuração inicial.",
                        "OK");
                    ConnectUsbBtn.IsEnabled = true;
                    return;
                }
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
            UsbStatusLabel.Text = "Porta serial aberta! Testando conexão automaticamente...";
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
                ConnectUsbBtn.Text = "❌ Desconectar";
                ConnectUsbBtn.BackgroundColor = Color.FromArgb("#DC2626");
                UsbStatusLabel.Text = $"Porta serial conectada a {baud} bps.";
                TxtBadgeStep1.Text = "✅ Conectado";
                TxtBadgeStep1.TextColor = Color.FromArgb("#4ADE80");
            }
            else
            {
                ConnectUsbBtn.Text = "🔌 Conectar USB";
                ConnectUsbBtn.BackgroundColor = Color.FromArgb("#059669");
                UsbStatusLabel.Text = "Console serial desconectado.";
                TxtBadgeStep1.Text = "🔴 Pendente";
                TxtBadgeStep1.TextColor = Color.FromArgb("#FCA5A5");

                // Restaura o botão de teste de conexão
                IdentifyBtn.Text = "⚡ Testar Conexão / Identificar Equipamento";
                IdentifyBtn.BackgroundColor = Color.FromArgb("#B91C1C");
                IdentifyBtn.IsEnabled = true;
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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            IdentifyBtn.IsEnabled = false;
            IdentifyBtn.Text = "⏳ Testando Conexão e Identificando Equipamento...";
            IdentifyBtn.BackgroundColor = Color.FromArgb("#D97706");
        });

        AppendLog("[*] Disparando teste de conexão e identificação autônoma do roteador...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _connManager.IdentifyDeviceAsync(cts.Token);
            UpdateIdentifiedDevice(result);

            if (result.OperatingState == DeviceOperatingState.PasswordProtected)
            {
                await TratarEquipamentoComSenhaAsync(result);
            }

            if (result.Manufacturer != DeviceManufacturer.Unknown)
            {
                // Sincroniza o ModelPicker conforme a série detectada
                SelectModelInPicker(result.Series);
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IdentifyBtn.Text = $"✅ {result.Manufacturer} {result.Series} • Re-testar Conexão";
                    IdentifyBtn.BackgroundColor = Color.FromArgb("#059669");
                });
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IdentifyBtn.Text = "⚠️ Não Identificado • Re-testar Conexão";
                    IdentifyBtn.BackgroundColor = Color.FromArgb("#D97706");
                });
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Falha ao interrogar roteador: {ex.Message}");
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IdentifyBtn.Text = "❌ Falha no Teste • Re-testar Conexão";
                IdentifyBtn.BackgroundColor = Color.FromArgb("#B91C1C");
            });
            await DisplayAlert("Identificação", ex.Message, "OK");
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IdentifyBtn.IsEnabled = true;
            });
        }
    }

    private void SelectModelInPicker(DeviceSeries series)
    {
        for (int i = 0; i < ModelPicker.Items.Count; i++)
        {
            var item = ModelPicker.Items[i];
            if (series == DeviceSeries.Msr954 && item.Contains("954")) { ModelPicker.SelectedIndex = i; return; }
            if (series == DeviceSeries.Msr930 && item.Contains("930")) { ModelPicker.SelectedIndex = i; return; }
            if (series == DeviceSeries.Msr1002 && item.Contains("1002")) { ModelPicker.SelectedIndex = i; return; }
            if (series == DeviceSeries.Series1900 && item.Contains("1900")) { ModelPicker.SelectedIndex = i; return; }
            if (series == DeviceSeries.Series2900 && item.Contains("2900")) { ModelPicker.SelectedIndex = i; return; }
            if (series == DeviceSeries.Isr921 && (item.Contains("900") || item.Contains("921"))) { ModelPicker.SelectedIndex = i; return; }
            if (series == DeviceSeries.Isr841 && (item.Contains("800") || item.Contains("841"))) { ModelPicker.SelectedIndex = i; return; }
            if (series == DeviceSeries.FortiGate40F && item.Contains("40F")) { ModelPicker.SelectedIndex = i; return; }
        }
    }

    private void UpdateIdentifiedDevice(DeviceDetectionResult res)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var activeBaud = (_connManager.CurrentTransport as AndroidUsbSerialTransport)?.BaudRate ?? 9600;
            var modelDisplay = res.Series switch
            {
                DeviceSeries.Isr841 => "Cisco Série 800 / C841M",
                DeviceSeries.Isr921 => "Cisco Série 900 / C921-4P",
                DeviceSeries.Series1900 => "Cisco Série 1900 / G2",
                DeviceSeries.Series2900 => "Cisco Série 2900 / G2",
                DeviceSeries.Msr954 => "HPE MSR 954 / 958",
                DeviceSeries.Msr930 => "HPE MSR 930 / 935",
                DeviceSeries.Msr1002 => "HPE MSR 1002 / 1003",
                DeviceSeries.FortiGate40F => "Fortinet FortiGate 40F",
                _ => res.Manufacturer == DeviceManufacturer.Unknown ? "Não identificado" : $"{res.Manufacturer} (Genérico)"
            };
            ModelLabel.Text = modelDisplay;
            BaudLabel.Text = $"{activeBaud} bps";
            PromptLabel.Text = string.IsNullOrWhiteSpace(res.RawPrompt) ? "(Sem resposta)" : res.RawPrompt.Trim();

            if (res.Series != DeviceSeries.Unknown)
            {
                SelectModelInPicker(res.Series);
            }

            if (res.OperatingState == DeviceOperatingState.Ready)
            {
                StateLabel.Text = "PRONTO (Sem Bloqueio)";
                StateLabel.TextColor = Color.FromArgb("#4ADE80");
                TxtBadgeStep1.Text = $"✅ {modelDisplay}";
                TxtBadgeStep1.TextColor = Color.FromArgb("#4ADE80");
            }
            else if (res.OperatingState == DeviceOperatingState.PasswordProtected)
            {
                StateLabel.Text = "🔒 PROTEGIDO POR SENHA";
                StateLabel.TextColor = Color.FromArgb("#FBBF24");
                TxtBadgeStep1.Text = "⚠️ Com Senha";
                TxtBadgeStep1.TextColor = Color.FromArgb("#FBBF24");
            }
            else
            {
                StateLabel.Text = res.OperatingState.ToString();
                StateLabel.TextColor = Color.FromArgb("#EF4444");
                TxtBadgeStep1.Text = "⚠️ Atenção";
                TxtBadgeStep1.TextColor = Color.FromArgb("#EF4444");
            }
        });
    }

    private void OnModelPickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (ModelPicker.SelectedIndex < 0) return;
        var selected = ModelPicker.Items[ModelPicker.SelectedIndex];
        TxtBadgeStep1.Text = "✅ Modelo Selecionado";
        TxtBadgeStep1.TextColor = Color.FromArgb("#4ADE80");
        AppendLog($"[*] Modelo selecionado manualmente: {selected}");
    }

    #endregion

    #region Passo 2: Ficha SAIP

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

        SaipClientLabel.Text = $"Cliente: {saip.ClienteRazaoSocial ?? "Não informado"}";
        var prodText = !string.IsNullOrWhiteSpace(saip.Produto) ? saip.Produto : (saip.TipoServico == SaipServiceType.IpVpn ? "IP VPN (MPLS)" : "BLD");
        SaipCircuitLabel.Text = $"Produto: {prodText} | Designação: {saip.DesignacaoIp ?? "-"} | OTS: {saip.NumeroOts ?? "-"}";
        SaipWanLabel.Text = $"WAN: {saip.WanIp}/{saip.WanCidr} (Gateway: {saip.WanGateway})";
        SaipLanLabel.Text = $"LAN: {saip.LanIp}/{saip.LanCidr}{(saip.IsLanArbitrada ? " [LAN Arbitrada]" : "")} | Host: {saip.HostLanIp}";
        SaipBandwidthLabel.Text = $"Banda Nominal: {(saip.BandaMbpsNominal.HasValue ? $"{saip.BandaMbpsNominal.Value} Mbps" : "N/D")}";

        TxtBadgeStep2.Text = "✅ Ficha OK";
        TxtBadgeStep2.TextColor = Color.FromArgb("#4ADE80");
        AppendLog($"[✓] Ficha SAIP carregada ({origem}): Produto '{prodText}', Cliente '{saip.ClienteRazaoSocial}', WAN '{saip.WanIp}', LAN '{saip.LanIp}'");
    }

    private void OnToggleManualEditClicked(object? sender, EventArgs e)
    {
        var saip = _connManager.LoadedCircuit;
        if (saip != null)
        {
            EditClienteEntry.Text = saip.ClienteRazaoSocial;
            EditDesignacaoEntry.Text = saip.DesignacaoIp;
            EditWanIpEntry.Text = $"{saip.WanIp}/{saip.WanCidr}";
            EditWanGwEntry.Text = saip.WanGateway;
            EditLanIpEntry.Text = $"{saip.LanIp}/{saip.LanCidr}";
            EditHostLanEntry.Text = saip.HostLanIp;
            EditBandaEntry.Text = saip.BandaMbpsNominal?.ToString("F0");
            EditOtsEntry.Text = saip.NumeroOts;
        }

        ManualEditCard.IsVisible = !ManualEditCard.IsVisible;
    }

    private void OnCancelManualEditClicked(object? sender, EventArgs e)
    {
        ManualEditCard.IsVisible = false;
    }

    private async void OnSaveManualEditClicked(object? sender, EventArgs e)
    {
        var wanInput = EditWanIpEntry.Text?.Trim() ?? string.Empty;
        var lanInput = EditLanIpEntry.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(wanInput) || string.IsNullOrWhiteSpace(lanInput))
        {
            await DisplayAlert("Campos Obrigatórios", "Informe ao menos os endereços de WAN e LAN.", "OK");
            return;
        }

        // Parsing simples de CIDR se presente
        var wanParts = wanInput.Split('/');
        var wanIp = wanParts[0].Trim();
        var wanCidr = wanParts.Length > 1 && int.TryParse(wanParts[1], out var wc) ? wc : 30;

        var lanParts = lanInput.Split('/');
        var lanIp = lanParts[0].Trim();
        var lanCidr = lanParts.Length > 1 && int.TryParse(lanParts[1], out var lc) ? lc : 29;

        double? banda = null;
        if (double.TryParse(EditBandaEntry.Text, out var bVal))
        {
            banda = bVal;
        }

        var newSaip = new SaipCircuitData
        {
            ClienteRazaoSocial = string.IsNullOrWhiteSpace(EditClienteEntry.Text) ? "Cliente Homologação" : EditClienteEntry.Text.Trim(),
            DesignacaoIp = string.IsNullOrWhiteSpace(EditDesignacaoEntry.Text) ? "MANUAL-CIRCUITO" : EditDesignacaoEntry.Text.Trim(),
            NumeroOts = EditOtsEntry.Text?.Trim(),
            WanIp = wanIp,
            WanCidr = wanCidr,
            WanGateway = EditWanGwEntry.Text?.Trim() ?? string.Empty,
            LanIp = lanIp,
            LanCidr = lanCidr,
            HostLanIp = EditHostLanEntry.Text?.Trim() ?? string.Empty,
            BandaMbpsNominal = banda,
            RawSource = "Edição Manual do Técnico"
        };

        SetSaipData(newSaip, "Edição Manual");
        ManualEditCard.IsVisible = false;
        await DisplayAlert("Parâmetros Atualizados", "Os parâmetros do circuito foram gravados para esta sessão de provisionamento.", "OK");
    }

    #endregion

    #region Passo 3: Iniciar Provisionamento (Fases 1 a 7)

    private async void OnIniciarModoAutomaticoClicked(object? sender, EventArgs e)
    {
        // 1. Validar Conexão Serial
        if (!_connManager.IsConnected)
        {
            await DisplayAlert("Conexão Serial Pendente",
                "Conecte o cabo USB OTG ao roteador e clique em 'Conectar USB' no Passo 1 antes de iniciar o provisionamento.", "OK");
            return;
        }

        // 2. Exigir que o equipamento tenha sido testado e identificado
        var detected = _connManager.LastDetectionResult;
        if (detected == null || detected.OperatingState == DeviceOperatingState.Unknown)
        {
            var tryIdentify = await DisplayAlert(
                "Identificação Obrigatória",
                "O equipamento conectado ainda não foi verificado na porta serial.\n\n" +
                "É obrigatório executar 'Testar Conexão / Identificar Equipamento' no Passo 1 para auditar a integridade, o modelo e o estado de acesso antes de iniciar o provisionamento.\n\n" +
                "Deseja testar a conexão e identificar o equipamento agora?",
                "TESTAR E IDENTIFICAR", "CANCELAR");

            if (tryIdentify)
            {
                await RunDeviceIdentificationAsync();
                detected = _connManager.LastDetectionResult;
                if (detected == null || detected.OperatingState == DeviceOperatingState.Unknown)
                {
                    return;
                }
            }
            else
            {
                return;
            }
        }

        // 3. Roteador em Modo ROMMON / Bootloader (Sem Sistema Operacional)
        var isRommon = detected.BootState == BootState.Rommon ||
                       detected.AccessState == AccessState.RommonOrBootware ||
                       detected.OperatingState == DeviceOperatingState.BootFailure ||
                       detected.RawPrompt?.Trim().StartsWith("rommon", StringComparison.OrdinalIgnoreCase) == true;

        if (isRommon)
        {
            var irParaFirmware = await DisplayAlert(
                "⚠️ Equipamento em Modo ROMMON / BootWare",
                $"O equipamento '{detected.Series}' encontra-se em modo de recuperação ROMMON / BootWare (sem firmware na memória Flash).\n\n" +
                "Nesse estado o roteador não aceita comandos de configuração SAIP diretamente. É OBRIGATÓRIO transferir e gravar a imagem de firmware (.bin) via TFTP ou FTP antes do provisionamento.\n\n" +
                "Deseja acessar a aba 'Firmware & WAN' para executar a recuperação agora?",
                "IR PARA FIRMWARE", "VOLTAR");

            if (irParaFirmware)
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync("//FirmwarePage");
                }
                else
                {
                    await Navigation.PushAsync(new FirmwarePage());
                }
            }
            return;
        }

        // 4. Equipamento Protegido por Senha
        if (detected.OperatingState == DeviceOperatingState.PasswordProtected)
        {
            await TratarEquipamentoComSenhaAsync(detected);
            return;
        }

        // 5. Garantir que o equipamento esteja acessível (Ready)
        if (detected.OperatingState != DeviceOperatingState.Ready)
        {
            await DisplayAlert("Equipamento Inacessível",
                $"O equipamento encontra-se no estado '{detected.OperatingState}'.\n\n" +
                $"Detalhes: {detected.Details}\n\n" +
                "Somente equipamentos com acesso liberado (PRONTO) podem receber o provisionamento.", "OK");
            return;
        }

        // 6. Validar Ficha SAIP
        if (_connManager.LoadedCircuit == null)
        {
            await DisplayAlert("Ficha SAIP Pendente",
                "Carregue o arquivo PDF ou cole os dados da Ficha SAIP no Passo 2 antes de iniciar a ativação.", "OK");
            return;
        }

        // 7. Confirmação Final de Início
        var confirm = await DisplayAlert("⚡ Iniciar Provisionamento",
            $"Equipamento auditado e liberado com sucesso:\n\n" +
            $"• Roteador: {detected.Manufacturer} {detected.Series} (Acesso OK)\n" +
            $"• Cliente: {_connManager.LoadedCircuit.ClienteRazaoSocial}\n" +
            $"• Designação: {_connManager.LoadedCircuit.DesignacaoIp}\n" +
            $"• WAN: {_connManager.LoadedCircuit.WanIp}/{_connManager.LoadedCircuit.WanCidr}\n" +
            $"• LAN: {_connManager.LoadedCircuit.LanIp}/{_connManager.LoadedCircuit.LanCidr}\n\n" +
            $"O SPARC executará as etapas de provisionamento, configuração de interfaces e testes técnicos.",
            "SIM, INICIAR AGORA", "CANCELAR");

        if (!confirm) return;

        // Transição de tela: Oculta Tela Inicial e exibe Provisionamento em Execução
        ViewTelaInicial.IsVisible = false;
        ViewModoAutomatico.IsVisible = true;

        _ = ExecutarModoAutomaticoAsync();
    }

    private async Task TratarEquipamentoComSenhaAsync(DeviceDetectionResult detected)
    {
        var modelName = detected.Series != DeviceSeries.Unknown ? detected.Series.ToString() : detected.Manufacturer.ToString();
        var choice = await DisplayActionSheet(
            $"🔒 Roteador Protegido por Senha ({modelName})",
            "Cancelar",
            null,
            "🔑 Informar Login e Senha",
            $"⚡ Zerar Configuração ({modelName})");

        if (choice == "🔑 Informar Login e Senha")
        {
            var user = await DisplayPromptAsync("Autenticação Manual", "Informe o usuário (deixe em branco se for apenas senha):", initialValue: "admin");
            if (user == null) return;

            var pass = await DisplayPromptAsync("Autenticação Manual", "Informe a senha de acesso:", placeholder: "Senha");
            if (pass == null) return;

            AppendLog($"[*] Tentando autenticação manual com as credenciais informadas (usuário '{user}')...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var ok = await _connManager.TryLoginAsync(user, pass, msg => AppendLog(msg), cts.Token);

            if (ok)
            {
                await DisplayAlert("✅ Acesso Liberado",
                    $"Credenciais aceitas com sucesso pelo equipamento!\n\n" +
                    "O roteador agora está liberado para provisionamento.", "OK");

                using var ctsId = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var newRes = await _connManager.IdentifyDeviceAsync(ctsId.Token);
                UpdateIdentifiedDevice(newRes);
            }
            else
            {
                var tentarReset = await DisplayAlert(
                    "❌ Credenciais Recusadas",
                    $"As credenciais informadas não foram aceitas pelo roteador.\n\n" +
                    $"Deseja seguir com o procedimento de Zerar Configuração para o {modelName}?",
                    "ZERAR CONFIGURAÇÃO", "TENTAR NOVAMENTE");

                if (tentarReset)
                {
                    if (Shell.Current != null)
                        await Shell.Current.GoToAsync("//RecoveryPage");
                    else
                        await Navigation.PushAsync(new RecoveryPage());
                }
                else
                {
                    await TratarEquipamentoComSenhaAsync(detected);
                }
            }
        }
        else if (choice != null && choice.StartsWith("⚡ Zerar Configuração"))
        {
            if (Shell.Current != null)
                await Shell.Current.GoToAsync("//RecoveryPage");
            else
                await Navigation.PushAsync(new RecoveryPage());
        }
    }

    private async Task ExecutarModoAutomaticoAsync()
    {
        _autoCts = new CancellationTokenSource();
        var ct = _autoCts.Token;

        void SetEtapa(int n, string texto, string corHex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var lbl = n switch
                {
                    1 => LblAutoEtapa1,
                    2 => LblAutoEtapa2,
                    3 => LblAutoEtapa3,
                    4 => LblAutoEtapa4,
                    5 => LblAutoEtapa5,
                    6 => LblAutoEtapa6,
                    7 => LblAutoEtapa7,
                    _ => null
                };
                if (lbl != null)
                {
                    lbl.Text = texto;
                    lbl.TextColor = Color.FromArgb(corHex);
                }
            });
        }

        void Progresso(double valor, string status)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                PbAutoGeral.Progress = valor / 100.0;
                TxtAutoPorcentagem.Text = $"{valor:F0}%";
                TxtAutoStatusGeral.Text = status;
            });
        }

        void LogAuto(string msg)
        {
            MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
        }

        // Reset inicial das etapas
        for (int i = 1; i <= 7; i++)
        {
            var nome = i switch
            {
                1 => "Zerar Configuração",
                2 => "Atualizar Firmware",
                3 => "Provisionar Equipamento",
                4 => "Configurar IP de Teste",
                5 => "Testar Conectividade (ICMP)",
                6 => "Testar Acesso Remoto (Telnet / SSH)",
                7 => "Testar Banda (Vazão vs Nominal)",
                _ => ""
            };
            SetEtapa(i, $"○ {i}. {nome} — aguardando", "#64748B");
        }

        BtnAutoCancelar.IsVisible = true;
        BtnAutoVoltar.IsVisible = false;
        BtnAutoCompartilharRelatorio.IsVisible = false;
        BtnAutoCertidaoY1564.IsVisible = false;
        BtnAutoVerScript.IsVisible = false;
        BtnAutoRetestarBanda.IsVisible = false;

        Progresso(5, "Provisionamento — Iniciando validações...");
        LogAuto("=================================================================");
        LogAuto("   SPARC MOBILE — EXECUÇÃO DO PROVISIONAMENTO (FASES 1 A 7)      ");
        LogAuto("=================================================================");

        var circuit = _connManager.LoadedCircuit!;
        var detected = _connManager.LastDetectionResult;
        var isForti = detected?.Manufacturer == DeviceManufacturer.Fortinet ||
                      (ModelPicker.SelectedItem?.ToString()?.Contains("Fortinet", StringComparison.OrdinalIgnoreCase) == true);

        bool ethBound = false;
        DeviceDisplay.Current.KeepScreenOn = true;
        try
        {
            // =====================================================================
            // FASE 1: ZERAR CONFIGURAÇÃO / FACTORY RESET
            // =====================================================================
            SetEtapa(1, "⏳ 1. Zerar Configuração — em execução", "#D97706");
            Progresso(15, "1/7 Zerando Configuração...");
            LogAuto("\n>>> [AUTO 1/7] Zeramento de Configuração / Factory Reset");

            if (detected?.OperatingState == DeviceOperatingState.Ready)
            {
                SetEtapa(1, "⏭ 1. Zerar Configuração — pulado (Equipamento Pronto)", "#16A34A");
                LogAuto("[*] Equipamento sem bloqueio de senha. Prosseguindo...");
            }
            else if (detected?.OperatingState == DeviceOperatingState.PasswordProtected)
            {
                LogAuto("[!] Equipamento protegido por senha. Executando quebra autônoma...");
                var resetOk = await _connManager.RecoverPasswordAsync(
                    msg => { LogAuto(msg); return Task.CompletedTask; },
                    null,
                    ct);
                if (resetOk)
                {
                    SetEtapa(1, "✅ 1. Zerar Configuração — OK (Desbloqueado)", "#16A34A");
                    LogAuto("[✓] Desbloqueio e zeramento realizados com sucesso!");
                }
                else
                {
                    SetEtapa(1, "⚠️ 1. Zerar Configuração — atenção", "#FBBF24");
                }
            }
            else
            {
                SetEtapa(1, "✅ 1. Zerar Configuração — OK", "#16A34A");
            }
            Progresso(25, "1/7 Concluído");

            // =====================================================================
            // FASE 2: ATUALIZAR FIRMWARE
            // =====================================================================
            var atualizarFw = ChkAtualizarFirmwareAuto.IsChecked;
            if (atualizarFw)
            {
                SetEtapa(2, "⏳ 2. Atualizar Firmware — em execução", "#D97706");
                Progresso(35, "2/7 Auditando Firmware...");
                LogAuto("\n>>> [AUTO 2/7] Auditoria e Atualização de Firmware");

                if (detected != null)
                {
                    var updater = new RouterDirectFirmwareUpdater(msg =>
                    {
                        LogAuto(msg);
                        return Task.CompletedTask;
                    });

                    // 1. Consulta versão homologada central (remota ou cache)
                    LogAuto($"[*] Consultando base de firmwares homologados para '{detected.Series}'...");
                    RemoteFirmwareInfo? officialRemote = null;
                    try
                    {
                        using var ctsFw = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        ctsFw.CancelAfter(TimeSpan.FromSeconds(10));
                        officialRemote = await _firmwareRepo.QueryRemoteForSeriesAsync(detected.Series, ctsFw.Token);
                    }
                    catch (Exception ex)
                    {
                        LogAuto($"  [AVISO] Falha ao consultar repositório online: {ex.Message}");
                    }

                    // 2. Audita versão em execução no equipamento com loop serial isolado
                    var audit = await _connManager.AuditFirmwareComplianceAsync(
                        detected.Series,
                        officialRemote,
                        s => { LogAuto(s); return Task.CompletedTask; },
                        ct);

                    var versaoExibida = !string.IsNullOrWhiteSpace(audit.CurrentVersion) && audit.CurrentVersion != "Não identificado"
                        ? audit.CurrentVersion
                        : $"{detected.Series} (Instalado)";

                    if (officialRemote == null)
                    {
                        LogAuto($"[AVISO] Nenhum firmware homologado cadastrado na base para {detected.Series}.");
                        LogAuto($"[✓] Firmware mantido na versão atual: {versaoExibida}");
                        SetEtapa(2, $"⚠️ 2. Firmware — {versaoExibida} (Sem Homologado)", "#CA8A04");
                    }
                    else if (audit.IsCompliant)
                    {
                        LogAuto($"[✓] Firmware em conformidade com a versão homologada ({officialRemote.FileName}). Nenhuma atualização necessária.");
                        SetEtapa(2, $"✅ 2. Firmware — {versaoExibida} (Já Homologado)", "#16A34A");
                    }
                    else
                    {
                        LogAuto($"[!] Firmware desatualizado detectado no {detected.Series}!");
                        LogAuto($"    Versão em execução : {versaoExibida}");
                        LogAuto($"    Versão homologada  : {officialRemote.FileName}");

                        // 3. Avalia topologia de hardware (HUB USB Simultâneo vs Porta Única com Swap vs Download WAN)
                        var ethMgr = AndroidEthernetManager.Instance;
                        bool hasSerial = _connManager.IsConnected || _connManager.HasSupportedSerialConnected();
                        bool hasEthHw = ethMgr.HasEthernetHardwareInterface() || _connManager.HasEthernetOrHubDeviceConnected();
                        bool hasEth = ethMgr.IsEthernetConnected();
                        bool isHubSimultaneous = ethMgr.IsHubUsbSimultaneousActive(hasSerial) || (hasSerial && hasEthHw);

                        var localFw = _firmwareRepo.GetLocalFirmware(detected.Series);

                            // Se o firmware homologado não estiver salvo no celular, tenta baixar via conexão móvel/Wi-Fi do smartphone
                            if (localFw == null && officialRemote != null)
                            {
                                try
                                {
                                    LogAuto($"[*] Baixando firmware homologado ({officialRemote.FileName}) para o celular...");
                                    var prog = new Progress<FirmwareDownloadProgress>(p =>
                                    {
                                        Progresso(40, $"Baixando no celular: {p.Percentage:F0}%");
                                    });
                                    await _firmwareRepo.DownloadFirmwareAsync(officialRemote, prog, ct);
                                    localFw = _firmwareRepo.GetLocalFirmware(detected.Series);
                                }
                                catch (Exception exDl)
                                {
                                    LogAuto($"[AVISO] Download celular indisponível: {exDl.Message}");
                                }
                            }

                            bool transferiuLocalmente = false;

                            // CENÁRIO A: HUB USB COM SERIAL E ETHERNET SIMULTANEAMENTE (NÃO PEDE TROCA DE CABOS!)
                            if (isHubSimultaneous || (hasSerial && (hasEthHw || hasEth)))
                            {
                                LogAuto("\n>>> [MODO HUB USB DETECTADO] Adaptador Serial e Adaptador Ethernet operando simultaneamente via HUB USB.");
                                LogAuto("[*] O console serial continuará conectado durante toda a transferência.");

                                if (localFw != null && File.Exists(localFw.LocalFilePath))
                                {
                                    if (!hasEth || string.IsNullOrWhiteSpace(ethMgr.GetEthernetIpAddress()))
                                    {
                                        var conectouCabo = await MainThread.InvokeOnMainThreadAsync(() =>
                                            DisplayAlert("🔌 Conectar Cabo de Rede no HUB",
                                                $"O HUB USB com Serial e Ethernet foi detectado!\n\n" +
                                                $"Firmware homologado: {localFw.FileName}\n\n" +
                                                "Por favor, conecte o cabo de rede RJ45 do HUB à porta LAN do roteador (ex: GE0/0 ou GE0/1) para iniciar a transferência de alta velocidade.\n\n" +
                                                "O cabo serial permanecerá conectado.",
                                                "CONECTEI O CABO", "PULAR ATUALIZAÇÃO"));

                                        if (conectouCabo)
                                        {
                                            bool tentarHubEth = true;
                                            while (tentarHubEth && !ct.IsCancellationRequested)
                                            {
                                                tentarHubEth = false;
                                                LogAuto("[*] Aguardando enlace Ethernet no HUB USB (até 15s)...");
                                                for (int w = 0; w < 15 && !ct.IsCancellationRequested; w++)
                                                {
                                                    await Task.Delay(1000, ct);
                                                    if (ethMgr.IsEthernetConnected() && !string.IsNullOrWhiteSpace(ethMgr.GetEthernetIpAddress())) break;
                                                }
                                                hasEth = ethMgr.IsEthernetConnected();
                                                var ipHub = ethMgr.GetEthernetIpAddress();
                                                if (!hasEth || string.IsNullOrWhiteSpace(ipHub))
                                                {
                                                    LogAuto("[ALERTA] Porta LAN do roteador permaneceu DESCONECTADA (sem link no HUB).");
                                                    var respHub = await MainThread.InvokeOnMainThreadAsync(() =>
                                                        DisplayAlert("⚠️ Porta LAN Desconectada / Sem Link",
                                                            "O cabo RJ45 conectado ao HUB USB não estabeleceu enlace elétrico com a porta LAN do roteador.\n\n" +
                                                            "• Verifique se o cabo está conectado à porta LAN correta (ex: GE0/0 ou GE0/1).\n" +
                                                            "• Verifique se os LEDs de LINK da porta Ethernet no roteador acenderam.\n" +
                                                            "• Se o roteador tiver mais de uma porta LAN, experimente conectar na outra porta.\n\n" +
                                                            "Deseja verificar o cabo de rede e tentar novamente?",
                                                            "TENTAR NOVAMENTE", "PULAR ATUALIZAÇÃO VIA HUB"));
                                                    if (respHub)
                                                    {
                                                        tentarHubEth = true;
                                                        continue;
                                                    }
                                                    else
                                                    {
                                                        LogAuto("[!] Atualização via HUB dispensada: porta LAN do roteador sem link físico.");
                                                        hasEth = false;
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    if (hasEth)
                                    {
                                        Progresso(40, "2/7 Aplicando Staging via Serial...");
                                        await _connManager.ApplyTemporaryLanStagingAsync(detected.Manufacturer, detected.Series, null, s => { LogAuto(s); return Task.CompletedTask; }, ct);

                                        var ethIp = ethMgr.GetEthernetIpAddress() ?? circuit.HostLanIp ?? "192.168.1.2";
                                        LogAuto($"[*] Transferindo firmware via HUB USB (OTG/ETH {ethIp})...");
                                        Progresso(40, "2/7 Gravando Firmware via OTG/ETH...");

                                        ShowFirmwareProgress(
                                            "GRAVAÇÃO DE FIRMWARE VIA HUB USB",
                                            localFw.FileName,
                                            "Flash do Roteador",
                                            isForti || detected.Manufacturer == DeviceManufacturer.Hpe ? "HUB OTG+ETH (FTP :2121)" : "HUB OTG+ETH (HTTP :8080)");

                                        var ethBoundLocal = ethMgr.BindProcessToEthernet(LogAuto);
                                        try
                                        {
                                            bool successLocal = false;
                                            if (isForti)
                                            {
                                                successLocal = await _connManager.RestoreFirmwareFortiFtpAsync(
                                                    localFw.LocalFilePath, ethIp, "sparc", "claro123", msg => { LogAuto(msg); AppendFirmwareProgressLog(msg); return Task.CompletedTask; }, ct);
                                            }
                                            else if (detected.Manufacturer == DeviceManufacturer.Hpe)
                                            {
                                                successLocal = await _connManager.UpgradeFirmwareHpeFtpAsync(
                                                    localFw.LocalFilePath, ethIp, "sparc", "claro123", msg => { LogAuto(msg); AppendFirmwareProgressLog(msg); return Task.CompletedTask; }, ct);
                                            }
                                            else
                                            {
                                                var routerIp = circuit.LanIp ?? "192.168.1.1";
                                                successLocal = await _connManager.UpgradeFirmwareCiscoHttpAsync(
                                                    localFw.LocalFilePath,
                                                    phoneIp: ethIp,
                                                    routerIp: routerIp,
                                                    telnetUser: "EBT",
                                                    telnetPass: "CQMR",
                                                    routerTempIp: circuit.LanIp ?? "192.168.1.1",
                                                    routerTempMask: circuit.LanSubnetMask ?? "255.255.255.0",
                                                    lanInterface: null,
                                                    expectedMd5: null,
                                                    progress: msg => { LogAuto(msg); AppendFirmwareProgressLog(msg); return Task.CompletedTask; },
                                                    ct: ct);
                                            }

                                            FinishFirmwareProgress(successLocal, successLocal
                                                ? $"Firmware {localFw.FileName} gravado e validado com sucesso na flash!"
                                                : "Transferência finalizada, mas o roteador não confirmou.");

                                            if (successLocal)
                                            {
                                                transferiuLocalmente = true;
                                                SetEtapa(2, $"✅ 2. Firmware — Atualizado via HUB USB ({localFw.FileName})", "#16A34A");
                                                LogAuto($"[✓] Firmware gravado com sucesso no roteador via HUB USB! Versão: {localFw.FileName}");
                                                // No HUB USB a porta serial nunca foi desconectada; mantém console aberto sem pedir reconexão!
                                            }
                                            else
                                            {
                                                LogAuto($"[!] Transferência via HUB USB não confirmada. Mantendo versão atual ({versaoExibida}).");
                                            }
                                        }
                                        catch (Exception exFw)
                                        {
                                            FinishFirmwareProgress(false, $"Erro na gravação: {exFw.Message}");
                                            LogAuto($"[!] Falha na gravação do firmware: {exFw.Message}");
                                        }
                                        finally
                                        {
                                            if (ethBoundLocal) ethMgr.UnbindProcessFromNetwork(LogAuto);
                                        }
                                    }
                                }
                            }

                            // CENÁRIO B: PORTA ÚNICA USB OU DOWNLOAD WAN
                            if (!transferiuLocalmente)
                            {
                                // Testa se a WAN do roteador tem internet para download direto
                                Progresso(38, "2/7 Verificando WAN para Upgrade...");
                                var wanDiag = await _connManager.CheckWanAndInternetAsync(detected.Series, s => { LogAuto(s); return Task.CompletedTask; }, ct);

                                if (wanDiag.IsPhysicalUp && wanDiag.HasInternet)
                                {
                                    LogAuto($"[*] Porta WAN ({wanDiag.InterfaceName}) UP e Internet OK. Disparando download direto do firmware homologado pelo roteador...");
                                    Progresso(40, "2/7 Gravando Firmware via WAN...");
                                    var url = RouterDirectFirmwareUpdater.BuildDirectDownloadUrl(detected.Series);
                                    bool ok = false;
                                    await _connManager.PauseReadLoopAsync();
                                    try
                                    {
                                        var sessWan = await _connManager.EnsureConsoleSessionAsync(s => { LogAuto(s); return Task.CompletedTask; }, ct);
                                        ok = await updater.TriggerDownloadOnRouterAsync(sessWan, detected.Series, url, officialRemote.FileName, ct);
                                    }
                                    finally
                                    {
                                        _connManager.ResumeReadLoop();
                                    }

                                    if (ok)
                                    {
                                        SetEtapa(2, $"✅ 2. Firmware — Atualizado para {officialRemote.FileName}", "#16A34A");
                                        LogAuto($"[✓] Firmware atualizado com sucesso no roteador via WAN!");
                                        transferiuLocalmente = true;
                                    }
                                    else
                                    {
                                        SetEtapa(2, $"⚠️ 2. Firmware — Falha download WAN ({versaoExibida})", "#CA8A04");
                                        LogAuto($"[!] Falha na transferência direta via WAN.");
                                    }
                                }

                                // Se WAN indisponível e celular tem porta única USB EXCLUSIVAMENTE (sem HUB Ethernet):
                                if (!transferiuLocalmente && localFw != null && File.Exists(localFw.LocalFilePath) && hasSerial && !isHubSimultaneous && !hasEthHw)
                                {
                                    LogAuto("\n>>> [ATUALIZAÇÃO EM BANCADA — PORTA ÚNICA USB]");
                                    LogAuto("[*] WAN indisponível e sem HUB Ethernet detectado. Solicitando troca física temporária de adaptadores.");
                                    var desejaSwap = await MainThread.InvokeOnMainThreadAsync(() =>
                                        DisplayAlert("🔄 Troca de Adaptador (Serial ➔ Ethernet)",
                                            $"Firmware desatualizado detectado ({versaoExibida}).\n" +
                                            $"A imagem homologada ({localFw.FileName}) está salva no celular.\n\n" +
                                            "Deseja atualizar via cabo de rede em bancada?\n" +
                                            "Como o celular possui uma única porta USB (sem HUB), faremos a troca temporária do cabo Serial pelo adaptador Ethernet.\n\n" +
                                            "O SPARC aplicará configuração de staging (IP 192.168.1.1 + DHCP + Telnet) no roteador antes de você desconectar a serial.",
                                            "SIM, FAZER TROCA", "DEIXAR PARA A WAN"));

                                    if (desejaSwap)
                                    {
                                        Progresso(40, "2/7 Aplicando Staging no Roteador...");
                                        await _connManager.ApplyTemporaryLanStagingAsync(detected.Manufacturer, detected.Series, null, s => { LogAuto(s); return Task.CompletedTask; }, ct);

                                        await MainThread.InvokeOnMainThreadAsync(() =>
                                            DisplayAlert("Troca de Adaptador",
                                                "1. Desconecte o cabo Console Serial da porta USB do celular.\n" +
                                                "2. Conecte o adaptador Ethernet USB ao celular (ligado à porta LAN do roteador).\n\n" +
                                                "Toque em OK após conectar o adaptador de rede.", "OK"));

                                        bool tentarSwapEth = true;
                                        while (tentarSwapEth && !ct.IsCancellationRequested)
                                        {
                                            tentarSwapEth = false;
                                            LogAuto("[*] Aguardando enlace Ethernet no smartphone (até 20s)...");
                                            for (int w = 0; w < 20 && !ct.IsCancellationRequested; w++)
                                            {
                                                await Task.Delay(1000, ct);
                                                if (ethMgr.IsEthernetConnected() && !string.IsNullOrWhiteSpace(ethMgr.GetEthernetIpAddress())) break;
                                            }

                                            hasEth = ethMgr.IsEthernetConnected();
                                            var ipEthAtual = ethMgr.GetEthernetIpAddress();

                                            if (!hasEth || string.IsNullOrWhiteSpace(ipEthAtual))
                                            {
                                                var hasHw = ethMgr.HasEthernetHardwareInterface();
                                                string detalheDiag = !hasHw
                                                    ? "O adaptador Ethernet USB não foi reconhecido pelo smartphone.\n\n• Verifique se o adaptador OTG/USB-C está bem encaixado na porta do celular."
                                                    : "⚠️ O adaptador USB foi reconhecido, porém a porta LAN do roteador permaneceu DESCONECTADA (sem link físico / elétrico)!\n\n" +
                                                      "Causas e Orientações Técnicas:\n" +
                                                      "• Conecte o cabo RJ45 na porta LAN correta (ex: GE0/0 ou GE0/1 no Cisco / HPE).\n" +
                                                      "• Verifique se os LEDs de LINK da porta Ethernet no roteador e no adaptador USB acenderam.\n" +
                                                      "• Verifique se o cabo de rede está íntegro e bem encaixado.\n" +
                                                      "• Se o roteador possuir mais de uma porta LAN, experimente conectar na outra porta (ex: alternar GE0/0 e GE0/1).";

                                                LogAuto("\n[ALERTA CRÍTICO] Interface Ethernet cabeada permaneceu DESCONECTADA!");
                                                LogAuto($"[!] {detalheDiag.Replace("\n", " ")}");

                                                var desejaReconferir = await MainThread.InvokeOnMainThreadAsync(() =>
                                                    DisplayAlert("⚠️ Porta LAN Desconectada / Sem Link Físico",
                                                        detalheDiag + "\n\nDeseja verificar os cabos e tentar novamente?",
                                                        "TENTAR NOVAMENTE", "CANCELAR ATUALIZAÇÃO BANCADA"));

                                                if (desejaReconferir)
                                                {
                                                    tentarSwapEth = true;
                                                    LogAuto("[*] Nova tentativa de detecção de enlace Ethernet solicitada pelo técnico...");
                                                    continue;
                                                }
                                                else
                                                {
                                                    LogAuto("[!] Atualização em bancada cancelada: LAN do roteador permaneceu desconectada.");
                                                    hasEth = false;
                                                    break;
                                                }
                                            }
                                        }

                                        if (!hasEth)
                                        {
                                            // Como o técnico desconectou a serial para o swap e a LAN não subiu,
                                            // DEVE solicitar a reconexão da serial para não quebrar as fases seguintes!
                                            if (!_connManager.HasSupportedSerialConnected())
                                            {
                                                await MainThread.InvokeOnMainThreadAsync(() =>
                                                    DisplayAlert("🔄 Reconecte o Cabo Serial",
                                                        "A atualização via cabo de rede não foi concluída pois a LAN permaneceu desconectada.\n\n" +
                                                        "Para prosseguir com o provisionamento via console:\n" +
                                                        "1. Desconecte o adaptador Ethernet do celular.\n" +
                                                        "2. Reconecte o cabo Console Serial USB ao smartphone.\n\n" +
                                                        "Toque em OK após reconectar o cabo serial.", "OK"));

                                                LogAuto("[*] Reconectando console serial USB para prosseguir com o provisionamento...");
                                                await _connManager.ConnectUsbAsync(null, 9600, s => LogAuto(s), ct);
                                            }
                                        }
                                        else
                                        {
                                            var ethIp = ethMgr.GetEthernetIpAddress() ?? circuit.HostLanIp ?? "192.168.1.2";
                                            LogAuto($"[*] Transferindo firmware localmente em bancada ({ethIp})...");
                                            Progresso(40, "2/7 Enviando Firmware via OTG/ETH...");

                                            ShowFirmwareProgress(
                                                "GRAVAÇÃO DE FIRMWARE EM BANCADA",
                                                localFw.FileName,
                                                "Flash do Roteador",
                                                isForti || detected.Manufacturer == DeviceManufacturer.Hpe ? "OTG + ETH (FTP :2121)" : "OTG + ETH (HTTP :8080)");

                                            var ethBoundLocal = ethMgr.BindProcessToEthernet(LogAuto);
                                            try
                                            {
                                                bool successLocal = false;
                                                if (isForti)
                                                {
                                                    successLocal = await _connManager.RestoreFirmwareFortiFtpAsync(
                                                        localFw.LocalFilePath, ethIp, "sparc", "claro123", msg => { LogAuto(msg); AppendFirmwareProgressLog(msg); return Task.CompletedTask; }, ct);
                                                }
                                                else if (detected.Manufacturer == DeviceManufacturer.Hpe)
                                                {
                                                    successLocal = await _connManager.UpgradeFirmwareHpeFtpAsync(
                                                        localFw.LocalFilePath, ethIp, "sparc", "claro123", msg => { LogAuto(msg); AppendFirmwareProgressLog(msg); return Task.CompletedTask; }, ct);
                                                }
                                                else
                                                {
                                                    var routerIp = circuit.LanIp ?? "192.168.1.1";
                                                    successLocal = await _connManager.UpgradeFirmwareCiscoHttpAsync(
                                                        localFw.LocalFilePath,
                                                        phoneIp: ethIp,
                                                        routerIp: routerIp,
                                                        telnetUser: "EBT",
                                                        telnetPass: "CQMR",
                                                        routerTempIp: circuit.LanIp ?? "192.168.1.1",
                                                        routerTempMask: circuit.LanSubnetMask ?? "255.255.255.0",
                                                        lanInterface: null,
                                                        expectedMd5: null,
                                                        progress: msg => { LogAuto(msg); AppendFirmwareProgressLog(msg); return Task.CompletedTask; },
                                                        ct: ct);
                                                }

                                                FinishFirmwareProgress(successLocal, successLocal
                                                    ? $"Firmware {localFw.FileName} gravado e validado com sucesso na flash!"
                                                    : "Transferência finalizada, mas o roteador não confirmou.");

                                                if (successLocal)
                                                {
                                                    transferiuLocalmente = true;
                                                    SetEtapa(2, $"✅ 2. Firmware — Atualizado via OTG/ETH ({localFw.FileName})", "#16A34A");
                                                    LogAuto($"[✓] Firmware gravado com sucesso no roteador via OTG/ETH local! Versão: {localFw.FileName}");
                                                }
                                                else
                                                {
                                                    LogAuto($"[!] Transferência via OTG/ETH não confirmada. Mantendo versão atual ({versaoExibida}).");
                                                }
                                            }
                                            catch (Exception exFw)
                                            {
                                                FinishFirmwareProgress(false, $"Erro na gravação: {exFw.Message}");
                                                LogAuto($"[!] Falha na gravação do firmware: {exFw.Message}");
                                            }
                                            finally
                                            {
                                                if (ethBoundLocal) ethMgr.UnbindProcessFromNetwork(LogAuto);

                                                // Sempre garante que a serial seja reconectada após o término do swap
                                                if (!_connManager.HasSupportedSerialConnected())
                                                {
                                                    await MainThread.InvokeOnMainThreadAsync(() =>
                                                        DisplayAlert("🔄 Reconectar Cabo Serial",
                                                            "A etapa de transferência via cabo de rede foi finalizada.\n\n" +
                                                            "Para continuar com o provisionamento da ficha SAIP:\n" +
                                                            "1. Desconecte o adaptador Ethernet do celular.\n" +
                                                            "2. Reconecte o cabo Console Serial USB ao smartphone.\n\n" +
                                                            "Toque em OK após reconectar a serial.", "OK"));

                                                    LogAuto("[*] Reconectando console serial USB...");
                                                    await _connManager.ConnectUsbAsync(null, 9600, s => LogAuto(s), ct);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            // Cenário C: Postergar atualização para quando a WAN estiver UP
                            if (!transferiuLocalmente)
                            {
                                _firmwarePostergado = true;
                                _postergadoSeries = detected.Series;
                                _postergadoRemote = officialRemote;
                                var remoteName = officialRemote?.FileName ?? "versão homologada";
                                LogAuto($"    ⚠️ O equipamento permanece na versão {versaoExibida}. A atualização para {remoteName} foi postergada para quando o link WAN for conectado.");
                                SetEtapa(2, $"⚠️ 2. Firmware — {versaoExibida} (Postergado p/ WAN)", "#CA8A04");
                            }
                        }
                    }
                else
                {
                    SetEtapa(2, "✅ 2. Firmware — OK (Validado)", "#16A34A");
                }
            }
            else
            {
                SetEtapa(2, "✅ 2. Firmware — mantido (opção do operador)", "#16A34A");
                LogAuto("\n>>> [AUTO 2/7] Atualização de firmware dispensada pelo operador.");
            }
            Progresso(45, "2/7 Concluído");

            // =====================================================================
            // FASE 3: PROVISIONAR EQUIPAMENTO (WAN, LAN, ROTAS, TELNET)
            // =====================================================================
            SetEtapa(3, "⏳ 3. Provisionar Equipamento — em execução", "#D97706");
            Progresso(50, "3/7 Provisionando Equipamento...");
            LogAuto("\n>>> [AUTO 3/7] Provisionamento do Circuito (WAN / LAN / Rotas / Telnet)");

            _connManager.IncluirNatLab = ChkNatLab.IsChecked;
            if (_connManager.IncluirNatLab)
            {
                LogAuto("[*] Opção NAT LAB habilitada: configurações de bancada/modem 4G serão aplicadas.");
            }

            await _connManager.ApplyProvisioningAsync(circuit, msg =>
            {
                LogAuto(msg);
                return Task.CompletedTask;
            }, ct);

            SetEtapa(3, "✅ 3. Provisionar Equipamento — OK", "#16A34A");
            Progresso(65, "3/7 Provisionamento Concluído");

            // =====================================================================
            // TRANSIÇÃO DE ADAPTADOR: CONSOLE USB ➔ ETHERNET OTG (RJ45)
            // =====================================================================
            var ethManager = AndroidEthernetManager.Instance;
            bool pularTestesRede = false;

            if (!ethManager.IsEthernetConnected())
            {
                LogAuto("\n>>> [TRANSIÇÃO DE INTERFACE] Interface Ethernet cabeada não detectada.");
                LogAuto("[*] Para prosseguir com testes de rede IP e Telnet, conecte o cabo RJ45 ao HUB USB-C ou substitua pelo adaptador Ethernet...");

                // Solicita a troca de adaptador ao técnico (mantendo serial ativa caso ele prefira pular)
                var trocou = await SolicitarTrocaAdaptadorAsync(circuit, ct);
                if (!trocou)
                {
                    pularTestesRede = true;
                    LogAuto("[!] Operador optou por dispensar os testes cabeados. Mantendo console serial ativo.");
                }
                else
                {
                    // Desconecta a porta serial preventivamente apenas se não houver HUB USB-C com serial ainda conectada
                    if (!_connManager.HasSupportedSerialConnected())
                    {
                        await _connManager.DisconnectUsbOnlyAsync();
                        LogAuto("[*] Conexão serial USB desconectada para uso exclusivo do adaptador Ethernet.");
                    }
                    else
                    {
                        LogAuto("[✓] HUB USB-C detectado: Console serial e interface Ethernet operando simultaneamente!");
                    }
                    LogAuto("[✓] Interface Ethernet conectada e validada pelo técnico!");
                }
            }
            else
            {
                LogAuto("\n[✓] Interface Ethernet OTG já conectada (HUB USB-C ativo)! Prosseguindo sem necessidade de troca de cabo.");
            }

            string icmpLan = "PULADO";
            string icmpWan = "PULADO";
            string icmpWeb = "PULADO";

            if (!pularTestesRede)
            {
                // =====================================================================
                // FASE 4: CONFIGURAR IP DE TESTE (HOST LAN + DNS & ETHERNET OTG BINDING)
                // =====================================================================
                SetEtapa(4, "⏳ 4. Configurar IP de Teste — em execução", "#D97706");
                Progresso(70, "4/7 Configurando IP de Teste...");
                LogAuto("\n>>> [AUTO 4/7] Configuração do Dispositivo de Teste (Host LAN & Ethernet OTG)");

                var hostIp = circuit.HostLanIp ?? "200.182.245.18";
                var lanMask = circuit.LanSubnetMask ?? "255.255.255.240";
                var lanGw = circuit.LanIp ?? "200.182.245.17";

                LogAuto($"[*] Host LAN Calculado : {hostIp}");
                LogAuto($"[*] Máscara de Rede    : {lanMask}");
                LogAuto($"[*] Gateway Roteador   : {lanGw}");
                LogAuto($"[*] Servidores DNS     : 1.1.1.1, 8.8.8.8");

                // Detecção e Isolamento de Rede via OTG + Ethernet
                var hasEth = ethManager.IsEthernetConnected();
                var ethIp = ethManager.GetEthernetIpAddress();

                if (hasEth && string.IsNullOrWhiteSpace(ethIp))
                {
                    LogAuto("[*] Adaptador Ethernet detectado. Aguardando concessão de IP via DHCP pelo roteador...");
                    for (int w = 0; w < 6 && string.IsNullOrWhiteSpace(ethIp) && !ct.IsCancellationRequested; w++)
                    {
                        await Task.Delay(500, ct);
                        ethIp = ethManager.GetEthernetIpAddress();
                    }
                }

                if (hasEth)
                {
                    LogAuto($"[✓] Adaptador Ethernet OTG cabeado operacional!");
                    if (!string.IsNullOrWhiteSpace(ethIp))
                    {
                        LogAuto($"[✓] IP da interface Ethernet no Android: {ethIp}");
                    }
                    else
                    {
                        LogAuto($"[!] Interface Ethernet sem IP estático detectado. IP requerido no Android: {hostIp} | Máscara: {lanMask}.");
                    }
                }
                else
                {
                    LogAuto("[!] Interface Ethernet física não detectada. Testes tentarão prosseguir pela pilha de rede ativa.");
                }

                // Amarra o processo do aplicativo exclusivamente à interface Ethernet
                ethBound = ethManager.BindProcessToEthernet(LogAuto);

                SetEtapa(4, $"✅ 4. IP de Teste — OK (Host: {hostIp})", "#16A34A");
                Progresso(75, "4/7 IP de Teste Definido");

                // =====================================================================
                // FASE 5: TESTAR CONECTIVIDADE ICMP (5a LAN, 5b WAN, 5c WEB)
                // =====================================================================
                SetEtapa(5, "⏳ 5. Testar Conectividade (ICMP) — em execução", "#D97706");
                Progresso(80, "5/7 Testando Conectividade ICMP...");
                LogAuto("\n>>> [AUTO 5/7] Teste ICMP Triplo (5a LAN, 5b WAN, 5c WEB)");

                _lastIcmpResult = await ExecutarTesteIcmpTriploAsync(circuit, ct);
                icmpLan = _lastIcmpResult.IsLanOk ? "OK" : "❌";
                icmpWan = _lastIcmpResult.IsWanOk ? "OK" : "❌";
                icmpWeb = _lastIcmpResult.IsWebOk ? "OK" : "❌";

                var allIcmpOk = _lastIcmpResult.IsLanOk && _lastIcmpResult.IsWanOk;
                var icmpColor = allIcmpOk ? "#16A34A" : "#EF4444";
                SetEtapa(5, $"{(allIcmpOk ? "✅" : "⚠️")} 5. ICMP: 5a LAN ({icmpLan}) | 5b WAN ({icmpWan}) | 5c WEB ({icmpWeb})", icmpColor);
                Progresso(85, "5/7 ICMP Concluído");

                // =====================================================================
                // FASE 6: TESTAR ACESSO REMOTO (TELNET / SSH) E MIGRAÇÃO DE SESSÃO
                // =====================================================================
                var accessTitle = isForti ? "Acesso Remoto (SSH/HTTPS)" : "Acesso Remoto (Telnet)";
                SetEtapa(6, $"⏳ 6. Testar {accessTitle} — em execução", "#D97706");
                Progresso(88, "6/7 Testando Acesso Remoto...");
                LogAuto($"\n>>> [AUTO 6/7] Validação de {accessTitle} e Migração de Sessão CLI");

                var telnetTarget = circuit.LanIp ?? "200.182.245.17";
                if (isForti)
                {
                    // No FortiOS Telnet vem desabilitado: testa SSH :22 ou HTTPS :443
                    var srv = new ConnectivityService(msg => { LogAuto(msg); return Task.CompletedTask; });
                    var (sshOk, sshLat, _) = await srv.TestTcpPortAsync(telnetTarget, 22, timeoutMs: 4000, cancellationToken: ct);
                    if (sshOk)
                    {
                        _lastTelnetResult = new ConnectivityService.TelnetTestResult(telnetTarget, 22, true, sshLat, "SSH Ativo", "OK", null);
                        SetEtapa(6, "✅ 6. Acesso Remoto (SSH) — OK", "#16A34A");
                        LogAuto($"[✓] Acesso remoto SSH (porta 22) respondendo ({sshLat}ms)!");
                    }
                    else
                    {
                        var (httpsOk, httpsLat, _) = await srv.TestTcpPortAsync(telnetTarget, 443, timeoutMs: 4000, cancellationToken: ct);
                        _lastTelnetResult = new ConnectivityService.TelnetTestResult(telnetTarget, 443, httpsOk, httpsLat, httpsOk ? "HTTPS Ativo" : "Falha", "OK", null);
                        SetEtapa(6, httpsOk ? "✅ 6. Acesso Remoto (HTTPS) — OK" : "⚠️ 6. Acesso Remoto — sem resposta", httpsOk ? "#16A34A" : "#FBBF24");
                    }
                }
                else
                {
                    try
                    {
                        // Cisco/HPE/Huawei/Datacom: Estabelece sessão Telnet e migra Terminal para Telnet
                        var isHpeDev = _connManager.LastDetectionResult?.Manufacturer == DeviceManufacturer.Hpe;
                        var defaultTelnetPass = isHpeDev ? "PRO1ANPRO1AN" : "CQMR";
                        var telnetOk = await _connManager.SwitchToTelnetSessionAsync(telnetTarget, 23, "EBT", defaultTelnetPass, LogAuto, ct);
                        _lastTelnetResult = new ConnectivityService.TelnetTestResult(telnetTarget, 23, telnetOk, 10, telnetOk ? "Telnet Conectado" : "Falha", "OK", null);
                        SetEtapa(6, telnetOk ? $"✅ 6. Telnet (TCP 23 EBT/{defaultTelnetPass}) — OK (Migrado)" : "⚠️ 6. Telnet — sem resposta (Cabo LAN ou IP pendente)", telnetOk ? "#16A34A" : "#FBBF24");
                    }
                    catch (Exception exTel)
                    {
                        LogAuto($"[!] Acesso Telnet indisponível: {exTel.Message}");
                        _lastTelnetResult = new ConnectivityService.TelnetTestResult(telnetTarget, 23, false, 0, "Inacessível", exTel.Message, null);
                        SetEtapa(6, "⚠️ 6. Telnet — sem resposta", "#FBBF24");
                    }
                }
                Progresso(92, "6/7 Acesso Remoto Concluído");

                // =====================================================================
                // FASE 7: TESTAR BANDA (VAZÃO HTTP VS NOMINAL SAIP)
                // =====================================================================
                if (_lastIcmpResult != null && (!_lastIcmpResult.IsWanOk || !_lastIcmpResult.IsWebOk))
                {
                    SetEtapa(7, "⏭ 7. Testar Banda — descartado (WAN sem resposta)", "#64748B");
                    LogAuto("\n>>> [AUTO 7/7] Teste de banda descartado: Gateway WAN/Internet sem resposta ICMP.");
                    _lastBandResult = new BandwidthTestResult(0, 0, 0, 0, "Nativo HTTP", "Descartado", false, "WAN offline");
                }
                else
                {
                    SetEtapa(7, "⏳ 7. Testar Banda — em execução", "#D97706");
                    Progresso(95, "7/7 Testando Largura de Banda...");
                    LogAuto("\n>>> [AUTO 7/7] Medição de Vazão HTTP contra CDN Neutra");

                    var bandSvc = new BandwidthTestService(msg => { LogAuto(msg); return Task.CompletedTask; });
                    _lastBandResult = await bandSvc.RunNativeHttpSpeedTestAsync(
                        testPayloadMegaBytes: 25,
                        sourceIpAddress: null,
                        onProgress: null,
                        cancellationToken: ct);

                    var aval = BandwidthTestService.AvaliarBanda(circuit.BandaMbpsNominal, _lastBandResult.DownloadMbps);
                    var bandOk = _lastBandResult.IsSuccess && (aval == null || aval.Aprovado);
                    var bandColor = bandOk ? "#16A34A" : "#D97706";
                    SetEtapa(7, $"{(bandOk ? "✅" : "⚠️")} 7. Testar Banda — {_lastBandResult.DownloadMbps:F1} Mbps ({aval?.Veredito ?? "Medido"})", bandColor);
                    LogAuto($"[✓] Resultado de Banda: Download {_lastBandResult.DownloadMbps:F1} Mbps | Latência {_lastBandResult.LatencyMs:F0}ms | Jitter {_lastBandResult.JitterMs:F1}ms");
                    if (aval != null)
                    {
                        LogAuto($"[*] Crítica Nominal SAIP: {aval.Veredito}");
                    }
                }
            }
            else
            {
                // Etapas 4 a 7 dispensadas por opção do técnico
                SetEtapa(4, "⏭ 4. IP de Teste — pulado (Sem Adaptador Ethernet)", "#64748B");
                SetEtapa(5, "⏭ 5. Conectividade ICMP — pulado (Sem Adaptador Ethernet)", "#64748B");
                SetEtapa(6, "⏭ 6. Acesso Remoto — pulado (Sem Adaptador Ethernet)", "#64748B");
                SetEtapa(7, "⏭ 7. Teste de Banda — pulado (Sem Adaptador Ethernet)", "#64748B");
                LogAuto("\n>>> Fases 4 a 7 descartadas. Provisionamento básico mantido com sucesso.");
            }

            Progresso(100, "Provisionamento Finalizado!");
            LogAuto("\n=================================================================");
            LogAuto("   ✅ PROVISIONAMENTO CONCLUÍDO COM SUCESSO!                    ");
            LogAuto("=================================================================");

            BtnAutoCancelar.IsVisible = false;
            BtnAutoVoltar.IsVisible = true;
            BtnAutoCompartilharRelatorio.IsVisible = true;
            BtnAutoCertidaoY1564.IsVisible = true;
            BtnAutoVerScript.IsVisible = true;
            BtnAutoRetestarBanda.IsVisible = true;

            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(500)); } catch { }

            await DisplayAlert("Provisionamento Concluído",
                $"Esteira executada com sucesso para o circuito '{circuit.DesignacaoIp ?? circuit.ClienteRazaoSocial}'!\n\n" +
                $"• Provisionamento: OK\n" +
                $"• ICMP LAN: {icmpLan} | WAN: {icmpWan} | WEB: {icmpWeb}\n" +
                $"• Banda: {_lastBandResult?.DownloadMbps:F1} Mbps\n\n" +
                $"O servidor DHCP temporário de testes será removido do roteador, restaurando a configuração 100% estática.\n\n" +
                $"Toque em 'Compartilhar Relatório' para gerar o documento técnico de ativação.",
                "OK");

            // Remoção do pool DHCP temporário: restaura a configuração 100% estática do roteador
            var sessaoAtiva = _connManager.TelnetSession ?? _connManager.CurrentSession;
            if (!isForti && sessaoAtiva != null)
            {
                LogAuto("\n[*] Removendo servidor DHCP temporário da LAN e garantindo configuração 100% estática na NVRAM...");
                await CiscoSaipConfigurator.RemoverDhcpTemporarioAsync(sessaoAtiva, circuit, LogAuto, CancellationToken.None);
            }

            // Se a atualização de firmware foi postergada na Fase 2 e a WAN agora está online (ICMP WAN ou WEB OK)
            if (_firmwarePostergado && _postergadoRemote != null && (_lastIcmpResult?.IsWanOk == true || _lastIcmpResult?.IsWebOk == true))
            {
                LogAuto("\n>>> [GATILHO WAN ATIVA] Conectividade WAN confirmada! A atualização de firmware homologado que havia sido postergada pode ser realizada agora.");
                var atualizarWanAgora = await MainThread.InvokeOnMainThreadAsync(() =>
                    DisplayAlert("🚀 Link WAN Ativo Detectado",
                        $"A porta WAN está comunicando com a rede!\n\n" +
                        $"Deseja disparar a atualização do firmware homologado ({_postergadoRemote.FileName}) diretamente pelo roteador agora?",
                        "SIM, ATUALIZAR VIA WAN", "MANTER VERSÃO ATUAL"));

                if (atualizarWanAgora)
                {
                    LogAuto($"[*] Disparando download direto do firmware homologado pelo roteador via WAN...");
                    var url = RouterDirectFirmwareUpdater.BuildDirectDownloadUrl(_postergadoSeries);
                    var targetSession = _connManager.TelnetSession ?? _connManager.CurrentSession;
                    if (targetSession != null)
                    {
                        ShowFirmwareProgress(
                            "DOWNLOAD DE FIRMWARE VIA WAN",
                            _postergadoRemote.FileName,
                            "Flash do Roteador",
                            "Link WAN / HTTP Direto");

                        var wanUpdater = new RouterDirectFirmwareUpdater(msg =>
                        {
                            LogAuto(msg);
                            AppendFirmwareProgressLog(msg);
                            return Task.CompletedTask;
                        });

                        try
                        {
                            var okFwWan = await wanUpdater.TriggerDownloadOnRouterAsync(targetSession, _postergadoSeries, url, _postergadoRemote.FileName, ct);
                            FinishFirmwareProgress(okFwWan, okFwWan
                                ? $"Download WAN finalizado e gravado na flash: {_postergadoRemote.FileName}!"
                                : "Falha no download direto via WAN.");

                            if (okFwWan)
                            {
                                SetEtapa(2, $"✅ 2. Firmware — Atualizado via WAN ({_postergadoRemote.FileName})", "#16A34A");
                                LogAuto("[✓] Firmware atualizado com sucesso no roteador via link WAN!");
                                _firmwarePostergado = false;
                            }
                            else
                            {
                                LogAuto("[!] Falha no download direto via WAN. O equipamento mantém a versão atual.");
                            }
                        }
                        catch (Exception exWanFw)
                        {
                            FinishFirmwareProgress(false, $"Erro na transferência WAN: {exWanFw.Message}");
                            LogAuto($"[!] Erro no download via WAN: {exWanFw.Message}");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogAuto("\n[!] Provisionamento cancelado pelo operador.");
            Progresso(0, "Cancelado");
            BtnAutoCancelar.IsVisible = false;
            BtnAutoVoltar.IsVisible = true;
        }
        catch (Exception ex)
        {
            LogAuto($"\n[ERRO AUTO] {ex.Message}");
            Progresso(0, $"Falha: {ex.Message}");
            BtnAutoCancelar.IsVisible = false;
            BtnAutoVoltar.IsVisible = true;
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(250)); } catch { }
            await DisplayAlert("Erro na Execução", ex.Message, "OK");
        }
        finally
        {
            DeviceDisplay.Current.KeepScreenOn = false;
            if (ethBound)
            {
                AndroidEthernetManager.Instance.UnbindProcessFromNetwork(LogAuto);
            }
        }
    }

    private async Task<TripleIcmpData> ExecutarTesteIcmpTriploAsync(SaipCircuitData saip, CancellationToken ct)
    {
        var srv = new ConnectivityService(msg =>
        {
            MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
            return Task.CompletedTask;
        });

        // 5a LAN
        var lanTarget = saip.LanIp ?? "200.182.245.17";
        AppendLog($"[*] 5a: Testando ICMP para Interface LAN ({lanTarget})...");
        var lanRes = await srv.TestPingAsync(lanTarget, count: 4, timeoutMs: 2000, cancellationToken: ct);

        // 5b WAN
        var wanTarget = saip.WanGateway ?? "201.90.204.21";
        AppendLog($"[*] 5b: Testando ICMP para Gateway WAN ({wanTarget})...");
        var wanRes = await srv.TestPingAsync(wanTarget, count: 4, timeoutMs: 2500, cancellationToken: ct);

        // 5c WEB
        AppendLog("[*] 5c: Testando ICMP para DNS Web (1.1.1.1)...");
        var webRes = await srv.TestPingAsync("1.1.1.1", count: 4, timeoutMs: 2500, cancellationToken: ct);

        return new TripleIcmpData(lanRes, wanRes, webRes);
    }

    private TaskCompletionSource<bool>? _trocaEthTcs;

    private async Task<bool> SolicitarTrocaAdaptadorAsync(SaipCircuitData circuit, CancellationToken ct)
    {
        _trocaEthTcs = new TaskCompletionSource<bool>();
        using var reg = ct.Register(() => _trocaEthTcs.TrySetResult(false));

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var hostIp = !string.IsNullOrWhiteSpace(circuit.HostLanIp) 
                    ? circuit.HostLanIp 
                    : IpCalculator.CalculateHostLanIp(circuit.LanIp, circuit.LanCidr);
                LblTrocaEthIpInfo.Text = $"IP Host Previsto: {hostIp} | Gateway (Roteador): {circuit.LanIp}";
                LblTrocaEthMaskInfo.Text = $"Máscara: {circuit.LanSubnetMask} (/{circuit.LanCidr}) • DHCP Temporário Ativo no Roteador";
            }
            catch
            {
                LblTrocaEthIpInfo.Text = $"Gateway (Roteador): {circuit.LanIp}";
                LblTrocaEthMaskInfo.Text = $"Máscara: {circuit.LanSubnetMask} • DHCP Temporário Ativo";
            }

            LblStatusTrocaEth.Text = "Aguardando conexão do adaptador Ethernet RJ45...";
            LblStatusTrocaEth.TextColor = Color.FromArgb("#38BDF8");
            IndicatorTrocaEth.IsRunning = true;
            IndicatorTrocaEth.IsVisible = true;
            FrameTrocaAdaptador.IsVisible = true;
        });

        // Polling de detecção em background (600ms)
        _ = Task.Run(async () =>
        {
            var ethManager = AndroidEthernetManager.Instance;
            while (!_trocaEthTcs.Task.IsCompleted && !ct.IsCancellationRequested)
            {
                await Task.Delay(600, ct);
                if (ethManager.IsEthernetConnected())
                {
                    var ip = ethManager.GetEthernetIpAddress();
                    if (!string.IsNullOrWhiteSpace(ip))
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            LblStatusTrocaEth.Text = $"✅ IP {ip} obtido via DHCP! Iniciando testes...";
                            LblStatusTrocaEth.TextColor = Color.FromArgb("#4ADE80");
                            IndicatorTrocaEth.IsRunning = false;
                            IndicatorTrocaEth.IsVisible = false;
                        });

                        // Intervalo para estabilização do link de rede no Android
                        await Task.Delay(1200, ct);
                        _trocaEthTcs.TrySetResult(true);
                        break;
                    }
                    else
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            LblStatusTrocaEth.Text = "⏳ Cabo Ethernet conectado! Solicitando IP via DHCP do roteador...";
                            LblStatusTrocaEth.TextColor = Color.FromArgb("#38BDF8");
                        });
                    }
                }
                else
                {
                    var hasHw = ethManager.HasEthernetHardwareInterface();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (hasHw)
                        {
                            LblStatusTrocaEth.Text = "⚠️ Adaptador USB plugado, mas LAN do roteador SEM LINK! Verifique cabo RJ45 e porta...";
                            LblStatusTrocaEth.TextColor = Color.FromArgb("#F87171");
                        }
                        else
                        {
                            LblStatusTrocaEth.Text = "Aguardando conexão do adaptador Ethernet RJ45...";
                            LblStatusTrocaEth.TextColor = Color.FromArgb("#38BDF8");
                        }
                    });
                }
            }
        }, ct);

        var resultado = await _trocaEthTcs.Task;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            FrameTrocaAdaptador.IsVisible = false;
        });

        return resultado;
    }

    private async void OnCopiarParametrosIpClicked(object? sender, EventArgs e)
    {
        if (_connManager.LoadedCircuit == null) return;
        var circuit = _connManager.LoadedCircuit;
        var hostIp = !string.IsNullOrWhiteSpace(circuit.HostLanIp)
            ? circuit.HostLanIp
            : IpCalculator.CalculateHostLanIp(circuit.LanIp, circuit.LanCidr);

        var texto = $"IP: {hostIp}\n" +
                    $"Máscara: {circuit.LanSubnetMask}\n" +
                    $"Prefixo: {circuit.LanCidr}\n" +
                    $"Gateway: {circuit.LanIp}\n" +
                    $"DNS: 1.1.1.1";

        await Clipboard.Default.SetTextAsync(texto);

        if (sender is Button btn)
        {
            var oldText = btn.Text;
            btn.Text = "✅ Dados Copiados!";
            btn.BackgroundColor = Color.FromArgb("#16A34A");
            await Task.Delay(2000);
            btn.Text = oldText;
            btn.BackgroundColor = Color.FromArgb("#334155");
        }
    }

    private async void OnAbrirConfigEthernetClicked(object? sender, EventArgs e)
    {
        await AndroidEthernetManager.Instance.OpenEthernetSettingsAsync();
    }

    private async void OnTrocaEthConfirmarClicked(object? sender, EventArgs e)
    {
        if (!AndroidEthernetManager.Instance.IsEthernetConnected())
        {
            await DisplayAlert("⚠️ Porta LAN Desconectada",
                "A interface Ethernet cabeada está sem link físico com o roteador.\n\n" +
                "• Verifique se o cabo RJ45 está conectado à porta LAN do roteador (ex: GE0/0 ou GE0/1).\n" +
                "• Verifique se os LEDs de LINK da porta Ethernet no roteador e no adaptador acenderam.", "OK");
            return;
        }
        _trocaEthTcs?.TrySetResult(true);
    }

    private async void OnTrocaEthPularClicked(object? sender, EventArgs e)
    {
        if (!_connManager.HasSupportedSerialConnected())
        {
            await DisplayAlert("🔄 Reconectar Cabo Serial",
                "Você optou por pular os testes Ethernet.\n\n" +
                "Se você desconectou a serial para ligar o adaptador de rede, reconecte o cabo Console Serial USB ao smartphone caso queira continuar operando o console.", "OK");
        }
        _trocaEthTcs?.TrySetResult(false);
    }

    private void OnAutoCancelarClicked(object? sender, EventArgs e)
    {
        _trocaEthTcs?.TrySetResult(false);
        _autoCts?.Cancel();
    }

    private void OnAutoVoltarClicked(object? sender, EventArgs e)
    {
        ViewModoAutomatico.IsVisible = false;
        ViewTelaInicial.IsVisible = true;
    }

    private async void OnAutoCompartilharRelatorioClicked(object? sender, EventArgs e)
    {
        if (_connManager.LoadedCircuit == null) return;
        var circuit = _connManager.LoadedCircuit;

        try
        {
            var detected = _connManager.LastDetectionResult;
            var reportData = new ActivationReportData(
                DataHora: DateTime.Now,
                ModeloEquipamento: detected?.Series.ToString() ?? ModelPicker.SelectedItem?.ToString() ?? "Cisco / HPE",
                PortaSerial: "USB OTG",
                BaudRate: (_connManager.CurrentTransport as AndroidUsbSerialTransport)?.BaudRate ?? 9600,
                ClienteRazaoSocial: circuit.ClienteRazaoSocial,
                DesignacaoIp: circuit.DesignacaoIp,
                NumeroOts: circuit.NumeroOts,
                PeRouter: circuit.PeRouter,
                WanIp: circuit.WanIp,
                WanCidr: circuit.WanCidr,
                WanGateway: circuit.WanGateway,
                WanSubnetMask: circuit.WanSubnetMask,
                WanInterface: "GigabitEthernet 0/0",
                LanIp: circuit.LanIp,
                LanCidr: circuit.LanCidr,
                LanBlockNetwork: circuit.LanBlockNetwork,
                LanSubnetMask: circuit.LanSubnetMask,
                HostLanIp: circuit.HostLanIp,
                LanInterface: "GigabitEthernet 0/1",
                Step1ZerarOk: true,
                Step2FirmwareOk: true,
                FirmwareNome: "Homologado",
                Step3SaipOk: true,
                Step4IpLocalOk: true,
                AdaptadorRedeLocal: "Ethernet OTG",
                IcmpResult: _lastIcmpResult,
                TelnetResult: _lastTelnetResult,
                BandResult: _lastBandResult,
                DiagnosticAlerts: new List<string>(),
                FalhaGeral: null,
                AppliedConfigScript: _connManager.LastAppliedConfig,
                BandaMbpsNominal: circuit.BandaMbpsNominal);

            var html = ActivationPdfReportService.GenerateHtml(reportData);
            var pdfPath = await ReportsStorageService.Instance.SaveReportAsync($"Relatorio_SPARC_{circuit.DesignacaoIp ?? "Circuito"}", html, ".html", "Ativação SAIP");

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Relatório SPARC - {circuit.DesignacaoIp ?? "Circuito"}",
                File = new ShareFile(pdfPath)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro no Relatório", ex.Message, "OK");
        }
    }

    private async void OnAutoVerScriptClicked(object? sender, EventArgs e)
    {
        var script = _connManager.LastAppliedConfig;
        if (string.IsNullOrWhiteSpace(script))
        {
            await DisplayAlert("Script de Configuração", "Nenhum script capturado nesta sessão.", "OK");
            return;
        }

        await DisplayAlert("Script Aplicado (Running-Config)",
            script.Length > 2000 ? script.Substring(0, 2000) + "\n\n[... Truncado para exibição ...]" : script,
            "FECHAR");
    }

    private async void OnAutoRetestarBandaClicked(object? sender, EventArgs e)
    {
        if (_connManager.LoadedCircuit == null) return;
        BtnAutoRetestarBanda.IsEnabled = false;
        AppendLog("\n[*] Re-testando largura de banda...");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var bandSvc = new BandwidthTestService(msg => { AppendLog(msg); return Task.CompletedTask; });
            _lastBandResult = await bandSvc.RunNativeHttpSpeedTestAsync(
                testPayloadMegaBytes: 25,
                sourceIpAddress: null,
                onProgress: null,
                cancellationToken: cts.Token);

            var aval = BandwidthTestService.AvaliarBanda(_connManager.LoadedCircuit.BandaMbpsNominal, _lastBandResult.DownloadMbps);
            var bandOk = _lastBandResult.IsSuccess && (aval == null || aval.Aprovado);
            LblAutoEtapa7.Text = $"{(bandOk ? "✅" : "⚠️")} 7. Testar Banda — {_lastBandResult.DownloadMbps:F1} Mbps ({aval?.Veredito ?? "Medido"})";
            LblAutoEtapa7.TextColor = Color.FromArgb(bandOk ? "#16A34A" : "#D97706");
            AppendLog($"[✓] Re-teste de Banda: {_lastBandResult.DownloadMbps:F1} Mbps ({aval?.Veredito ?? "OK"})");
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Falha no re-teste de banda: {ex.Message}");
        }
        finally
        {
            BtnAutoRetestarBanda.IsEnabled = true;
        }
    }

    private async void OnAbrirTerminalClicked(object? sender, EventArgs e)
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("//TerminalPage");
        }
    }

    private async void OnAbrirRecuperacaoClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new RecoveryPage());
    }

    private async void OnAbrirAnalisadorClicked(object? sender, EventArgs e)
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("//Y1564Page");
        }
        else
        {
            await Navigation.PushAsync(new Y1564Page());
        }
    }

    private async void OnAutoCertidaoY1564Clicked(object? sender, EventArgs e)
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("//Y1564Page");
        }
        else
        {
            await Navigation.PushAsync(new Y1564Page());
        }
    }

    #endregion

    private async void OnNavigateHistoricoClicked(object? sender, EventArgs e)
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("ReportsHistoryPage");
        }
        else
        {
            await Navigation.PushAsync(new ReportsHistoryPage());
        }
    }

    private async void OnNavigateActivationClicked(object? sender, EventArgs e)
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("ActivationPage");
        }
        else
        {
            await Navigation.PushAsync(new ActivationPage());
        }
    }

    private async void OnCheckAppUpdateClicked(object? sender, EventArgs e)
    {
        try
        {
            var updateService = new NetworkDevice.Core.Firmware.SparcAppUpdateService();
            var (hasUpdate, release, msg) = await updateService.CheckForUpdateAsync(AppInfo.Current.VersionString, "android");

            if (hasUpdate && release != null)
            {
                await Services.AndroidAppUpdater.DownloadAndInstallAsync(this, release);
            }
            else
            {
                await DisplayAlert("SPARC Mobile Atualizado", msg, "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Verificação OTA", $"Não foi possível verificar atualizações: {ex.Message}", "OK");
        }
    }

    private void AppendLog(string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TxtAutoLog.Text += $"\n{message}";
            // Mantém tamanho gerenciável no buffer
            if (TxtAutoLog.Text.Length > 20000)
            {
                TxtAutoLog.Text = TxtAutoLog.Text.Substring(TxtAutoLog.Text.Length - 10000);
            }
        });
    }

    #region Overlay de Progresso de Firmware em Primeiro Plano

    public void ShowFirmwareProgress(string titulo, string fileName, string target, string method)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { DeviceDisplay.Current.KeepScreenOn = true; } catch { }
            OverlayFirmwareProgress.IsVisible = true;
            FwProgressSpinner.IsRunning = true;
            FwProgressTitleLabel.Text = titulo;
            FwProgressTitleLabel.TextColor = Color.FromArgb("#38BDF8");
            FwProgressFileNameLabel.Text = fileName;
            FwProgressTargetLabel.Text = target;
            FwProgressMethodLabel.Text = method;
            FwProgressStageLabel.Text = "Iniciando transferência...";
            FwProgressPercentLabel.Text = "0%";
            FwProgressBar.Progress = 0.0;
            FwProgressDetailLabel.Text = "Estabelecendo comunicação com o roteador...";
            FwProgressBytesLabel.Text = "0 / 0 MB";
            FwProgressLogEditor.Text = $"[*] {DateTime.Now:HH:mm:ss} - Iniciando gravação de {fileName}...\n";
            FwProgressDismissBtn.IsVisible = false;
        });
    }

    public void UpdateFirmwareProgress(double percentage, string stage, string details, string bytesText)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!OverlayFirmwareProgress.IsVisible) OverlayFirmwareProgress.IsVisible = true;
            var clamped = Math.Clamp(percentage / 100.0, 0.0, 1.0);
            FwProgressBar.Progress = clamped;
            FwProgressPercentLabel.Text = $"{percentage:F0}%";
            if (!string.IsNullOrWhiteSpace(stage)) FwProgressStageLabel.Text = stage;
            if (!string.IsNullOrWhiteSpace(details)) FwProgressDetailLabel.Text = details;
            if (!string.IsNullOrWhiteSpace(bytesText)) FwProgressBytesLabel.Text = bytesText;
            AppendFirmwareProgressLog($"[{percentage:F0}%] {stage} - {details}");
        });
    }

    public void AppendFirmwareProgressLog(string line)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var txt = FwProgressLogEditor.Text ?? "";
                if (txt.Length > 2500) txt = txt.Substring(txt.Length - 1500);
                FwProgressLogEditor.Text = txt + line + "\n";
            }
            catch { }
        });
    }

    public void FinishFirmwareProgress(bool success, string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { DeviceDisplay.Current.KeepScreenOn = false; } catch { }
            FwProgressSpinner.IsRunning = false;
            if (success)
            {
                FwProgressTitleLabel.Text = "✅ FIRMWARE GRAVADO COM SUCESSO!";
                FwProgressTitleLabel.TextColor = Color.FromArgb("#4ADE80");
                FwProgressBar.Progress = 1.0;
                FwProgressPercentLabel.Text = "100%";
                FwProgressStageLabel.Text = "Operação Finalizada com Sucesso!";
                FwProgressDetailLabel.Text = message;
                FwProgressDismissBtn.Text = "CONCLUIR";
                FwProgressDismissBtn.BackgroundColor = Color.FromArgb("#16A34A");
                FwProgressDismissBtn.IsVisible = true;
                AppendFirmwareProgressLog($"[✓] {message}");
            }
            else
            {
                FwProgressTitleLabel.Text = "❌ FALHA NA ATUALIZAÇÃO";
                FwProgressTitleLabel.TextColor = Color.FromArgb("#EF4444");
                FwProgressStageLabel.Text = "Erro durante gravação do firmware";
                FwProgressDetailLabel.Text = message;
                FwProgressDismissBtn.Text = "FECHAR";
                FwProgressDismissBtn.BackgroundColor = Color.FromArgb("#B91C1C");
                FwProgressDismissBtn.IsVisible = true;
                AppendFirmwareProgressLog($"[!] {message}");
            }
        });
    }

    private void OnDismissFirmwareProgressClicked(object? sender, EventArgs e)
    {
        OverlayFirmwareProgress.IsVisible = false;
        try { DeviceDisplay.Current.KeepScreenOn = false; } catch { }
    }

    #endregion
}
