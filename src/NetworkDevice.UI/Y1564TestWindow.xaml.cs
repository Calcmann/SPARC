using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

public partial class Y1564TestWindow : Window
{
    private readonly SaipCircuitData? _circuit;
    private readonly string? _sourceIpAddress;
    private CancellationTokenSource? _testCts;
    private CancellationTokenSource? _mtuCts;
    private Y1564Result? _lastResult;
    private string? _generatedPdfPath;
    private MefTierProfile _currentMefProfile = MefTierProfile.UpTo1200Km;

    private DigitalLoopbackService? _loopService;
    private PromiscuousLoopbackService? _promiscuousLoopService;
    private readonly PathMtuDiscoveryService _mtuService = new();

    public Y1564TestWindow(SaipCircuitData? circuit, string? sourceIpAddress = null, int initialTabIndex = 0)
    {
        InitializeComponent();
        _circuit = circuit;
        _sourceIpAddress = sourceIpAddress;

        if (TcAnalisador != null)
        {
            TcAnalisador.SelectedIndex = Math.Clamp(initialTabIndex, 0, 2);
        }

        CarregarAdaptadoresDeRede();
        VerificarNpcapStatus();

        if (_circuit != null)
        {
            var cleanCliente = SaipParser.CleanRazaoSocial(_circuit.ClienteRazaoSocial) ?? "Não informado";
            var desig = _circuit.DesignacaoIp ?? _circuit.NumeroOts ?? "Circuito Carregado";
            TxtHeaderCircuito.Text = $"Circuito: {desig} — {cleanCliente}";

            if (_circuit.BandaMbpsNominal is > 0)
            {
                TxtTargetBandwidth.Text = _circuit.BandaMbpsNominal.Value.ToString("F0");
                TxtMetricTargetRate.Text = $"Alvo: {_circuit.BandaMbpsNominal.Value:F0} Mbps";
            }

            if (!string.IsNullOrWhiteSpace(_circuit.WanGateway))
            {
                TxtRemoteIp.Text = _circuit.WanGateway;
                TxtMtuTargetIp.Text = _circuit.WanGateway;
            }
        }
        else
        {
            TxtHeaderCircuito.Text = "Circuito: Não informado (Analisador de Dados Avulso)";
        }

        AtualizarRegraSlaResumo();

        Closed += Y1564TestWindow_Closed;
    }

