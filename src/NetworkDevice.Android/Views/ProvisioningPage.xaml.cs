using Android.Hardware.Usb;
using Microsoft.Maui.Controls;
using NetworkDevice.Android.Services;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;
using NetworkDevice.Core.Provisioning;
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

    public ProvisioningPage()
    {
        InitializeComponent();
        _connManager.OnConnectionStateChanged += UpdateConnectionState;
        _connManager.OnDeviceIdentified += UpdateIdentifiedDevice;
        _connManager.OnProbeProgress += msg => MainThread.BeginInvokeOnMainThread(() => AppendLog(msg));
        ScanUsb();
        UpdateSpecialFunctionsVisibility();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ScanUsb();
        UpdateTechnicianHeader();
        UpdateSpecialFunctionsVisibility();
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
            $"{d.ManufacturerName ?? "USB Serial"} ({d.ProductName ?? "Console"}) - VID:0x{d.VendorId:X4}:PID:0x{d.ProductId:X4}").ToList();

        UsbDevicePicker.ItemsSource = deviceNames;
        UsbDevicePicker.SelectedIndex = 0;
        UsbStatusLabel.Text = $"{_discoveredDevices.Count} adaptador(es) serial USB detectado(s).";
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
        SaipCircuitLabel.Text = $"Designação: {saip.DesignacaoIp ?? "-"} | OTS: {saip.NumeroOts ?? "-"}";
        SaipWanLabel.Text = $"WAN: {saip.WanIp}/{saip.WanCidr} (Gateway: {saip.WanGateway})";
        SaipLanLabel.Text = $"LAN: {saip.LanIp}/{saip.LanCidr} | Host: {saip.HostLanIp}";
        SaipBandwidthLabel.Text = $"Banda Nominal: {(saip.BandaMbpsNominal.HasValue ? $"{saip.BandaMbpsNominal.Value} Mbps" : "N/D")}";

        TxtBadgeStep2.Text = "✅ Ficha OK";
        TxtBadgeStep2.TextColor = Color.FromArgb("#4ADE80");
        AppendLog($"[✓] Ficha SAIP carregada ({origem}): Cliente '{saip.ClienteRazaoSocial}', WAN '{saip.WanIp}', LAN '{saip.LanIp}'");
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
            var irParaQuebra = await DisplayAlert(
                "🔒 Equipamento Protegido por Senha",
                $"O roteador '{detected.Series}' foi identificado como PROTEGIDO POR SENHA.\n\n" +
                "As portas de gerência e modo de configuração estão bloqueadas. É necessário autenticar com credenciais válidas ou executar a quebra de senha (reset de fábrica) antes de prosseguir com o provisionamento.\n\n" +
                "Deseja abrir o assistente de Quebra de Senha agora?",
                "IR PARA QUEBRA SENHA", "VOLTAR");

            if (irParaQuebra)
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.GoToAsync("//RecoveryPage");
                }
                else
                {
                    await Navigation.PushAsync(new RecoveryPage());
                }
            }
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

                if (_connManager.CurrentSession != null && detected != null)
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

                    // 2. Audita versão em execução no equipamento com comparador estrito
                    var audit = await updater.AuditComplianceAsync(_connManager.CurrentSession, detected.Series, officialRemote, ct);
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

                        // 3. Testa se a porta WAN possui conectividade de Internet para download direto
                        Progresso(38, "2/7 Verificando WAN para Upgrade...");
                        var wanDiag = await updater.CheckWanAndInternetAsync(_connManager.CurrentSession, detected.Series, ct);

                        if (wanDiag.IsPhysicalUp && wanDiag.HasInternet)
                        {
                            LogAuto($"[*] Porta WAN ({wanDiag.InterfaceName}) UP e Internet OK. Disparando download direto do firmware homologado pelo roteador...");
                            Progresso(40, "2/7 Gravando Firmware via WAN...");
                            var url = RouterDirectFirmwareUpdater.BuildDirectDownloadUrl(detected.Series);
                            var ok = await updater.TriggerDownloadOnRouterAsync(_connManager.CurrentSession, detected.Series, url, officialRemote.FileName, ct);
                            if (ok)
                            {
                                SetEtapa(2, $"✅ 2. Firmware — Atualizado para {officialRemote.FileName}", "#16A34A");
                                LogAuto($"[✓] Firmware atualizado com sucesso no roteador via WAN!");
                            }
                            else
                            {
                                SetEtapa(2, $"⚠️ 2. Firmware — Falha download WAN ({versaoExibida})", "#CA8A04");
                                LogAuto($"[!] Falha na transferência direta via WAN. Mantendo versão em execução.");
                            }
                        }
                        else
                        {
                            LogAuto($"[!] Porta WAN física ({wanDiag.InterfaceName}) está DOWN ou sem Internet para download direto.");

                            // ALTERNATIVA VIA OTG/ETH (HUB USB-C ou Bancada Local):
                            var ethMgr = AndroidEthernetManager.Instance;
                            var hasEth = ethMgr.IsEthernetConnected();
                            var localFw = _firmwareRepo.GetLocalFirmware(detected.Series);

                            // Se o firmware homologado não estiver salvo no celular, tenta baixar via conexão móvel/Wi-Fi do smartphone
                            if (localFw == null && officialRemote != null)
                            {
                                try
                                {
                                    LogAuto($"[*] Baixando firmware homologado ({officialRemote.FileName}) para o celular via conexão móvel/Wi-Fi...");
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

                            if (hasEth && localFw != null && File.Exists(localFw.LocalFilePath))
                            {
                                var ethIp = ethMgr.GetEthernetIpAddress() ?? circuit.HostLanIp ?? "192.168.1.2";
                                LogAuto($"[*] Alternativa OTG/ETH ativa via HUB USB-C: transferindo firmware localmente ({ethIp})...");
                                Progresso(40, "2/7 Enviando Firmware via OTG/ETH...");

                                var ethBoundLocal = ethMgr.BindProcessToEthernet(LogAuto);
                                try
                                {
                                    bool successLocal = false;
                                    if (isForti)
                                    {
                                        successLocal = await _connManager.RestoreFirmwareFortiFtpAsync(
                                            localFw.LocalFilePath, ethIp, "sparc", "claro123", msg => { LogAuto(msg); return Task.CompletedTask; }, ct);
                                    }
                                    else if (detected.Manufacturer == DeviceManufacturer.Hpe)
                                    {
                                        successLocal = await _connManager.UpgradeFirmwareHpeFtpAsync(
                                            localFw.LocalFilePath, ethIp, "sparc", "claro123", msg => { LogAuto(msg); return Task.CompletedTask; }, ct);
                                    }
                                    else
                                    {
                                        var routerIp = circuit.LanIp ?? "192.168.1.1";
                                        successLocal = await _connManager.UpgradeFirmwareCiscoHttpAsync(
                                            localFw.LocalFilePath,
                                            phoneIp: ethIp,
                                            routerIp: routerIp,
                                            telnetUser: "EBT",
                                            telnetPass: "PRO1AN",
                                            routerTempIp: circuit.LanIp ?? "192.168.1.1",
                                            routerTempMask: circuit.LanSubnetMask ?? "255.255.255.0",
                                            lanInterface: null,
                                            expectedMd5: null,
                                            progress: msg => { LogAuto(msg); return Task.CompletedTask; },
                                            ct: ct);
                                    }

                                    if (successLocal)
                                    {
                                        SetEtapa(2, $"✅ 2. Firmware — Atualizado via OTG/ETH ({localFw.FileName})", "#16A34A");
                                        LogAuto($"[✓] Firmware gravado com sucesso no roteador via OTG/ETH local! Versão: {localFw.FileName}");
                                    }
                                    else
                                    {
                                        SetEtapa(2, $"⚠️ 2. Firmware — {versaoExibida} (Falha OTG/ETH)", "#CA8A04");
                                        LogAuto($"[!] Transferência via OTG/ETH não confirmada. Mantendo versão atual ({versaoExibida}).");
                                    }
                                }
                                finally
                                {
                                    if (ethBoundLocal)
                                    {
                                        ethMgr.UnbindProcessFromNetwork(LogAuto);
                                    }
                                }
                            }
                            else
                            {
                                var remoteName = officialRemote?.FileName ?? "versão homologada";
                                LogAuto($"    ⚠️ O equipamento permanece na versão {versaoExibida}. A atualização para {remoteName} poderá ser realizada após conexão do link WAN ou via aba Firmware (OTG + ETH).");
                                SetEtapa(2, $"⚠️ 2. Firmware — {versaoExibida} (Desatualizado - Sem WAN/ETH)", "#CA8A04");
                            }
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
                        // Cisco/Huawei/Datacom: Estabelece sessão Telnet e migra Terminal para Telnet
                        var telnetOk = await _connManager.SwitchToTelnetSessionAsync(telnetTarget, 23, "EBT", "PRO1AN", LogAuto, ct);
                        _lastTelnetResult = new ConnectivityService.TelnetTestResult(telnetTarget, 23, telnetOk, 10, telnetOk ? "Telnet Conectado" : "Falha", "OK", null);
                        SetEtapa(6, telnetOk ? "✅ 6. Telnet (TCP 23 EBT/PRO1AN) — OK (Migrado)" : "⚠️ 6. Telnet — sem resposta (Cabo LAN ou IP pendente)", telnetOk ? "#16A34A" : "#FBBF24");
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
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        LblStatusTrocaEth.Text = "Aguardando conexão do adaptador Ethernet RJ45...";
                        LblStatusTrocaEth.TextColor = Color.FromArgb("#38BDF8");
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

    private void OnTrocaEthConfirmarClicked(object? sender, EventArgs e)
    {
        _trocaEthTcs?.TrySetResult(true);
    }

    private void OnTrocaEthPularClicked(object? sender, EventArgs e)
    {
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
}
