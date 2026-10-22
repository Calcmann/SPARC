using Microsoft.Maui.Controls;
using NetworkDevice.Android.Services;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Device;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;
using NetworkDevice.Fortinet;
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
    private DigitalLoopbackStats? _lastStats;

    public Y1564Page()
    {
        InitializeComponent();
        ModePicker.SelectedIndex = 0; // 0: Refletor MTS-5800 (Padrão)
        ServicePicker.SelectedIndex = 0; // BLD
        TierPicker.SelectedIndex = 0; // <= 1200 Km
        DurationPicker.SelectedIndex = 1; // 5 Minutos (Recomendado)
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadCircuitInputs();
        CheckEthernetStatus();
        DeviceAdaptationLabel.Text = SmartphoneModelAdapter.Instance.CurrentProfile.SummaryBadge;
    }

    private async void OnDeviceDetailsClicked(object? sender, EventArgs e)
    {
        await SmartphoneModelAdapter.Instance.ShowHardwareDiagnosticsAsync(this);
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
            BadgeSaipStatus.BackgroundColor = Color.FromArgb("#064E3B");
            TxtSaipStatus.Text = "✓ SAIP Carregada";

            var cliente = SaipParser.CleanRazaoSocial(saip.ClienteRazaoSocial);
            TxtClienteNome.Text = string.IsNullOrWhiteSpace(cliente) ? "Cliente Homologação" : cliente;

            var desig = saip.DesignacaoIp ?? saip.NumeroOts ?? "Circuito SAIP";
            TxtDesignacao.Text = desig;

            double cir = saip.BandaMbpsNominal ?? 0;
            if (cir <= 0 && saip.BandaKbps.HasValue && saip.BandaKbps.Value > 0)
            {
                cir = saip.BandaKbps.Value / 1000.0;
            }
            if (cir <= 0) cir = 100.0;

            TxtVelocidadeCir.Text = $"{cir:F0} Mbps";
            BandwidthEntry.Text = $"{cir:F0}";

            var gw = saip.WanGateway;
            if (string.IsNullOrWhiteSpace(gw) || gw == "0.0.0.0")
            {
                gw = saip.PeLoopbackIp ?? "201.90.204.21";
            }
            TxtDestinoIp.Text = gw;
            RemoteIpEntry.Text = gw;

            // IP do QT-600 Alvo (NOC)
            if (TxtIpQt600Alvo != null)
            {
                TxtIpQt600Alvo.Text = saip.PeLoopbackIp ?? saip.WanGateway ?? gw;
            }

            // Host LAN Homologado (SAIP)
            if (TxtHostLanIp != null)
            {
                TxtHostLanIp.Text = !string.IsNullOrWhiteSpace(saip.HostLanIp)
                    ? $"{saip.HostLanIp} (/{saip.LanCidr})"
                    : (!string.IsNullOrWhiteSpace(saip.LanIp) ? $"{saip.LanIp}/{saip.LanCidr}" : "—");
            }

            // Auto-seleciona o serviço oficial
            if (saip.TipoServico == SaipServiceType.IpVpn)
            {
                ServicePicker.SelectedIndex = 1; // MPLS
            }
            else if (saip.TipoServico == SaipServiceType.VozDigitalE1)
            {
                ServicePicker.SelectedIndex = 2; // VOZ Digital E1
            }
            else if (saip.TipoServico == SaipServiceType.VozSipIp)
            {
                ServicePicker.SelectedIndex = 3; // VOZ SIP / PABX IP
            }
            else if (saip.Produto?.Contains("VOZ", StringComparison.OrdinalIgnoreCase) == true ||
                     saip.Produto?.Contains("SIP", StringComparison.OrdinalIgnoreCase) == true)
            {
                ServicePicker.SelectedIndex = 3; // VOZ SIP
            }
            else
            {
                ServicePicker.SelectedIndex = 0; // BLD
            }
        }
        else
        {
            BadgeSaipStatus.BackgroundColor = Color.FromArgb("#7F1D1D");
            TxtSaipStatus.Text = "⚠️ Sem SAIP";
            TxtClienteNome.Text = "Cliente Homologação";
            TxtDesignacao.Text = "Circuito";
            TxtVelocidadeCir.Text = "100 Mbps (Padrão)";
            TxtDestinoIp.Text = "201.90.204.21";
            RemoteIpEntry.Text = "201.90.204.21";
            BandwidthEntry.Text = "100";
            if (TxtIpQt600Alvo != null) TxtIpQt600Alvo.Text = "10.129.241.146";
            if (TxtHostLanIp != null) TxtHostLanIp.Text = "192.168.1.10 (/24)";
        }

        DetectarMacDispositivo();
    }

    private void DetectarMacDispositivo()
    {
        if (TxtMacDispositivo == null) return;
        try
        {
            var mac = ObterMacDispositivoLocal();
            TxtMacDispositivo.Text = mac;
        }
        catch
        {
            TxtMacDispositivo.Text = "02:00:00:00:00:00";
        }
    }

    private static string ObterMacDispositivoLocal()
    {
        try
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                            n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .ToList();

            var eth = interfaces.FirstOrDefault(i => i.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet);
            if (eth != null)
            {
                var bytes = eth.GetPhysicalAddress().GetAddressBytes();
                if (bytes.Length == 6)
                    return string.Join(":", bytes.Select(b => b.ToString("X2")));
            }

            var any = interfaces.FirstOrDefault();
            if (any != null)
            {
                var bytes = any.GetPhysicalAddress().GetAddressBytes();
                if (bytes.Length == 6)
                    return string.Join(":", bytes.Select(b => b.ToString("X2")));
            }
        }
        catch { }
        return "00:E0:4C:68:01:23";
    }

    private async void OnCopiarMacClicked(object? sender, EventArgs e)
    {
        var mac = TxtMacDispositivo?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(mac))
        {
            await Clipboard.Default.SetTextAsync(mac);
            await DisplayAlert("MAC Copiado", $"Endereço MAC copiado:\n{mac}", "OK");
        }
    }

    private void CheckEthernetStatus()
    {
        var ethManager = AndroidEthernetManager.Instance;
        var hasEth = ethManager.IsEthernetConnected();
        var ethIp = ethManager.GetEthernetIpAddress();

        if (hasEth)
        {
            if (string.IsNullOrWhiteSpace(ethIp))
            {
                EthStatusBadge.Text = "⚠️ ETH UP (Sem IP)";
                EthStatusBadge.TextColor = Color.FromArgb("#F59E0B");
                EthDetailsLabel.Text = "Cabo conectado, mas sem IP. Toque em '⚡ Ativar DHCP' abaixo.";
                if (TxtOtgIp != null)
                {
                    TxtOtgIp.Text = "Sem IP (DHCP Pendente)";
                    TxtOtgIp.TextColor = Color.FromArgb("#F59E0B");
                }
                if (BadgeDhcpStatus != null) BadgeDhcpStatus.BackgroundColor = Color.FromArgb("#78350F");
                if (TxtDhcpStatus != null)
                {
                    TxtDhcpStatus.Text = "Sem IP";
                    TxtDhcpStatus.TextColor = Color.FromArgb("#FBBF24");
                }
                if (TxtDhcpLog != null && (string.IsNullOrWhiteSpace(TxtDhcpLog.Text?.Trim()) || TxtDhcpLog.Text.StartsWith("💡")))
                {
                    TxtDhcpLog.Text = "⚠️ Interface OTG conectada sem IP. Toque em '⚡ Ativar DHCP' para configurar o roteador via Serial.";
                }
            }
            else
            {
                EthStatusBadge.Text = $"🔌 ETH UP ({ethIp})";
                EthStatusBadge.TextColor = Color.FromArgb("#4ADE80");
                EthDetailsLabel.Text = "Adaptador OTG pronto para isolamento do teste Y.1564.";
                if (TxtOtgIp != null)
                {
                    TxtOtgIp.Text = ethIp;
                    TxtOtgIp.TextColor = Color.FromArgb("#4ADE80");
                }
                if (BadgeDhcpStatus != null) BadgeDhcpStatus.BackgroundColor = Color.FromArgb("#065F46");
                if (TxtDhcpStatus != null)
                {
                    TxtDhcpStatus.Text = "IP Concedido";
                    TxtDhcpStatus.TextColor = Color.FromArgb("#34D399");
                }
            }
        }
        else
        {
            EthStatusBadge.Text = "❌ ETH Desconectada";
            EthStatusBadge.TextColor = Color.FromArgb("#EF4444");
            EthDetailsLabel.Text = "Recomendado conectar o cabo Ethernet OTG ao roteador antes de testar.";
            if (TxtOtgIp != null)
            {
                TxtOtgIp.Text = "Desconectado";
                TxtOtgIp.TextColor = Color.FromArgb("#EF4444");
            }
            if (BadgeDhcpStatus != null) BadgeDhcpStatus.BackgroundColor = Color.FromArgb("#1E293B");
            if (TxtDhcpStatus != null)
            {
                TxtDhcpStatus.Text = "Desconectado";
                TxtDhcpStatus.TextColor = Color.FromArgb("#94A3B8");
            }
        }
    }

    private async void OnAtivarDhcpClicked(object? sender, EventArgs e)
    {
        await GerenciarDhcpRoteadorAsync(ativar: true);
    }

    private async void OnRemoverDhcpClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlert(
            "Remover DHCP da LAN?",
            "Deseja remover o servidor DHCP temporário da LAN do roteador e regravação 100% estática na NVRAM?\n\n" +
            "• Use esta opção após concluir a Certidão de Nascimento para entregar o roteador homologado à Claro/Embratel.",
            "SIM, REMOVER DHCP", "CANCELAR");

        if (confirm)
        {
            await GerenciarDhcpRoteadorAsync(ativar: false);
        }
    }

    private async void OnRenovarIpClicked(object? sender, EventArgs e)
    {
        CheckEthernetStatus();
        var ethManager = AndroidEthernetManager.Instance;
        var ethIp = ethManager.GetEthernetIpAddress();
        if (!string.IsNullOrWhiteSpace(ethIp))
        {
            ethManager.BindProcessToEthernet(AppendLog);
            if (TxtDhcpLog != null) TxtDhcpLog.Text = $"[✓] IP Ethernet verificado e ativo: {ethIp}";
            await DisplayAlert("IP Ethernet Ativo", $"A interface OTG possui o endereço IP:\n\n{ethIp}\n\nO tráfego de teste está vinculado à Ethernet.", "OK");
        }
        else
        {
            if (TxtDhcpLog != null) TxtDhcpLog.Text = "[*] Sondando concessão de lease DHCP do roteador...";
            await AguardarIpEthernetAsync(8);
        }
    }

    private async Task GerenciarDhcpRoteadorAsync(bool ativar)
    {
        var circuit = _connManager.LoadedCircuit;
        if (circuit == null)
        {
            await DisplayAlert("Ficha SAIP Obrigatória",
                "Não há Ficha SAIP carregada para obter os dados de LAN do roteador.\nCarregue a SAIP na tela de provisionamento antes de gerenciar o DHCP.",
                "OK");
            return;
        }

        if (BtnAtivarDhcp != null) BtnAtivarDhcp.IsEnabled = false;
        if (BtnRemoverDhcp != null) BtnRemoverDhcp.IsEnabled = false;
        if (BtnRenovarIp != null) BtnRenovarIp.IsEnabled = false;

        try
        {
            // 1. Garante sessão de comunicação com o roteador (Serial ou Telnet)
            DeviceSession? session = _connManager.TelnetSession ?? _connManager.CurrentSession;
            if (session == null || !_connManager.IsConnected)
            {
                if (!_connManager.HasSupportedSerialConnected())
                {
                    await DisplayAlert("Console Serial Desconectado",
                        "Nenhum adaptador serial USB homologado foi detectado no barramento OTG.\n\n" +
                        "Conecte o cabo de console Serial ao smartphone para gerenciar o DHCP do roteador.",
                        "OK");
                    return;
                }

                if (TxtDhcpLog != null) TxtDhcpLog.Text = "[*] Conectando ao console Serial do roteador (9600 bps)...";
                await _connManager.ConnectUsbAsync(null, 9600, msg => MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (TxtDhcpLog != null) TxtDhcpLog.Text = msg;
                }));
                session = _connManager.CurrentSession;
            }

            if (session == null)
            {
                if (TxtDhcpLog != null) TxtDhcpLog.Text = "[ERRO] Não foi possível estabelecer sessão com o roteador.";
                await DisplayAlert("Erro de Conexão", "Não foi possível abrir a sessão de console com o roteador.", "OK");
                return;
            }

            var isSerial = session == _connManager.CurrentSession;
            var isHpe = _connManager.LastDetectionResult?.Manufacturer == DeviceManufacturer.Hpe;
            var isForti = _connManager.LastDetectionResult?.Manufacturer == DeviceManufacturer.Fortinet;

            if (isSerial) await _connManager.PauseReadLoopAsync();
            try
            {
                if (ativar)
                {
                    if (TxtDhcpLog != null) TxtDhcpLog.Text = "[*] Configurando pool DHCP temporário na LAN do roteador...";
                    Action<string> logger = s => MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (TxtDhcpLog != null) TxtDhcpLog.Text = s;
                        AppendLog(s);
                    });

                    if (isForti)
                    {
                        await NetworkDevice.Fortinet.FortiOsSaipConfigurator.AtivarDhcpTemporarioAsync(session, circuit, "internal", logger, CancellationToken.None);
                    }
                    else if (isHpe)
                    {
                        await HpeSaipConfigurator.AtivarDhcpTemporarioAsync(session, circuit, logger, CancellationToken.None);
                    }
                    else
                    {
                        await CiscoSaipConfigurator.AtivarDhcpTemporarioAsync(session, circuit, logger, CancellationToken.None);
                    }

                    if (BadgeDhcpStatus != null) BadgeDhcpStatus.BackgroundColor = Color.FromArgb("#065F46");
                    if (TxtDhcpStatus != null)
                    {
                        TxtDhcpStatus.Text = "DHCP Ativo";
                        TxtDhcpStatus.TextColor = Color.FromArgb("#34D399");
                    }

                    // Aguarda a concessão do lease pelo Android
                    await AguardarIpEthernetAsync(15);
                }
                else
                {
                    if (TxtDhcpLog != null) TxtDhcpLog.Text = "[*] Removendo DHCP do roteador e regravando NVRAM 100% estática...";
                    Action<string> logger = s => MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (TxtDhcpLog != null) TxtDhcpLog.Text = s;
                        AppendLog(s);
                    });

                    if (isForti)
                    {
                        await NetworkDevice.Fortinet.FortiOsSaipConfigurator.RemoverDhcpTemporarioAsync(session, circuit, logger, CancellationToken.None);
                    }
                    else if (isHpe)
                    {
                        await HpeSaipConfigurator.RemoverDhcpTemporarioAsync(session, circuit, logger, CancellationToken.None);
                    }
                    else
                    {
                        await CiscoSaipConfigurator.RemoverDhcpTemporarioAsync(session, circuit, logger, CancellationToken.None);
                    }

                    if (BadgeDhcpStatus != null) BadgeDhcpStatus.BackgroundColor = Color.FromArgb("#7F1D1D");
                    if (TxtDhcpStatus != null)
                    {
                        TxtDhcpStatus.Text = "DHCP Removido";
                        TxtDhcpStatus.TextColor = Color.FromArgb("#FCA5A5");
                    }
                    if (TxtDhcpLog != null) TxtDhcpLog.Text = "[✓] DHCP removido! Roteador salvo 100% estático para entrega homologada.";
                    await DisplayAlert("DHCP Removido", "Servidor DHCP removido e configuração salva 100% estática na NVRAM!", "OK");
                }
            }
            finally
            {
                if (isSerial) _connManager.ResumeReadLoop();
            }
        }
        catch (Exception ex)
        {
            if (TxtDhcpLog != null) TxtDhcpLog.Text = $"[ERRO] {ex.Message}";
            await DisplayAlert("Erro DHCP", $"Falha ao comunicar com o roteador: {ex.Message}", "OK");
        }
        finally
        {
            if (BtnAtivarDhcp != null) BtnAtivarDhcp.IsEnabled = true;
            if (BtnRemoverDhcp != null) BtnRemoverDhcp.IsEnabled = true;
            if (BtnRenovarIp != null) BtnRenovarIp.IsEnabled = true;
            CheckEthernetStatus();
        }
    }

    private async Task AguardarIpEthernetAsync(int maxSeconds)
    {
        var ethManager = AndroidEthernetManager.Instance;
        for (int i = 1; i <= maxSeconds; i++)
        {
            await Task.Delay(1000);
            var ip = ethManager.GetEthernetIpAddress();
            if (!string.IsNullOrWhiteSpace(ip))
            {
                CheckEthernetStatus();
                ethManager.BindProcessToEthernet(AppendLog);
                if (TxtDhcpLog != null) TxtDhcpLog.Text = $"[✓] Conectado! IP Ethernet obtido com sucesso: {ip}";
                try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(200)); } catch { }
                await DisplayAlert("IP Obtido com Sucesso",
                    $"Interface OTG Ethernet recebeu IP do roteador:\n\n" +
                    $"• IP do Smartphone: {ip}\n" +
                    $"• Gateway: {ethManager.GetEthernetGatewayAddress() ?? _connManager.LoadedCircuit?.LanIp ?? "OK"}\n\n" +
                    "O SPARC está pronto para o teste oficial com o MTS-5800 / QT-600!",
                    "OK");
                return;
            }

            if (TxtDhcpLog != null) TxtDhcpLog.Text = $"[*] Aguardando lease DHCP do roteador no celular ({i}/{maxSeconds}s)...";
        }

        CheckEthernetStatus();
        var hostIp = _connManager.LoadedCircuit?.HostLanIp ?? "IP Fixo";
        if (TxtDhcpLog != null) TxtDhcpLog.Text = $"[!] Nenhum lease DHCP concedido após {maxSeconds}s. Alternativa: configure IP Fixo ({hostIp}) no Android.";
        var configFixo = await DisplayAlert("DHCP Pendente no Android",
            $"O roteador recebeu a configuração de DHCP, mas o smartphone ainda não renovou o lease automático.\n\n" +
            $"• Host Homologado na SAIP: {hostIp}\n" +
            $"• Máscara: {_connManager.LoadedCircuit?.LanSubnetMask ?? "255.255.255.0"}\n" +
            $"• Gateway: {_connManager.LoadedCircuit?.LanIp ?? "—"}\n\n" +
            "Deseja abrir as Configurações de Rede do Android para aplicar IP Fixo manual ou reconectar o cabo?",
            "Configurar IP Estático", "Aguardar Mais");

        if (configFixo)
        {
            if (!string.IsNullOrWhiteSpace(hostIp))
            {
                try { await Clipboard.Default.SetTextAsync(hostIp); } catch { }
            }
            await ethManager.OpenEthernetSettingsAsync();
        }
    }

    private async void OnConfigEthernetClicked(object? sender, EventArgs e)
    {
        var saip = _connManager.LoadedCircuit;
        var ethManager = AndroidEthernetManager.Instance;

        var action = await DisplayActionSheet(
            "🌐 Conexão Ethernet (OTG)",
            "Fechar",
            null,
            "⚡ Ativar DHCP Temporário no Roteador (Serial)",
            "🚫 Remover DHCP do Roteador (Entrega Estática)",
            "🔄 Renovar Lease / Forçar DHCP",
            "📋 Copiar IP Fixo Homologado (SAIP)",
            "⚙️ Abrir Configurações de Rede do Android",
            "📋 Ver Parâmetros do Circuito");

        if (action == "⚡ Ativar DHCP Temporário no Roteador (Serial)")
        {
            await GerenciarDhcpRoteadorAsync(ativar: true);
        }
        else if (action == "🚫 Remover DHCP do Roteador (Entrega Estática)")
        {
            OnRemoverDhcpClicked(sender, e);
        }
        else if (action == "🔄 Renovar Lease / Forçar DHCP")
        {
            OnRenovarIpClicked(sender, e);
        }
        else if (action == "📋 Copiar IP Fixo Homologado (SAIP)" && saip != null)
        {
            var ipFixo = saip.HostLanIp ?? saip.LanIp ?? "";
            if (!string.IsNullOrWhiteSpace(ipFixo))
            {
                await Clipboard.Default.SetTextAsync(ipFixo);
                await DisplayAlert("IP Copiado", $"Endereço IP copiado para a área de transferência:\n\n• IP: {ipFixo}\n• Máscara: {saip.LanSubnetMask}\n• Gateway: {saip.LanIp}", "OK");
            }
        }
        else if (action == "⚙️ Abrir Configurações de Rede do Android")
        {
            await ethManager.OpenEthernetSettingsAsync();
        }
        else if (action == "📋 Ver Parâmetros do Circuito" && saip != null)
        {
            await DisplayAlert("Parâmetros do Circuito",
                $"• WAN: {saip.WanIp}/{saip.WanCidr}\n" +
                $"• Gateway PE: {saip.WanGateway}\n" +
                $"• LAN: {saip.LanIp}/{saip.LanCidr}\n" +
                $"• Host LAN: {saip.HostLanIp}\n\n" +
                "ℹ️ O SPARC opera com DHCP automático na LAN.",
                "OK");
        }
    }



    private void OnModePickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (ModePicker == null) return;
        var isRefletor = ModePicker.SelectedIndex == 0;

        if (isRefletor)
        {
            LayoutModoRefletor.IsVisible = true;
            LayoutModoMaster.IsVisible = false;

            LossTitleLabel.Text = "Pacotes Refletidos";
            DelayTitleLabel.Text = "Central QT-600:";
            JitterTitleLabel.Text = "Taxa PPS";

            if (_localReflector?.IsRunning == true)
            {
                VerdictBorder.BackgroundColor = Color.FromArgb("#059669");
                VerdictLabel.Text = "🟢 REFLETOR ATIVO";
            }
            else
            {
                VerdictBorder.BackgroundColor = Color.FromArgb("#334155");
                VerdictLabel.Text = "⚪ AGUARDANDO REFLETOR";
                ProgressTimerLabel.Text = "Tempo: 00:00 (Indefinido)";
            }
        }
        else
        {
            LayoutModoRefletor.IsVisible = false;
            LayoutModoMaster.IsVisible = true;

            LossTitleLabel.Text = "Perda (FLR)";
            DelayTitleLabel.Text = "Delay Médio (FTD)";
            JitterTitleLabel.Text = "Jitter Médio (IFDV)";

            VerdictBorder.BackgroundColor = Color.FromArgb("#334155");
            VerdictLabel.Text = "⚪ PRONTO PARA TESTE";
            ProgressTimerLabel.Text = "Tempo: 00:00 / 05:00";
        }
    }

    private void OnServicePickerSelectedIndexChanged(object? sender, EventArgs e)
    {
        // Feedback visual se necessário
    }

    #region Modo Refletor MTS-5800 (Padrão Central QT-600)

    private async void OnToggleReflectorClicked(object? sender, EventArgs e)
    {
        if (_localReflector?.IsRunning == true)
        {
            StopLocalReflector();
        }
        else
        {
            var ethIp = AndroidEthernetManager.Instance.GetEthernetIpAddress();
            if (string.IsNullOrWhiteSpace(ethIp))
            {
                var ativar = await DisplayAlert(
                    "⚠️ Interface Ethernet Sem IP",
                    "A interface Ethernet OTG do smartphone está conectada, mas não possui endereço IP (DHCP pendente).\n\n" +
                    "Sem IP, o smartphone NÃO responderá ao tráfego do MTS-5800 / QT-600.\n\n" +
                    "Deseja ATIVAR O DHCP no roteador agora via Serial antes de ligar o refletor?",
                    "⚡ ATIVAR DHCP NO ROTEADOR", "CANCELAR");

                if (ativar)
                {
                    await GerenciarDhcpRoteadorAsync(ativar: true);
                    ethIp = AndroidEthernetManager.Instance.GetEthernetIpAddress();
                    if (string.IsNullOrWhiteSpace(ethIp))
                    {
                        var forcar = await DisplayAlert("Aviso Conectividade",
                            "O roteador foi configurado, mas o Android ainda não exibiu IP. Deseja ligar o refletor mesmo assim?",
                            "LIGAR ASSIM MESMO", "CANCELAR");
                        if (!forcar) return;
                    }
                }
                else
                {
                    return;
                }
            }

            StartLocalReflector();
        }
    }

    private void StartLocalReflector()
    {
        try
        {
            var ethBound = AndroidEthernetManager.Instance.BindProcessToEthernet(AppendLog);
            DeviceDisplay.Current.KeepScreenOn = true;
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(200)); } catch { }

            _localReflector = new DigitalLoopbackService();
            _localReflector.StatsUpdated += stats =>
            {
                _lastStats = stats;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ThroughputLabel.Text = $"{stats.CurrentRxMbps:F1} / {stats.CurrentTxMbps:F1} Mbps";
                    LossLabel.Text = $"{stats.TotalPacketsTx:N0} pkts";
                    DelayLabel.Text = !string.IsNullOrEmpty(stats.LastRemoteEndPoint) ? stats.LastRemoteEndPoint : "Aguardando";
                    JitterLabel.Text = $"{stats.CurrentRxPps:N0} PPS";
                    ProgressTimerLabel.Text = $"Tempo Decorrido: {stats.Elapsed:mm\\:ss} (Indefinido - QT-600)";
                    VerdictBorder.BackgroundColor = Color.FromArgb("#059669");
                    VerdictLabel.Text = "🟢 REFLETOR ATIVO";
                });
            };

            _localReflector.LogMessage += msg => AppendLog(msg);
            var profile = SmartphoneModelAdapter.Instance.CurrentProfile;
            _localReflector.Start(port: 5001, bufferSizeBytes: profile.RecommendedUdpBufferSizeBytes);

            ToggleReflectorBtn.Text = "🛑 DESLIGAR REFLETOR (PORTA 5001 ATIVA)";
            ToggleReflectorBtn.BackgroundColor = Color.FromArgb("#DC2626");
            VerdictBorder.BackgroundColor = Color.FromArgb("#059669");
            VerdictLabel.Text = "🟢 REFLETOR ATIVO (MTS-5800)";
            ProgressTimerLabel.Text = "Tempo: 00:00 (Indefinido - QT-600)";

            AppendLog("=================================================================");
            AppendLog("   [MTS-5800] REFLETOR DIGITAL ATIVO NA PORTA UDP 5001 (LLB)    ");
            AppendLog("=================================================================");
            AppendLog($"[*] Smartphone        : {profile.FriendlyModelName} ({profile.TotalRamFormatted} RAM, {profile.CpuCores} núcleos)");
            AppendLog($"[*] Buffer UDP Otimiz.: {profile.RecommendedUdpBufferSizeBytes / (1024 * 1024)} MB ({profile.ScreenCategoryName})");
            AppendLog($"[*] IP Destino/Gateway: {TxtDestinoIp.Text}");
            AppendLog($"[*] Duração           : Indefinida (controlada pelo QT-600)");
            AppendLog("[*] Aguardando início do teste automatizado de 15 min pela central...");
        }
        catch (Exception ex)
        {
            AppendLog($"[!] Falha ao iniciar refletor MTS-5800: {ex.Message}");
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

                DeviceDisplay.Current.KeepScreenOn = false;
                AndroidEthernetManager.Instance.UnbindProcessFromNetwork(AppendLog);
                try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(200)); } catch { }

                ToggleReflectorBtn.Text = "🟢 ATIVAR REFLETOR MTS-5800 (LOOPBACK LLB)";
                ToggleReflectorBtn.BackgroundColor = Color.FromArgb("#059669");
                VerdictBorder.BackgroundColor = Color.FromArgb("#334155");
                VerdictLabel.Text = "⚪ REFLETOR DESLIGADO";

                var pkts = _lastStats?.TotalPacketsTx ?? 0;
                AppendLog($"[*] Refletor MTS-5800 desligado pelo operador. Total refletido: {pkts:N0} pacotes.");

                if (pkts > 0)
                {
                    ShareCertidaoBtn.IsVisible = true;
                }
            }
            catch { }
        }
    }

    #endregion

    #region Modo Master Móvel (Validar contra PE Claro)

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
            0 => 60,   // 1 Minuto
            1 => 300,  // 5 Minutos
            2 => 900,  // 15 Minutos (Oficial)
            _ => 300
        };

        // Tipo de Serviço Oficial (Descritivo Técnico)
        var servicoTag = ServicePicker.SelectedIndex switch
        {
            1 => "MPLS",
            2 => "VOZ",
            _ => "BLD"
        };

        // Distância MEF
        var (tierLabel, distKm) = TierPicker.SelectedIndex switch
        {
            1 => ("≤ 250 Km (Metropolitano)", 250),
            2 => ("≤ 7000 Km (Nacional)", 7000),
            _ => ("≤ 1200 Km (Regional)", 1200)
        };

        // Limites da Tabela 1 e 2 Embratel
        double lossMax;
        double delayMax;
        double jitterMax;
        int frameSize;

        switch (servicoTag)
        {
            case "VOZ":
                frameSize = 64;
                if (distKm <= 250) { lossMax = 0.02; delayMax = 20; jitterMax = 6; }
                else if (distKm <= 1200) { lossMax = 0.02; delayMax = 50; jitterMax = 16; }
                else { lossMax = 0.05; delayMax = 154; jitterMax = 20; }
                break;

            case "MPLS":
                frameSize = 1500;
                if (distKm <= 250) { lossMax = 0.02; delayMax = 40; jitterMax = 16; }
                else if (distKm <= 1200) { lossMax = 0.02; delayMax = 150; jitterMax = 80; }
                else { lossMax = 0.05; delayMax = 230; jitterMax = 80; }
                break;

            default: // BLD
                frameSize = 1500;
                if (distKm <= 250) { lossMax = 0.20; delayMax = 74; jitterMax = 28; }
                else if (distKm <= 1200) { lossMax = 0.20; delayMax = 250; jitterMax = 160; }
                else { lossMax = 0.20; delayMax = 460; jitterMax = 260; }
                break;
        }

        var saip = _connManager.LoadedCircuit;
        var config = new Y1564TestConfig(
            RemoteIp: targetIp,
            RemotePort: 5001,
            TargetBandwidthMbps: targetMbps,
            FrameSizeBytes: frameSize,
            IsVariableFrame: false,
            VariableFrameSizes: null,
            FrameSizeLabel: frameSize.ToString(),
            Duration: TimeSpan.FromSeconds(durationSeconds),
            ClientName: TxtClienteNome.Text ?? "Claro S.A.",
            Designation: TxtDesignacao.Text ?? "Circuito",
            SlaLossPercent: lossMax,
            SlaDelayMs: delayMax,
            SlaJitterMs: jitterMax,
            DistanceTier: tierLabel,
            SourceIpAddress: null,
            Mode: Y1564TestMode.GatewayTrafficLoad);

        StartTestBtn.IsEnabled = false;
        CancelTestBtn.IsEnabled = true;
        ShareCertidaoBtn.IsVisible = false;

        VerdictBorder.BackgroundColor = Color.FromArgb("#0284C7");
        VerdictLabel.Text = "🔵 TESTANDO (0%)";
        VerdictLabel.TextColor = Color.FromArgb("#FFFFFF");

        AppendLog("=================================================================");
        AppendLog("       INICIANDO CERTIFICAÇÃO OFICIAL ITU-T Y.1564 (SPARC)       ");
        AppendLog("=================================================================");
        AppendLog($"[*] Destino Remoto : {targetIp}");
        AppendLog($"[*] Perfil Serviço : {servicoTag}");
        AppendLog($"[*] Banda Alvo CIR : {targetMbps:F1} Mbps");
        AppendLog($"[*] Duração        : {durationSeconds / 60.0:F0} minutos ({durationSeconds}s)");
        AppendLog($"[*] Limites SLA    : FLR<={lossMax:F2}%, FTD<={delayMax:F0}ms, IFDV<={jitterMax:F0}ms");

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
            AppendLog($"   VEREDITO FINAL: {(_lastResult.IsPass ? "APROVADO CONFORME SLA MEF" : "REPROVADO POR VIOLAÇÃO")}");
            AppendLog($"   Vazão Medida : {_lastResult.RxThroughputMbps:F1} Mbps");
            AppendLog($"   Perda FLR    : {_lastResult.LossPercentage:F2}% (Limite: {lossMax:F2}%)");
            AppendLog($"   Delay FTD    : {_lastResult.DelayAvgMs:F1} ms (Limite: {delayMax:F0} ms)");
            AppendLog($"   Jitter IFDV  : {_lastResult.JitterAvgMs:F1} ms (Limite: {jitterMax:F0} ms)");
            AppendLog("=================================================================");

            ShareCertidaoBtn.IsVisible = true;
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(_lastResult.IsPass ? 500 : 250)); } catch { }
            await DisplayAlert("Certidão Finalizada",
                $"Certificação Y.1564 concluída!\n\n" +
                $"• Veredito: {(_lastResult.IsPass ? "APROVADO CONFORME SLA MEF" : "REPROVADO")}\n" +
                $"• Vazão: {_lastResult.RxThroughputMbps:F1} Mbps\n" +
                $"• Perda: {_lastResult.LossPercentage:F2}%\n" +
                $"• Delay: {_lastResult.DelayAvgMs:F1} ms\n\n" +
                $"Toque em 'Compartilhar Certidão de Nascimento' para emitir o relatório técnico.", "OK");
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

            VerdictLabel.Text = $"🔵 TESTANDO ({p.PercentComplete:F0}%)";
        });
    }

    private void OnCancelTestClicked(object? sender, EventArgs e)
    {
        _testCts?.Cancel();
    }

    #endregion

    #region Emissão e Compartilhamento de Certidão Oficial

    private async void OnShareCertidaoClicked(object? sender, EventArgs e)
    {
        try
        {
            string html;
            string fileNameOficial;

            var rawDesig = TxtDesignacao.Text ?? _connManager.LoadedCircuit?.DesignacaoIp ?? "Circuito";
            // Padrão Embratel (Pág. 41 do Descritivo): SPO/IP/47287 -> SPO-IP-47287
            var nomeSanitizado = rawDesig.Trim().Replace('/', '-').Replace('\\', '-');
            foreach (var c in Path.GetInvalidFileNameChars()) nomeSanitizado = nomeSanitizado.Replace(c, '-');

            if (_lastResult != null)
            {
                html = Y1564PdfReportService.GenerateHtml(_lastResult);
                fileNameOficial = $"Certidao_{nomeSanitizado}";
            }
            else if (_lastStats != null)
            {
                // Relatório de Homologação em Modo Refletor MTS-5800
                html = GerarHtmlRefletorMts5800(_lastStats, rawDesig);
                fileNameOficial = $"Refletor_MTS5800_{nomeSanitizado}";
            }
            else
            {
                await DisplayAlert("Sem Dados", "Nenhum teste ou sessão de refletor realizada para exportação.", "OK");
                return;
            }

            var filePath = await ReportsStorageService.Instance.SaveReportAsync(fileNameOficial, html, ".html", "Certidão Y.1564");

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"Certidão de Nascimento Y.1564 — {nomeSanitizado}",
                File = new ShareFile(filePath)
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Erro ao Compartilhar", ex.Message, "OK");
        }
    }

    private string GerarHtmlRefletorMts5800(DigitalLoopbackStats stats, string desig)
    {
        var cliente = TxtClienteNome.Text ?? "Claro S.A.";
        var cir = TxtVelocidadeCir.Text ?? "100 Mbps";
        var pe = TxtDestinoIp.Text ?? "—";
        var central = stats.LastRemoteEndPoint ?? "Central Embratel / QT-600";

        return $@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<title>Certidão de Homologação de Refletor MTS-5800</title>
<style>
body {{ font-family: 'Segoe UI', Arial, sans-serif; background: #0B0F19; color: #F8FAFC; margin: 20px; }}
.card {{ background: #111827; border: 1px solid #1F2937; border-radius: 8px; padding: 18px; margin-bottom: 14px; }}
h1 {{ color: #38BDF8; font-size: 20px; margin-top: 0; }}
h2 {{ color: #F59E0B; font-size: 14px; margin-bottom: 8px; }}
.badge {{ background: #059669; color: white; padding: 4px 8px; border-radius: 4px; font-size: 11px; font-weight: bold; }}
table {{ width: 100%; border-collapse: collapse; margin-top: 10px; }}
td, th {{ border: 1px solid #1E293B; padding: 8px; font-size: 12px; }}
th {{ background: #0F172A; color: #94A3B8; text-align: left; }}
.metric {{ font-size: 22px; font-weight: bold; color: #34D399; }}
</style>
</head>
<body>
<div class='card'>
  <h1>📜 CERTIDÃO DE HOMOLOGAÇÃO DE LAÇO — ITU-T Y.1564</h1>
  <p><span class='badge'>MTS-5800 LLB EMULADO (SPARC MOBILE)</span> &nbsp; Compatível com NetAnalyst / QT-600 Central</p>
  <p><strong>Cliente:</strong> {cliente}<br/>
     <strong>Designação:</strong> {desig}<br/>
     <strong>CIR Contratado:</strong> {cir}<br/>
     <strong>Gateway PE:</strong> {pe}<br/>
     <strong>Origem Detectada (QT-600):</strong> {central}<br/>
     <strong>Porta UDP:</strong> 5001 (L3/L4 Swap Bidirecional)<br/>
     <strong>Duração da Sessão:</strong> {stats.Elapsed:hh\:mm\:ss}<br/>
     <strong>Data / Hora:</strong> {DateTime.Now:dd/MM/yyyy HH:mm:ss}</p>
</div>
<div class='card'>
  <h2>Métricas da Sessão de Refletor</h2>
  <table>
    <tr><th>Métrica</th><th>Valor Aferido</th></tr>
    <tr><td>Total de Pacotes Recebidos (Rx)</td><td class='metric'>{stats.TotalPacketsRx:N0}</td></tr>
    <tr><td>Total de Pacotes Refletidos (Tx)</td><td class='metric'>{stats.TotalPacketsTx:N0}</td></tr>
    <tr><td>Vazão Instantânea Rx</td><td>{stats.CurrentRxMbps:F1} Mbps</td></tr>
    <tr><td>Vazão Instantânea Tx</td><td>{stats.CurrentTxMbps:F1} Mbps</td></tr>
    <tr><td>Taxa Máxima de Pacotes</td><td>{stats.CurrentRxPps:N0} PPS</td></tr>
    <tr><td>Veredito Operacional</td><td style='color:#4ADE80; font-weight:bold;'>✓ SESSÃO CONCLUÍDA COM SUCESSO</td></tr>
  </table>
</div>
</body>
</html>";
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