    private void CarregarAdaptadoresDeRede()
    {
        var physicalList = new List<Tuple<string, string?>>();
        var virtualList = new List<Tuple<string, string?>>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                var isVirtual = ni.Description.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                                ni.Name.Contains("Tailscale", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                                ni.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("KM-TEST", StringComparison.OrdinalIgnoreCase) ||
                                ni.Name.Contains("Topaz", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("Topaz", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("Warsaw", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("GAS", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("Npcap", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("TAP", StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                                ni.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase);

                var ipProp = ni.GetIPProperties();
                var ipv4List = ipProp.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(u.Address))
                    .Select(u => u.Address.ToString())
                    .ToList();

                var bestIp = ipv4List.FirstOrDefault(ip => !ip.StartsWith("169.254.")) ?? ipv4List.FirstOrDefault();
                var isConnected = ni.OperationalStatus == OperationalStatus.Up;

                var displayName = string.IsNullOrWhiteSpace(ni.Description) || ni.Description.Equals(ni.Name, StringComparison.OrdinalIgnoreCase)
                    ? ni.Name
                    : $"{ni.Name} — {ni.Description}";

                if (!isConnected)
                {
                    displayName += " [Cabo Desconectado]";
                }
                else if (string.IsNullOrWhiteSpace(bestIp))
                {
                    displayName += " [Sem IPv4]";
                }
                else if (bestIp.StartsWith("169.254."))
                {
                    displayName += $" ({bestIp}) [APIPA/Link-Local]";
                }
                else
                {
                    displayName += $" ({bestIp})";
                }

                var isSpecialVirtual = !string.IsNullOrEmpty(bestIp) && (bestIp.StartsWith("100.") || bestIp.StartsWith("54.232."));
                if (isVirtual || isSpecialVirtual)
                {
                    virtualList.Add(Tuple.Create($"{displayName} [Virtual/VPN/Loopback]", bestIp));
                }
                else
                {
                    physicalList.Add(Tuple.Create(displayName, bestIp));
                }
            }
        }
        catch { }

        var allList = physicalList.Concat(virtualList).ToList();

        // Preenche ComboBox da Aba 1 (Origem Y.1564)
        CmbSourceInterface.Items.Clear();
        var autoItem = new ComboBoxItem { Content = "Automático (Roteamento do SO)", Tag = null };
        CmbSourceInterface.Items.Add(autoItem);

        ComboBoxItem? selectedItem = null;
        foreach (var item in allList)
        {
            var cbi = new ComboBoxItem { Content = item.Item1, Tag = item.Item2 };
            CmbSourceInterface.Items.Add(cbi);
            // Auto-seleciona se _sourceIpAddress bater E estiver na physicalList
            if (!string.IsNullOrWhiteSpace(_sourceIpAddress) &&
                !_sourceIpAddress.StartsWith("100.") &&
                !_sourceIpAddress.StartsWith("54.232.") &&
                physicalList.Any(p => p.Item2 == _sourceIpAddress) &&
                item.Item2 == _sourceIpAddress)
            {
                selectedItem = cbi;
            }
        }
        CmbSourceInterface.SelectedItem = selectedItem ?? autoItem;

        // Preenche ComboBox da Aba 2 (Escuta Loop Digital)
        CmbLoopInterface.Items.Clear();
        CmbLoopInterface.Items.Add(new ComboBoxItem { Content = "0.0.0.0 (Todas as Interfaces)", Tag = null });
        foreach (var item in allList)
        {
            CmbLoopInterface.Items.Add(new ComboBoxItem { Content = item.Item1, Tag = item.Item2 });
        }
        CmbLoopInterface.SelectedIndex = 0;
    }

    #region Aba 1: ITU-T Y.1564 (Master)

    private void CmbTestMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTestMode?.SelectedItem is ComboBoxItem item)
        {
            var isGateway = item.Tag?.ToString() == "gateway";
            if (TxtRemoteIpLabel != null)
            {
                TxtRemoteIpLabel.Text = isGateway ? "IP do Gateway / PE (Claro):" : "IP do QT Remoto (VIAVI):";
            }
            if (TxtModeDescription != null)
            {
                TxtModeDescription.Text = isGateway
                    ? "Injeta carga de estresse até o CIR e monitora saturação do link via sondas contínuas."
                    : "Mede vazão real L1 (Rx), perda (FLR) e latência de ida e volta (Requer Refletor na porta UDP).";
            }
            if (TxtMetricRxRateHeader != null)
            {
                TxtMetricRxRateHeader.Text = isGateway ? "L1 Mbps (Carga Tx)" : "L1 Mbps (ULR / Rx)";
            }
            if (TxtMetricLossHeader != null)
            {
                TxtMetricLossHeader.Text = isGateway ? "Perda sob Carga (FLR)" : "Loss (Perda de Quadros)";
            }
            if (TxtRemotePort != null)
            {
                TxtRemotePort.IsEnabled = !isGateway;
            }
        }
    }

    private void CmbMefTier_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbMefTier?.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            _currentMefProfile = tag switch
            {
                "250" => MefTierProfile.UpTo250Km,
                "7000" => MefTierProfile.UpTo7000Km,
                _ => MefTierProfile.UpTo1200Km
            };

            if (TxtSlaLossInput != null && !TxtSlaLossInput.IsFocused)
            {
                TxtSlaLossInput.Text = _currentMefProfile.FlrMaxPercent.ToString("F2", CultureInfo.InvariantCulture);
            }
            if (TxtSlaDelayInput != null && !TxtSlaDelayInput.IsFocused)
            {
                TxtSlaDelayInput.Text = _currentMefProfile.FtdMaxMs.ToString("F0", CultureInfo.InvariantCulture);
            }
            if (TxtSlaJitterInput != null && !TxtSlaJitterInput.IsFocused)
            {
                TxtSlaJitterInput.Text = _currentMefProfile.IfdvMaxMs.ToString("F0", CultureInfo.InvariantCulture);
            }

            AtualizarRegraSlaResumo();
        }
    }

    private void TxtSlaLossInput_TextChanged(object sender, TextChangedEventArgs e) => AtualizarRegraSlaResumo();
    private void TxtSlaDelayInput_TextChanged(object sender, TextChangedEventArgs e) => AtualizarRegraSlaResumo();
    private void TxtSlaJitterInput_TextChanged(object sender, TextChangedEventArgs e) => AtualizarRegraSlaResumo();

    private void AtualizarRegraSlaResumo()
    {
        double flr = ObterSlaLoss();
        double ftd = ObterSlaDelay();
        double ifdv = ObterSlaJitter();

        if (TxtMefTierSummary != null)
        {
            TxtMefTierSummary.Text = $"Regra SLA: FLR <= {flr:F2}% | FTD <= {ftd:F0}ms | IFDV <= {ifdv:F0}ms";
        }
        if (TxtSlaLoss != null) TxtSlaLoss.Text = $"SLA FLR: <= {flr:F2}%";
        if (TxtSlaDelay != null) TxtSlaDelay.Text = $"SLA FTD: <= {ftd:F0} ms";
        if (TxtSlaJitter != null) TxtSlaJitter.Text = $"SLA IFDV: <= {ifdv:F0} ms";
        if (TxtMetricJitterMax != null) TxtMetricJitterMax.Text = $"Max: — | SLA: {ifdv:F0} ms";
    }

    private double ObterSlaLoss() =>
        double.TryParse(TxtSlaLossInput?.Text?.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v : _currentMefProfile.FlrMaxPercent;

    private double ObterSlaDelay() =>
        double.TryParse(TxtSlaDelayInput?.Text?.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v : _currentMefProfile.FtdMaxMs;

    private double ObterSlaJitter() =>
        double.TryParse(TxtSlaJitterInput?.Text?.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v : _currentMefProfile.IfdvMaxMs;

    private void Log(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLogOutput.ScrollToEnd();
        });
    }

    private async void BtnIniciar_Click(object sender, RoutedEventArgs e)
    {
        var remoteIp = TxtRemoteIp.Text?.Trim();
        if (string.IsNullOrWhiteSpace(remoteIp))
        {
            MessageBox.Show(this, "Informe o endereço IP do testador remoto VIAVI/JDSU (QT).",
                "IP do QT Obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtRemoteIp.Focus();
            return;
        }

        if (!int.TryParse(TxtRemotePort.Text?.Trim(), out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show(this, "Porta UDP inválida. Informe um valor entre 1 e 65535.",
                "Porta Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtRemotePort.Focus();
            return;
        }

        if (!double.TryParse(TxtTargetBandwidth.Text?.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var targetMbps) || targetMbps <= 0)
        {
            MessageBox.Show(this, "Largura de banda CIR inválida. Informe um valor numérico em Mbps (ex: 200).",
                "Banda Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtTargetBandwidth.Focus();
            return;
        }

        var slaLossPercent = ObterSlaLoss();
        var slaDelayMs = ObterSlaDelay();
        var slaJitterMs = ObterSlaJitter();

        var frameSize = 1500;
        var isVariable = false;
        int[]? variableSizes = null;
        var frameLabel = "1500";

        if (CmbFrameSize.SelectedItem is ComboBoxItem itemFrame)
        {
            var tag = itemFrame.Tag?.ToString();
            switch (tag)
            {
                case "imix":
                    isVariable = true;
                    variableSizes = new[] { 64, 512, 1500 };
                    frameLabel = "Variável / IMIX (64, 512, 1500 B)";
                    break;
                case "imix_full":
                    isVariable = true;
                    variableSizes = new[] { 64, 128, 256, 512, 1024, 1500 };
                    frameLabel = "Variável Completo (64-1500 B)";
                    break;
                case "imix_jumbo":
                    isVariable = true;
                    variableSizes = new[] { 64, 512, 1500, 9000 };
                    frameLabel = "Variável c/ Jumbo (64-9000 B)";
                    break;
                case "9000":
                    frameSize = 9000;
                    frameLabel = "9000 (Jumbo)";
                    break;
                case "9216":
                    frameSize = 9216;
                    frameLabel = "9216 (Jumbo Max)";
                    break;
                default:
                    if (int.TryParse(tag, out var fs)) frameSize = fs;
                    frameLabel = frameSize >= 9000 ? $"{frameSize} (Jumbo)" : frameSize.ToString();
                    break;
            }
        }

        var durationMinutes = 15.0;
        if (CmbDuration.SelectedItem is ComboBoxItem itemDur && double.TryParse(itemDur.Tag?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var dur))
        {
            durationMinutes = dur;
        }

        var duration = TimeSpan.FromMinutes(durationMinutes);

        string? selectedSourceIp = null;
        if (CmbSourceInterface.SelectedItem is ComboBoxItem cbiSrc && cbiSrc.Tag != null)
        {
            selectedSourceIp = cbiSrc.Tag.ToString();
        }

        var testMode = (CmbTestMode?.SelectedItem is ComboBoxItem cbiMode && cbiMode.Tag?.ToString() == "gateway")
            ? Y1564TestMode.GatewayTrafficLoad
            : Y1564TestMode.LoopbackBidirectional;

        // Validação preventiva da capacidade física da interface selecionada
        if (!string.IsNullOrWhiteSpace(selectedSourceIp) && IPAddress.TryParse(selectedSourceIp, out var srcIp))
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var ipProps = nic.GetIPProperties();
                if (ipProps.UnicastAddresses.Any(u => u.Address.Equals(srcIp)))
                {
                    if (nic.Speed > 0)
                    {
                        var nicSpeedMbps = nic.Speed / 1_000_000.0;
                        if (targetMbps > (nicSpeedMbps * 0.99))
                        {
                            var resp = MessageBox.Show(this,
                                $"Atenção: A banda CIR solicitada ({targetMbps:F0} Mbps) é superior à capacidade da placa de rede local '{nic.Name}' ({nicSpeedMbps:F0} Mbps)!\n\n" +
                                $"Para testar taxas superiores a 100 Mbps, a placa de rede do computador deve ser Gigabit (1000 Mbps) e estar conectada em porta compatível.\n\nDeseja prosseguir mesmo assim?",
                                "Capacidade da Placa de Rede Excedida", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                            if (resp != MessageBoxResult.Yes)
                            {
                                return;
                            }
                        }
                    }
                    break;
                }
            }
        }

        // Prepara UI para o teste
        BtnIniciar.IsEnabled = false;
        BtnInterromper.IsEnabled = true;
        CmbTestMode.IsEnabled = false;
        TxtRemoteIp.IsEnabled = false;
        TxtRemotePort.IsEnabled = false;
        TxtTargetBandwidth.IsEnabled = false;
        CmbFrameSize.IsEnabled = false;
        TxtSlaLossInput.IsEnabled = false;
        TxtSlaDelayInput.IsEnabled = false;
        TxtSlaJitterInput.IsEnabled = false;
        CmbDuration.IsEnabled = false;
        CmbMefTier.IsEnabled = false;
        CmbSourceInterface.IsEnabled = false;

        BtnVisualizarPdf.Visibility = Visibility.Collapsed;
        BtnSalvarPdfComo.Visibility = Visibility.Collapsed;

        BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); // #0284C7
        TxtStatusGeral.Text = "🔵 CERTIFICANDO (0%)";
        PbTestProgress.Value = 0;
        TxtTimeRemaining.Text = $"Tempo: 00:00:00 / {duration:hh\\:mm\\:ss} (0%)";
        TxtMetricTargetRate.Text = $"Alvo: {targetMbps:F0} Mbps";

        TxtLogOutput.Clear();
        var modeName = testMode == Y1564TestMode.LoopbackBidirectional ? "Y.1564 Loopback Bidirecional" : "Validação contra Gateway (Saturação)";
        Log($"=== INÍCIO DA CERTIFICAÇÃO ITU-T Y.1564 ({modeName}) ===");
        Log($"Destino: QT {remoteIp}:{port}");
        Log($"Parâmetros: CIR = {targetMbps:F0} Mbps, Frame = {frameLabel}, Duração = {durationMinutes} min");
        Log($"Perfil MEF: {_currentMefProfile.Name} (FTD <= {slaDelayMs:F0}ms, IFDV <= {slaJitterMs:F0}ms, FLR <= {slaLossPercent:F2}%)");
        if (!string.IsNullOrWhiteSpace(selectedSourceIp))
        {
            Log($"Interface de Origem Vinculada: {selectedSourceIp}");
        }

        _testCts = new CancellationTokenSource();

        var clientName = SaipParser.CleanRazaoSocial(_circuit?.ClienteRazaoSocial) ?? "NORTEL ELETR";
        var desig = _circuit?.DesignacaoIp ?? _circuit?.NumeroOts ?? "LFS/IP/02924";

        var config = new Y1564TestConfig(
            RemoteIp: remoteIp,
            RemotePort: port,
            TargetBandwidthMbps: targetMbps,
            FrameSizeBytes: frameSize,
            IsVariableFrame: isVariable,
            VariableFrameSizes: variableSizes,
            FrameSizeLabel: frameLabel,
            Duration: duration,
            ClientName: clientName,
            Designation: desig,
            SlaLossPercent: slaLossPercent,
            SlaDelayMs: slaDelayMs,
            SlaJitterMs: slaJitterMs,
            DistanceTier: _currentMefProfile.DistanceLabel,
            SourceIpAddress: selectedSourceIp,
            Mode: testMode);

        var isGatewayMode = testMode == Y1564TestMode.GatewayTrafficLoad;
        var progressHandler = new Progress<Y1564LiveProgress>(p =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!isGatewayMode)
                {
                    if (p.RxPackets > 0)
                    {
                        TxtMetricRxRate.Text = $"{p.CurrentRxMbps:F2} Mbps";
                        TxtMetricLoss.Text = $"{p.CurrentLossPercent:F2} %";
                        TxtMetricLoss.Foreground = p.CurrentLossPercent <= slaLossPercent
                            ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                            : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                        TxtPacketCounters.Text = $"Tx: {p.TxPackets:N0} | Rx: {p.RxPackets:N0} | Perdidos: {Math.Max(0, p.TxPackets - p.RxPackets):N0} ({p.CurrentLossPercent:F2}%) | OOS: {p.OosPackets}";
                    }
                    else
                    {
                        TxtMetricRxRate.Text = "0,00 Mbps";
                        TxtMetricLoss.Text = "100,00 %";
                        TxtMetricLoss.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                        TxtPacketCounters.Text = $"Tx: {p.TxPackets:N0} | Rx: 0 (Sem Refletor) | Perdidos: {p.TxPackets:N0} (100,00%) | OOS: 0";
                    }
                }
                else
                {
                    TxtMetricRxRate.Text = $"{p.CurrentTxMbps:F2} Mbps";
                    TxtMetricLoss.Text = $"{p.CurrentLossPercent:F2} %";
                    TxtMetricLoss.Foreground = p.CurrentLossPercent <= slaLossPercent
                        ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
                        : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    TxtPacketCounters.Text = $"Tx Carga: {p.TxPackets:N0} | Sondas OK: {p.RxPackets:N0} | Perda Carga: {p.CurrentLossPercent:F2}% | Descartes Locais: {p.TxDroppedLocally:N0}";
                }

                if (!isGatewayMode && p.RxPackets == 0)
                {
                    TxtMetricDelay.Text = "— ms";
                    TxtMetricDelayMinMax.Text = p.IcmpDelayAvgMs > 0
                        ? $"Sem Refletor | Ping ICMP: {p.IcmpDelayAvgMs:F1} ms"
                        : "Sem Refletor (Aguardando)";
                    TxtMetricJitter.Text = "— ms";
                    TxtMetricJitterMax.Text = "Sem Refletor";
                }
                else
                {
                    TxtMetricDelay.Text = $"{p.CurrentDelayAvgMs:F2} ms";
                    TxtMetricDelayMinMax.Text = $"Min: {p.CurrentDelayMinMs:F2} ms | Max: {p.CurrentDelayMaxMs:F2} ms";
                    TxtMetricJitter.Text = $"{p.CurrentJitterAvgMs:F2} ms";
                    TxtMetricJitterMax.Text = $"Max: {p.CurrentJitterMaxMs:F2} ms | SLA: {slaJitterMs:F0} ms";
                }

                TxtTimeRemaining.Text = $"Tempo: {p.Elapsed:hh\\:mm\\:ss} / {p.TotalDuration:hh\\:mm\\:ss} ({p.PercentComplete:F0}%)";
                PbTestProgress.Value = p.PercentComplete;

                TxtStatusGeral.Text = $"🔵 EM TESTE ({p.PercentComplete:F0}%)";
            });
        });

        var testService = new Y1564TestService();

        try
        {
            var result = await Task.Run(async () =>
            {
                return await testService.RunTestAsync(config, progressHandler, msg => Log(msg), _testCts.Token);
            });

            _lastResult = result;

            TxtMetricRxRate.Text = $"{result.RxThroughputMbps:F2} Mbps";
            TxtMetricLoss.Text = $"{result.LossPercentage:F2} %";
            TxtSlaLoss.Text = $"SLA FLR: <= {result.SlaLossPercent:F2}%";

            if (!isGatewayMode && result.RxPackets == 0)
            {
                TxtMetricDelay.Text = "— ms";
                TxtMetricDelayMinMax.Text = "Sem Refletor (Fluxo não recebido)";
                TxtMetricJitter.Text = "— ms";
                TxtMetricJitterMax.Text = "Sem Refletor";
            }
            else
            {
                TxtMetricDelay.Text = $"{result.DelayAvgMs:F2} ms";
                TxtMetricDelayMinMax.Text = $"Min: {result.DelayMinMs:F2} ms | Max: {result.DelayMaxMs:F2} ms";
                TxtMetricJitter.Text = $"{result.JitterAvgMs:F2} ms";
                TxtMetricJitterMax.Text = $"Max: {result.JitterMaxMs:F2} ms";
            }

            TxtSlaDelay.Text = $"SLA FTD: <= {result.SlaDelayMs:F0} ms";
            TxtSlaJitter.Text = $"SLA IFDV: <= {result.SlaJitterMs:F0} ms";

            TxtPacketCounters.Text = $"Tx: {result.TxPackets:N0} | Rx: {result.RxPackets:N0} | Perdidos: {Math.Max(0, result.TxPackets - result.RxPackets):N0} ({result.LossPercentage:F2}%) | OOS: {result.OosPackets}";
            PbTestProgress.Value = 100;

            if (result.IsPass)
            {
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtStatusGeral.Text = "🟢 CONCLUÍDO (PASS)";
                Log($"[SUCESSO] Circuito atende integralmente aos requisitos da ITU-T Y.1564 / SLA Claro!");
            }
            else
            {
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                TxtStatusGeral.Text = "🔴 REPROVADO (FAIL)";
                Log($"[ALERTA] Teste finalizado com reprovação (FAIL). Verifique as perdas ou jitter.");
            }

            Log($"Gerando documento oficial 'Certidão de Nascimento - Y.1564' em PDF...");
            _generatedPdfPath = await Y1564PdfReportService.GenerateReportPdfAsync(result, null, CancellationToken.None);
            Log($"[OK] Certidão gerada com sucesso: {_generatedPdfPath}");

            BtnVisualizarPdf.Visibility = Visibility.Visible;
            BtnSalvarPdfComo.Visibility = Visibility.Visible;

            var abrirAgora = MessageBox.Show(
                $"Certidão de Nascimento Y.1564 gerada com sucesso!\n\n" +
                $"Resultado: {(result.IsPass ? "🟢 PASS (Aprovado)" : "🔴 FAIL (Reprovado)")}\n" +
                $"Referência MEF: {result.DistanceTier}\n" +
                $"Vazão L1: {result.RxThroughputMbps:F2} Mbps (Alvo: {result.NetworkUlrMbps:F0} Mbps)\n" +
                $"Perda (FLR): {result.LossPercentage:F2}% (SLA: <= {result.SlaLossPercent:F2}%)\n" +
                $"Latência (FTD): {result.DelayAvgMs:F2} ms (SLA: <= {result.SlaDelayMs:F0} ms)\n" +
                $"Jitter (IFDV): {result.JitterAvgMs:F2} ms (SLA: <= {result.SlaJitterMs:F0} ms)\n\n" +
                $"Deseja abrir o arquivo PDF agora?",
                "SPARC — Certidão de Nascimento Y.1564",
                MessageBoxButton.YesNo,
                result.IsPass ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (abrirAgora == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = _generatedPdfPath, UseShellExecute = true });
                }
                catch { }
            }
        }
        catch (OperationCanceledException)
        {
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            TxtStatusGeral.Text = "⏹ INTERROMPIDO";
            Log("[AVISO] O teste foi interrompido manualmente pelo operador.");
        }
        catch (Exception ex)
        {
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            TxtStatusGeral.Text = "❌ ERRO NO TESTE";
            Log($"[ERRO] Falha durante o teste Y.1564: {ex.Message}");
            MessageBox.Show(this, $"Erro durante a execução da certificação Y.1564:\n\n{ex.Message}",
                "Falha na Certificação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnIniciar.IsEnabled = true;
            BtnInterromper.IsEnabled = false;
            CmbTestMode.IsEnabled = true;
            TxtRemoteIp.IsEnabled = true;
            TxtRemotePort.IsEnabled = true;
            TxtTargetBandwidth.IsEnabled = true;
            CmbFrameSize.IsEnabled = true;
            TxtSlaLossInput.IsEnabled = true;
            TxtSlaDelayInput.IsEnabled = true;
            TxtSlaJitterInput.IsEnabled = true;
            CmbDuration.IsEnabled = true;
            CmbMefTier.IsEnabled = true;
            CmbSourceInterface.IsEnabled = true;
            _testCts?.Dispose();
            _testCts = null;
        }
    }

    private void BtnInterromper_Click(object sender, RoutedEventArgs e)
    {
        if (_testCts != null && !_testCts.IsCancellationRequested)
        {
            Log("[COMANDO] Solicitando interrupção imediata do fluxo Y.1564...");
            _testCts.Cancel();
            BtnInterromper.IsEnabled = false;
        }
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
                MessageBox.Show(this, $"Não foi possível abrir o arquivo PDF:\n{ex.Message}", "Visualizar PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show(this, "Nenhum arquivo PDF foi gerado ou o arquivo foi movido.", "Visualizar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnSalvarPdfComo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_generatedPdfPath) || !File.Exists(_generatedPdfPath))
        {
            MessageBox.Show(this, "O relatório PDF ainda não foi gerado.", "Salvar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var desig = _circuit?.DesignacaoIp ?? _circuit?.NumeroOts ?? "Circuito";
        foreach (var c in Path.GetInvalidFileNameChars()) desig = desig.Replace(c, '_');

        var dlg = new SaveFileDialog
        {
            Title = "Salvar Certidão de Nascimento Y.1564 em PDF",
            Filter = "Documento PDF (*.pdf)|*.pdf|Todos os Arquivos (*.*)|*.*",
            FileName = $"Certidao_Y1564_{desig}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.Copy(_generatedPdfPath, dlg.FileName, true);
                Log($"[OK] Certidão copiada para: {dlg.FileName}");
                MessageBox.Show(this, $"Certidão salva com sucesso em:\n{dlg.FileName}", "Salvar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Erro ao salvar o arquivo PDF:\n{ex.Message}", "Erro ao Salvar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region Aba 2: Loop Digital (Smart Reflector VIAVI / JDSU)



    private void LogLoop(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLoopLogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            TxtLoopLogOutput.ScrollToEnd();
        });
    }

    private void VerificarNpcapStatus()
    {
        bool npcapOk = PromiscuousLoopbackService.IsNpcapInstalled();
        PnlNpcapWarning.Visibility = npcapOk ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BtnInstalarNpcap_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var setupPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "npcap-setup.exe");
            if (!System.IO.File.Exists(setupPath))
            {
                setupPath = @"C:\SPARC\npcap-setup.exe";
            }

            if (System.IO.File.Exists(setupPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = setupPath,
                    UseShellExecute = true,
                    Verb = "runas"
                });
                MessageBox.Show(this, "O instalador do Npcap foi aberto.\n\nSiga os passos do instalador na tela (I Agree -> Install -> Finish).\nApós concluir a instalação, o modo promíscuo de enlace L2/L3 estará 100% ativo!",
                    "Instalação do Npcap", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = "https://npcap.com/#download", UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao abrir o instalador do Npcap: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CmbLoopInterface_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string? target = null;
        if (CmbLoopInterface?.SelectedItem is ComboBoxItem cbi)
        {
            target = cbi.Tag?.ToString();
            if (string.IsNullOrEmpty(target))
            {
                target = cbi.Content?.ToString();
            }
        }
        AtualizarStatusLinkFisico(target);
    }

    private void AtualizarStatusLinkFisico(string? adapterNameOrIp)
    {
        if (TxtLoopNicSpeedBadge == null) return;

        if (string.IsNullOrWhiteSpace(adapterNameOrIp) || adapterNameOrIp.Contains("0.0.0.0"))
        {
            TxtLoopNicSpeedBadge.Text = "Todas as interfaces ativas";
            TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            return;
        }

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                var ipList = ni.GetIPProperties().UnicastAddresses.Select(u => u.Address.ToString()).ToList();
                var matchIp = ipList.Any(ip => adapterNameOrIp.Contains(ip));
                var matchName = ni.Name.Equals(adapterNameOrIp, StringComparison.OrdinalIgnoreCase) ||
                                ni.Description.Equals(adapterNameOrIp, StringComparison.OrdinalIgnoreCase) ||
                                adapterNameOrIp.Contains(ni.Name, StringComparison.OrdinalIgnoreCase);

                if (matchIp || matchName)
                {
                    if (ni.OperationalStatus != OperationalStatus.Up)
                    {
                        TxtLoopNicSpeedBadge.Text = "⚪ Cabo Desconectado";
                        TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(244, 63, 94));
                        return;
                    }

                    long speedBps = ni.Speed;
                    if (speedBps >= 10_000_000_000)
                    {
                        TxtLoopNicSpeedBadge.Text = $"🟢 10 Gbps ({ni.NetworkInterfaceType})";
                        TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    }
                    else if (speedBps >= 1_000_000_000)
                    {
                        TxtLoopNicSpeedBadge.Text = $"🟢 1.000 Mbps Gigabit Full Duplex";
                        TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                    }
                    else if (speedBps >= 100_000_000)
                    {
                        TxtLoopNicSpeedBadge.Text = $"⚠️ 100 Mbps (Gargalo Físico Detectado)";
                        TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    }
                    else if (speedBps > 0)
                    {
                        TxtLoopNicSpeedBadge.Text = $"⚠️ {speedBps / 1_000_000.0:F0} Mbps (Velocidade Baixa)";
                        TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                    }
                    else
                    {
                        TxtLoopNicSpeedBadge.Text = $"🟢 Link Ativo ({ni.OperationalStatus})";
                        TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                    }
                    return;
                }
            }

            TxtLoopNicSpeedBadge.Text = "Interface ativa";
            TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        }
        catch
        {
            TxtLoopNicSpeedBadge.Text = "Status indisponível";
            TxtLoopNicSpeedBadge.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }
    }

    private void CmbLoopMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbLoopMode?.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
        {
            bool isUdp = tag == "UdpSocket";
            if (TxtLoopPort != null)
            {
                TxtLoopPort.IsEnabled = isUdp;
            }
            if (PnlLoopPort != null)
            {
                PnlLoopPort.Opacity = isUdp ? 1.0 : 0.4;
            }
        }
    }

    private void BtnLoopIniciar_Click(object sender, RoutedEventArgs e)
    {
        var selectedMode = (CmbLoopMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PromiscuousL3";
        string adapterNameOrIp = string.Empty;
        IPAddress? bindAddress = null;

        if (CmbLoopInterface.SelectedItem is ComboBoxItem cbi && cbi.Tag != null)
        {
            adapterNameOrIp = cbi.Tag.ToString() ?? string.Empty;
            if (IPAddress.TryParse(adapterNameOrIp, out var parsedIp))
            {
                bindAddress = parsedIp;
            }
        }

        // Extrai parâmetros de performance e anti-bufferbloat
        int bufferSizeBytes = 2 * 1024 * 1024;
        if (CmbLoopBufferSize?.SelectedItem is ComboBoxItem bufItem && int.TryParse(bufItem.Tag?.ToString(), out var parsedBuf))
        {
            bufferSizeBytes = parsedBuf;
        }

        int workerCount = 0;
        if (CmbLoopWorkers?.SelectedItem is ComboBoxItem workItem && int.TryParse(workItem.Tag?.ToString(), out var parsedWorkers))
        {
            workerCount = parsedWorkers;
        }

        bool enableBpf = ChkLoopBpfFilter?.IsChecked == true;

        if (selectedMode is "PromiscuousL3" or "PromiscuousL2")
        {
            if (!PromiscuousLoopbackService.IsNpcapInstalled())
            {
                var r = MessageBox.Show(this,
                    "O driver de enlace Npcap não foi detectado no sistema.\n\n" +
                    "Para fechar loop promíscuo no padrão MTS-5800 (L2/L3 Swap com Acterna BERT/IPoE), é necessário o Npcap.\n\n" +
                    "Deseja iniciar a instalação do Npcap agora?",
                    "Driver Npcap Necessário", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (r == MessageBoxResult.Yes)
                {
                    BtnInstalarNpcap_Click(this, new RoutedEventArgs());
                }
                return;
            }

            try
            {
                var loopMode = selectedMode == "PromiscuousL2" ? LoopbackMode.Layer2MacSwap : LoopbackMode.Layer3IpSwap;
                _promiscuousLoopService = new PromiscuousLoopbackService();
                _promiscuousLoopService.StatsUpdated += OnPromiscuousStatsUpdated;
                _promiscuousLoopService.LogMessage += LogLoop;

                _promiscuousLoopService.Start(adapterNameOrIp, loopMode, bufferSizeBytes, enableBpf);

                BadgeLoopStatus.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtLoopStatus.Text = selectedMode == "PromiscuousL2" 
                    ? "🟢 LOOP PROMÍSCUO L2 ATIVO (MAC SWAP)" 
                    : "🟢 LOOP PROMÍSCUO L3 ATIVO (MTS-5800 SWAP)";
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtStatusGeral.Text = "🟢 LOOP PROMÍSCUO ATIVO";

                BtnLoopIniciar.IsEnabled = false;
                BtnLoopParar.IsEnabled = true;
                CmbLoopMode.IsEnabled = false;
                CmbLoopInterface.IsEnabled = false;
                if (CmbLoopBufferSize != null) CmbLoopBufferSize.IsEnabled = false;
                if (CmbLoopWorkers != null) CmbLoopWorkers.IsEnabled = false;
                if (ChkLoopBpfFilter != null) ChkLoopBpfFilter.IsEnabled = false;

                TxtLoopLogOutput.Clear();
                LogLoop($"=== LOOP PROMÍSCUO DE ENLACE ATIVADO NA MÁQUINA LOCAL ===");
                LogLoop($"Modo: {(selectedMode == "PromiscuousL2" ? "Layer 2 MAC Swap" : "Layer 3 IP Swap (MTS-5800 Acterna BERT / IPoE)")}.");
                LogLoop($"Interface: {adapterNameOrIp}. Driver: {PromiscuousLoopbackService.GetPcapVersion()}.");
                LogLoop($"Otimizações: Buffer Anti-Bufferbloat {bufferSizeBytes / 1024} KB, Pipeline SendQueue, BPF Kernel: {(enableBpf ? "Sim" : "Não")}.");
                LogLoop($"Refletindo 100% dos quadros na taxa física de linha. O gerador VIAVI/JDSU já pode injetar tráfego.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Erro ao iniciar Loop Promíscuo L2/L3:\n\n{ex.Message}", "Falha", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            // Modo L4 UDP Socket
            if (!int.TryParse(TxtLoopPort.Text?.Trim(), out var port) || port < 1 || port > 65535)
            {
                MessageBox.Show(this, "Porta UDP para Loop Digital inválida. Informe um valor entre 1 e 65535.",
                    "Porta Inválida", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtLoopPort.Focus();
                return;
            }

            try
            {
                _loopService = new DigitalLoopbackService();
                _loopService.StatsUpdated += OnLoopStatsUpdated;
                _loopService.LogMessage += LogLoop;

                _loopService.Start(port, bindAddress, workerCount, bufferSizeBytes);

                BadgeLoopStatus.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtLoopStatus.Text = "🟢 LOOP DIGITAL ATIVO — REFLETINDO UDP";
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                TxtStatusGeral.Text = "🟢 LOOP DIGITAL ATIVO";

                BtnLoopIniciar.IsEnabled = false;
                BtnLoopParar.IsEnabled = true;
                TxtLoopPort.IsEnabled = false;
                CmbLoopMode.IsEnabled = false;
                CmbLoopInterface.IsEnabled = false;
                if (CmbLoopBufferSize != null) CmbLoopBufferSize.IsEnabled = false;
                if (CmbLoopWorkers != null) CmbLoopWorkers.IsEnabled = false;
                if (ChkLoopBpfFilter != null) ChkLoopBpfFilter.IsEnabled = false;

                TxtLoopLogOutput.Clear();
                LogLoop($"=== LOOP DIGITAL UDP ATIVADO NA MÁQUINA LOCAL ===");
                LogLoop($"Refletor UDP operando na porta {port} (Bind: {bindAddress?.ToString() ?? "0.0.0.0"}).");
                LogLoop($"Otimizações: Buffer {bufferSizeBytes / 1024} KB, Workers: {(workerCount == 0 ? "Multi-core Auto" : workerCount.ToString())}.");
                LogLoop($"O testador remoto (VIAVI, JDSU, EXFO, Iperf ou SPARC) já pode iniciar injeção de tráfego.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Não foi possível iniciar o Loop Digital:\n\n{ex.Message}",
                    "Falha ao Iniciar Loop", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnLoopParar_Click(object sender, RoutedEventArgs e)
    {
        if (_promiscuousLoopService != null)
        {
            LogLoop("[COMANDO] Parando refletor promíscuo L2/L3...");
            await _promiscuousLoopService.StopAsync();
            _promiscuousLoopService.Dispose();
            _promiscuousLoopService = null;
        }

        if (_loopService != null)
        {
            LogLoop("[COMANDO] Parando refletor digital UDP...");
            await _loopService.StopAsync();
            _loopService.Dispose();
            _loopService = null;
        }

        BadgeLoopStatus.Background = new SolidColorBrush(Color.FromRgb(39, 39, 42));
        TxtLoopStatus.Text = "⚪ LOOP DIGITAL DESLIGADO";
        BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));
        TxtStatusGeral.Text = "⚪ PRONTO PARA TESTES";

        BtnLoopIniciar.IsEnabled = true;
        BtnLoopParar.IsEnabled = false;
        CmbLoopMode.IsEnabled = true;
        CmbLoopInterface.IsEnabled = true;
        if (CmbLoopBufferSize != null) CmbLoopBufferSize.IsEnabled = true;
        if (CmbLoopWorkers != null) CmbLoopWorkers.IsEnabled = true;
        if (ChkLoopBpfFilter != null) ChkLoopBpfFilter.IsEnabled = true;
        if (CmbLoopMode.SelectedItem is ComboBoxItem cbi && cbi.Tag?.ToString() == "UdpSocket")
        {
            TxtLoopPort.IsEnabled = true;
        }
    }

    private void BtnRefreshInterfaces_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CarregarAdaptadoresDeRede();

            string? target = null;
            if (CmbLoopInterface?.SelectedItem is ComboBoxItem cbi)
            {
                target = cbi.Tag?.ToString() ?? cbi.Content?.ToString();
            }
            AtualizarStatusLinkFisico(target);

            LogLoop("[SISTEMA] 🔄 Interfaces de rede e endereços IPv4 recapturados com sucesso.");
            Log("[SISTEMA] 🔄 Interfaces de rede e endereços IPv4 recapturados com sucesso.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao atualizar interfaces: {ex.Message}", "Atualização", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnLoopZerar_Click(object sender, RoutedEventArgs e)
    {
        _promiscuousLoopService?.ResetStats();
        _loopService?.ResetStats();

        TxtLoopRxMbps.Text = "0.00 Mbps";
        TxtLoopRxPps.Text = "0 pps";
        TxtLoopTxMbps.Text = "0.00 Mbps";
        TxtLoopTxPps.Text = "0 pps";
        TxtLoopTotalPkts.Text = "0";
        TxtLoopPktsRxVsTx.Text = "Rx: 0 | Tx: 0";
        TxtLoopTotalBytes.Text = "0.00 MB";
        TxtLoopUptime.Text = "Tempo: 00:00:00";
        TxtLoopLastRemote.Text = "Aguardando tráfego...";

        LogLoop("[SISTEMA] 🧹 Contadores de teste e volume de pacotes zerados pelo operador.");
    }

    private void BtnLimparLogLoop_Click(object sender, RoutedEventArgs e)
    {
        TxtLoopLogOutput.Clear();
        LogLoop("[SISTEMA] 🧹 Log de tráfego do refletor limpo.");
    }

    private void BtnY1564Zerar_Click(object sender, RoutedEventArgs e)
    {
        TxtMetricRxRate.Text = "— Mbps";
        TxtMetricLoss.Text = "— %";
        TxtMetricDelay.Text = "— ms";
        TxtMetricDelayMinMax.Text = "Min: — | Max: —";
        TxtMetricJitter.Text = "— ms";
        TxtMetricJitterMax.Text = "Max: —";
        TxtTimeRemaining.Text = "Tempo: 00:00:00 / 00:00:00 (0%)";
        TxtPacketCounters.Text = "Tx: 0 | Rx: 0 | Perdidos: 0 (0.00%) | OOS: 0";
        PbTestProgress.Value = 0;
        TxtLogOutput.Clear();
        Log("[SISTEMA] 🧹 Resultados e métricas da certificação Y.1564 foram limpos.");
    }

    private void BtnMtuZerar_Click(object sender, RoutedEventArgs e)
    {
        TxtMtuDetected.Text = "— Bytes";
        TxtMtuStatusBadge.Text = "Aguardando teste...";
        TxtMtuJumboSupport.Text = "—";
        TxtMtuRtt.Text = "— ms";
        TxtMtuLogOutput.Clear();
        TxtMtuLogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] [SISTEMA] 🧹 Dados do teste de MTU limpos.\n");
    }

    private void OnPromiscuousStatsUpdated(PromiscuousLoopStats stats)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLoopRxMbps.Text = $"{stats.CurrentRxMbps:F2} Mbps";
            TxtLoopRxPps.Text = $"{stats.CurrentRxPps:N0} pps";
            TxtLoopTxMbps.Text = $"{stats.CurrentTxMbps:F2} Mbps";
            TxtLoopTxPps.Text = $"{stats.CurrentTxPps:N0} pps";

            TxtLoopTotalPkts.Text = $"{stats.TotalPacketsTx:N0}";
            TxtLoopPktsRxVsTx.Text = $"Rx: {stats.TotalPacketsRx:N0} | Tx: {stats.TotalPacketsTx:N0}";

            double mb = stats.TotalBytesTx / (1024.0 * 1024.0);
            TxtLoopTotalBytes.Text = mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F2} MB";
            TxtLoopUptime.Text = $"Tempo: {stats.Elapsed:hh\\:mm\\:ss}";

            if (!string.IsNullOrWhiteSpace(stats.LastRemoteMac))
            {
                TxtLoopLastRemote.Text = !string.IsNullOrWhiteSpace(stats.LastRemoteIp)
                    ? $"{stats.LastRemoteIp} ({stats.LastRemoteMac})"
                    : stats.LastRemoteMac;
            }
        });
    }

    private void OnLoopStatsUpdated(DigitalLoopbackStats stats)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLoopRxMbps.Text = $"{stats.CurrentRxMbps:F2} Mbps";
            TxtLoopRxPps.Text = $"{stats.CurrentRxPps:N0} pps";
            TxtLoopTxMbps.Text = $"{stats.CurrentTxMbps:F2} Mbps";
            TxtLoopTxPps.Text = $"{stats.CurrentTxPps:N0} pps";

            TxtLoopTotalPkts.Text = $"{stats.TotalPacketsTx:N0}";
            TxtLoopPktsRxVsTx.Text = $"Rx: {stats.TotalPacketsRx:N0} | Tx: {stats.TotalPacketsTx:N0}";

            double mb = stats.TotalBytesTx / (1024.0 * 1024.0);
            TxtLoopTotalBytes.Text = mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F2} MB";
            TxtLoopUptime.Text = $"Tempo: {stats.Elapsed:hh\\:mm\\:ss}";

            if (!string.IsNullOrWhiteSpace(stats.LastRemoteEndPoint))
            {
                TxtLoopLastRemote.Text = stats.LastRemoteEndPoint;
            }
        });
    }

    private void BtnCopiarComandoCiscoSla_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText("ip sla responder");
            MessageBox.Show(this, "Comando 'ip sla responder' copiado para a área de transferência!\n\nVocê pode colar diretamente no terminal de configuração global do Cisco IOS (conf t).",
                "Comando Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    #endregion

    #region Aba 3: Teste de MTU & Conectividade (Path MTU Discovery)

    private async void BtnMtuTestar_Click(object sender, RoutedEventArgs e)
    {
        var targetIp = TxtMtuTargetIp.Text?.Trim();
        if (string.IsNullOrWhiteSpace(targetIp))
        {
            MessageBox.Show(this, "Informe o endereço IP de destino para o teste de MTU.",
                "IP de Destino Obrigatório", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtMtuTargetIp.Focus();
            return;
        }

        BtnMtuTestar.IsEnabled = false;
        TxtMtuDetected.Text = "Testando...";
        TxtMtuStatusBadge.Text = "Descobrindo MTU...";
        TxtMtuJumboSupport.Text = "—";
        TxtMtuRtt.Text = "—";
        TxtMtuLogOutput.Clear();

        _mtuCts = new CancellationTokenSource();
        IProgress<string> progress = new Progress<string>(msg =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtMtuLogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
                TxtMtuLogOutput.ScrollToEnd();
            });
        });

        try
        {
            var res = await _mtuService.DiscoverPathMtuAsync(targetIp, progress, _mtuCts.Token);

            TxtMtuDetected.Text = res.DiscoveredPathMtu > 0 ? $"{res.DiscoveredPathMtu} B" : "Falha";
            TxtMtuStatusBadge.Text = res.SupportsStandardMtu1500 ? "✅ MTU 1500 OK" : "⚠️ MTU Restrito";
            TxtMtuJumboSupport.Text = res.SupportsJumboFrame9000 ? "✅ Sim (9000 B)" : "❌ Não (Padrão 1500)";
            TxtMtuRtt.Text = res.LatencyMs >= 0 ? $"{res.LatencyMs} ms" : "Timeout";

            progress.Report($"[RESUMO] {res.Details}");
        }
        catch (Exception ex)
        {
            TxtMtuDetected.Text = "Erro";
            TxtMtuStatusBadge.Text = "Falha no Teste";
            progress.Report($"[ERRO] {ex.Message}");
        }
        finally
        {
            BtnMtuTestar.IsEnabled = true;
            _mtuCts?.Dispose();
            _mtuCts = null;
        }
    }

    #endregion

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Y1564TestWindow_Closed(object? sender, EventArgs e)
    {
        if (_promiscuousLoopService != null)
        {
            try
            {
                await _promiscuousLoopService.StopAsync();
                _promiscuousLoopService.Dispose();
                _promiscuousLoopService = null;
            }
            catch { }
        }

        if (_loopService != null)
        {
            try
            {
                await _loopService.StopAsync();
                _loopService.Dispose();
                _loopService = null;
            }
            catch { }
        }

        if (_testCts != null)
        {
            try
            {
                _testCts.Cancel();
                _testCts.Dispose();
                _testCts = null;
            }
            catch { }
        }

        if (_mtuCts != null)
        {
            try
            {
                _mtuCts.Cancel();
                _mtuCts.Dispose();
                _mtuCts = null;
            }
            catch { }
        }
    }
}
