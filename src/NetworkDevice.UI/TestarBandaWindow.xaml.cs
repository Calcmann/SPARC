using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Diagnostics.Broadband;
using NetworkDevice.Core.Provisioning;

namespace NetworkDevice.UI;

/// <summary>
/// Módulo unificado de Teste de Banda Larga do SPARC.
/// Integra em abas:
///  - Aba 1: Telemetria de Banda Larga &amp; Qualidade (Speed.Cloudflare Anycast, Jitter, Bufferbloat, Topologia Óptica)
///  - Aba 2: Testar Link sem Roteador (Modo Acesso / WAN Direta EDD / GPON com IP estático e testes ICMP)
/// </summary>
public partial class TestarBandaWindow : Window
{
    #region Estado da Aba 1 (Telemetria de Banda Larga)

    private readonly BroadbandTestEngine _engine;
    private CancellationTokenSource? _ctsTelemetria;
    private List<BroadbandServerInfo> _discoveredServers = new();
    private BroadbandGeoInfo? _currentGeoInfo;
    private BroadbandTestResult? _lastResultTelemetria;
    private double? _nominalMbps;
    private string? _ultimoPdfGeradoTelemetria;

    private readonly List<Point> _dlPointsCanvas = new();
    private readonly List<Point> _ulPointsCanvas = new();
    private readonly List<(double StepRatio, double SpeedMbps)> _dlRawValues = new();
    private readonly List<(double StepRatio, double SpeedMbps)> _ulRawValues = new();
    private double _canvasMaxSpeed = 50.0;

    #endregion

    #region Estado da Aba 2 (Link sem Roteador / Modo Acesso)

    private readonly SaipCircuitData? _circuit;
    private CancellationTokenSource? _testCtsAcesso;
    private string? _generatedPdfPathAcesso;
    private bool _isIpWanApplied;
    private string? _appliedAdapterName;

    private ConnectivityTestResult? _peResult;
    private ConnectivityTestResult? _googleResult;
    private ConnectivityTestResult? _cloudflareResult;
    private BandwidthTestResult? _bandResult;

    #endregion

    public TestarBandaWindow(SaipCircuitData? circuit = null, int initialTabIndex = 0)
    {
        InitializeComponent();

        _circuit = circuit;
        _nominalMbps = circuit?.BandaMbpsNominal;
        _engine = new BroadbandTestEngine(LogTelemetriaAsync);

        if (initialTabIndex >= 0 && initialTabIndex < MainTabControl.Items.Count)
        {
            MainTabControl.SelectedIndex = initialTabIndex;
        }

        // Inicializa Aba 2 com dados da Ficha SAIP se disponível
        PreencherDadosCircuitoAcesso();
        CarregarAdaptadoresDeRedeAcesso();

        Loaded += TestarBandaWindow_Loaded;
    }

