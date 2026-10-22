using System;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Provisioning;

namespace NetworkDevice.UI;

/// <summary>
/// Janela simplificada e de alta usabilidade para emissão da Certidão de Nascimento oficial (ITU-T Y.1564)
/// no padrão operacional da Claro / Embratel (compatível com MTS-5800 e QT-600).
/// </summary>
public partial class CertidaoNascimentoWindow : Window
{
    private readonly SaipCircuitData? _circuit;
    private readonly string? _sourceIpAddress;
    private CancellationTokenSource? _testCts;
    private Y1564Result? _lastResult;
    private string? _generatedPdfPath;
    private DigitalLoopbackService? _loopbackService;
    private bool _isLoopbackActive;

    public CertidaoNascimentoWindow(SaipCircuitData? circuit, string? sourceIpAddress = null)
    {
        InitializeComponent();
        _circuit = circuit;
        _sourceIpAddress = sourceIpAddress;

        PreencherDadosCircuito();
        AtualizarParametrosPorServico();
    }

    private void PreencherDadosCircuito()
    {
        if (_circuit == null)
        {
            TxtClienteNome.Text = "⚠️ Ficha SAIP não importada";
            TxtClienteNome.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            TxtDesignacao.Text = "—";
            TxtVelocidadeCir.Text = "100 Mbps (Padrão)";
            TxtDestinoIp.Text = "192.168.1.1";
            return;
        }

        var cliente = SaipParser.CleanRazaoSocial(_circuit.ClienteRazaoSocial);
        TxtClienteNome.Text = string.IsNullOrWhiteSpace(cliente) ? "Cliente Homologação" : cliente;

        var desig = _circuit.DesignacaoIp ?? _circuit.NumeroOts ?? "Circuito SAIP";
        TxtDesignacao.Text = desig;

        double cir = _circuit.BandaMbpsNominal ?? 0;
        if (cir <= 0 && _circuit.BandaKbps.HasValue && _circuit.BandaKbps.Value > 0)
        {
            cir = _circuit.BandaKbps.Value / 1000.0;
        }
        if (cir <= 0) cir = 100.0;

        TxtVelocidadeCir.Text = $"{cir:F0} Mbps";
        TxtMetricaVelocidadeSub.Text = $"Alvo: {cir:F0} Mbps (CIR)";

        var gw = _circuit.WanGateway;
        if (string.IsNullOrWhiteSpace(gw)) gw = _circuit.PeLoopbackIp ?? _sourceIpAddress ?? "192.168.1.1";
        TxtDestinoIp.Text = gw;

        // Preenche IP do QT-600 Alvo (conforme OT-ENG-053 Passo 6.8 / 11.4)
        if (TxtIpQt600Alvo != null)
        {
            var qtTarget = _circuit.PeLoopbackIp ?? _circuit.WanGateway ?? gw;
            TxtIpQt600Alvo.Text = qtTarget;
        }

        // Preenche Tecnologia de Acesso e Sinalização
        if (TxtTecnologiaSinalizacao != null)
        {
            var ac = _circuit.TipoAcesso ?? "FO EDD";
            var sin = !string.IsNullOrWhiteSpace(_circuit.Sinalizacao) ? _circuit.Sinalizacao : (_circuit.TipoServico == SaipServiceType.IpVpn ? "MPLS / VPN" : "IP / Padrão");
            TxtTecnologiaSinalizacao.Text = $"{ac} / {sin}";
        }

        // Auto-detecção e exibição do endereço MAC do dispositivo de rede local
        DetectarMacDispositivo();

        // Auto-seleção do serviço oficial baseado na SAIP
        if (_circuit.TipoServico == SaipServiceType.IpVpn)
        {
            SelecionarComboPorTag(CmbTipoServico, "MPLS");
        }
        else if (_circuit.TipoServico == SaipServiceType.VozDigitalE1)
        {
            SelecionarComboPorTag(CmbTipoServico, "VOZ_E1");
        }
        else if (_circuit.TipoServico == SaipServiceType.VozSipIp)
        {
            SelecionarComboPorTag(CmbTipoServico, "VOZ_SIP");
        }
        else if (_circuit.Produto?.Contains("VOZ", StringComparison.OrdinalIgnoreCase) == true ||
                 _circuit.Produto?.Contains("SIP", StringComparison.OrdinalIgnoreCase) == true)
        {
            SelecionarComboPorTag(CmbTipoServico, "VOZ_SIP");
        }
        else
        {
            SelecionarComboPorTag(CmbTipoServico, "BLD");
        }
    }

