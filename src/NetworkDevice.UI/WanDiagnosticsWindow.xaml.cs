using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using NetworkDevice.Core.Detection;

namespace NetworkDevice.UI;

/// <summary>
/// Janela de diagnóstico didático e proteção contra inversão de cabos LAN/WAN (Fase 5b).
/// Mostra em tempo real o status físico de ambas as portas, alerta sobre inversão
/// e permite ao técnico selecionar o fluxo correto (campo vs bancada).
/// </summary>
public partial class WanDiagnosticsWindow : Window
{
    private readonly Func<Task<PortasDiagnosticoResultado>>? _recheckCallback;
    public WanActionDecision Decision { get; private set; } = WanActionDecision.ProceedOffline;

    public WanDiagnosticsWindow(
        PortasDiagnosticoResultado resultadoInicial,
        Func<Task<PortasDiagnosticoResultado>>? recheckCallback = null)
    {
        InitializeComponent();
        _recheckCallback = recheckCallback;

        if (_recheckCallback == null)
        {
            BtnRechecar.Visibility = Visibility.Collapsed;
        }

        AtualizarVisualizacao(resultadoInicial);
    }

    public void AtualizarVisualizacao(PortasDiagnosticoResultado res)
    {
        TxtSubHeader.Text = $"Equipamento identificado: {res.Modelo}";
        TxtPortaLanNome.Text = res.LanInterfaceExibicao;
        TxtPortaWanNome.Text = res.WanInterfaceExibicao;

        // Atualização Card LAN
        if (res.LanUp)
        {
            TxtLanStatus.Text = "🟢 LINK UP";
            TxtLanStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            BadgeLanStatus.Background = new SolidColorBrush(Color.FromRgb(6, 95, 70));
            CardLan.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        }
        else
        {
            TxtLanStatus.Text = "🔴 LINK DOWN";
            TxtLanStatus.Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165));
            BadgeLanStatus.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            CardLan.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }

        // Atualização Card WAN (Sem menção a cor)
        if (res.WanUp)
        {
            TxtWanStatus.Text = "🟢 LINK UP";
            TxtWanStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            BadgeWanStatus.Background = new SolidColorBrush(Color.FromRgb(6, 95, 70));
            CardWan.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        }
        else
        {
            TxtWanStatus.Text = "🔴 LINK DOWN";
            TxtWanStatus.Foreground = new SolidColorBrush(Color.FromRgb(252, 165, 165));
            BadgeWanStatus.Background = new SolidColorBrush(Color.FromRgb(127, 29, 29));
            CardWan.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }

        // Banner de Inversão de Cabos
        if (res.PossivelInversaoCabos)
        {
            BannerInversao.Visibility = Visibility.Visible;
            TxtInversaoDesc.Text = res.MensagemDiagnostico;
            BadgeIconBorder.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            TxtIcon.Text = "🚨";
            TxtTituloPrincipal.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
        else
        {
            BannerInversao.Visibility = Visibility.Collapsed;
            BadgeIconBorder.Background = new SolidColorBrush(Color.FromRgb(120, 53, 15));
            TxtIcon.Text = "⚠️";
            TxtTituloPrincipal.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        }
    }

    private void BtnConecteiWan_Click(object sender, RoutedEventArgs e)
    {
        Decision = WanActionDecision.Retry;
        DialogResult = true;
        Close();
    }

    private void BtnBancada_Click(object sender, RoutedEventArgs e)
    {
        Decision = WanActionDecision.ProceedInBenchMode;
        DialogResult = true;
        Close();
    }

    private void BtnOffline_Click(object sender, RoutedEventArgs e)
    {
        Decision = WanActionDecision.ProceedOffline;
        DialogResult = false;
        Close();
    }

    private async void BtnRechecar_Click(object sender, RoutedEventArgs e)
    {
        if (_recheckCallback == null) return;

        BtnRechecar.IsEnabled = false;
        BtnConecteiWan.IsEnabled = false;
        BtnBancada.IsEnabled = false;
        BtnOffline.IsEnabled = false;

        TxtRecheckStatus.Text = "Consultando status físico das portas e tabela ARP via console...";
        TxtRecheckStatus.Visibility = Visibility.Visible;

        try
        {
            var res = await _recheckCallback();
            AtualizarVisualizacao(res);
            TxtRecheckStatus.Text = "Status das portas atualizado com sucesso!";
            await Task.Delay(2000);
            TxtRecheckStatus.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            TxtRecheckStatus.Text = $"Falha ao rechecar: {ex.Message}";
        }
        finally
        {
            BtnRechecar.IsEnabled = true;
            BtnConecteiWan.IsEnabled = true;
            BtnBancada.IsEnabled = true;
            BtnOffline.IsEnabled = true;
        }
    }
}