    private async void TestarBandaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CarregarAdaptadoresRedeTelemetria();
        await DescobrirServidoresEGeolocalizacaoAsync();
    }

    #region Lógica da Aba 1: Telemetria de Banda Larga & Qualidade

    private void CarregarAdaptadoresRedeTelemetria()
    {
        try
        {
            CbAdaptadoresRede.Items.Clear();
            CbAdaptadoresRede.Items.Add(new ComboBoxItem
            {
                Content = "— Todas as Interfaces (Padrão do SO) —",
                Tag = null,
                IsSelected = true
            });

            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderBy(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1);

            foreach (var ni in interfaces)
            {
                var ipProp = ni.GetIPProperties();
                var ipv4 = ipProp.UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
                    .Address.ToString();

                var tipo = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "🔌 [Ethernet]" : "📶 [Wi-Fi/Outro]";
                var label = $"{tipo} {ni.Name} ({ipv4 ?? "sem IPv4"})";

                CbAdaptadoresRede.Items.Add(new ComboBoxItem
                {
                    Content = label,
                    Tag = ipv4
                });
            }
        }
        catch (Exception ex)
        {
            LogTelemetria($"[AVISO] Erro ao listar adaptadores de rede: {ex.Message}");
        }
    }

    private async Task DescobrirServidoresEGeolocalizacaoAsync()
    {
        BtnRecarregarServidores.IsEnabled = false;
        TxtInfoProximidade.Text = "Descobrindo geolocalização do link e listando servidores mais próximos...";

        try
        {
            var srcIp = GetSelectedSourceIpTelemetria();
            _currentGeoInfo = await _engine.GetClientGeoInfoAsync(srcIp);

            if (_currentGeoInfo != null)
            {
                TxtGeoIpPublico.Text = $"IP: {_currentGeoInfo.PublicIp}";
                TxtGeoAsn.Text = $"ASN: {_currentGeoInfo.Asn}";
                TxtGeoLocalidade.Text = $"Local: {_currentGeoInfo.ClientCity}, {_currentGeoInfo.ClientCountry}";
                TxtGeoCoordenadas.Text = _currentGeoInfo.ClientLatitude.HasValue
                    ? $"GPS: {_currentGeoInfo.ClientLatitude:F4}, {_currentGeoInfo.ClientLongitude:F4}"
                    : "GPS Indisponível";

                TxtGeoPoP.Text = $"PoP: {_currentGeoInfo.ServerColoIata} ({_currentGeoInfo.ServerCity})";
                TxtGeoServidor.Text = $"Servidor: {_currentGeoInfo.ServerCity}";
                TxtGeoServidorLocal.Text = $"Localidade: {_currentGeoInfo.ServerCountry}";
                TxtDistanciaKm.Text = _currentGeoInfo.DistanceKm.HasValue
                    ? $"~{_currentGeoInfo.DistanceKm:F1} km"
                    : "Estimando...";
            }

            _discoveredServers = await _engine.DiscoverServersAsync(_currentGeoInfo, srcIp);

            CbServidores.Items.Clear();
            CbServidores.Items.Add(new ComboBoxItem
            {
                Content = "⭐ [AUTOMÁTICO] Menor Distância + Menor Latência (Recomendado)",
                Tag = null,
                IsSelected = true
            });

            foreach (var s in _discoveredServers)
            {
                CbServidores.Items.Add(new ComboBoxItem
                {
                    Content = s.DisplayText,
                    Tag = s
                });
            }

            TxtInfoProximidade.Text = $"✓ {_discoveredServers.Count} servidores encontrados e ordenados por proximidade e menor latência. Fallback inteligente ativo.";
        }
        catch (Exception ex)
        {
            TxtInfoProximidade.Text = $"[AVISO] Erro na descoberta automática de servidores: {ex.Message}";
        }
        finally
        {
            BtnRecarregarServidores.IsEnabled = true;
        }
    }

    private string? GetSelectedSourceIpTelemetria()
    {
        if (CbAdaptadoresRede.SelectedItem is ComboBoxItem item && item.Tag is string ip)
        {
            return ip;
        }
        return null;
    }

    private async void BtnRecarregarServidores_Click(object sender, RoutedEventArgs e)
    {
        await DescobrirServidoresEGeolocalizacaoAsync();
    }

    private void CbServidores_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CbServidores.SelectedItem is ComboBoxItem item && item.Tag is BroadbandServerInfo server)
        {
            TxtGeoServidor.Text = $"Servidor: {server.Sponsor}";
            TxtGeoPoP.Text = $"Alvo: {server.Location}";
            TxtGeoServidorLocal.Text = $"País: {server.Country}";
            TxtGeoTecnologia.Text = $"Tipo: {server.ServerType}";

            double? dist = server.DistanceKm;
            if (!dist.HasValue && _currentGeoInfo?.ClientLatitude != null && _currentGeoInfo.ClientLongitude != null)
            {
                var coords = BroadbandTestEngine.ResolveCityCoordinates(server.Location);
                if (coords.HasValue)
                {
                    dist = BroadbandTestEngine.CalculateDistanceKm(
                        _currentGeoInfo.ClientLatitude.Value,
                        _currentGeoInfo.ClientLongitude.Value,
                        coords.Value.Lat,
                        coords.Value.Lon);
                }
            }

            TxtDistanciaKm.Text = dist.HasValue
                ? $"~{dist.Value:F1} km"
                : "Estimando...";
        }
        else if (_currentGeoInfo != null)
        {
            TxtGeoServidor.Text = $"Servidor: {_currentGeoInfo.ServerCity}";
            TxtGeoPoP.Text = $"PoP: {_currentGeoInfo.ServerColoIata}";
            TxtGeoServidorLocal.Text = $"País: {_currentGeoInfo.ServerCountry}";
            TxtDistanciaKm.Text = _currentGeoInfo.DistanceKm.HasValue
                ? $"~{_currentGeoInfo.DistanceKm:F1} km"
                : "Estimando...";
        }
    }

    private async void BtnIniciarTelemetria_Click(object sender, RoutedEventArgs e)
    {
        _ctsTelemetria?.Dispose();
        _ctsTelemetria = new CancellationTokenSource();

        BtnIniciarTelemetria.IsEnabled = false;
        BtnCancelarTelemetria.IsEnabled = true;
        BtnGerarPdfTelemetria.IsEnabled = false;
        CbServidores.IsEnabled = false;
        CbAdaptadoresRede.IsEnabled = false;
        BtnRecarregarServidores.IsEnabled = false;

        BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199));
        TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        TxtStatusGeral.Text = "EXECUTANDO TELEMETRIA...";

        ResetarGraficoEIndicadoresTelemetria();

        var selectedServer = (CbServidores.SelectedItem as ComboBoxItem)?.Tag as BroadbandServerInfo;
        var srcIp = GetSelectedSourceIpTelemetria();

        var progressHandler = new Progress<BroadbandProgressEvent>(OnProgressEventTelemetria);

        try
        {
            LogTelemetria("[*] Disparando esteira de medição de banda larga com telemetria contínua...");
            _lastResultTelemetria = await _engine.RunTestWithFallbackAsync(
                selectedServer,
                _discoveredServers,
                srcIp,
                progressHandler,
                _ctsTelemetria.Token);

            if (_nominalMbps.HasValue && _nominalMbps.Value > 0)
            {
                _lastResultTelemetria = _lastResultTelemetria with
                {
                    AvaliacaoNominal = BandwidthTestService.AvaliarBanda(_nominalMbps, _lastResultTelemetria.DownloadAvgMbps)
                };
            }

            AtualizarResultadoFinalTelemetria(_lastResultTelemetria);
        }
        catch (OperationCanceledException)
        {
            LogTelemetria("[!] Teste cancelado pelo operador.");
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            TxtStatusGeral.Text = "TESTE CANCELADO";
            TxtOverlayGrafico.Text = "Teste cancelado.";
            OverlayContainer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LogTelemetria($"[ERRO] Falha crítica na execução do teste: {ex.Message}");
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusGeral.Text = "FALHA NO TESTE";
            TxtOverlayGrafico.Text = $"Falha: {ex.Message}";
            OverlayContainer.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnIniciarTelemetria.IsEnabled = true;
            BtnCancelarTelemetria.IsEnabled = false;
            CbServidores.IsEnabled = true;
            CbAdaptadoresRede.IsEnabled = true;
            BtnRecarregarServidores.IsEnabled = true;
        }
    }

    private void ResetarGraficoEIndicadoresTelemetria()
    {
        TxtDlTelemetriaMbps.Text = "0.0";
        TxtUlTelemetriaMbps.Text = "0.0";
        TxtDlTelemetriaPico.Text = "Pico: 0.0 Mbps";
        TxtUlTelemetriaPico.Text = "Pico: 0.0 Mbps";
        TxtLatenciaMs.Text = "0";
        TxtJitterMs.Text = "0.0 ms";
        TxtPacketLoss.Text = "0%";
        TxtBufferbloatGrade.Text = "—";
        TxtBufferbloatDesc.Text = "Aguardando";
        PbDlTelemetria.Value = 0;
        PbUlTelemetria.Value = 0;

        _dlRawValues.Clear();
        _ulRawValues.Clear();
        _dlPointsCanvas.Clear();
        _ulPointsCanvas.Clear();
        PolylineDownload.Points.Clear();
        PolylineUpload.Points.Clear();
        PolygonDownloadArea.Points.Clear();
        PolygonUploadArea.Points.Clear();
        _canvasMaxSpeed = 50.0;

        TxtEficienciaRota.Text = "Piso Fibra: Calculando...";
        TxtOverlayGrafico.Text = "Medindo tráfego em tempo real...";
        OverlayContainer.Visibility = Visibility.Collapsed;
    }

    private void OnProgressEventTelemetria(BroadbandProgressEvent ev)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLogStatus.Text = ev.StatusText;

            if (ev.CurrentLatencyMs > 0)
            {
                TxtLatenciaMs.Text = ev.CurrentLatencyMs.ToString("F0");
            }
            if (ev.CurrentJitterMs > 0)
            {
                TxtJitterMs.Text = $"{ev.CurrentJitterMs:F1} ms";
            }

            if (ev.Phase == "Download")
            {
                TxtDlTelemetriaMbps.Text = ev.CurrentSpeedMbps.ToString("F1");
                PbDlTelemetria.Value = Math.Min(100, ev.Percent);
                if (ev.NewPoint != null)
                {
                    AdicionarPontoGrafico(ev.NewPoint.SpeedMbps, isDownload: true);
                }
            }
            else if (ev.Phase == "Upload")
            {
                TxtUlTelemetriaMbps.Text = ev.CurrentSpeedMbps.ToString("F1");
                PbUlTelemetria.Value = Math.Min(100, ev.Percent);
                if (ev.NewPoint != null)
                {
                    AdicionarPontoGrafico(ev.NewPoint.SpeedMbps, isDownload: false);
                }
            }
        });
    }

    private void AdicionarPontoGrafico(double mbps, bool isDownload)
    {
        if (mbps > _canvasMaxSpeed)
        {
            _canvasMaxSpeed = mbps * 1.25;
            RedesenharGraficoCanvas();
        }

        var w = CanvasGrafico.ActualWidth > 0 ? CanvasGrafico.ActualWidth : 900;
        var h = CanvasGrafico.ActualHeight > 0 ? CanvasGrafico.ActualHeight : 140;

        var rawList = isDownload ? _dlRawValues : _ulRawValues;
        var maxSlots = isDownload ? 28.0 : 18.0;

        var startX = isDownload ? 10.0 : (w * 0.52);
        var availableW = isDownload ? (w * 0.46) : (w * 0.45);

        var curIdx = rawList.Count;
        var ratio = Math.Min(1.0, curIdx / maxSlots);
        var x = startX + ratio * availableW;
        var y = Math.Max(5.0, h - (mbps / _canvasMaxSpeed) * (h - 18));

        rawList.Add((ratio, mbps));
        var pt = new Point(x, y);

        if (isDownload)
        {
            _dlPointsCanvas.Add(pt);
            PolylineDownload.Points.Add(pt);
            AtualizarPoligonoArea(true, h);
        }
        else
        {
            _ulPointsCanvas.Add(pt);
            PolylineUpload.Points.Add(pt);
            AtualizarPoligonoArea(false, h);
        }
    }

    private void AtualizarPoligonoArea(bool isDownload, double h)
    {
        var poly = isDownload ? PolygonDownloadArea : PolygonUploadArea;
        var points = isDownload ? _dlPointsCanvas : _ulPointsCanvas;

        poly.Points.Clear();
        if (points.Count < 2) return;

        poly.Points.Add(new Point(points[0].X, h));
        foreach (var p in points)
        {
            poly.Points.Add(p);
        }
        poly.Points.Add(new Point(points.Last().X, h));
    }

    private void RedesenharGraficoCanvas()
    {
        var w = CanvasGrafico.ActualWidth > 0 ? CanvasGrafico.ActualWidth : 900;
        var h = CanvasGrafico.ActualHeight > 0 ? CanvasGrafico.ActualHeight : 140;

        PolylineDownload.Points.Clear();
        _dlPointsCanvas.Clear();
        var dlStartX = 10.0;
        var dlAvailableW = w * 0.46;
        foreach (var (ratio, mbps) in _dlRawValues)
        {
            var x = dlStartX + ratio * dlAvailableW;
            var y = Math.Max(5.0, h - (mbps / _canvasMaxSpeed) * (h - 18));
            var pt = new Point(x, y);
            _dlPointsCanvas.Add(pt);
            PolylineDownload.Points.Add(pt);
        }
        AtualizarPoligonoArea(true, h);

        PolylineUpload.Points.Clear();
        _ulPointsCanvas.Clear();
        var ulStartX = w * 0.52;
        var ulAvailableW = w * 0.45;
        foreach (var (ratio, mbps) in _ulRawValues)
        {
            var x = ulStartX + ratio * ulAvailableW;
            var y = Math.Max(5.0, h - (mbps / _canvasMaxSpeed) * (h - 18));
            var pt = new Point(x, y);
            _ulPointsCanvas.Add(pt);
            PolylineUpload.Points.Add(pt);
        }
        AtualizarPoligonoArea(false, h);
    }

    private void AtualizarResultadoFinalTelemetria(BroadbandTestResult r)
    {
        TxtDlTelemetriaMbps.Text = r.DownloadAvgMbps.ToString("F1");
        TxtUlTelemetriaMbps.Text = r.UploadAvgMbps.ToString("F1");
        TxtDlTelemetriaPico.Text = $"Pico: {r.DownloadPeakMbps:F1} Mbps";
        TxtUlTelemetriaPico.Text = $"Pico: {r.UploadPeakMbps:F1} Mbps";
        TxtLatenciaMs.Text = r.LatencyIdleMs.ToString("F0");
        TxtJitterMs.Text = $"{r.JitterMs:F1} ms";
        TxtPacketLoss.Text = $"{r.PacketLossPct:F1}%";

        if (r.Bufferbloat != null)
        {
            TxtBufferbloatGrade.Text = r.Bufferbloat.Grade;
            TxtBufferbloatDesc.Text = r.Bufferbloat.Description;

            var gradeColor = r.Bufferbloat.Grade switch
            {
                "A+" or "A" => Color.FromRgb(16, 185, 129),
                "B" => Color.FromRgb(59, 130, 246),
                "C" => Color.FromRgb(245, 158, 11),
                _ => Color.FromRgb(239, 68, 68)
            };
            TxtBufferbloatGrade.Foreground = new SolidColorBrush(gradeColor);
        }

        if (r.GeoInfo != null)
        {
            TxtGeoServidor.Text = $"Servidor: {r.PrimaryServer?.Sponsor ?? r.GeoInfo.ServerCity}";
            TxtGeoPoP.Text = $"Alvo: {r.GeoInfo.ServerCity}";
            TxtGeoServidorLocal.Text = $"País: {r.GeoInfo.ServerCountry}";
            TxtGeoTecnologia.Text = $"Tipo: {r.PrimaryServer?.ServerType ?? "HTTP Stream"}";
            if (r.GeoInfo.DistanceKm.HasValue)
            {
                TxtDistanciaKm.Text = $"~{r.GeoInfo.DistanceKm.Value:F1} km";
            }
        }

        if (r.LatencyQuality != null)
        {
            TxtEficienciaRota.Text = $"Piso Fibra: ~{r.LatencyQuality.TheoreticalFiberRttMs:F1}ms • Efic: {r.LatencyQuality.RouteEfficiencyPct:F0}% ({r.LatencyQuality.RouteQualityStatus})";
            TxtEficienciaRota.ToolTip = r.LatencyQuality.DiagnosticExplanation;
        }

        if (r.IsSuccess)
        {
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            TxtStatusGeral.Text = "TELEMETRIA HOMOLOGADA COM SUCESSO";
            BtnGerarPdfTelemetria.IsEnabled = true;
            LogTelemetria($"[SUCESSO] Vazão Final: Download {r.DownloadAvgMbps:F1} Mbps | Upload {r.UploadAvgMbps:F1} Mbps | Jitter {r.JitterMs:F1} ms");
        }
        else
        {
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusGeral.Text = "FALHA NA HOMOLOGAÇÃO";
        }
    }

    private void BtnCancelarTelemetria_Click(object sender, RoutedEventArgs e)
    {
        _ctsTelemetria?.Cancel();
    }

    private async void BtnGerarPdfTelemetria_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResultTelemetria == null) return;

        BtnGerarPdfTelemetria.IsEnabled = false;
        try
        {
            LogTelemetria("[*] Gerando Relatório Executivo em PDF com gráficos vetoriais...");
            var pdfPath = await BroadbandPdfReportService.GenerateReportPdfAsync(_lastResultTelemetria);
            _ultimoPdfGeradoTelemetria = pdfPath;

            LogTelemetria($"[OK] Relatório gerado com sucesso em: {pdfPath}");

            var res = MessageBox.Show(
                $"Relatório de Banda Larga gerado com sucesso!\n\nArquivo:\n{pdfPath}\n\nDeseja abrir o relatório agora?",
                "SPARC — Relatório Emitido",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes && File.Exists(pdfPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            LogTelemetria($"[ERRO] Falha ao gerar relatório PDF: {ex.Message}");
            MessageBox.Show($"Erro ao gerar PDF: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnGerarPdfTelemetria.IsEnabled = true;
        }
    }

    private void BtnAbrirPastaTelemetria_Click(object sender, RoutedEventArgs e)
    {
        var reportsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "Relatorios_BandaLarga");
        if (!Directory.Exists(reportsDir))
        {
            Directory.CreateDirectory(reportsDir);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = reportsDir,
            UseShellExecute = true
        });
    }

    private void LogTelemetria(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLogTelemetria.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLogTelemetria.ScrollToEnd();
        });
    }

    private Task LogTelemetriaAsync(string message)
    {
        LogTelemetria(message);
        return Task.CompletedTask;
    }

    #endregion

    #region Lógica da Aba 2: Testar Link sem Roteador (Modo Acesso)

    private void PreencherDadosCircuitoAcesso()
    {
        if (_circuit == null)
        {
            TxtBadgeSaip.Text = "ℹ️ Modo Manual (Sem Ficha SAIP)";
            TxtBadgeSaip.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            BorderBadgeSaip.Background = new SolidColorBrush(Color.FromRgb(69, 26, 3));

            TxtClienteNome.Text = "Cliente Acesso Direto";
            TxtDesignacao.Text = "ACESSO-DIRETO-01";
            TxtVelocidadeCir.Text = "100 Mbps";
            TxtPeRouter.Text = "PE-CLARO-EDD";
            TxtWanIp.Text = "10.0.0.2";
            TxtWanMascara.Text = "255.255.255.252";
            TxtWanGateway.Text = "10.0.0.1";
            TxtDestinoPe.Text = "Alvo: 10.0.0.1";
            return;
        }

        var cliente = SaipParser.CleanRazaoSocial(_circuit.ClienteRazaoSocial);
        TxtClienteNome.Text = string.IsNullOrWhiteSpace(cliente) ? "Cliente Homologação" : cliente;

        var desig = _circuit.DesignacaoIp ?? _circuit.NumeroOts ?? "Circuito SAIP";
        TxtDesignacao.Text = desig;

        TxtVelocidadeCir.Text = _circuit.BandaMbpsNominal.HasValue
            ? $"{_circuit.BandaMbpsNominal.Value:F0} Mbps"
            : "100 Mbps";

        TxtPeRouter.Text = string.IsNullOrWhiteSpace(_circuit.PeRouter) ? "PE Claro/Embratel" : _circuit.PeRouter;

        TxtWanIp.Text = string.IsNullOrWhiteSpace(_circuit.WanIp) ? "10.0.0.2" : _circuit.WanIp;
        TxtWanMascara.Text = string.IsNullOrWhiteSpace(_circuit.WanSubnetMask) ? "255.255.255.252" : _circuit.WanSubnetMask;
        TxtWanGateway.Text = string.IsNullOrWhiteSpace(_circuit.WanGateway) ? "10.0.0.1" : _circuit.WanGateway;

        TxtDestinoPe.Text = $"Alvo: {TxtWanGateway.Text}";
    }

    private void CarregarAdaptadoresDeRedeAcesso()
    {
        try
        {
            CmbAdaptadoresAcesso.Items.Clear();
            var adapters = HostNetworkManager.GetEthernetAdapters();

            foreach (var adapter in adapters)
            {
                CmbAdaptadoresAcesso.Items.Add(adapter);
            }

            if (CmbAdaptadoresAcesso.Items.Count > 0)
            {
                CmbAdaptadoresAcesso.SelectedIndex = 0;
            }
            else
            {
                TxtStatusPlaca.Text = "⚠️ Nenhuma placa encontrada";
                TxtStatusPlaca.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
        }
        catch (Exception ex)
        {
            LogAcesso($"[!] Erro ao listar placas de rede: {ex.Message}");
        }
    }

    private void CmbAdaptadoresAcesso_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var adapter = CmbAdaptadoresAcesso.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(adapter)) return;

        AtualizarStatusPlacaSelecionada(adapter);
    }

    private void AtualizarStatusPlacaSelecionada(string adapterName)
    {
        try
        {
            var snap = HostNetworkManager.CaptureSnapshot(adapterName);
            if (snap == null)
            {
                TxtStatusPlaca.Text = "⚪ Sem IPv4 ativo";
                TxtStatusPlaca.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                TxtInfoAdaptador.Text = $"Adaptador '{adapterName}': desconectado ou sem endereço IPv4 atribuído.";
                return;
            }

            if (snap.DhcpEnabled)
            {
                TxtStatusPlaca.Text = "⚪ Placa em DHCP";
                TxtStatusPlaca.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                BorderStatusPlaca.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                TxtInfoAdaptador.Text = $"Adaptador '{adapterName}': DHCP ativo (IP atual: {snap.IpAddress ?? "N/D"}).";
            }
            else
            {
                var wanEsperado = TxtWanIp.Text.Trim();
                if (string.Equals(snap.IpAddress, wanEsperado, StringComparison.OrdinalIgnoreCase))
                {
                    TxtStatusPlaca.Text = $"🟢 IP WAN Ativo: {snap.IpAddress}";
                    TxtStatusPlaca.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                    BorderStatusPlaca.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
                    _isIpWanApplied = true;
                    _appliedAdapterName = adapterName;
                }
                else
                {
                    TxtStatusPlaca.Text = $"🟡 IP Fixo: {snap.IpAddress}";
                    TxtStatusPlaca.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                    BorderStatusPlaca.Background = new SolidColorBrush(Color.FromRgb(69, 26, 3));
                }
                TxtInfoAdaptador.Text = $"Adaptador '{adapterName}': IP estático {snap.IpAddress}/{snap.SubnetMask} (Gateway: {snap.Gateway ?? "N/D"}).";
            }
        }
        catch (Exception ex)
        {
            TxtInfoAdaptador.Text = $"Adaptador '{adapterName}': erro ao ler status ({ex.Message}).";
        }
    }

    private void BtnAtualizarAdaptadores_Click(object sender, RoutedEventArgs e)
    {
        CarregarAdaptadoresDeRedeAcesso();
        LogAcesso("[*] Lista de adaptadores de rede atualizada.");
    }

    private async void BtnAplicarIpWan_Click(object sender, RoutedEventArgs e)
    {
        await ConfigurarIpWanWindowsAsync();
    }

    private async Task<bool> ConfigurarIpWanWindowsAsync()
    {
        var adapter = CmbAdaptadoresAcesso.SelectedItem?.ToString();
        var ip = TxtWanIp.Text.Trim();
        var mask = TxtWanMascara.Text.Trim();
        var gw = TxtWanGateway.Text.Trim();

        if (string.IsNullOrWhiteSpace(adapter))
        {
            MessageBox.Show(this, "Selecione uma placa de rede Ethernet válida.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(mask) || string.IsNullOrWhiteSpace(gw))
        {
            MessageBox.Show(this, "Preencha os campos de IP WAN, Máscara e Gateway do circuito.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        LogAcesso($"\n[*] Aplicando IP WAN {ip}/{mask} (GW: {gw}) na placa '{adapter}'...");
        BtnAplicarIpWan.IsEnabled = false;

        try
        {
            var (ok, outMsg) = await HostNetworkManager.SetStaticIpAsync(
                adapter, ip, mask, gw, CancellationToken.None,
                dnsServers: new[] { "1.1.1.1", "8.8.8.8" },
                isRestore: false,
                gatewayMetric: 1);

            if (ok)
            {
                _isIpWanApplied = true;
                _appliedAdapterName = adapter;
                LogAcesso($"[✓] IP WAN {ip} aplicado com sucesso na placa '{adapter}'!");
                AtualizarStatusPlacaSelecionada(adapter);
                return true;
            }
            else
            {
                LogAcesso($"[!] Falha ao aplicar IP: {outMsg}");
                MessageBox.Show(this, $"Falha ao configurar placa de rede:\n\n{outMsg}\n\nExecute o SPARC como Administrador se o Windows exigir privilégios UAC.", "Erro de Configuração", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            LogAcesso($"[!] Exceção ao configurar rede: {ex.Message}");
            MessageBox.Show(this, $"Erro: {ex.Message}", "Erro de Rede", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            BtnAplicarIpWan.IsEnabled = true;
        }
    }

    private async void BtnRestaurarDhcp_Click(object sender, RoutedEventArgs e)
    {
        await RestaurarDhcpWindowsAsync();
    }

    private async Task<bool> RestaurarDhcpWindowsAsync()
    {
        var adapter = _appliedAdapterName ?? CmbAdaptadoresAcesso.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(adapter)) return false;

        LogAcesso($"\n[*] Restaurando placa '{adapter}' para a configuração original / DHCP...");
        BtnRestaurarDhcp.IsEnabled = false;

        try
        {
            var (ok, logMsg) = await HostNetworkManager.RestoreLastAsync(adapter, TxtWanIp.Text.Trim(), CancellationToken.None);
            LogAcesso(logMsg);

            _isIpWanApplied = false;
            _appliedAdapterName = null;
            AtualizarStatusPlacaSelecionada(adapter);
            return ok;
        }
        catch (Exception ex)
        {
            LogAcesso($"[!] Erro ao restaurar placa: {ex.Message}");
            return false;
        }
        finally
        {
            BtnRestaurarDhcp.IsEnabled = true;
        }
    }

    private async void BtnTestarIcmp_Click(object sender, RoutedEventArgs e)
    {
        await ExecutarTestesIcmpAcessoAsync();
    }

    private async Task<bool> ExecutarTestesIcmpAcessoAsync()
    {
        var gw = TxtWanGateway.Text.Trim();
        var localIp = TxtWanIp.Text.Trim();

        if (string.IsNullOrWhiteSpace(gw))
        {
            MessageBox.Show(this, "Informe o IP do Gateway PE da operadora.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        TxtDestinoPe.Text = $"Alvo: {gw}";
        BtnTestarIcmp.IsEnabled = false;
        _testCtsAcesso = new CancellationTokenSource();

        try
        {
            var srv = new ConnectivityService(msg => { LogAcesso(msg); return Task.CompletedTask; });

            // 1. Gateway PE
            SetBadgeStatus(BadgeIcmpPe, TxtBadgeIcmpPe, "🔵 PING...", "#0284C7");
            LogAcesso($"\n[*] Testando ICMP para Gateway PE ({gw})...");
            _peResult = await srv.TestPingAsync(gw, count: 4, timeoutMs: 2000, sourceIpAddress: localIp, cancellationToken: _testCtsAcesso.Token);
            AtualizarMetricasIcmp(BadgeIcmpPe, TxtBadgeIcmpPe, TxtMetricasPe, _peResult);

            // 2. Google DNS (8.8.8.8)
            SetBadgeStatus(BadgeIcmpGoogle, TxtBadgeIcmpGoogle, "🔵 PING...", "#0284C7");
            LogAcesso("[*] Testando ICMP para Google DNS (8.8.8.8)...");
            _googleResult = await srv.TestPingAsync("8.8.8.8", count: 4, timeoutMs: 2000, sourceIpAddress: localIp, cancellationToken: _testCtsAcesso.Token);
            AtualizarMetricasIcmp(BadgeIcmpGoogle, TxtBadgeIcmpGoogle, TxtMetricasGoogle, _googleResult);

            // 3. Cloudflare DNS (1.1.1.1)
            SetBadgeStatus(BadgeIcmpCf, TxtBadgeIcmpCf, "🔵 PING...", "#0284C7");
            LogAcesso("[*] Testando ICMP para Cloudflare DNS (1.1.1.1)...");
            _cloudflareResult = await srv.TestPingAsync("1.1.1.1", count: 4, timeoutMs: 2000, sourceIpAddress: localIp, cancellationToken: _testCtsAcesso.Token);
            AtualizarMetricasIcmp(BadgeIcmpCf, TxtBadgeIcmpCf, TxtMetricasCf, _cloudflareResult);

            var peOk = _peResult?.IsSuccess == true;
            var webOk = _googleResult?.IsSuccess == true || _cloudflareResult?.IsSuccess == true;

            if (peOk && webOk)
            {
                LogAcesso("[✓] Todos os testes de conectividade ICMP responderam com sucesso!");
                return true;
            }
            else if (peOk && !webOk)
            {
                LogAcesso("[AVISO] Gateway PE respondeu, mas os destinos de Internet pública (8.8.8.8 / 1.1.1.1) falharam.");
                return false;
            }
            else
            {
                LogAcesso("[FALHA] Sem resposta ICMP do Gateway PE. Verifique enlace físico / cabo de acesso.");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            LogAcesso("[*] Teste ICMP cancelado.");
            return false;
        }
        catch (Exception ex)
        {
            LogAcesso($"[!] Erro durante testes ICMP: {ex.Message}");
            return false;
        }
        finally
        {
            BtnTestarIcmp.IsEnabled = true;
        }
    }

    private void AtualizarMetricasIcmp(Border badge, TextBlock txtBadge, TextBlock txtMetricas, ConnectivityTestResult? res)
    {
        if (res == null) return;

        if (res.IsSuccess)
        {
            SetBadgeStatus(badge, txtBadge, "✅ RESPOSTA OK", "#059669");
            txtMetricas.Text = $"RTT Médio: {res.AvgRttMs:F1} ms | Jitter: {res.JitterMs:F1} ms | Perda: {res.PacketLossPercentage:F0}%";
            txtMetricas.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
        }
        else
        {
            SetBadgeStatus(badge, txtBadge, "❌ SEM RESPOSTA", "#DC2626");
            txtMetricas.Text = $"RTT Médio: — | Perda: {res.PacketLossPercentage:F0}% (Timeout)";
            txtMetricas.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
    }

    private async void BtnIniciarSpeedtest_Click(object sender, RoutedEventArgs e)
    {
        await ExecutarSpeedtestAcessoAsync();
    }

    private async Task<bool> ExecutarSpeedtestAcessoAsync()
    {
        var localIp = TxtWanIp.Text.Trim();
        BtnIniciarSpeedtest.IsEnabled = false;
        PbSpeedtest.Value = 0;
        TxtProgressoSpeedtest.Text = "Iniciando medição de banda...";

        var modoCli = CmbMetodoSpeedtest.SelectedIndex == 1;
        var srv = new BandwidthTestService(msg => { LogAcesso(msg); return Task.CompletedTask; });
        _testCtsAcesso = new CancellationTokenSource();

        try
        {
            LogAcesso($"\n[*] Iniciando aferição de vazão (Modo: {(modoCli ? "Speedtest CLI" : "Nativo HTTP CDN")})...");

            BandwidthTestResult result;
            if (modoCli)
            {
                result = await srv.RunSpeedtestCliAsync(customCliPath: null, sourceIpAddress: localIp, cancellationToken: _testCtsAcesso.Token);
            }
            else
            {
                result = await srv.RunNativeHttpSpeedTestAsync(
                    testPayloadMegaBytes: 50,
                    sourceIpAddress: localIp,
                    onProgress: (pct, speedMbps) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            PbSpeedtest.Value = pct;
                            TxtDlAcessoMbps.Text = $"{speedMbps:F1} Mbps";
                            TxtProgressoSpeedtest.Text = $"Medindo vazão de download... {pct:F0}% ({speedMbps:F1} Mbps)";
                        });
                    },
                    cancellationToken: _testCtsAcesso.Token);
            }

            _bandResult = result;
            PbSpeedtest.Value = 100;
            TxtDlAcessoMbps.Text = $"{result.DownloadMbps:F1} Mbps";
            TxtUlAcessoMbps.Text = $"{result.UploadMbps:F1} Mbps";
            TxtLatenciaSpeedtest.Text = $"{result.LatencyMs:F1} ms";

            double nominalCir = 100;
            var cirTxt = TxtVelocidadeCir.Text.Replace("Mbps", "").Trim();
            if (double.TryParse(cirTxt, out var parsedCir)) nominalCir = parsedCir;

            var avaliacao = BandwidthTestService.AvaliarBanda(nominalCir, result.DownloadMbps);
            if (avaliacao != null)
            {
                TxtVereditoBanda.Text = avaliacao.Veredito;
                TxtVereditoBanda.Foreground = avaliacao.Aprovado
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(248, 113, 113));

                TxtProgressoSpeedtest.Text = $"Concluído: {result.DownloadMbps:F1} Mbps ({avaliacao.Percentual:F1}% do CIR nominal de {nominalCir} Mbps).";
            }
            else
            {
                TxtVereditoBanda.Text = result.IsSuccess ? "Vazão Medida com Sucesso" : "Falha na Aferição";
                TxtVereditoBanda.Foreground = result.IsSuccess
                    ? new SolidColorBrush(Color.FromRgb(74, 222, 128))
                    : new SolidColorBrush(Color.FromRgb(248, 113, 113));
                TxtProgressoSpeedtest.Text = result.Message;
            }

            LogAcesso($"[✓] Speedtest finalizado: Download {result.DownloadMbps:F1} Mbps | Upload {result.UploadMbps:F1} Mbps | Ping {result.LatencyMs:F1} ms.");
            return result.IsSuccess;
        }
        catch (OperationCanceledException)
        {
            TxtProgressoSpeedtest.Text = "Aferição cancelada.";
            LogAcesso("[*] Speedtest cancelado pelo operador.");
            return false;
        }
        catch (Exception ex)
        {
            TxtProgressoSpeedtest.Text = $"Erro: {ex.Message}";
            LogAcesso($"[!] Erro durante o Speedtest: {ex.Message}");
            return false;
        }
        finally
        {
            BtnIniciarSpeedtest.IsEnabled = true;
        }
    }

    private async void BtnTesteCompleto_Click(object sender, RoutedEventArgs e)
    {
        BtnTesteCompleto.IsEnabled = false;
        BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199));
        TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        TxtStatusGeral.Text = "TESTANDO LINK DIRETO...";

        try
        {
            LogAcesso("================================================================================");
            LogAcesso("       🚀 INICIANDO TESTE COMPLETO DE LINK SEM ROTEADOR (MODO ACESSO)           ");
            LogAcesso("================================================================================");

            // 1. Configura IP WAN no Windows
            LogAcesso("\n[FASE 1/4] Configuração do IP WAN na placa de rede do notebook...");
            var ipOk = await ConfigurarIpWanWindowsAsync();
            if (!ipOk)
            {
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
                BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TxtStatusGeral.Text = "FALHA CONFIGURAÇÃO IP";
                return;
            }

            LogAcesso("[*] Aguardando 2 segundos para sincronização do enlace físico...");
            await Task.Delay(2000);

            // 2. Testes ICMP
            LogAcesso("\n[FASE 2/4] Aferições de conectividade ICMP (PE, Google, Cloudflare)...");
            var icmpOk = await ExecutarTestesIcmpAcessoAsync();

            // 3. Teste de Banda (Speedtest)
            LogAcesso("\n[FASE 3/4] Aferição de largura de banda e vazão...");
            var bandOk = await ExecutarSpeedtestAcessoAsync();

            // 4. Emissão do Laudo Técnico em PDF
            LogAcesso("\n[FASE 4/4] Gerando Laudo Técnico oficial em PDF...");
            await GerarLaudoPdfAcessoAsync();

            var vereditoFinal = icmpOk && bandOk;
            if (vereditoFinal)
            {
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
                BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtStatusGeral.Text = "100% HOMOLOGADO";

                MessageBox.Show(this,
                    "✓ Teste de Link Direto Concluído com Sucesso!\n\n" +
                    $"• Conectividade ICMP: OK\n" +
                    $"• Download: {_bandResult?.DownloadMbps:F1} Mbps\n" +
                    $"• Upload: {_bandResult?.UploadMbps:F1} Mbps\n" +
                    $"• Laudo Técnico em PDF emitido e disponível para visualização.",
                    "Homologação Aprovada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(69, 26, 3));
                BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                TxtStatusGeral.Text = "NÃO CONFORME";

                MessageBox.Show(this,
                    "⚠️ O teste foi concluído com observações de não conformidade.\n\n" +
                    $"O Laudo Técnico em PDF foi gerado apontando os itens reprovados.",
                    "Homologação Concluída com Alertas",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusGeral.Text = "ERRO NO TESTE";
            LogAcesso($"[!] Erro crítico na esteira completa: {ex.Message}");
        }
        finally
        {
            BtnTesteCompleto.IsEnabled = true;
        }
    }

    private async Task GerarLaudoPdfAcessoAsync()
    {
        try
        {
            double? nominalCir = null;
            var cirTxt = TxtVelocidadeCir.Text.Replace("Mbps", "").Trim();
            if (double.TryParse(cirTxt, out var parsedCir)) nominalCir = parsedCir;

            int wanCidr = 30;
            if (int.TryParse(TxtWanMascara.Text.Replace("/", "").Trim(), out var parsedCidr))
                wanCidr = parsedCidr;
            else if (_circuit != null)
                wanCidr = _circuit.WanCidr;

            var alerts = new List<string>();
            if (_peResult?.IsSuccess != true)
                alerts.Add("Falha no Gateway PE da operadora: sem resposta ICMP no ponto de demarcação.");
            if (_googleResult?.IsSuccess != true && _cloudflareResult?.IsSuccess != true)
                alerts.Add("Falha de navegação na Internet pública: sem resposta ICMP para 8.8.8.8 e 1.1.1.1.");
            if (_bandResult != null && nominalCir.HasValue && _bandResult.DownloadMbps < (nominalCir.Value * 0.92))
                alerts.Add($"Vazão de download ({_bandResult.DownloadMbps:F1} Mbps) abaixo do mínimo regulatório de 92% do CIR contratado ({nominalCir.Value * 0.92:F1} Mbps).");

            var reportData = new DirectAccessReportData(
                DataHora: DateTime.Now,
                ClienteRazaoSocial: TxtClienteNome.Text.Trim(),
                DesignacaoIp: TxtDesignacao.Text.Trim(),
                NumeroOts: _circuit?.NumeroOts,
                PeRouter: TxtPeRouter.Text.Trim(),
                WanIp: TxtWanIp.Text.Trim(),
                WanCidr: wanCidr,
                WanGateway: TxtWanGateway.Text.Trim(),
                WanSubnetMask: TxtWanMascara.Text.Trim(),
                BandaMbpsNominal: nominalCir,
                AdaptadorRedeLocal: CmbAdaptadoresAcesso.SelectedItem?.ToString() ?? "Placa de Rede Ethernet",
                PeResult: _peResult,
                GoogleResult: _googleResult,
                CloudflareResult: _cloudflareResult,
                BandResult: _bandResult,
                DiagnosticAlerts: alerts);

            var pdfPath = await ActivationPdfReportService.GenerateDirectAccessPdfAsync(reportData);
            _generatedPdfPathAcesso = pdfPath;

            LogAcesso($"[✓] Laudo Técnico oficial emitido: {System.IO.Path.GetFileName(pdfPath)}");

            BtnVisualizarPdf.Visibility = Visibility.Visible;
            BtnSalvarPdf.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LogAcesso($"[!] Falha ao emitir Laudo PDF: {ex.Message}");
        }
    }

    private void BtnVisualizarPdf_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_generatedPdfPathAcesso) || !File.Exists(_generatedPdfPathAcesso))
        {
            MessageBox.Show(this, "O Laudo PDF ainda não foi gerado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _generatedPdfPathAcesso,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao abrir arquivo: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnSalvarPdf_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_generatedPdfPathAcesso) || !File.Exists(_generatedPdfPathAcesso))
        {
            MessageBox.Show(this, "O Laudo PDF ainda não foi gerado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var rawDesig = TxtDesignacao.Text.Trim().Replace('/', '-').Replace('\\', '-');
            var sfd = new SaveFileDialog
            {
                Filter = "Documento PDF (*.pdf)|*.pdf|Arquivo HTML (*.html)|*.html",
                FileName = $"Laudo_AcessoDireto_{rawDesig}_{DateTime.Now:yyyyMMdd}.pdf"
            };

            if (sfd.ShowDialog(this) == true)
            {
                File.Copy(_generatedPdfPathAcesso, sfd.FileName, true);
                MessageBox.Show(this, $"Laudo salvo com sucesso em:\n{sfd.FileName}", "Laudo Salvo", MessageBoxButton.OK, MessageBoxImage.Information);
                LogAcesso($"[✓] Cópia do laudo salva em: {sfd.FileName}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao salvar cópia: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnLimparLog_Click(object sender, RoutedEventArgs e)
    {
        TxtLogConsole.Text = string.Empty;
    }

    private void LogAcesso(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLogConsole.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            ScrollLog.ScrollToEnd();
        });
    }

    private void SetBadgeStatus(Border border, TextBlock textBlock, string text, string hexColor)
    {
        border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
        textBlock.Text = text;
    }

    #endregion

    #region Utilitários Comuns & Fechamento Seguro

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        _ctsTelemetria?.Cancel();
        _testCtsAcesso?.Cancel();
        Close();
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isIpWanApplied)
        {
            e.Cancel = true;
            var res = MessageBox.Show(this,
                "A placa de rede do Windows ainda está configurada com o IP WAN do circuito.\n\n" +
                "Deseja restaurar para DHCP antes de fechar a janela?",
                "Restaurar Configuração IP",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                await RestaurarDhcpWindowsAsync();
            }

            _isIpWanApplied = false;
            Close();
            return;
        }

        _ctsTelemetria?.Cancel();
        _testCtsAcesso?.Cancel();

        base.OnClosing(e);
    }

    #endregion
}