    private void DetectarMacDispositivo()
    {
        if (TxtMacDispositivo == null) return;
        try
        {
            var preferredIp = _sourceIpAddress ?? _circuit?.HostLanIp ?? _circuit?.LanIp;
            var mac = ObterMacLocal(preferredIp);
            TxtMacDispositivo.Text = mac;
        }
        catch
        {
            TxtMacDispositivo.Text = "00:E0:4C:68:01:23";
        }
    }

    public static string ObterMacLocal(string? preferredIp = null)
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            if (!string.IsNullOrWhiteSpace(preferredIp))
            {
                var match = interfaces.FirstOrDefault(i =>
                    i.GetIPProperties().UnicastAddresses.Any(u => u.Address.ToString() == preferredIp));
                if (match != null)
                {
                    var bytes = match.GetPhysicalAddress().GetAddressBytes();
                    if (bytes.Length == 6)
                        return string.Join(":", bytes.Select(b => b.ToString("X2")));
                }
            }

            var eth = interfaces.FirstOrDefault(i => i.NetworkInterfaceType == NetworkInterfaceType.Ethernet);
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

    private void BtnCopiarMac_Click(object sender, RoutedEventArgs e)
    {
        var mac = TxtMacDispositivo.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(mac))
        {
            Clipboard.SetText(mac);
            MessageBox.Show($"Endereço MAC copiado com sucesso para a Área de Transferência:\n\n{mac}", "MAC Dispositivo", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void SelecionarComboPorTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                break;
            }
        }
    }

    private void CmbTipoServico_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AtualizarParametrosPorServico();
    }

    private void CmbDistanciaMef_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AtualizarParametrosPorServico();
    }

    private void CmbModoOperacao_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbModoOperacao == null || BtnIniciarCertidao == null) return;
        var tag = (CmbModoOperacao.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Loopback";

        if (tag == "Loopback")
        {
            if (BorderDuracaoIndefinida != null) BorderDuracaoIndefinida.Visibility = Visibility.Visible;
            if (CmbDuracao != null) CmbDuracao.Visibility = Visibility.Collapsed;
            if (LblDuracaoTitulo != null) LblDuracaoTitulo.Text = "Tempo do Refletor:";

            if (_isLoopbackActive)
            {
                BtnIniciarCertidao.Content = "⏹ DESLIGAR REFLETOR (LLB)";
                BtnIniciarCertidao.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                TxtEtapaProgresso.Text = "Refletor MTS-5800 ativo na porta 5001. Aguardando finalização pela Central QT-600...";
            }
            else
            {
                BtnIniciarCertidao.Content = "🟢 ATIVAR REFLETOR MTS-5800 (LOOPBACK LLB)";
                BtnIniciarCertidao.Background = new SolidColorBrush(Color.FromRgb(5, 150, 105)); // Verde
                TxtEtapaProgresso.Text = "Modo Refletor (compatível MTS-5800 LLB): pronto para responder ao QT-600.";
                TxtTempoRestante.Text = "00:00 (Indefinido)";
            }
        }
        else
        {
            if (BorderDuracaoIndefinida != null) BorderDuracaoIndefinida.Visibility = Visibility.Collapsed;
            if (CmbDuracao != null) CmbDuracao.Visibility = Visibility.Visible;
            if (LblDuracaoTitulo != null) LblDuracaoTitulo.Text = "Duração do Teste:";

            BtnIniciarCertidao.Content = "▶ EMITIR CERTIDÃO DE NASCIMENTO";
            BtnIniciarCertidao.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Vermelho Claro
            TxtEtapaProgresso.Text = "Aguardando início do teste automatizado...";
            TxtTempoRestante.Text = "00:00 / 05:00";
        }
    }

    /// <summary>
    /// Calcula os limites SLA MEF 23.1 e configurações oficiais Embratel
    /// </summary>
    private void AtualizarParametrosPorServico()
    {
        if (CmbTipoServico == null || CmbDistanciaMef == null || TxtResumoParametros == null) return;

        var servicoTag = (CmbTipoServico.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "BLD";
        var distTag = (CmbDistanciaMef.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1200";

        int distKm = int.TryParse(distTag, out int d) ? d : 1200;

        // Limites da Tabela 1 e Tabela 2 do Descritivo Técnico Embratel
        double maxLoss;
        double maxDelay;
        double maxJitter;
        string vlan;
        string frameSize;
        string dscp;

        switch (servicoTag)
        {
            case "VOZ_E1":
                vlan = "S-VLAN 200 (Tronco E1)";
                frameSize = "64 Bytes (G.729) / 256 Bytes (G.711)";
                dscp = "EF (46) — Alta Prioridade (Voz Digital R2D/ISDN)";
                if (distKm <= 250) { maxLoss = 0.005; maxDelay = 20; maxJitter = 5; }
                else if (distKm <= 1200) { maxLoss = 0.01; maxDelay = 35; maxJitter = 10; }
                else { maxLoss = 0.02; maxDelay = 80; maxJitter = 15; }
                break;

            case "VOZ_SIP":
            case "VOZ":
                vlan = "S-VLAN 200 (Tronco SIP)";
                frameSize = "64 Bytes / 256 Bytes (SIP RTP)";
                dscp = "EF (46) — Alta Prioridade (Voz SIP/PABX IP)";
                if (distKm <= 250) { maxLoss = 0.01; maxDelay = 25; maxJitter = 6; }
                else if (distKm <= 1200) { maxLoss = 0.02; maxDelay = 50; maxJitter = 16; }
                else { maxLoss = 0.05; maxDelay = 154; maxJitter = 20; }
                break;

            case "MPLS":
                vlan = "S-VLAN 400 (ou 300 s/ CPE)";
                frameSize = "1518 Bytes (DIX)";
                dscp = "Misto (Voz EF, Vídeo AF41, BE CS1)";
                if (distKm <= 250) { maxLoss = 0.02; maxDelay = 40; maxJitter = 16; }
                else if (distKm <= 1200) { maxLoss = 0.02; maxDelay = 150; maxJitter = 80; }
                else { maxLoss = 0.05; maxDelay = 230; maxJitter = 80; }
                break;

            default: // BLD
                vlan = "S-VLAN 100";
                frameSize = "1518 Bytes (DIX)";
                dscp = "CS1 (8) — Best Effort";
                if (distKm <= 250) { maxLoss = 0.20; maxDelay = 74; maxJitter = 28; }
                else if (distKm <= 1200) { maxLoss = 0.20; maxDelay = 250; maxJitter = 160; }
                else { maxLoss = 0.20; maxDelay = 460; maxJitter = 260; }
                break;
        }

        TxtResumoParametros.Text = $"Parâmetros Oficiais: {vlan} | Quadro: {frameSize} | DSCP: {dscp}\n" +
                                   $"Critérios MEF 23.1: Perda (FLR) <= {maxLoss:F2}% | Latência (FTD) <= {maxDelay:F0}ms | Jitter (IFDV) <= {maxJitter:F0}ms";

        TxtMetricaPerdaSub.Text = $"SLA: <= {maxLoss:F2}%";
        TxtMetricaLatenciaSub.Text = $"SLA: <= {maxDelay:F0} ms";
        TxtMetricaJitterSub.Text = $"SLA: <= {maxJitter:F0} ms";
    }

    private async void BtnIniciarCertidao_Click(object sender, RoutedEventArgs e)
    {
        var modoTag = (CmbModoOperacao.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Master";

        if (modoTag == "Loopback")
        {
            if (_isLoopbackActive)
            {
                PararLoopback();
            }
            else
            {
                IniciarLoopback();
            }
            return;
        }

        await ExecutarTesteMasterAsync();
    }

    private async Task ExecutarTesteMasterAsync()
    {
        if (_circuit == null)
        {
            MessageBox.Show(this,
                "Para emitir a Certidão de Nascimento oficial (ITU-T Y.1564), é necessário carregar a Ficha SAIP na tela inicial do SPARC.",
                "Ficha SAIP Obrigatória", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        double cir = _circuit.BandaMbpsNominal ?? 0;
        if (cir <= 0 && _circuit.BandaKbps.HasValue && _circuit.BandaKbps.Value > 0)
        {
            cir = _circuit.BandaKbps.Value / 1000.0;
        }
        if (cir <= 0) cir = 100.0;

        var remoteIp = _circuit.WanGateway;
        if (string.IsNullOrWhiteSpace(remoteIp) || remoteIp == "0.0.0.0")
        {
            remoteIp = _circuit.PeLoopbackIp ?? _sourceIpAddress ?? "192.168.1.1";
        }

        var duracaoItem = (CmbDuracao.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1";
        double minutos = double.TryParse(duracaoItem, out var m) ? m : 1.0;
        var duracao = TimeSpan.FromMinutes(minutos);

        var servicoTag = (CmbTipoServico.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "BLD";
        var distTag = (CmbDistanciaMef.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "1200";
        int distKm = int.TryParse(distTag, out int d) ? d : 1200;

        // SLA
        double maxLoss = (servicoTag == "VOZ" || servicoTag == "MPLS")
            ? (distKm <= 1200 ? 0.02 : 0.05)
            : 0.20;
        double maxDelay = servicoTag == "VOZ"
            ? (distKm <= 250 ? 20.0 : (distKm <= 1200 ? 50.0 : 154.0))
            : (servicoTag == "MPLS"
                ? (distKm <= 250 ? 40.0 : (distKm <= 1200 ? 150.0 : 230.0))
                : (distKm <= 250 ? 74.0 : (distKm <= 1200 ? 250.0 : 460.0)));
        double maxJitter = servicoTag == "VOZ"
            ? (distKm <= 250 ? 6.0 : (distKm <= 1200 ? 16.0 : 20.0))
            : (servicoTag == "MPLS"
                ? (distKm <= 250 ? 16.0 : (distKm <= 1200 ? 80.0 : 80.0))
                : (distKm <= 250 ? 28.0 : (distKm <= 1200 ? 160.0 : 260.0)));

        int frameSize = servicoTag == "VOZ" ? 64 : 1500;
        string frameLabel = servicoTag == "VOZ" ? "64" : "1500";

        // Prepara UI
        BtnIniciarCertidao.IsEnabled = false;
        BtnInterromper.IsEnabled = true;
        CmbTipoServico.IsEnabled = false;
        CmbModoOperacao.IsEnabled = false;
        CmbDistanciaMef.IsEnabled = false;
        CmbDuracao.IsEnabled = false;

        BtnVisualizarPdf.Visibility = Visibility.Collapsed;
        BtnSalvarPdf.Visibility = Visibility.Collapsed;
        BtnAbrirPasta.Visibility = Visibility.Collapsed;

        BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199));
        TxtStatusGeral.Text = "🔵 TESTANDO (0%)";
        PbProgresso.Value = 0;
        TxtTempoRestante.Text = $"00:00 / {duracao:mm\\:ss}";
        TxtEtapaProgresso.Text = "Fase 1: Aferição de Banda (SCT 100% CIR)...";

        TxtLogSimplificado.Text = $"[{DateTime.Now:HH:mm:ss}] Iniciando validação ITU-T Y.1564 contra Gateway {remoteIp}...\n";
        TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] Circuito: {_circuit.DesignacaoIp} | Cliente: {TxtClienteNome.Text}\n";
        TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] Alvo CIR: {cir:F0} Mbps | Duração: {minutos:F0} min | Perfil: {servicoTag}\n";

        _testCts = new CancellationTokenSource();

        var clientName = SaipParser.CleanRazaoSocial(_circuit.ClienteRazaoSocial) ?? "Cliente";
        var desig = _circuit.DesignacaoIp ?? _circuit.NumeroOts ?? "Circuito";

        var config = new Y1564TestConfig(
            RemoteIp: remoteIp,
            RemotePort: 5001,
            TargetBandwidthMbps: cir,
            FrameSizeBytes: frameSize,
            IsVariableFrame: false,
            VariableFrameSizes: null,
            FrameSizeLabel: frameLabel,
            Duration: duracao,
            ClientName: clientName,
            Designation: desig,
            SlaLossPercent: maxLoss,
            SlaDelayMs: maxDelay,
            SlaJitterMs: maxJitter,
            DistanceTier: $"≤ {distKm} Km",
            SourceIpAddress: _sourceIpAddress,
            Mode: Y1564TestMode.GatewayTrafficLoad);

        var progressHandler = new Progress<Y1564LiveProgress>(p =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtMetricaVelocidade.Text = $"{p.CurrentRxMbps:F1} Mbps";
                TxtMetricaPerda.Text = $"{p.CurrentLossPercent:F2} %";
                TxtMetricaPerda.Foreground = p.CurrentLossPercent <= maxLoss
                    ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));

                if (p.IcmpDelayAvgMs > 0)
                {
                    TxtMetricaLatencia.Text = $"{p.IcmpDelayAvgMs:F1} ms";
                    TxtMetricaLatencia.Foreground = p.IcmpDelayAvgMs <= maxDelay
                        ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                        : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }

                if (p.CurrentJitterAvgMs > 0)
                {
                    TxtMetricaJitter.Text = $"{p.CurrentJitterAvgMs:F1} ms";
                }

                PbProgresso.Value = Math.Min(100, p.PercentComplete);
                TxtTempoRestante.Text = $"{p.Elapsed:mm\\:ss} / {duracao:mm\\:ss}";
                TxtStatusGeral.Text = $"🔵 TESTANDO ({p.PercentComplete:F0}%)";

                if (p.PercentComplete > 30 && TxtEtapaProgresso.Text.StartsWith("Fase 1", StringComparison.Ordinal))
                {
                    TxtEtapaProgresso.Text = "Fase 2: Estabilidade, Latência e Perda de Pacotes (SPT)...";
                }
            });
        });

        try
        {
            var testService = new Y1564TestService();
            var result = await testService.RunTestAsync(
                config,
                progressHandler,
                msg => Dispatcher.Invoke(() => {
                    TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                    ScrollLog.ScrollToEnd();
                }),
                _testCts.Token);
            _lastResult = result;

            PbProgresso.Value = 100;
            TxtTempoRestante.Text = $"{duracao:mm\\:ss} / {duracao:mm\\:ss}";
            TxtMetricaVelocidade.Text = $"{result.RxThroughputMbps:F1} Mbps";
            TxtMetricaPerda.Text = $"{result.LossPercentage:F2} %";
            TxtMetricaLatencia.Text = $"{result.DelayAvgMs:F1} ms";
            TxtMetricaJitter.Text = $"{result.JitterAvgMs:F1} ms";

            if (result.IsPass)
            {
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtStatusGeral.Text = "🟢 APROVADO (PASS)";
                TxtEtapaProgresso.Text = "✓ Circuito aprovado em conformidade com o SLA oficial Claro!";
                TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] [SUCESSO] Circuito APROVADO dentro dos parâmetros MEF 23.1!\n";
            }
            else
            {
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                TxtStatusGeral.Text = "🔴 REPROVADO (FAIL)";
                TxtEtapaProgresso.Text = "⚠️ Atenção: circuito apresentou divergências nos limites de SLA.";
                TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] [REPROVADO] Circuito não atingiu os requisitos mínimos de SLA.\n";
            }

            // Geração da Certidão Oficial em PDF
            TxtEtapaProgresso.Text += " Gerando Certidão de Nascimento em PDF...";
            _generatedPdfPath = await Y1564PdfReportService.GenerateReportPdfAsync(result, null, CancellationToken.None);
            TxtEtapaProgresso.Text = (result.IsPass ? "✓ Certidão de Nascimento emitida com sucesso!" : "⚠️ Certidão emitida com registro de não conformidade.");
            TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] Certidão gerada: {Path.GetFileName(_generatedPdfPath)}\n";

            BtnVisualizarPdf.Visibility = Visibility.Visible;
            BtnSalvarPdf.Visibility = Visibility.Visible;
            BtnAbrirPasta.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            TxtStatusGeral.Text = "⏹ CANCELADO";
            TxtEtapaProgresso.Text = "Teste interrompido pelo operador.";
            TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] Teste cancelado manualmente.\n";
        }
        catch (Exception ex)
        {
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TxtStatusGeral.Text = "❌ ERRO";
            TxtEtapaProgresso.Text = $"Falha no teste: {ex.Message}";
            TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] [ERRO] {ex.Message}\n";
        }
        finally
        {
            BtnIniciarCertidao.IsEnabled = true;
            BtnInterromper.IsEnabled = false;
            CmbTipoServico.IsEnabled = true;
            CmbModoOperacao.IsEnabled = true;
            CmbDistanciaMef.IsEnabled = true;
            CmbDuracao.IsEnabled = true;
        }
    }

    private void IniciarLoopback()
    {
        try
        {
            _loopbackService = new DigitalLoopbackService();
            _loopbackService.StatsUpdated += stats =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtMetricaVelocidade.Text = stats.CurrentRxMbps > 0 ? $"{stats.CurrentRxMbps:F1} Mbps" : "0.0 Mbps";
                    TxtMetricaVelocidadeSub.Text = $"Tx: {stats.CurrentTxMbps:F1} Mbps | {stats.CurrentRxPps:N0} PPS";

                    TxtMetricaPerda.Text = $"{stats.TotalPacketsRx:N0} pkts";
                    TxtMetricaPerdaSub.Text = $"Refletidos: {stats.TotalPacketsTx:N0}";

                    TxtMetricaLatencia.Text = !string.IsNullOrEmpty(stats.LastRemoteEndPoint) ? stats.LastRemoteEndPoint : "Aguardando";
                    TxtMetricaLatenciaSub.Text = "Origem Central (QT-600)";

                    TxtMetricaJitter.Text = stats.Elapsed.ToString(@"hh\:mm\:ss");
                    TxtMetricaJitterSub.Text = "Tempo Ativo";

                    TxtTempoRestante.Text = $"{stats.Elapsed:mm\\:ss} (Indefinido)";
                    TxtEtapaProgresso.Text = $"Refletor MTS-5800 Ativo na porta 5001. Pacotes refletidos: {stats.TotalPacketsTx:N0}";
                });
            };

            _loopbackService.LogMessage += msg =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                    ScrollLog.ScrollToEnd();
                });
            };

            _loopbackService.Start(port: 5001);
            _isLoopbackActive = true;

            BtnIniciarCertidao.Content = "⏹ DESLIGAR REFLETOR (LLB)";
            BtnIniciarCertidao.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            TxtStatusGeral.Text = "🟢 REFLETOR ATIVO (MTS-5800)";
            TxtEtapaProgresso.Text = "Modo Refletor Ativo na porta UDP 5001. Aguardando disparo do QT-600 central (tempo indefinido)...";
            TxtTempoRestante.Text = "00:00 (Indefinido)";
            TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] [MTS-5800] Modo Loopback (LLB) iniciado na porta 5001. Duração: Indefinida.\n";
            ScrollLog.ScrollToEnd();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível iniciar o Refletor na porta 5001: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PararLoopback()
    {
        _isLoopbackActive = false;
        try
        {
            _loopbackService?.Dispose();
            _loopbackService = null;
        }
        catch { }

        BtnIniciarCertidao.Content = "🟢 ATIVAR REFLETOR MTS-5800 (LOOPBACK LLB)";
        BtnIniciarCertidao.Background = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));
        TxtStatusGeral.Text = "⚪ REFLETOR DESLIGADO";
        TxtEtapaProgresso.Text = "Refletor desligado pelo operador.";
        TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] [MTS-5800] Refletor encerrado.\n";
        ScrollLog.ScrollToEnd();
    }

    private void BtnInterromper_Click(object sender, RoutedEventArgs e)
    {
        _testCts?.Cancel();
    }

    private void BtnZerar_Click(object sender, RoutedEventArgs e)
    {
        TxtMetricaVelocidade.Text = "— Mbps";
        TxtMetricaPerda.Text = "— %";
        TxtMetricaLatencia.Text = "— ms";
        TxtMetricaJitter.Text = "— ms";
        TxtMetricaPerda.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        TxtMetricaLatencia.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        PbProgresso.Value = 0;
        var tag = (CmbModoOperacao.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Loopback";
        if (tag == "Loopback")
        {
            TxtTempoRestante.Text = "00:00 (Indefinido)";
            TxtEtapaProgresso.Text = "Modo Refletor (compatível MTS-5800 LLB): pronto para responder ao QT-600.";
            TxtLogSimplificado.Text = "• Limpeza realizada. Clique em 'ATIVAR REFLETOR MTS-5800' para iniciar.";
            TxtStatusGeral.Text = "⚪ AGUARDANDO REFLETOR";
        }
        else
        {
            TxtTempoRestante.Text = "00:00 / 05:00";
            TxtEtapaProgresso.Text = "Pronto para novo teste automatizado.";
            TxtLogSimplificado.Text = "• Limpeza realizada. Clique em 'EMITIR CERTIDÃO DE NASCIMENTO' para iniciar.";
            TxtStatusGeral.Text = "⚪ PRONTO PARA TESTE";
        }
        BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));
        BtnVisualizarPdf.Visibility = Visibility.Collapsed;
        BtnSalvarPdf.Visibility = Visibility.Collapsed;
        BtnAbrirPasta.Visibility = Visibility.Collapsed;
    }

    private void BtnVisualizarPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_generatedPdfPath) && File.Exists(_generatedPdfPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _generatedPdfPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Erro ao abrir PDF: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnSalvarPdf_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedPdfPath) || !File.Exists(_generatedPdfPath))
        {
            MessageBox.Show(this, "A Certidão em PDF ainda não foi gerada.", "Salvar Certidão", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Padrão do Descritivo Técnico Embratel (Pág. 41):
        // Designação SPO/IP/47287 => SPO-IP-47287.pdf
        var rawDesig = _circuit?.DesignacaoIp ?? _circuit?.NumeroOts ?? "Circuito";
        var nomeOficial = rawDesig.Trim().Replace('/', '-').Replace('\\', '-');
        foreach (var c in Path.GetInvalidFileNameChars()) nomeOficial = nomeOficial.Replace(c, '-');
        if (!nomeOficial.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) nomeOficial += ".pdf";

        var dlg = new SaveFileDialog
        {
            Title = "Salvar Certidão de Nascimento Oficial",
            Filter = "Documento PDF (*.pdf)|*.pdf|Todos os Arquivos (*.*)|*.*",
            FileName = nomeOficial
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.Copy(_generatedPdfPath, dlg.FileName, true);
                MessageBox.Show(this, $"Certidão salva com sucesso em:\n{dlg.FileName}", "Certidão Salva", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Erro ao salvar PDF: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnAbrirPasta_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "Certidoes_Y1564");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch { }
    }

    private void BtnAbrirAvancado_Click(object sender, RoutedEventArgs e)
    {
        var hostIp = _circuit?.HostLanIp ?? _sourceIpAddress;
        var adv = new Y1564TestWindow(_circuit, hostIp) { Owner = this.Owner };
        adv.Show();
        this.Close();
    }

    private async void BtnAtivarDhcpNaCertidao_Click(object sender, RoutedEventArgs e)
    {
        await GerenciarDhcpRoteadorAsync(ativar: true);
    }

    private async void BtnRemoverDhcpNaCertidao_Click(object sender, RoutedEventArgs e)
    {
        await GerenciarDhcpRoteadorAsync(ativar: false);
    }

    private async Task GerenciarDhcpRoteadorAsync(bool ativar)
    {
        if (_circuit == null)
        {
            MessageBox.Show(this, "Não há Ficha SAIP carregada para obter os dados de LAN do roteador.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ports = SerialPort.GetPortNames();
        if (ports == null || ports.Length == 0)
        {
            MessageBox.Show(this, "Nenhuma porta serial COM detectada para comunicação com o console do roteador.", "Console Desconectado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetPort = ports.FirstOrDefault(p => !p.Equals("COM1", StringComparison.OrdinalIgnoreCase)) ?? ports[0];
        try
        {
            var msg = ativar ? "Ativando DHCP temporário na LAN..." : "Removendo DHCP e regravando NVRAM estática...";
            TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] [DHCP ON-DEMAND] Conectando via {targetPort}: {msg}\n";
            ScrollLog.ScrollToEnd();

            var sessionOptions = new NetworkDevice.Core.Session.SessionOptions
            {
                PromptMatcher = NetworkDevice.Core.Session.RegexPromptMatcher.Universal(),
                CommandTimeout = TimeSpan.FromSeconds(20),
                ConnectTimeout = TimeSpan.FromSeconds(10)
            };

            var transport = new NetworkDevice.Protocols.Serial.SerialTransport(targetPort, 9600);
            await using var session = new NetworkDevice.Core.Session.DeviceSession(transport, sessionOptions);
            await session.ConnectAsync(CancellationToken.None);

            if (ativar)
            {
                await NetworkDevice.Cisco.CiscoSaipConfigurator.AtivarDhcpTemporarioAsync(session, _circuit, s => Dispatcher.Invoke(() => { TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] {s}\n"; ScrollLog.ScrollToEnd(); }), CancellationToken.None);
                MessageBox.Show(this, "Servidor DHCP temporário ativado na LAN do roteador com sucesso!\n\nO smartphone ou PC receberá IP dinâmico.", "DHCP Ativo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                await NetworkDevice.Cisco.CiscoSaipConfigurator.RemoverDhcpTemporarioAsync(session, _circuit, s => Dispatcher.Invoke(() => { TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] {s}\n"; ScrollLog.ScrollToEnd(); }), CancellationToken.None);
                MessageBox.Show(this, "Servidor DHCP removido e configuração salva 100% estática na NVRAM!", "DHCP Removido", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            TxtLogSimplificado.Text += $"[{DateTime.Now:HH:mm:ss}] [ERRO DHCP] {ex.Message}\n";
            ScrollLog.ScrollToEnd();
            MessageBox.Show(this, $"Erro ao comunicar com o roteador via {targetPort}: {ex.Message}", "Erro DHCP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        _testCts?.Cancel();
        PararLoopback();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _testCts?.Cancel();
        PararLoopback();
        base.OnClosed(e);
    }
}
