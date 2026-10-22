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
using Microsoft.Win32;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Provisioning;

namespace NetworkDevice.UI;

/// <summary>
/// Janela para homologação de links em Modo Acesso Direto (sem CPE/roteador da operadora).
/// Configura o IP WAN diretamente na placa de rede do Windows, testa ICMP (PE, Google, Cloudflare),
/// realiza medição de vazão (Speedtest) e emite o Laudo Técnico oficial em PDF.
/// </summary>
public partial class TesteLinkSemRoteadorWindow : Window
{
    private readonly SaipCircuitData? _circuit;
    private CancellationTokenSource? _testCts;
    private string? _generatedPdfPath;
    private bool _isIpWanApplied;
    private string? _appliedAdapterName;

    // Resultados dos testes para o Laudo
    private ConnectivityTestResult? _peResult;
    private ConnectivityTestResult? _googleResult;
    private ConnectivityTestResult? _cloudflareResult;
    private BandwidthTestResult? _bandResult;

    public TesteLinkSemRoteadorWindow(SaipCircuitData? circuit = null)
    {
        InitializeComponent();
        _circuit = circuit;

        PreencherDadosCircuito();
        CarregarAdaptadoresDeRede();
    }

    private void PreencherDadosCircuito()
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

    private void CarregarAdaptadoresDeRede()
    {
        try
        {
            CmbAdaptadoresRede.Items.Clear();
            var adapters = HostNetworkManager.GetEthernetAdapters();

            foreach (var adapter in adapters)
            {
                CmbAdaptadoresRede.Items.Add(adapter);
            }

            if (CmbAdaptadoresRede.Items.Count > 0)
            {
                CmbAdaptadoresRede.SelectedIndex = 0;
            }
            else
            {
                TxtStatusPlaca.Text = "⚠️ Nenhuma placa encontrada";
                TxtStatusPlaca.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
        }
        catch (Exception ex)
        {
            Log($"[!] Erro ao listar adaptadores de rede: {ex.Message}");
        }
    }

    private void CmbAdaptadoresRede_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = CmbAdaptadoresRede.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected)) return;

        AtualizarStatusPlacaSelecionada(selected);
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
                TxtInfoAdaptador.Text = $"Adaptador '{adapterName}': DHCP ativo (IP atual: {snap.IpAddress ?? "N/D"}).";
            }
            else
            {
                var wanEsperado = TxtWanIp.Text.Trim();
                if (string.Equals(snap.IpAddress, wanEsperado, StringComparison.OrdinalIgnoreCase))
                {
                    TxtStatusPlaca.Text = $"🟢 IP WAN Ativo: {snap.IpAddress}";
                    TxtStatusPlaca.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                    _isIpWanApplied = true;
                    _appliedAdapterName = adapterName;
                }
                else
                {
                    TxtStatusPlaca.Text = $"🟡 IP Fixo: {snap.IpAddress}";
                    TxtStatusPlaca.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
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
        CarregarAdaptadoresDeRede();
        Log("[*] Lista de placas de rede atualizada.");
    }

    #region Configuração de Rede do Windows

    private async void BtnAplicarIpWan_Click(object sender, RoutedEventArgs e)
    {
        await ConfigurarIpWanWindowsAsync();
    }

    private async Task<bool> ConfigurarIpWanWindowsAsync()
    {
        var adapter = CmbAdaptadoresRede.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(adapter))
        {
            MessageBox.Show(this, "Selecione a placa de rede conectada ao cabo do circuito.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var ip = TxtWanIp.Text.Trim();
        var mask = TxtWanMascara.Text.Trim();
        var gw = TxtWanGateway.Text.Trim();

        if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(mask) || string.IsNullOrWhiteSpace(gw))
        {
            MessageBox.Show(this, "Preencha o IP WAN, Máscara e Gateway PE antes de configurar a placa.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        Log($"\n[*] Configurando placa '{adapter}' com IP WAN {ip}/{mask} (Gateway: {gw}, DNS: 1.1.1.1 e 8.8.8.8)...");
        BtnAplicarIpWan.IsEnabled = false;

        try
        {
            // Aplica com métrica 1 para priorizar o roteamento de internet pela interface do circuito
            var (ok, outMsg) = await HostNetworkManager.SetStaticIpAsync(
                adapter, ip, mask, gw, CancellationToken.None,
                dnsServers: new[] { "1.1.1.1", "8.8.8.8" },
                isRestore: false,
                gatewayMetric: 1);

            if (ok)
            {
                _isIpWanApplied = true;
                _appliedAdapterName = adapter;
                Log($"[✓] IP WAN {ip} aplicado com sucesso na placa '{adapter}'!");
                AtualizarStatusPlacaSelecionada(adapter);
                return true;
            }
            else
            {
                Log($"[!] Falha ao aplicar IP: {outMsg}");
                MessageBox.Show(this, $"Falha ao configurar placa de rede:\n\n{outMsg}\n\nExecute o SPARC como Administrador se o Windows exigir privilégios UAC.", "Erro de Configuração", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log($"[!] Exceção ao configurar rede: {ex.Message}");
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
        var adapter = _appliedAdapterName ?? CmbAdaptadoresRede.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(adapter)) return false;

        Log($"\n[*] Restaurando placa '{adapter}' para a configuração original / DHCP...");
        BtnRestaurarDhcp.IsEnabled = false;

        try
        {
            var (ok, logMsg) = await HostNetworkManager.RestoreLastAsync(adapter, TxtWanIp.Text.Trim(), CancellationToken.None);
            Log(logMsg);

            _isIpWanApplied = false;
            _appliedAdapterName = null;
            AtualizarStatusPlacaSelecionada(adapter);
            return ok;
        }
        catch (Exception ex)
        {
            Log($"[!] Erro ao restaurar placa: {ex.Message}");
            return false;
        }
        finally
        {
            BtnRestaurarDhcp.IsEnabled = true;
        }
    }

    #endregion

    #region Testes de Conectividade ICMP

    private async void BtnTestarIcmp_Click(object sender, RoutedEventArgs e)
    {
        await ExecutarTestesIcmpAsync();
    }

    private async Task<bool> ExecutarTestesIcmpAsync()
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
        _testCts = new CancellationTokenSource();

        try
        {
            var srv = new ConnectivityService(msg => { Log(msg); return Task.CompletedTask; });

            // 1. Gateway PE
            SetBadgeStatus(BadgeIcmpPe, TxtBadgeIcmpPe, "🔵 PING...", "#0284C7");
            Log($"\n[*] Testando ICMP para Gateway PE ({gw})...");
            _peResult = await srv.TestPingAsync(gw, count: 4, timeoutMs: 2000, sourceIpAddress: localIp, cancellationToken: _testCts.Token);
            AtualizarMetricasIcmp(BadgeIcmpPe, TxtBadgeIcmpPe, TxtMetricasPe, _peResult);

            // 2. Google DNS (8.8.8.8)
            SetBadgeStatus(BadgeIcmpGoogle, TxtBadgeIcmpGoogle, "🔵 PING...", "#0284C7");
            Log("[*] Testando ICMP para Google DNS (8.8.8.8)...");
            _googleResult = await srv.TestPingAsync("8.8.8.8", count: 4, timeoutMs: 2000, sourceIpAddress: localIp, cancellationToken: _testCts.Token);
            AtualizarMetricasIcmp(BadgeIcmpGoogle, TxtBadgeIcmpGoogle, TxtMetricasGoogle, _googleResult);

            // 3. Cloudflare DNS (1.1.1.1)
            SetBadgeStatus(BadgeIcmpCf, TxtBadgeIcmpCf, "🔵 PING...", "#0284C7");
            Log("[*] Testando ICMP para Cloudflare DNS (1.1.1.1)...");
            _cloudflareResult = await srv.TestPingAsync("1.1.1.1", count: 4, timeoutMs: 2000, sourceIpAddress: localIp, cancellationToken: _testCts.Token);
            AtualizarMetricasIcmp(BadgeIcmpCf, TxtBadgeIcmpCf, TxtMetricasCf, _cloudflareResult);

            var peOk = _peResult?.IsSuccess == true;
            var webOk = _googleResult?.IsSuccess == true || _cloudflareResult?.IsSuccess == true;

            if (peOk && webOk)
            {
                Log("[✓] Todos os testes de conectividade ICMP responderam com sucesso!");
                return true;
            }
            else if (peOk && !webOk)
            {
                Log("[AVISO] Gateway PE respondeu, mas os destinos de Internet pública (8.8.8.8 / 1.1.1.1) falharam.");
                return false;
            }
            else
            {
                Log("[FALHA] Sem resposta ICMP do Gateway PE. Verifique enlace físico / cabo de acesso.");
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            Log("[*] Teste ICMP cancelado.");
            return false;
        }
        catch (Exception ex)
        {
            Log($"[!] Erro durante testes ICMP: {ex.Message}");
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

    #endregion

    #region Teste de Banda (Speedtest)

    private async void BtnIniciarSpeedtest_Click(object sender, RoutedEventArgs e)
    {
        await ExecutarSpeedtestAsync();
    }

    private async Task<bool> ExecutarSpeedtestAsync()
    {
        var localIp = TxtWanIp.Text.Trim();
        BtnIniciarSpeedtest.IsEnabled = false;
        PbSpeedtest.Value = 0;
        TxtProgressoSpeedtest.Text = "Iniciando medição de banda...";

        var modoCli = CmbMetodoSpeedtest.SelectedIndex == 1;
        var srv = new BandwidthTestService(msg => { Log(msg); return Task.CompletedTask; });
        _testCts = new CancellationTokenSource();

        try
        {
            Log($"\n[*] Iniciando aferição de vazão (Modo: {(modoCli ? "Speedtest CLI" : "Nativo HTTP CDN")})...");

            BandwidthTestResult result;
            if (modoCli)
            {
                result = await srv.RunSpeedtestCliAsync(customCliPath: null, sourceIpAddress: localIp, cancellationToken: _testCts.Token);
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
                            TxtDownloadMbps.Text = $"{speedMbps:F1} Mbps";
                            TxtProgressoSpeedtest.Text = $"Medindo vazão de download... {pct:F0}% ({speedMbps:F1} Mbps)";
                        });
                    },
                    cancellationToken: _testCts.Token);
            }

            _bandResult = result;
            PbSpeedtest.Value = 100;
            TxtDownloadMbps.Text = $"{result.DownloadMbps:F1} Mbps";
            TxtUploadMbps.Text = $"{result.UploadMbps:F1} Mbps";
            TxtLatenciaSpeedtest.Text = $"{result.LatencyMs:F1} ms";

            // Avaliação contra CIR nominal
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

            Log($"[✓] Speedtest finalizado: Download {result.DownloadMbps:F1} Mbps | Upload {result.UploadMbps:F1} Mbps | Ping {result.LatencyMs:F1} ms.");
            return result.IsSuccess;
        }
        catch (OperationCanceledException)
        {
            TxtProgressoSpeedtest.Text = "Aferição cancelada.";
            Log("[*] Speedtest cancelado pelo operador.");
            return false;
        }
        catch (Exception ex)
        {
            TxtProgressoSpeedtest.Text = $"Erro: {ex.Message}";
            Log($"[!] Erro durante o Speedtest: {ex.Message}");
            return false;
        }
        finally
        {
            BtnIniciarSpeedtest.IsEnabled = true;
        }
    }

    #endregion

    #region Teste Completo e Emissão de Laudo PDF

    private async void BtnTesteCompleto_Click(object sender, RoutedEventArgs e)
    {
        BtnTesteCompleto.IsEnabled = false;
        SetStatusGeral("🔵 TESTANDO...", "#0284C7");

        try
        {
            Log("================================================================================");
            Log("       🚀 INICIANDO TESTE COMPLETO DE LINK SEM ROTEADOR (MODO ACESSO)           ");
            Log("================================================================================");

            // 1. Configura IP WAN no Windows
            Log("\n[FASE 1/4] Configuração do IP WAN na placa de rede do notebook...");
            var ipOk = await ConfigurarIpWanWindowsAsync();
            if (!ipOk)
            {
                SetStatusGeral("🔴 FALHA CONFIGURAÇÃO IP", "#DC2626");
                return;
            }

            Log("[*] Aguardando 2 segundos para sincronização do enlace físico...");
            await Task.Delay(2000);

            // 2. Testes ICMP
            Log("\n[FASE 2/4] Aferições de conectividade ICMP (PE, Google, Cloudflare)...");
            var icmpOk = await ExecutarTestesIcmpAsync();

            // 3. Teste de Banda (Speedtest)
            Log("\n[FASE 3/4] Aferição de largura de banda e vazão...");
            var bandOk = await ExecutarSpeedtestAsync();

            // 4. Emissão do Laudo Técnico em PDF
            Log("\n[FASE 4/4] Gerando Laudo Técnico oficial em PDF...");
            await GerarLaudoPdfAsync();

            var vereditoFinal = icmpOk && bandOk;
            if (vereditoFinal)
            {
                SetStatusGeral("🟢 100% HOMOLOGADO", "#059669");
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
                SetStatusGeral("⚠️ NÃO CONFORME", "#D97706");
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
            SetStatusGeral("🔴 ERRO NO TESTE", "#DC2626");
            Log($"[!] Erro crítico na esteira completa: {ex.Message}");
        }
        finally
        {
            BtnTesteCompleto.IsEnabled = true;
        }
    }

    private async Task GerarLaudoPdfAsync()
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
                AdaptadorRedeLocal: CmbAdaptadoresRede.SelectedItem?.ToString() ?? "Placa de Rede Ethernet",
                PeResult: _peResult,
                GoogleResult: _googleResult,
                CloudflareResult: _cloudflareResult,
                BandResult: _bandResult,
                DiagnosticAlerts: alerts);

            var pdfPath = await ActivationPdfReportService.GenerateDirectAccessPdfAsync(reportData);
            _generatedPdfPath = pdfPath;

            Log($"[✓] Laudo Técnico oficial emitido: {Path.GetFileName(pdfPath)}");

            BtnVisualizarPdf.Visibility = Visibility.Visible;
            BtnSalvarPdf.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log($"[!] Falha ao emitir Laudo PDF: {ex.Message}");
        }
    }

    private void BtnVisualizarPdf_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_generatedPdfPath) || !File.Exists(_generatedPdfPath))
        {
            MessageBox.Show(this, "O Laudo PDF ainda não foi gerado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _generatedPdfPath,
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
        if (string.IsNullOrWhiteSpace(_generatedPdfPath) || !File.Exists(_generatedPdfPath))
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
                File.Copy(_generatedPdfPath, sfd.FileName, true);
                MessageBox.Show(this, $"Laudo salvo com sucesso em:\n{sfd.FileName}", "Laudo Salvo", MessageBoxButton.OK, MessageBoxImage.Information);
                Log($"[✓] Cópia do laudo salva em: {sfd.FileName}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erro ao salvar cópia: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Utilitários de Interface e Fechamento

    private void SetBadgeStatus(Border border, TextBlock textBlock, string text, string hexColor)
    {
        textBlock.Text = text;
        border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
    }

    private void SetStatusGeral(string text, string hexColor)
    {
        TxtStatusGeral.Text = text;
        BadgeStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
    }

    private void Log(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            TxtLogConsole.Text += $"\n[{time}] {msg}";
            ScrollLog.ScrollToEnd();
        });
    }

    private void BtnLimparLog_Click(object sender, RoutedEventArgs e)
    {
        TxtLogConsole.Text = "[LOG] Console de execução limpo.";
    }

    private async void BtnFechar_Click(object sender, RoutedEventArgs e)
    {
        if (_isIpWanApplied)
        {
            var res = MessageBox.Show(this,
                "A placa de rede do Windows ainda está configurada com o IP WAN estático do circuito.\n\n" +
                "Deseja restaurar as configurações originais / DHCP antes de sair?",
                "Restaurar Placa de Rede",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                await RestaurarDhcpWindowsAsync();
                Close();
            }
            else if (res == MessageBoxResult.No)
            {
                Close();
            }
        }
        else
        {
            Close();
        }
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

        base.OnClosing(e);
    }

    #endregion
}
