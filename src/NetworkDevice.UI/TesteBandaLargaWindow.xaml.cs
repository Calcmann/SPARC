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
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Diagnostics.Broadband;

namespace NetworkDevice.UI;

public partial class TesteBandaLargaWindow : Window
{
    private readonly BroadbandTestEngine _engine;
    private CancellationTokenSource? _cts;
    private List<BroadbandServerInfo> _discoveredServers = new();
    private BroadbandGeoInfo? _currentGeoInfo;
    private BroadbandTestResult? _lastResult;
    private double? _nominalMbps;
    private string? _ultimoPdfGerado;

    private readonly List<Point> _dlPointsCanvas = new();
    private readonly List<Point> _ulPointsCanvas = new();
    private double _canvasMaxSpeed = 50.0;

    public TesteBandaLargaWindow(double? nominalMbps = null)
    {
        InitializeComponent();
        _nominalMbps = nominalMbps;
        _engine = new BroadbandTestEngine(LogAsync);

        Loaded += TesteBandaLargaWindow_Loaded;
    }

    private async void TesteBandaLargaWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CarregarAdaptadoresRede();
        await DescobrirServidoresEGeolocalizacaoAsync();
    }

    private void CarregarAdaptadoresRede()
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
            Log($"[AVISO] Erro ao listar adaptadores de rede: {ex.Message}");
        }
    }

    private async Task DescobrirServidoresEGeolocalizacaoAsync()
    {
        BtnRecarregarServidores.IsEnabled = false;
        TxtInfoProximidade.Text = "Descobrindo geolocalização do link e listando servidores mais próximos...";

        try
        {
            var srcIp = GetSelectedSourceIp();
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

    private string? GetSelectedSourceIp()
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

    private async void BtnIniciarTeste_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        BtnIniciarTeste.IsEnabled = false;
        BtnCancelar.IsEnabled = true;
        BtnGerarRelatorioPdf.IsEnabled = false;
        CbServidores.IsEnabled = false;
        CbAdaptadoresRede.IsEnabled = false;
        BtnRecarregarServidores.IsEnabled = false;

        BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(2, 132, 199));
        TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        TxtStatusGeral.Text = "EXECUTANDO TESTE...";

        ResetarGraficoEIndicadores();

        var selectedServer = (CbServidores.SelectedItem as ComboBoxItem)?.Tag as BroadbandServerInfo;
        var srcIp = GetSelectedSourceIp();

        var progressHandler = new Progress<BroadbandProgressEvent>(OnProgressEvent);

        try
        {
            Log("[*] Disparando esteira de medição de banda larga...");
            _lastResult = await _engine.RunTestWithFallbackAsync(
                selectedServer,
                _discoveredServers,
                srcIp,
                progressHandler,
                _cts.Token);

            if (_nominalMbps.HasValue && _nominalMbps.Value > 0)
            {
                _lastResult = _lastResult with
                {
                    AvaliacaoNominal = BandwidthTestService.AvaliarBanda(_nominalMbps, _lastResult.DownloadAvgMbps)
                };
            }

            AtualizarResultadoFinal(_lastResult);
        }
        catch (OperationCanceledException)
        {
            Log("[!] Teste cancelado pelo operador.");
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            TxtStatusGeral.Text = "TESTE CANCELADO";
            TxtOverlayGrafico.Text = "Teste cancelado.";
            TxtOverlayGrafico.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log($"[ERRO] Falha crítica na execução do teste: {ex.Message}");
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusGeral.Text = "FALHA NO TESTE";
            TxtOverlayGrafico.Text = $"Falha: {ex.Message}";
            TxtOverlayGrafico.Visibility = Visibility.Visible;
        }
        finally
        {
            BtnIniciarTeste.IsEnabled = true;
            BtnCancelar.IsEnabled = false;
            CbServidores.IsEnabled = true;
            CbAdaptadoresRede.IsEnabled = true;
            BtnRecarregarServidores.IsEnabled = true;
        }
    }

    private void ResetarGraficoEIndicadores()
    {
        TxtDownloadMbps.Text = "0.0";
        TxtUploadMbps.Text = "0.0";
        TxtDownloadPico.Text = "Pico: 0.0 Mbps";
        TxtUploadPico.Text = "Pico: 0.0 Mbps";
        TxtLatenciaMs.Text = "0";
        TxtJitterMs.Text = "0.0 ms";
        TxtPacketLoss.Text = "0%";
        TxtBufferbloatGrade.Text = "—";
        TxtBufferbloatDesc.Text = "Aguardando";
        PbDownload.Value = 0;
        PbUpload.Value = 0;

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

    private void OnProgressEvent(BroadbandProgressEvent ev)
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
                TxtDownloadMbps.Text = ev.CurrentSpeedMbps.ToString("F1");
                PbDownload.Value = Math.Min(100, ev.Percent);
                if (ev.NewPoint != null)
                {
                    AdicionarPontoGrafico(ev.NewPoint.SpeedMbps, isDownload: true);
                }
            }
            else if (ev.Phase == "Upload")
            {
                TxtUploadMbps.Text = ev.CurrentSpeedMbps.ToString("F1");
                PbUpload.Value = Math.Min(100, ev.Percent);
                if (ev.NewPoint != null)
                {
                    AdicionarPontoGrafico(ev.NewPoint.SpeedMbps, isDownload: false);
                }
            }
        });
    }

    private readonly List<(double StepRatio, double SpeedMbps)> _dlRawValues = new();
    private readonly List<(double StepRatio, double SpeedMbps)> _ulRawValues = new();

    private void AdicionarPontoGrafico(double mbps, bool isDownload)
    {
        if (mbps > _canvasMaxSpeed)
        {
            _canvasMaxSpeed = mbps * 1.25;
            RedesenharGraficoCanvas();
        }

        var w = CanvasGrafico.ActualWidth > 0 ? CanvasGrafico.ActualWidth : 900;
        var h = CanvasGrafico.ActualHeight > 0 ? CanvasGrafico.ActualHeight : 145;

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

        // Ponto de base esquerdo
        poly.Points.Add(new Point(points[0].X, h));

        // Curva
        foreach (var p in points)
        {
            poly.Points.Add(p);
        }

        // Ponto de base direito
        poly.Points.Add(new Point(points.Last().X, h));
    }

    private void RedesenharGraficoCanvas()
    {
        var w = CanvasGrafico.ActualWidth > 0 ? CanvasGrafico.ActualWidth : 900;
        var h = CanvasGrafico.ActualHeight > 0 ? CanvasGrafico.ActualHeight : 145;

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

    private void AtualizarResultadoFinal(BroadbandTestResult r)
    {
        TxtDownloadMbps.Text = r.DownloadAvgMbps.ToString("F1");
        TxtUploadMbps.Text = r.UploadAvgMbps.ToString("F1");
        TxtDownloadPico.Text = $"Pico: {r.DownloadPeakMbps:F1} Mbps";
        TxtUploadPico.Text = $"Pico: {r.UploadPeakMbps:F1} Mbps";
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
            TxtStatusGeral.Text = "TESTE HOMOLOGADO COM SUCESSO";
            BtnGerarRelatorioPdf.IsEnabled = true;
            Log($"[SUCESSO] Vazão Final: Download {r.DownloadAvgMbps:F1} Mbps | Upload {r.UploadAvgMbps:F1} Mbps | Jitter {r.JitterMs:F1} ms");
        }
        else
        {
            BadgeStatus.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            BadgeStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusDot.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtStatusGeral.Text = "FALHA NA HOMOLOGAÇÃO";
        }
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private async void BtnGerarRelatorioPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult == null) return;

        BtnGerarRelatorioPdf.IsEnabled = false;
        try
        {
            Log("[*] Gerando Relatório Executivo em PDF com gráficos vetoriais...");
            var pdfPath = await BroadbandPdfReportService.GenerateReportPdfAsync(_lastResult);
            _ultimoPdfGerado = pdfPath;

            Log($"[OK] Relatório gerado com sucesso em: {pdfPath}");

            var res = MessageBox.Show(
                $"Relatório de Banda Larga gerado com sucesso!\n\nArquivo:\n{pdfPath}\n\nDeseja abrir o relatório agora?",
                "SPARC — Relatório Emitido",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (res == MessageBoxResult.Yes && System.IO.File.Exists(pdfPath))
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
            Log($"[ERRO] Falha ao gerar relatório PDF: {ex.Message}");
            MessageBox.Show($"Erro ao gerar PDF: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnGerarRelatorioPdf.IsEnabled = true;
        }
    }

    private void BtnAbrirPasta_Click(object sender, RoutedEventArgs e)
    {
        var reportsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "Relatorios_BandaLarga");
        if (!System.IO.Directory.Exists(reportsDir))
        {
            System.IO.Directory.CreateDirectory(reportsDir);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = reportsDir,
            UseShellExecute = true
        });
    }

    private void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
            TxtLog.ScrollToEnd();
        });
    }

    private Task LogAsync(string message)
    {
        Log(message);
        return Task.CompletedTask;
    }
}
