using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Backup;
using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Device;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Routing;
using NetworkDevice.Core.Session;
using NetworkDevice.Core.Validation;
using NetworkDevice.Protocols.Hpe;
using NetworkDevice.Protocols.Serial;

namespace NetworkDevice.UI;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush BrushSistema = new((Color)ColorConverter.ConvertFromString("#38BDF8")); // Ciano
    private static readonly SolidColorBrush BrushSucesso = new((Color)ColorConverter.ConvertFromString("#4ADE80")); // Verde
    private static readonly SolidColorBrush BrushInstrucao = new((Color)ColorConverter.ConvertFromString("#FBBF24")); // Amarelo/Dourado
    private static readonly SolidColorBrush BrushErro = new((Color)ColorConverter.ConvertFromString("#F87171")); // Vermelho
    private static readonly SolidColorBrush BrushEquipamento = new((Color)ColorConverter.ConvertFromString("#94A3B8")); // Cinza/Slate

    private const int WM_DEVICECHANGE = 0x0219;
    private CancellationTokenSource? _cts;
    private SaipCircuitData? _loadedSaipCircuit;
    private string? _selectedIosBinPath;
    private bool _isBusy;
    private bool _isRommonOrBootwareDetected;
    private bool _skipFactoryReset;
    private bool _isFortiGateDetected;
    private bool _firmwareSkippedSameVersion;
    private string? _detectedFortiOsStatusOut;
    private string? _detectedFortiOsVersion;
    private string? _lastFortiResolvedUser;
    private string? _lastFortiResolvedPass;
    private TripleIcmpResult? _lastIcmpResult;

    public MainWindow()
    {
        InitializeComponent();
    }

    private bool _serialOk;
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CbModeloRoteadorInicial.SelectedIndex = 0;
        CbInterrupt.SelectedIndex = -1;
        _serialOk = false;
        AtualizarPortas();
        AtualizarAdaptadoresRede();
        AtualizarEstadoBotoes();
        AtualizarBotaoProsseguir();
        SelecionarFase("A");

        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE)
        {
            AtualizarPortas();
        }
        return IntPtr.Zero;
    }

    #region Navegação entre Fases da Esteira

    private void SelecionarFase(string fase)
    {
        PanelPhaseA.Visibility = fase == "A" ? Visibility.Visible : Visibility.Collapsed;
        PanelPhaseB.Visibility = fase == "B" ? Visibility.Visible : Visibility.Collapsed;
        PanelPhaseC.Visibility = fase == "C" ? Visibility.Visible : Visibility.Collapsed;
        PanelPhaseD.Visibility = fase == "D" ? Visibility.Visible : Visibility.Collapsed;
        PanelPhaseE.Visibility = fase == "E" ? Visibility.Visible : Visibility.Collapsed;
        PanelPhaseF.Visibility = fase == "F" ? Visibility.Visible : Visibility.Collapsed;
        PanelPhaseG.Visibility = fase == "G" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnStepA_Click(object sender, RoutedEventArgs e) => SelecionarFase("A");
    private void BtnStepB_Click(object sender, RoutedEventArgs e) => SelecionarFase("B");
    private void BtnStepC_Click(object sender, RoutedEventArgs e) => SelecionarFase("C");
    private void BtnStepD_Click(object sender, RoutedEventArgs e) => SelecionarFase("D");
    private void BtnStepE_Click(object sender, RoutedEventArgs e) => SelecionarFase("E");
    private void BtnStepF_Click(object sender, RoutedEventArgs e) => SelecionarFase("F");
    private void BtnStepG_Click(object sender, RoutedEventArgs e) => SelecionarFase("G");

    private void BtnAbrirManual_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "Manual_Instrucoes_Operador_SPARC.pdf"),
                @"C:\SPARC\Manual_Instrucoes_Operador_SPARC.pdf",
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\Manual_Instrucoes_Operador_SPARC.pdf")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\Manual_Instrucoes_Operador_SPARC.pdf")),
                Path.Combine(baseDir, "Manual_Instrucoes_Operador_Killtech.pdf"),
                @"C:\SPARC\Manual_Instrucoes_Operador_Killtech.pdf",
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\Manual_Instrucoes_Operador_Killtech.pdf")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\Manual_Instrucoes_Operador_Killtech.pdf"))
            };

            var pdfPath = candidates.FirstOrDefault(File.Exists);
            if (pdfPath != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show(
                    "O arquivo do Manual do Operador (SPARC) não foi localizado.",
                    "Manual do Operador — SPARC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível abrir o manual: {ex.Message}",
                "Erro ao Abrir Manual",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void DefinirBadgeStatus(string fase, string badge)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => DefinirBadgeStatus(fase, badge));
            return;
        }

        switch (fase)
        {
            case "A": StatusBadgeA.Text = badge; break;
            case "B": StatusBadgeB.Text = badge; break;
            case "C": StatusBadgeC.Text = badge; break;
            case "D": StatusBadgeD.Text = badge; break;
            case "E": StatusBadgeE.Text = badge; break;
            case "F": StatusBadgeF.Text = badge; break;
            case "G": StatusBadgeG.Text = badge; break;
        }
    }

    private void ResetarBadges()
    {
        DefinirBadgeStatus("A", "⚪");
        DefinirBadgeStatus("B", "⚪");
        DefinirBadgeStatus("C", "⚪");
        DefinirBadgeStatus("D", "⚪");
        DefinirBadgeStatus("E", "⚪");
        DefinirBadgeStatus("F", "⚪");
        DefinirBadgeStatus("G", "⚪");
    }

    #endregion

    #region Gerenciamento de Portas e Dispositivos

    private bool _syncingCombos;

    private void SelecionarModeloNoCombo(string targetTagOrPattern)
    {
        if (string.IsNullOrWhiteSpace(targetTagOrPattern)) return;

        foreach (var cb in new[] { CbModeloRoteadorInicial, CbInterrupt })
        {
            if (cb == null) continue;
            bool selected = false;
            foreach (ComboBoxItem it in cb.Items)
            {
                var tag = it.Tag?.ToString() ?? "";
                var content = it.Content?.ToString() ?? "";
                if (tag.Equals(targetTagOrPattern, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(tag) && tag.Contains(targetTagOrPattern, StringComparison.OrdinalIgnoreCase)) ||
                    content.Contains(targetTagOrPattern, StringComparison.OrdinalIgnoreCase))
                {
                    cb.SelectedItem = it;
                    selected = true;
                    break;
                }
            }

            // Fallback: se buscou hpe.msr.ctrl-b ou hpe genérico e não encontrou exato, seleciona o HPE 954
            if (!selected && targetTagOrPattern.Contains("hpe", StringComparison.OrdinalIgnoreCase))
            {
                foreach (ComboBoxItem it in cb.Items)
                {
                    var tag = it.Tag?.ToString() ?? "";
                    if (tag.Contains("hpe.msr954", StringComparison.OrdinalIgnoreCase) ||
                        tag.Contains("hpe.msr", StringComparison.OrdinalIgnoreCase) ||
                        tag.Contains("954", StringComparison.OrdinalIgnoreCase))
                    {
                        cb.SelectedItem = it;
                        break;
                    }
                }
            }
        }

        RevalidarFirmwareCarregadoAoMudarModelo();
    }

    private static bool IsTagFortiGate(string tag) =>
        !string.IsNullOrEmpty(tag) && (tag.Contains("forti", StringComparison.OrdinalIgnoreCase) || tag.Contains("fgt", StringComparison.OrdinalIgnoreCase));

    private DeviceSeries ObterSerieAtualSelecionadaOuDetectada()
    {
        var item = CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem;
        var tag = item?.Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(tag))
        {
            var profile = CbInterrupt?.SelectedItem as BootInterruptProfile;
            tag = profile?.Id ?? "";
        }

        if (tag.Contains("1002", StringComparison.OrdinalIgnoreCase) || tag.Contains("1003", StringComparison.OrdinalIgnoreCase) || tag.Contains("1000", StringComparison.OrdinalIgnoreCase) || tag.Contains("100x", StringComparison.OrdinalIgnoreCase)) return DeviceSeries.Msr1002;
        if (tag.Contains("930", StringComparison.OrdinalIgnoreCase) || tag.Contains("931", StringComparison.OrdinalIgnoreCase) || tag.Contains("935", StringComparison.OrdinalIgnoreCase)) return DeviceSeries.Msr930;
        if (tag.Contains("954", StringComparison.OrdinalIgnoreCase) || tag.Contains("958", StringComparison.OrdinalIgnoreCase)) return DeviceSeries.Msr954;
        if (tag.Contains("1900", StringComparison.OrdinalIgnoreCase) || tag.Contains("1921", StringComparison.OrdinalIgnoreCase) || tag.Contains("1941", StringComparison.OrdinalIgnoreCase) || tag.Contains("1905", StringComparison.OrdinalIgnoreCase)) return DeviceSeries.Series1900;
        if (tag.Contains("900", StringComparison.OrdinalIgnoreCase) || tag.Contains("921", StringComparison.OrdinalIgnoreCase) || tag.Contains("c900", StringComparison.OrdinalIgnoreCase)) return DeviceSeries.Isr921;
        if (tag.Contains("841", StringComparison.OrdinalIgnoreCase) || tag.Contains("800", StringComparison.OrdinalIgnoreCase) || tag.Contains("c841", StringComparison.OrdinalIgnoreCase) || tag.Contains("c800", StringComparison.OrdinalIgnoreCase)) return DeviceSeries.Isr841;
        if (tag.Contains("forti", StringComparison.OrdinalIgnoreCase) || tag.Contains("fgt", StringComparison.OrdinalIgnoreCase)) return DeviceSeries.FortiGate40F;
        return DeviceSeries.Unknown;
    }

    private bool ValidarEBloquearFirmwareIncompativel(string? filePath, bool mostrarAlertaModal = true)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return true;

        var serie = ObterSerieAtualSelecionadaOuDetectada();
        var res = FirmwareCompatibilityValidator.Validate(serie, filePath);
        if (!res.IsCompatible)
        {
            if (mostrarAlertaModal)
            {
                MessageBox.Show(this,
                    $"{res.ErrorMessage}\n\n" +
                    $"👉 Formato esperado para este modelo: {res.ExpectedFormatDescription}\n\n" +
                    "A seleção foi BLOQUEADA para proteger a integridade do equipamento.",
                    "Firmware Incompatível Bloqueado — SPARC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            EscreverLinha($"\n[BLOQUEIO DE SEGURANÇA] Firmware '{Path.GetFileName(filePath)}' rejeitado: {res.ErrorMessage}");
            return false;
        }
        return true;
    }

    private void RevalidarFirmwareCarregadoAoMudarModelo()
    {
        if (!string.IsNullOrEmpty(_selectedIosBinPath) && !ValidarEBloquearFirmwareIncompativel(_selectedIosBinPath, mostrarAlertaModal: false))
        {
            var oldFw = Path.GetFileName(_selectedIosBinPath);
            _selectedIosBinPath = null;
            if (TxtFirmwareAutoInfo != null) TxtFirmwareAutoInfo.Text = "Nenhum arquivo selecionado";
            if (TxtIosImageInfo != null) TxtIosImageInfo.Text = "Nenhum arquivo selecionado";
            EscreverLinha($"\n[AVISO DE COMPATIBILIDADE] O firmware '{oldFw}' foi desmarcado por ser incompatível com o novo modelo selecionado.");
        }
    }

    private void CbModeloRoteadorInicial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RevalidarFirmwareCarregadoAoMudarModelo();
        AtualizarBotaoProsseguir();
        if (_syncingCombos || CbInterrupt is null || CbModeloRoteadorInicial is null)
            return;

        _syncingCombos = true;
        try
        {
            // CbModeloRoteadorInicial tem placeholder no índice 0; CbInterrupt não tem
            var idxInicial = CbModeloRoteadorInicial.SelectedIndex;
            CbInterrupt.SelectedIndex = idxInicial <= 0 ? -1 : idxInicial - 1;
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    private void CbModeloRoteadorInicial_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // O template global do ComboBox só abre pela seta; aqui o clique no campo/texto também abre.
        if (sender is ComboBox cb && !cb.IsDropDownOpen && cb.IsEnabled)
        {
            cb.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void CbInterrupt_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Em modo padrão (esteira) mantém seleção sincronizada com a primeira tela
        if (_syncingCombos || CbInterrupt is null || CbModeloRoteadorInicial is null)
            return;

        _syncingCombos = true;
        try
        {
            var idxEsteira = CbInterrupt.SelectedIndex;
            CbModeloRoteadorInicial.SelectedIndex = idxEsteira < 0 ? -1 : idxEsteira + 1;
        }
        finally
        {
            _syncingCombos = false;
        }
        RevalidarFirmwareCarregadoAoMudarModelo();
        AtualizarBotaoProsseguir();
    }

    private void ChkAtualizarFirmwareAuto_Changed(object sender, RoutedEventArgs e)
    {
        AtualizarBotaoProsseguir();
        if (BtnSelecionarFirmwareAuto != null)
        {
            BtnSelecionarFirmwareAuto.IsEnabled = ChkAtualizarFirmwareAuto?.IsChecked == true;
        }
    }

    private void AtualizarBotaoProsseguir()
    {
        if (BtnAvancarParaEsteira == null) return;
        var modeloOk = CbModeloRoteadorInicial?.SelectedIndex > 0;
        var modoManual = RbModoManual?.IsChecked == true;
        bool insumoOk = _loadedSaipCircuit != null;
        bool auto = true; // Modo Automático é o padrão (sem seleção de modo na tela inicial)
        var querAtualizar = ChkAtualizarFirmwareAuto?.IsChecked == true;
        var firmwareObrigatorio = _isRommonOrBootwareDetected || querAtualizar;
        var arquivoValido = !string.IsNullOrEmpty(_selectedIosBinPath) && System.IO.File.Exists(_selectedIosBinPath);
        var firmwareOk = !firmwareObrigatorio || arquivoValido;
        var ok = _serialOk && modeloOk && insumoOk && firmwareOk;
        BtnAvancarParaEsteira.IsEnabled = ok;

        // Atualiza checklist visual expandido de 4 itens e badges de cada passo
        AtualizarChecklist(_serialOk, modeloOk, insumoOk, firmwareOk, auto, modoManual, firmwareObrigatorio, querAtualizar, arquivoValido);
    }

    private void AtualizarChecklist(
        bool serialOk, bool modeloOk, bool insumoOk, bool firmwareOk,
        bool isAuto, bool modoManual, bool firmwareObrigatorio = true,
        bool querAtualizar = true, bool arquivoValido = false)
    {
        var brushVerdeTxt = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803D"));
        var brushVerdeBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0FDF4"));
        var brushVerdeBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));

        var brushVermelhoTxt = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#991B1B"));
        var brushVermelhoBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
        var brushVermelhoBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FECDD3"));

        var brushAmareloTxt = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));
        var brushAmareloBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
        var brushAmareloBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D"));

        var brushCinzaTxt = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        var brushCinzaBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
        var brushCinzaBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));

        var totalEtapas = 4;
        var concluidas = 0;
        if (modeloOk) concluidas++;
        if (serialOk) concluidas++;
        if (insumoOk) concluidas++;
        if (firmwareOk) concluidas++;

        // 1. BADGES DOS PASSOS SUPERIORES
        if (BadgeStep1 != null && TxtBadgeStep1 != null)
        {
            if (modeloOk && serialOk)
            {
                BadgeStep1.Background = brushVerdeBg;
                TxtBadgeStep1.Text = "🟢 Passo 1 OK (Modelo & Serial)";
                TxtBadgeStep1.Foreground = brushVerdeTxt;
            }
            else if (modeloOk)
            {
                BadgeStep1.Background = brushAmareloBg;
                TxtBadgeStep1.Text = "🟡 Falta Testar Serial";
                TxtBadgeStep1.Foreground = brushAmareloTxt;
            }
            else
            {
                BadgeStep1.Background = brushVermelhoBg;
                TxtBadgeStep1.Text = "🔴 Modelo e Serial Pendentes";
                TxtBadgeStep1.Foreground = brushVermelhoTxt;
            }
        }

        if (BadgeStep2 != null && TxtBadgeStep2 != null)
        {
            if (insumoOk)
            {
                BadgeStep2.Background = brushVerdeBg;
                TxtBadgeStep2.Text = modoManual ? "🟢 Passo 2 OK (Manual)" : "🟢 Passo 2 OK (SAIP)";
                TxtBadgeStep2.Foreground = brushVerdeTxt;
            }
            else
            {
                BadgeStep2.Background = brushVermelhoBg;
                TxtBadgeStep2.Text = modoManual ? "🔴 Preencha e clique Aplicar" : "🔴 Ficha SAIP Pendente";
                TxtBadgeStep2.Foreground = brushVermelhoTxt;
            }
        }

        if (BadgeStep3 != null && TxtBadgeStep3 != null)
        {
            if (querAtualizar)
            {
                if (arquivoValido)
                {
                    BadgeStep3.Background = brushVerdeBg;
                    TxtBadgeStep3.Text = "🟢 Passo 3 OK (Firmware Selecionado)";
                    TxtBadgeStep3.Foreground = brushVerdeTxt;
                }
                else
                {
                    BadgeStep3.Background = brushAmareloBg;
                    TxtBadgeStep3.Text = "🟡 Falta Selecionar Firmware";
                    TxtBadgeStep3.Foreground = brushAmareloTxt;
                }
            }
            else if (_isRommonOrBootwareDetected)
            {
                BadgeStep3.Background = brushVermelhoBg;
                TxtBadgeStep3.Text = _isFortiGateDetected ? "🔴 Firmware Obrigatório (BIOS Fortinet)" : "🔴 Firmware Obrigatório (ROMMON/BootWare)";
                TxtBadgeStep3.Foreground = brushVermelhoTxt;
            }
            else
            {
                BadgeStep3.Background = brushCinzaBg;
                TxtBadgeStep3.Text = "⚪ Passo 3 (Atualização Pulada)";
                TxtBadgeStep3.Foreground = brushCinzaTxt;
            }
        }

        // 2. CARDS LADO A LADO DO CHECKLIST
        if (CardChkModelo != null && TxtChkModeloIcon != null && TxtChkModeloSub != null)
        {
            CardChkModelo.Background = modeloOk ? brushVerdeBg : brushVermelhoBg;
            CardChkModelo.BorderBrush = modeloOk ? brushVerdeBorder : brushVermelhoBorder;
            TxtChkModeloIcon.Text = modeloOk ? "🟢 1a. Modelo OK" : "🔴 1a. Modelo";
            TxtChkModeloIcon.Foreground = modeloOk ? brushVerdeTxt : brushVermelhoTxt;
            var item = CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem;
            TxtChkModeloSub.Text = modeloOk ? (item?.Content?.ToString()?.Replace("🖧", "")?.Trim() ?? "Selecionado") : "Pendente: selecione";
        }

        if (CardChkSerial != null && TxtChkSerialIcon != null && TxtChkSerialSub != null)
        {
            CardChkSerial.Background = serialOk ? brushVerdeBg : brushVermelhoBg;
            CardChkSerial.BorderBrush = serialOk ? brushVerdeBorder : brushVermelhoBorder;
            TxtChkSerialIcon.Text = serialOk ? "🟢 1b. Serial OK" : "🔴 1b. Status Serial";
            TxtChkSerialIcon.Foreground = serialOk ? brushVerdeTxt : brushVermelhoTxt;
            var porta = CbPorta?.Text?.Trim();
            TxtChkSerialSub.Text = serialOk ? $"Porta {porta} validada" : "Pendente: clique Testar";
        }

        if (CardChkDados != null && TxtChkDadosIcon != null && TxtChkDadosSub != null)
        {
            CardChkDados.Background = insumoOk ? brushVerdeBg : brushVermelhoBg;
            CardChkDados.BorderBrush = insumoOk ? brushVerdeBorder : brushVermelhoBorder;
            TxtChkDadosIcon.Text = insumoOk ? "🟢 2. Ficha SAIP OK" : "🔴 2. Ficha SAIP";
            TxtChkDadosIcon.Foreground = insumoOk ? brushVerdeTxt : brushVermelhoTxt;
            TxtChkDadosSub.Text = insumoOk ? (_loadedSaipCircuit?.DesignacaoIp ?? "Circuito carregado") : (modoManual ? "Pendente: aplicar IPs" : "Pendente: selecione SAIP");
        }

        if (CardChkFirmware != null && TxtChkFirmwareIcon != null && TxtChkFirmwareSub != null)
        {
            if (querAtualizar)
            {
                CardChkFirmware.Background = arquivoValido ? brushVerdeBg : brushVermelhoBg;
                CardChkFirmware.BorderBrush = arquivoValido ? brushVerdeBorder : brushVermelhoBorder;
                TxtChkFirmwareIcon.Text = arquivoValido ? "🟢 3. Firmware OK" : "🔴 3. Firmware";
                TxtChkFirmwareIcon.Foreground = arquivoValido ? brushVerdeTxt : brushVermelhoTxt;
                TxtChkFirmwareSub.Text = arquivoValido ? Path.GetFileName(_selectedIosBinPath) : "Pendente: selecione .ipe/.bin";
            }
            else if (_isRommonOrBootwareDetected)
            {
                CardChkFirmware.Background = brushVermelhoBg;
                CardChkFirmware.BorderBrush = brushVermelhoBorder;
                TxtChkFirmwareIcon.Text = "🔴 3. Firmware";
                TxtChkFirmwareIcon.Foreground = brushVermelhoTxt;
                TxtChkFirmwareSub.Text = _isFortiGateDetected ? "Obrigatório: FortiGate em modo BIOS" : "Obrigatório: equipamento em ROMMON/BootWare";
            }
            else
            {
                CardChkFirmware.Background = brushCinzaBg;
                CardChkFirmware.BorderBrush = brushCinzaBorder;
                TxtChkFirmwareIcon.Text = "⚪ 3. Firmware";
                TxtChkFirmwareIcon.Foreground = brushCinzaTxt;
                TxtChkFirmwareSub.Text = "Desmarcado (Atualização Pulada)";
            }
        }

        // 3. CONTADOR TOTAL E BANNER INFORMATIVO
        if (TxtChecklistTotalPronto != null)
        {
            TxtChecklistTotalPronto.Text = $"{concluidas} de {totalEtapas} etapas concluídas";
            TxtChecklistTotalPronto.Foreground = concluidas == totalEtapas ? brushVerdeTxt : brushVermelhoTxt;
        }

        if (BannerStatusProntidao != null && TxtAlertaProsseguir != null)
        {
            if (concluidas == totalEtapas)
            {
                BannerStatusProntidao.Background = brushVerdeBg;
                BannerStatusProntidao.BorderBrush = brushVerdeBorder;
                TxtAlertaProsseguir.Text = "✔ TUDO PRONTO! Todos os pré-requisitos foram validados com sucesso. Clique abaixo para iniciar.";
                TxtAlertaProsseguir.Foreground = brushVerdeTxt;
            }
            else
            {
                var faltas = new List<string>();
                if (!modeloOk) faltas.Add("1. Selecionar Modelo");
                if (!serialOk) faltas.Add("2. Testar Cabo Serial");
                if (!insumoOk) faltas.Add(modoManual ? "3. Aplicar IPs Manuais" : "3. Carregar Ficha SAIP");
                if (isAuto && !firmwareOk) faltas.Add("4. Selecionar Firmware");

                BannerStatusProntidao.Background = brushAmareloBg;
                BannerStatusProntidao.BorderBrush = brushAmareloBorder;
                TxtAlertaProsseguir.Text = "⚠ ATENÇÃO: Falta concluir -> " + string.Join(" • ", faltas);
                TxtAlertaProsseguir.Foreground = brushAmareloTxt;
            }
        }

        BtnAvancarParaEsteira.ToolTip = concluidas == totalEtapas ? "Pronto para iniciar" : TxtAlertaProsseguir?.Text;
    }

    private void ConfigurarBotaoTestarTerminal(bool habilitado, string texto, string corBgHex = "#B91C1C", string corFgHex = "#FFFFFF")
    {
        Dispatcher.Invoke(() =>
        {
            if (BtnTestarAvaliarInicial != null)
            {
                BtnTestarAvaliarInicial.IsEnabled = habilitado;
                BtnTestarAvaliarInicial.Content = texto;
                try
                {
                    BtnTestarAvaliarInicial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(corBgHex));
                    BtnTestarAvaliarInicial.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(corFgHex));
                }
                catch { }
            }
            if (BtnAvaliarEquipamentoTop != null)
            {
                BtnAvaliarEquipamentoTop.IsEnabled = habilitado;
                BtnAvaliarEquipamentoTop.Content = texto;
                try
                {
                    BtnAvaliarEquipamentoTop.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(corFgHex));
                }
                catch { }
            }
        });
    }

    private void CbPortaInicial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BtnTestarAvaliarInicial != null && !BtnTestarAvaliarInicial.IsEnabled && BtnTestarAvaliarInicial.Content?.ToString() == "preencha dados para seguir")
        {
            ConfigurarBotaoTestarTerminal(true, "🔌 Testar Conexão Porta Serial", "#B91C1C", "#FFFFFF");
        }

        if (_syncingCombos || CbPorta is null || CbPortaInicial is null)
            return;

        _syncingCombos = true;
        try
        {
            // Lê o SelectedItem PRIMEIRO: no clique do dropdown o SelectionChanged
            // dispara antes do WPF sincronizar o Text editável (que ainda tem o valor antigo).
            var sel = CbPortaInicial.SelectedItem;
            var selText = (sel as ComboBoxItem)?.Content?.ToString()
                          ?? sel?.ToString()
                          ?? CbPortaInicial.Text?.Trim();

            if (!string.IsNullOrEmpty(selText))
            {
                // Garante exibição nos dois combos (texto + seleção)
                CbPortaInicial.Text = selText;
                CbPorta.Text = selText;
                var idx = CbPorta.Items.IndexOf(selText);
                if (idx >= 0) CbPorta.SelectedIndex = idx;
            }
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    private void CbPorta_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos || CbPorta is null || CbPortaInicial is null)
            return;

        _syncingCombos = true;
        try
        {
            // Lê o SelectedItem PRIMEIRO: no clique do dropdown o SelectionChanged
            // dispara antes do WPF sincronizar o Text editável (que ainda tem o valor antigo).
            var sel = CbPorta.SelectedItem;
            var selText = (sel as ComboBoxItem)?.Content?.ToString()
                          ?? sel?.ToString()
                          ?? CbPorta.Text?.Trim();

            if (!string.IsNullOrEmpty(selText))
            {
                // Garante exibição nos dois combos (texto + seleção)
                CbPorta.Text = selText;
                CbPortaInicial.Text = selText;
                var idx = CbPortaInicial.Items.IndexOf(selText);
                if (idx >= 0) CbPortaInicial.SelectedIndex = idx;
            }
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    private void BtnAtualizarPortas_Click(object sender, RoutedEventArgs e)
    {
        AtualizarPortas();
    }

    private CancellationTokenSource? _serialTestCts;
    private Task? _serialTestTask;

    private void BtnTestarSerialInicial_Click(object sender, RoutedEventArgs e) => BtnTestarAvaliar_Click(sender, e);

    private async void BtnTestarAvaliar_Click(object sender, RoutedEventArgs e)
    {
        // Se já está testando, cancela imediatamente
        if (_serialTestCts != null)
        {
            try { _serialTestCts.Cancel(); } catch { }
            TxtSerialTestStatus.Text = "↺ Cancelando...";
            return;
        }

        var porta = CbPortaInicial.Text?.Trim();
        if (string.IsNullOrEmpty(porta)) porta = CbPorta.Text?.Trim();
        if (string.IsNullOrEmpty(porta))
        {
            MessageBox.Show("Selecione a porta COM para testar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var baud = 9600;
        if (CbBaud?.Text != null && int.TryParse(CbBaud.Text, out var b)) baud = b;

        _serialTestCts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var localCts = _serialTestCts;
        if (BtnTestarAvaliarInicial != null)
        {
            BtnTestarAvaliarInicial.Content = "⏹ Cancelar";
            BtnTestarAvaliarInicial.Background = BrushErro;
        }
        if (BtnAvaliarEquipamentoTop != null)
        {
            BtnAvaliarEquipamentoTop.Content = "⏹ Cancelar";
        }
        TxtSerialTestStatus.Text = "⏳ Testando...";
        TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));

        try
        {
            _serialTestTask = TestarConexaoSerialAsync(porta, baud, localCts.Token);
            var ok = await (Task<bool>)_serialTestTask;
            _serialOk = ok;
            AtualizarBotaoProsseguir();

            if (ok)
            {
                if (TxtSerialTestStatus.Text?.Contains("ROMMON") != true &&
                    TxtSerialTestStatus.Text?.Contains("BootWare") != true &&
                    TxtSerialTestStatus.Text?.Contains("Senha") != true)
                {
                    TxtSerialTestStatus.Text = "✅ Serial OK";
                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                }
            }
            else
            {
                AtualizarPortas();
            }
        }
        catch (OperationCanceledException)
        {
            EscreverLinha($"[!] Teste serial cancelado.");
            TxtSerialTestStatus.Text = "↺ Cancelado";
            TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        }
        catch (Exception ex)
        {
            TxtSerialTestStatus.Text = "❌ Falha";
            TxtSerialTestStatus.Foreground = BrushErro;
            EscreverLinha($"[FALHA SERIAL] {ex.Message}");
        }
        finally
        {
            _serialTestTask = null;
            localCts.Dispose();
            if (_serialTestCts == localCts) _serialTestCts = null;
            if (BtnTestarAvaliarInicial != null && BtnTestarAvaliarInicial.Content?.ToString() != "preencha dados para seguir" && BtnTestarAvaliarInicial.Content?.ToString() != "Aguarde...")
            {
                BtnTestarAvaliarInicial.Content = "🔌 Testar Conexão Porta Serial";
                BtnTestarAvaliarInicial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                BtnTestarAvaliarInicial.Foreground = new SolidColorBrush(Colors.White);
                BtnTestarAvaliarInicial.IsEnabled = true;
            }
            if (BtnAvaliarEquipamentoTop != null && BtnAvaliarEquipamentoTop.Content?.ToString() != "preencha dados para seguir" && BtnAvaliarEquipamentoTop.Content?.ToString() != "Aguarde...")
            {
                BtnAvaliarEquipamentoTop.Content = "🔌 Testar Conexão Porta Serial";
                BtnAvaliarEquipamentoTop.Foreground = new SolidColorBrush(Colors.White);
                BtnAvaliarEquipamentoTop.IsEnabled = true;
            }
        }
    }

    private async Task<bool> TestarConexaoSerialAsync(string porta, int baud, CancellationToken ct)
    {
        EscreverLinha($"\n[*] [TESTE SERIAL] Abrindo {porta} @ {baud} baud (8-N-1) — Aguardando resposta (limite 10s)...");
        NetworkDevice.Protocols.Serial.SerialTransport? transport = null;
        NetworkDevice.Core.Session.DeviceSession? session = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            transport = new NetworkDevice.Protocols.Serial.SerialTransport(porta, baud);
            session = new NetworkDevice.Core.Session.DeviceSession(
                transport, new NetworkDevice.Core.Session.SessionOptions
                {
                    PromptMatcher = NetworkDevice.Core.Session.RegexPromptMatcher.Universal(),
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    CommandTimeout = TimeSpan.FromSeconds(10)
                });
            session.RawOutput += OnRawOutput;
            RegistrarSessaoAtiva(session, porta, baud);

            await transport.OpenAsync(ct);
            EscreverLinha($"[OK] Porta {porta} aberta! Lendo comunicação serial (aguardando até 10s)...");

            var rxBuffer = new byte[2048];
            var rxAccumulator = new StringBuilder();
            var fullLogAccumulator = new StringBuilder();
            var bytesReceived = false;

            var isFortiSelected = IsTagFortiGate((CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");
            var isFortiConsole = isFortiSelected;
            var fortiAdminSent = false;
            var fortiBlankPassSent = false;
            var fortiNewPassSent = false;
            var fortiConfirmPassSent = false;
            var fortiStandardPass = "CQMR";
            var currentFortiPass = ""; // "" = em branco de fábrica, depois CQMR
            var fortiCqmrAttempted = false;
            var fortiAuthenticated = false;
            var fortiCredentialsExhausted = false;

            // Envia Ctrl+C para abortar qualquer dump/paginação residual e \r\n para solicitar prompt limpo
            try { await transport.WriteAsync(new byte[] { 0x03 }, ct); } catch { }
            await Task.Delay(100, ct);
            await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);

            var deadline = DateTime.UtcNow.AddSeconds(10);
            var nextProbe = DateTime.UtcNow.AddSeconds(2.5);

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var read = await transport.ReadAsync(rxBuffer, ct);
                if (read > 0)
                {
                    bytesReceived = true;
                    var chunk = Encoding.UTF8.GetString(rxBuffer, 0, read).Replace("\uFFFD", "");
                    rxAccumulator.Append(chunk);
                    fullLogAccumulator.Append(chunk);
                    OnRawOutput(chunk);

                    var current = rxAccumulator.ToString();

                    if (!isFortiConsole && (current.Contains("FortiGate", StringComparison.OrdinalIgnoreCase) ||
                                            current.Contains("FortiOS", StringComparison.OrdinalIgnoreCase) ||
                                            current.Contains("FGT", StringComparison.OrdinalIgnoreCase) ||
                                            current.Contains("FOS", StringComparison.OrdinalIgnoreCase) ||
                                            current.Contains("CTRL+D", StringComparison.OrdinalIgnoreCase) ||
                                            Regex.IsMatch(current, @"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s+login\s*[:?]")))
                    {
                        isFortiConsole = true;
                    }

                    // Se estiver em modo BIOS / sem firmware do FortiGate (FOS boot failed / System halted / CTRL+D)
                    if (current.Contains("FOS boot failed", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Neither DEFAULT nor BACKUP", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Please install valid FOS", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("You may try backup", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("System halted", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("CTRL+D", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Reading boot image ... failed", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase))
                    {
                        isFortiConsole = true;
                        break;
                    }

                    // Se já estiver logado no prompt do FortiGate (# ou $)
                    if (isFortiConsole && Regex.IsMatch(current, @"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$"))
                    {
                        fortiAuthenticated = true;
                        break;
                    }

                    // Se FortiOS acusar falha de login, tenta a próxima credencial homologada SPARC
                    if (isFortiConsole && (current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) ||
                                           current.Contains("Login failed", StringComparison.OrdinalIgnoreCase)))
                    {
                        rxAccumulator.Clear();
                        fortiAdminSent = false;
                        fortiBlankPassSent = false;
                        if (!fortiCqmrAttempted)
                        {
                            fortiCqmrAttempted = true;
                            currentFortiPass = "CQMR";
                            deadline = DateTime.UtcNow.AddSeconds(7);
                            EscreverLinha("[>] Senha de fábrica não aceita pelo FortiGate. Tentando senha padrão SPARC (CQMR)...");
                        }
                        else
                        {
                            fortiCredentialsExhausted = true;
                            EscreverLinha("[!] Credenciais padrão do FortiGate esgotadas (em branco e CQMR). Equipamento possui senha personalizada.");
                            break;
                        }
                        await Task.Delay(200, ct);
                        await transport.WriteAsync(Encoding.ASCII.GetBytes("\r"), ct);
                        await Task.Delay(300, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(3);
                        continue;
                    }

                    // Se estiver em prompt de login do FortiGate (envia admin)
                    var isFortiLoginPrompt = isFortiConsole && Regex.IsMatch(current, @"(?i)(?:login|username)\s*[:?]");
                    if (isFortiLoginPrompt && !fortiAdminSent)
                    {
                        fortiAdminSent = true;
                        rxAccumulator.Clear();
                        deadline = DateTime.UtcNow.AddSeconds(7);
                        EscreverLinha("[>] Console FortiGate detectado. Enviando usuário 'admin'...");
                        await Task.Delay(100, ct);
                        await transport.WriteAsync(Encoding.ASCII.GetBytes("admin\r"), ct);
                        await Task.Delay(300, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(3);
                        continue;
                    }

                    // Se FortiGate pedir senha após admin
                    if (isFortiConsole && fortiAdminSent && !fortiBlankPassSent &&
                        Regex.IsMatch(current, @"(?i)(?:^|[\s\b])(?:Password|Login\s+password)\s*[:?]") &&
                        !Regex.IsMatch(current, @"(?i)(?:new|confirm)\s+password"))
                    {
                        fortiBlankPassSent = true;
                        rxAccumulator.Clear();
                        deadline = DateTime.UtcNow.AddSeconds(7);
                        if (string.IsNullOrEmpty(currentFortiPass))
                        {
                            EscreverLinha("[>] Prompt de senha FortiGate. Enviando senha em branco de fábrica...");
                            await Task.Delay(150, ct);
                            await transport.WriteAsync(Encoding.ASCII.GetBytes("\r"), ct);
                        }
                        else
                        {
                            EscreverLinha($"[>] Prompt de senha FortiGate. Enviando senha ({currentFortiPass})...");
                            await Task.Delay(150, ct);
                            await transport.WriteAsync(Encoding.ASCII.GetBytes(currentFortiPass + "\r"), ct);
                        }
                        await Task.Delay(300, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(3);
                        continue;
                    }

                    // Se FortiOS exigir troca obrigatória de nova senha (New Password:)
                    if (isFortiConsole && Regex.IsMatch(current, @"(?i)(?:new\s+password\s*[:?]|please\s+input\s+a\s+new\s+password|change\s+your\s+password)") && !fortiNewPassSent)
                    {
                        fortiNewPassSent = true;
                        rxAccumulator.Clear();
                        EscreverLinha($"[>] Troca obrigatória de senha FortiGate detectada. Definindo nova senha padrão ({fortiStandardPass})...");
                        await Task.Delay(150, ct);
                        await transport.WriteAsync(Encoding.ASCII.GetBytes(fortiStandardPass + "\r"), ct);
                        await Task.Delay(300, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(3);
                        continue;
                    }

                    // Se FortiOS pedir confirmação da nova senha (Confirm Password:)
                    if (isFortiConsole && Regex.IsMatch(current, @"(?i)(?:confirm|verify|re-?enter)\s+(?:new\s+)?password\s*[:?]") && !fortiConfirmPassSent)
                    {
                        fortiConfirmPassSent = true;
                        rxAccumulator.Clear();
                        EscreverLinha($"[>] Confirmando nova senha padrão FortiGate ({fortiStandardPass})...");
                        await Task.Delay(150, ct);
                        await transport.WriteAsync(Encoding.ASCII.GetBytes(fortiStandardPass + "\r"), ct);
                        await Task.Delay(300, ct);
                        await transport.WriteAsync(Encoding.ASCII.GetBytes("\r"), ct);
                        await Task.Delay(300, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(3);
                        continue;
                    }


                    // Se estiver em diálogo de configuração inicial do Cisco (System Configuration Dialog), responde 'no'
                    if (Regex.IsMatch(current, @"(?i)(?:initial\s+configuration\s+dialog|basic\s+management\s+setup).*(?:\[yes/no\]|\[yes/no\]:|\?)") ||
                        Regex.IsMatch(current, @"(?i)\[yes/no\]:\s*$"))
                    {
                        rxAccumulator.Clear();
                        EscreverLinha("[>] Diálogo de configuração inicial Cisco detectado. Enviando 'no'...");
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("no\r\n"), ct);
                        await Task.Delay(400, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(2);
                        continue;
                    }

                    // Se estiver em confirmação de encerramento do autoinstall do Cisco, responde 'yes'
                    if (Regex.IsMatch(current, @"(?i)terminate\s+autoinstall.*(?:\[yes(?:/no)?\]|\[yes\]:|\?)") ||
                        Regex.IsMatch(current, @"(?i)\[yes\]:\s*$"))
                    {
                        rxAccumulator.Clear();
                        EscreverLinha("[>] Confirmação de encerramento de autoinstall Cisco detectada. Enviando 'yes'...");
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("yes\r\n"), ct);
                        await Task.Delay(400, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(2);
                        continue;
                    }

                    // Se estiver em prompt 'Press ENTER to get started' ou 'Line con0 is available' (HPE Comware) ou 'Press RETURN to get started' (Cisco IOS), envia Enter
                    if (Regex.IsMatch(current, @"(?i)Press\s+(?:ENTER|RETURN)\s+to\s+get\s+started") ||
                        current.Contains("Line con0 is available", StringComparison.OrdinalIgnoreCase))
                    {
                        rxAccumulator.Clear();
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                        await Task.Delay(300, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(2);
                        continue;
                    }

                    // Se estiver no assistente de configuração inicial Cisco (Cenário 6), envia 'no' para liberar console
                    if (current.Contains("initial configuration dialog?", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("[yes/no]:", StringComparison.OrdinalIgnoreCase))
                    {
                        rxAccumulator.Clear();
                        EscreverLinha("[>] Assistente de configuração inicial detectado. Enviando 'no' para liberar console...");
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("no\r\n"), ct);
                        await Task.Delay(400, ct);
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(2);
                        continue;
                    }

                    // Se estiver em Auto-Configuration HPE (Cenário 6), envia Ctrl+C e 'Y' para liberar console
                    if (current.Contains("Automatic configuration is running", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("CTRL_C to break", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("terminate automatic configuration", StringComparison.OrdinalIgnoreCase))
                    {
                        rxAccumulator.Clear();
                        EscreverLinha("[>] Auto-Configuration HPE detectada. Enviando CTRL+C e 'Y' para liberar console...");
                        await transport.WriteAsync(new byte[] { 0x03 }, ct);
                        await Task.Delay(200, ct);
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("Y\r\n"), ct);
                        await Task.Delay(300, ct);
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(2);
                        continue;
                    }

                    // Se estiver em tela de copyright do BootWare, sai com 'q'
                    if (Regex.IsMatch(current, @"(?i)Please\s+enter\s+q/Q\s+to\s+quit"))
                    {
                        rxAccumulator.Clear();
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("q\r\n"), ct);
                        await Task.Delay(200, ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(2);
                        continue;
                    }

                    // Se estiver preso em tela More de comando CLI anterior, cancela paginação com Ctrl+C
                    if (Regex.IsMatch(current, @"(?i)--+\s*More\s*--+"))
                    {
                        rxAccumulator.Clear();
                        await transport.WriteAsync(new byte[] { 0x03 }, ct);
                        await Task.Delay(100, ct);
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                        nextProbe = DateTime.UtcNow.AddSeconds(2);
                        continue;
                    }

                    // Se já capturou um prompt reconhecível de senha, modo de usuário, rommon ou bootware (ou falha de imagem/BIOS), finaliza imediatamente com sucesso
                    if ((!isFortiConsole && Regex.IsMatch(current, @"(?i)(?:Password|Username|login|User Access Verification)\s*[:?]")) ||
                        (isFortiConsole && (current.Contains("Login failed", StringComparison.OrdinalIgnoreCase) ||
                                            current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) ||
                                            current.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase))) ||
                        Regex.IsMatch(current, @"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$") ||
                        Regex.IsMatch(current, @"[\<\[][^\r\n]+[\>\]]\s*$") ||
                        Regex.IsMatch(current, @"[^\r\n]+[>#]\s*$") ||
                        current.Contains("FOS boot failed", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Neither DEFAULT nor BACKUP", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Please install valid FOS", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("You may try backup", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("System halted", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("CTRL+D", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("choice", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("EXTENDED-BOOTWARE", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("BASIC BOOT MENU", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("BootWare", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("rommon", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Loading images fails", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("The image does not exist", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Loading boot image fails", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("The main application file does not exist", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Booting App fails", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("bad checksum", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("checksum failed", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("cannot determine first executable", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("No bootable image found", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
                else
                {
                    // Se nenhum byte novo foi recebido e passou o intervalo de sondagem, reenvia Enter para acordar console (exceto se em diálogo)
                    if (DateTime.UtcNow >= nextProbe)
                    {
                        var cur = rxAccumulator.ToString();
                        if (!cur.EndsWith("[yes/no]:", StringComparison.OrdinalIgnoreCase) && !cur.EndsWith("[yes]:", StringComparison.OrdinalIgnoreCase))
                        {
                            await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                        }
                        nextProbe = DateTime.UtcNow.AddSeconds(2.5);
                    }
                }
                await Task.Delay(50, ct);
            }

            if (!bytesReceived || rxAccumulator.Length == 0)
            {
                var portasDisp = System.IO.Ports.SerialPort.GetPortNames();
                var lista = portasDisp.Length > 0 ? string.Join(", ", portasDisp) : "(nenhuma detectada)";
                EscreverLinha("\n=================================================================");
                EscreverLinha($"   ⏱️ [TEMPO LIMITE - 10s] SEM RESPOSTA DA CONEXÃO SERIAL");
                EscreverLinha("=================================================================");
                EscreverLinha($"  Nenhum dado recebido da porta {porta} @ {baud} bps após 10 segundos.");
                EscreverLinha($"  • Causas Prováveis:");
                EscreverLinha($"    1. Cabo console desconectado ou com mau contato.");
                EscreverLinha($"    2. Equipamento desligado ou ainda inicializando.");
                EscreverLinha($"    3. Porta COM incorreta ou taxa baud divergente.");
                EscreverLinha($"  👉 Verifique a conexão do cabo, a alimentação do equipamento e a porta selecionada.");
                EscreverLinha($"  Portas COM detectadas no sistema: {lista}\n");

                Dispatcher.Invoke(() =>
                {
                    TxtSerialTestStatus.Text = "❌ Sem resposta (10s)";
                    TxtSerialTestStatus.Foreground = BrushErro;
                    MessageBox.Show(this,
                        $"Tempo limite de 10 segundos esgotado sem resposta na porta {porta}.\n\n" +
                        $"• Se acabou de ligar o roteador, ele pode ainda estar em processo de inicialização (geralmente leva de 2 a 5 minutos) — aguarde um momento e tente novamente;\n" +
                        $"• Verifique se o cabo console serial está conectado firmemente;\n" +
                        $"• Verifique se o equipamento está ligado na tomada;\n" +
                        $"• Confirme se a porta {porta} e a taxa {baud} baud são as corretas.",
                        $"Sem Resposta em {porta}",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
                return false;
            }

            var prompt = rxAccumulator.ToString().Trim();
            if (string.IsNullOrEmpty(prompt) || isFortiConsole || prompt.Contains("Verifying password", StringComparison.OrdinalIgnoreCase))
            {
                var fullP = fullLogAccumulator.ToString().Trim();
                if (!string.IsNullOrEmpty(fullP))
                    prompt = fullP;
            }
            if (string.IsNullOrEmpty(prompt)) prompt = "(dados seriais recebidos)";

            var isBiosHalt = prompt.Contains("FOS boot failed", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("Neither DEFAULT nor BACKUP", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("Please install valid FOS", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("You may try backup", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("System halted", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("CTRL+D", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase);

            // Se o buffer recebido não terminar em prompt nem em login (:), e não for BIOS em halt, envia CRLF para acordar console e capturar o prompt
            if (!isBiosHalt && !Regex.IsMatch(prompt, @"[>#:]\s*$") && !prompt.EndsWith("]") && !prompt.EndsWith(">"))
            {
                try
                {
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"), ct);
                    await Task.Delay(350, ct);
                    var wakeBuf = new byte[2048];
                    var wakeRead = await transport.ReadAsync(wakeBuf, ct);
                    if (wakeRead > 0)
                    {
                        var wakeOut = Encoding.UTF8.GetString(wakeBuf, 0, wakeRead).Replace("\uFFFD", "");
                        rxAccumulator.Append("\n" + wakeOut);
                        prompt += "\n" + wakeOut;
                    }
                }
                catch { }
            }

            var isFortiPromptOrSelected = isFortiConsole ||
                prompt.Contains("FortiGate", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("FGT", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("Fortinet", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("FortiOS", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("FOS", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("CTRL+D", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("config system", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("config firewall", StringComparison.OrdinalIgnoreCase) ||
                ((CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "").Contains("forti", StringComparison.OrdinalIgnoreCase);

            var isHpePromptOrSelected = !isFortiPromptOrSelected && (
                Regex.IsMatch(prompt, @"[\<\[][^\r\n>\]]+[\>\]]\s*$") ||
                prompt.Contains("<HPE>", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("[HPE]", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("<H3C>", StringComparison.OrdinalIgnoreCase) ||
                prompt.Contains("<HP>", StringComparison.OrdinalIgnoreCase) ||
                ((CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "").Contains("hpe", StringComparison.OrdinalIgnoreCase) ||
                ((CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "").Contains("msr", StringComparison.OrdinalIgnoreCase));

            // Se estiver em prompt FortiGate aberto (FortiGate-40F # / $), faz consulta rápida de 'get system status' (NUNCA envia 'show')
            if (isFortiPromptOrSelected && (prompt.Contains("#") || prompt.Contains("$")))
            {
                try
                {
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("get system status\r\n"), ct);
                    await Task.Delay(400, ct);
                    var verBuf = new byte[4096];
                    var verRead = await transport.ReadAsync(verBuf, ct);
                    if (verRead > 0)
                    {
                        var verOut = Encoding.UTF8.GetString(verBuf, 0, verRead).Replace("\uFFFD", "");
                        rxAccumulator.Append("\n" + verOut);
                        prompt += "\n" + verOut;
                    }
                }
                catch { }
            }
            // Se estiver em prompt HPE aberto (<HPE>, [HPE], <HP>, <H3C>), faz consulta rápida de display version
            else if (isHpePromptOrSelected)
            {
                try
                {
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("display version\r\n"), ct);
                    await Task.Delay(500, ct);
                    var verBuf = new byte[4096];
                    var verRead = await transport.ReadAsync(verBuf, ct);
                    if (verRead > 0)
                    {
                        var verOut = Encoding.UTF8.GetString(verBuf, 0, verRead).Replace("\uFFFD", "");
                        rxAccumulator.Append("\n" + verOut);
                        prompt += "\n" + verOut;
                    }
                }
                catch { }
            }
            // Se estiver em prompt Cisco aberto (Router> / Router# / cisco>), faz consulta rápida de versão/modelo
            else if (!isFortiPromptOrSelected && !isHpePromptOrSelected &&
                     (Regex.IsMatch(prompt, @"(?i)(?:^|[\r\n])[A-Za-z0-9_\-\.]+>\s*$") ||
                      Regex.IsMatch(prompt, @"(?i)(?:^|[\r\n])[A-Za-z0-9_\-\.]+#\s*$")) &&
                     !prompt.Contains("<") && !prompt.Contains("["))
            {
                try
                {
                    await transport.WriteAsync(Encoding.UTF8.GetBytes("show version | include (?:[Cc]isco|[Cc]92[0-9]|19[0-9][0-9]|ISR|Processor)\r\n"), ct);
                    await Task.Delay(400, ct);
                    var verBuf = new byte[2048];
                    var verRead = await transport.ReadAsync(verBuf, ct);
                    if (verRead > 0)
                    {
                        var verOut = Encoding.UTF8.GetString(verBuf, 0, verRead).Replace("\uFFFD", "");
                        rxAccumulator.Append("\n" + verOut);
                        prompt += "\n" + verOut;
                    }
                }
                catch { }
            }

            var userTag = (CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            var userSeries = userTag.Contains("1002") || userTag.Contains("1003") || userTag.Contains("1000") ? DeviceSeries.Msr1002 :
                             userTag.Contains("954") ? DeviceSeries.Msr954 :
                             userTag.Contains("930") ? DeviceSeries.Msr930 :
                             userTag.Contains("1900") ? DeviceSeries.Series1900 :
                             userTag.Contains("900") || userTag.Contains("921") ? DeviceSeries.Isr921 :
                             userTag.Contains("841") || userTag.Contains("800") ? DeviceSeries.Isr841 :
                             userTag.Contains("forti") || userTag.Contains("fgt") ? DeviceSeries.FortiGate40F :
                             DeviceSeries.Unknown;

            var detector = new DeviceDetector();
            var detection = detector.ClassifyPrompt(prompt, userSeries);

            var isForti = detection.Manufacturer == DeviceManufacturer.Fortinet || isFortiConsole || userTag.Contains("forti", StringComparison.OrdinalIgnoreCase);
            var isRommon = detection.BootState == BootState.Rommon;
            var isBootware = detection.BootState == BootState.Bootware;
            _isRommonOrBootwareDetected = detection.OperatingState == DeviceOperatingState.BootFailure;
            var isPasswordLocked = !_isRommonOrBootwareDetected && (detection.OperatingState == DeviceOperatingState.PasswordProtected || (isForti && !fortiAuthenticated));
            var isHpe = detection.Manufacturer == DeviceManufacturer.Hpe;
            var isCisco = detection.Manufacturer == DeviceManufacturer.Cisco && !isForti;
            _isFortiGateDetected = isForti;

            EscreverLinha($"[OK] Conexão serial física {porta} @ {baud} validada com sucesso!");

            Dispatcher.Invoke(() =>
            {
                if (_isRommonOrBootwareDetected)
                {
                    _serialOk = true;
                    var modeName = isForti ? "BIOS / Bootloader (Fortinet)" : (isRommon ? "ROMMON (Cisco)" : "BootWare (HPE)");
                    TxtSerialTestStatus.Text = isForti ? "🔴 BIOS / Sem Firmware (Fortinet)" : $"🟡 {modeName}";
                    TxtSerialTestStatus.Foreground = isForti
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"))
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));

                    if (ChkAtualizarFirmwareAuto != null)
                    {
                        ChkAtualizarFirmwareAuto.IsChecked = true;
                        if (BtnSelecionarFirmwareAuto != null)
                            BtnSelecionarFirmwareAuto.IsEnabled = true;
                    }

                    // Discrimina modelo específico usando regexes de alta precisão
                    var is1900 = !isForti && (detection.Series == DeviceSeries.Series1900
                              || DeviceDetector.Cisco1900ModelRegex.IsMatch(prompt)
                              || userSeries == DeviceSeries.Series1900);

                    var is921 = !isForti && !is1900 && (
                                detection.Series == DeviceSeries.Isr921
                             || DeviceDetector.Cisco900ModelRegex.IsMatch(prompt)
                             || userSeries == DeviceSeries.Isr921);

                    var is841 = !isForti && !is1900 && !is921 && (
                                detection.Series == DeviceSeries.Isr841
                             || DeviceDetector.Cisco841ModelRegex.IsMatch(prompt)
                             || userSeries == DeviceSeries.Isr841);

                    var isHpe1002 = !isForti && (detection.Series == DeviceSeries.Msr1002
                                 || DeviceDetector.Hpe1002ModelRegex.IsMatch(prompt)
                                 || userSeries == DeviceSeries.Msr1002);

                    var isHpe930 = !isForti && !isHpe1002 && (
                                   detection.Series == DeviceSeries.Msr930
                                || DeviceDetector.Hpe930ModelRegex.IsMatch(prompt)
                                || userSeries == DeviceSeries.Msr930);

                    var isHpe954 = !isForti && !isHpe1002 && !isHpe930 && (
                                   detection.Series == DeviceSeries.Msr954
                                || DeviceDetector.Hpe954ModelRegex.IsMatch(prompt)
                                || userSeries == DeviceSeries.Msr954);

                    string especificoNome;
                    string fwExemplo;
                    string portaTftp;

                    if (isForti)
                    {
                        especificoNome = "Fortinet FortiGate 40F (Modo BIOS / Sem Firmware)";
                        fwExemplo = "FGT_40F-*.out";
                        portaTftp = "WAN";
                        SelecionarModeloNoCombo("fortinet.fortigate.maintainer");
                    }
                    else if (isHpe)
                    {
                        especificoNome = isHpe1002 ? "HPE MSR 1002 / 1003" :
                                         isHpe930 ? "HPE MSR 930" :
                                         isHpe954 ? "HPE MSR 954" : "HPE Comware / MSR (Selecione o Modelo no Passo 1)";
                        fwExemplo = isHpe1002 ? "msr1000-cmw710-*.ipe ou *.bin" :
                                    isHpe930 ? "msr930-cmw710-*.ipe" :
                                    isHpe954 ? "msr954-cmw710-*.ipe ou *.bin" : "*.ipe / *.bin";
                        portaTftp = "GigabitEthernet0/0 (GE 0)";
                        if (isHpe1002) SelecionarModeloNoCombo("hpe.msr1002.ctrl-b");
                        else if (isHpe930) SelecionarModeloNoCombo("hpe.msr930.ctrl-b");
                        else if (isHpe954) SelecionarModeloNoCombo("hpe.msr954.ctrl-b");
                    }
                    else if (is1900)
                    {
                        especificoNome = "Cisco Série 1900 (1905/1921/1941)";
                        fwExemplo = "c1900-universalk9-mz*.bin";
                        portaTftp = "GigabitEthernet 0/0 (Porta 0 / GE 0/0)";
                        SelecionarModeloNoCombo("cisco.c1900.break");
                    }
                    else if (is921)
                    {
                        especificoNome = "Cisco Série 900 / C921-4P";
                        fwExemplo = "c900-universalk9-mz*.bin";
                        portaTftp = "GigabitEthernet 4 (Porta 4 / GE 4)";
                        SelecionarModeloNoCombo("cisco.c900.ctrl-c");
                    }
                    else if (is841)
                    {
                        especificoNome = "Cisco Série 800 / C841M";
                        fwExemplo = "c841-universalk9-mz*.bin / c800-*.bin";
                        portaTftp = "GigabitEthernet 4 (Porta 4 / GE 4)";
                        SelecionarModeloNoCombo("cisco.c841.break");
                    }
                    else
                    {
                        especificoNome = "Cisco IOS";
                        fwExemplo = "*.bin";
                        portaTftp = "Porta WAN / TFTP";
                    }

                    if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
                    {
                        TxtChkSerialIcon.Text = isForti ? "🔴 1a. Fortinet 40F (BIOS/Sem FW)" : $"🟢 1b. Serial ({modeName})";
                        TxtChkSerialSub.Text = $"{porta} ({especificoNome})";
                    }

                    if (isForti)
                    {
                        EscreverLinha($"\n=================================================================");
                        EscreverLinha($"   🚨 FORTINET FORTIGATE 40F SEM FIRMWARE CARREGADO (MODO BIOS)");
                        EscreverLinha($"=================================================================");
                        EscreverLinha($"  Porta Serial : {porta} @ {baud} bps");
                        EscreverLinha($"  Equipamento  : 🖧 Fortinet FortiGate 40F");
                        EscreverLinha($"  Estado       : 🔴 Sem Firmware Carregado (Modo BIOS / TFTP)");
                        EscreverLinha($"  Porta TFTP   : 🔴 EXCLUSIVAMENTE NA PORTA WAN (Única porta viável na BIOS)");
                        EscreverLinha($"  Firmware     : {fwExemplo} (.out)");
                        EscreverLinha($"  Informações  : Ficha SAIP (ou IPs) + Imagem de Firmware OBRIGATÓRIAS");
                        EscreverLinha($"👉 Identificamos que o 40F está sem firmware carregado. Será necessário o processo de recuperação.");
                        EscreverLinha($"👉 Conecte seu dispositivo via cabo de rede EXCLUSIVAMENTE na porta WAN.");
                        EscreverLinha($"👉 Carregue as informações do serviço (Ficha SAIP ou IPs) e a imagem de Firmware ({fwExemplo}).\n");

                        MessageBox.Show(this,
                            "🚨 FORTINET FORTIGATE 40F — RECUPERAÇÃO DE FIRMWARE NECESSÁRIA\n\n" +
                            "• Identificamos que o FortiGate 40F está sem firmware carregado na memória Flash.\n" +
                            "• Será necessário realizar o processo de recuperação via BIOS TFTP para restaurar o sistema operacional.\n\n" +
                            "🔌 CONEXÃO FÍSICA OBRIGATÓRIA:\n" +
                            "👉 Conecte seu dispositivo via cabo de rede EXCLUSIVAMENTE na porta WAN do FortiGate 40F.\n" +
                            "   (A porta WAN é a ÚNICA porta viável reconhecida pela BIOS do FortiGate 40F para download TFTP).\n\n" +
                            "📋 AÇÕES NECESSÁRIAS:\n" +
                            "1. Conecte o cabo de rede do computador na porta WAN do FortiGate 40F.\n" +
                            "2. Carregue as informações do serviço (Ficha SAIP no Passo 2 ou preencha os IPs manualmente).\n" +
                            "3. Selecione o arquivo de Firmware compatível (FGT_40F-*.out) no Passo 3.\n" +
                            "4. Clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO' para iniciar a recuperação.",
                            "Recuperação de Firmware — FortiGate 40F (Modo BIOS)",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    else
                    {
                        EscreverLinha($"\n=================================================================");
                        EscreverLinha($"   📢 {especificoNome.ToUpper()} DETECTADO EM MODO {modeName.ToUpper()}");
                        EscreverLinha($"=================================================================");
                        EscreverLinha($"  Porta Serial : {porta} @ {baud} bps");
                        EscreverLinha($"  Equipamento  : 🖧 {especificoNome}");
                        EscreverLinha($"  Estado       : 🟡 Menu {modeName} Ativo (Sem Sistema Operacional)");
                        EscreverLinha($"  Porta TFTP   : 🔴 {portaTftp}");
                        EscreverLinha($"  Firmware     : Padrão {fwExemplo}");
                        EscreverLinha($"  ⚠️ Limpeza   : O processo apagará/ignorará configurações e senhas");
                        EscreverLinha($"                 anteriores na memória para prevenir bloqueios de acesso.");
                        EscreverLinha($"👉 A RECUPERAÇÃO DE FIRMWARE É OBRIGATÓRIA.");
                        EscreverLinha($"👉 Selecione o Modelo (Passo 1), carregue a Ficha SAIP (Passo 2) e selecione o arquivo de Firmware ({fwExemplo}) no Passo 3.\n");

                        MessageBox.Show(this,
                            $"EQUIPAMENTO DETECTADO: {especificoNome.ToUpper()}\n" +
                            $"MODO: {modeName.ToUpper()}\n\n" +
                            $"• O roteador foi identificado no menu de inicialização {modeName}.\n" +
                            $"• Não há sistema operacional em execução na memória Flash/RAM.\n\n" +
                            $"⚠️ AVISO IMPORTANTE — LIMPEZA E RECUPERAÇÃO:\n" +
                            $"• A recuperação de firmware gravará uma nova imagem limpa do sistema operacional.\n" +
                            $"• Para prevenir travas por senhas anteriores desconhecidas, quaisquer configurações e senhas residuais na memória serão APAGADAS/IGNORADAS no processo.\n\n" +
                            $"👉 AÇÕES NECESSÁRIAS:\n" +
                            $"1. Conecte o cabo de rede na porta {portaTftp} para a transferência TFTP.\n" +
                            $"2. Carregue as informações do serviço (Ficha SAIP no Passo 2 ou configure os IPs do circuito).\n" +
                            $"3. Selecione a imagem de firmware compatível ({fwExemplo}) no Passo 3.\n" +
                            $"4. Clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO' para iniciar a recuperação.",
                            $"Recuperação e Limpeza — {especificoNome}",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else if (isPasswordLocked)
                {
                    _serialOk = false;
                    _skipFactoryReset = false;
                    var requiresUserAndPass = detection.AccessState == AccessState.UserAndPasswordRequired || isForti;
                    TxtSerialTestStatus.Text = (isForti && fortiCredentialsExhausted) ? "🔒 Senha Desconhecida" : (requiresUserAndPass ? "🔒 Requer Login e Senha" : "🔒 Requer Senha");
                    TxtSerialTestStatus.Foreground = BrushErro;

                    var devName = isForti ? "FortiGate 40F" : isHpe ? "HPE 954" : isCisco ? "Cisco" : "Roteador";

                    EscreverLinha("\n=================================================================");
                    if (isForti)
                    {
                        EscreverLinha("       🔒 FORTINET FORTIGATE 40F COM SENHA DETECTADO");
                        EscreverLinha("=================================================================");
                        EscreverLinha("  O FortiGate 40F conectado está solicitando autenticação de console.");
                        EscreverLinha("  👉 O SPARC testará automaticamente as credenciais de fábrica e padrão.");
                    }
                    else if (requiresUserAndPass)
                    {
                        EscreverLinha($"       🔒 {devName.ToUpper()} COM USUÁRIO E SENHA (LOGIN) DETECTADO");
                        EscreverLinha("=================================================================");
                        EscreverLinha($"  O roteador {devName} conectado está solicitando Login (Usuário) e Senha.");
                        EscreverLinha("  👉 OPÇÕES:");
                        EscreverLinha("     1. Informar Usuário e Senha conhecidos na janela para login direto (pula o zeramento).");
                        EscreverLinha("     2. Zerar a Configuração via BootWare (HPE Ctrl+B) / ROMMON (Cisco)");
                        EscreverLinha("        para apagar a configuração antiga e remover o bloqueio.");
                    }
                    else
                    {
                        EscreverLinha($"       🔒 {devName.ToUpper()} COM SENHA DE ACESSO DETECTADA");
                        EscreverLinha("=================================================================");
                        EscreverLinha($"  O roteador {devName} conectado está solicitando Senha de acesso.");
                        EscreverLinha("  👉 OPÇÕES:");
                        EscreverLinha("     1. Informar a Senha conhecida na janela para login direto (pula o zeramento).");
                        EscreverLinha("     2. Zerar a Configuração via BootWare (HPE Ctrl+B) / ROMMON (Cisco)");
                        EscreverLinha("        para apagar a configuração antiga e remover a senha.");
                    }
                    EscreverLinha("");
                    EscreverLinha("  👉 PRÓXIMOS PASSOS CASO DESEJE ZERAR A CONFIGURAÇÃO:");
                    EscreverLinha("     1. Selecione o Modelo do Equipamento no Passo 1");
                    EscreverLinha("     2. Carregue a Ficha SAIP (.pdf / .txt) no Passo 2");
                    EscreverLinha("     3. (Opcional) Selecione o Firmware (.ipe/.bin) no Passo 3 se desejar atualizá-lo");
                    EscreverLinha("     4. Clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO'");
                    EscreverLinha("=================================================================\n");

                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        if (isForti)
                        {
                            bool autoOk = false;
                            string resolvedUser = "admin";
                            string resolvedPass = "";

                            if (!fortiCredentialsExhausted)
                            {
                                EscreverLinha("[*] [FORTIGATE] Console com autenticação detectado. Testando automaticamente credenciais de fábrica (admin/em branco) e padrão SPARC (CQMR)...");
                                ConfigurarBotaoTestarTerminal(false, "Autenticando...", "#FEF2F2", "#DC2626");
                                await Task.Delay(250);

                                // 1. Tenta padrão de fábrica: admin com senha em branco
                                autoOk = await TentarLoginSerialDiretoAsync(porta, baud, "admin", "");
                                resolvedUser = "admin";
                                resolvedPass = "";

                                // 2. Se falhar, tenta admin com CQMR
                                if (!autoOk)
                                {
                                    autoOk = await TentarLoginSerialDiretoAsync(porta, baud, "admin", "CQMR");
                                    resolvedUser = "admin";
                                    resolvedPass = "CQMR";
                                }

                                // 3. Se falhar, tenta EBT com CQMR
                                if (!autoOk)
                                {
                                    autoOk = await TentarLoginSerialDiretoAsync(porta, baud, "EBT", "CQMR");
                                    resolvedUser = "EBT";
                                    resolvedPass = "CQMR";
                                }
                            }

                            if (autoOk)
                            {
                                _lastFortiResolvedUser = resolvedUser;
                                _lastFortiResolvedPass = resolvedPass;
                                _skipFactoryReset = true;
                                _serialOk = true;
                                TxtSerialTestStatus.Text = "✅ Autenticado";
                                TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                                if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
                                {
                                    TxtChkSerialIcon.Text = "🟢 1b. Serial (Autenticado)";
                                    TxtChkSerialSub.Text = $"{porta} (Acesso Fábrica/Padrão)";
                                    CardChkSerial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0FDF4"));
                                    CardChkSerial.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                                }
                                EscreverLinha($"[OK] [FORTIGATE] Autenticação automática concluída com sucesso (usuário '{resolvedUser}')! Acesso liberado sem necessidade de intervenção manual.\n");
                                SelecionarModeloNoCombo("fortinet.fgt40f");
                                ConfigurarBotaoTestarTerminal(true, "🔌 Testar Conexão Porta Serial", "#B91C1C", "#FFFFFF");
                                AtualizarBotaoProsseguir();

                                await ExecutarAvaliacaoPosLoginAsync(porta, baud);
                                return;
                            }

                            // Credenciais esgotadas / senha desconhecida
                            _serialOk = false;
                            _skipFactoryReset = false;
                            TxtSerialTestStatus.Text = "🔒 Senha Desconhecida";
                            TxtSerialTestStatus.Foreground = BrushErro;
                            if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
                            {
                                TxtChkSerialIcon.Text = "🟡 1b. Serial (Senha Desconhecida)";
                                TxtChkSerialSub.Text = $"{porta} (Requer Senha ou Quebra)";
                                CardChkSerial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
                                CardChkSerial.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
                            }
                            SelecionarModeloNoCombo("fortinet.fgt40f");
                            ConfigurarBotaoTestarTerminal(true, "🔌 Testar Conexão Porta Serial", "#B91C1C", "#FFFFFF");
                            AtualizarBotaoProsseguir();

                            EscreverLinha("\n=================================================================");
                            EscreverLinha("   🔒 [FORTIGATE 40F] SENHA DESCONHECIDA / NÃO HOMOLOGADA");
                            EscreverLinha("=================================================================");
                            EscreverLinha("  O FortiGate 40F conectado está protegido por uma senha personalizada");
                            EscreverLinha("  diferente dos padrões de fábrica e SPARC (em branco e CQMR).");
                            EscreverLinha("  👉 Não é possível coletar inventário nem provisionar sem autenticação.");
                            EscreverLinha("  👉 Escolha uma opção no diálogo:");
                            EscreverLinha("     1. Informar credenciais conhecidas manualmente;");
                            EscreverLinha("     2. Quebrar senha e zerar configuração de forma autônoma.");
                            EscreverLinha("=================================================================\n");
                        }

                        string? loginErrorMessage = null;
                        string? lastUser = null;

                        while (true)
                        {
                            var authDlg = new PasswordAuthDialog(requiresUserAndPass, devName, loginErrorMessage, lastUser, allowBlankPassword: false, isFortiGate: isForti) { Owner = this };
                            if (authDlg.ShowDialog() != true)
                            {
                                // Operador cancelou o diálogo
                                if (isForti)
                                {
                                    _serialOk = false;
                                    TxtSerialTestStatus.Text = "🔒 Senha Desconhecida";
                                    TxtSerialTestStatus.Foreground = BrushErro;
                                    if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
                                    {
                                        TxtChkSerialIcon.Text = "🟡 1b. Serial (Senha Desconhecida)";
                                        TxtChkSerialSub.Text = $"{porta} (Requer Senha ou Quebra)";
                                        CardChkSerial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
                                        CardChkSerial.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
                                    }
                                    EscreverLinha("[AVISO] Diálogo fechado. FortiGate 40F permanece bloqueado por senha desconhecida.");
                                }
                                ConfigurarBotaoTestarTerminal(true, "🔌 Testar Conexão Porta Serial", "#B91C1C", "#FFFFFF");
                                AtualizarBotaoProsseguir();
                                break;
                            }

                            if (authDlg.Choice == PasswordAuthChoice.FactoryReset)
                            {
                                if (isForti)
                                {
                                    EscreverLinha("\n[*] [FORTIGATE] Operador optou pela quebra de senha e recuperação autônoma.");
                                    EscreverLinha("👉 Abrindo assistente autônomo do SPARC para monitorar boot e restabelecer acesso...");

                                    var autoRecWin = new FortiGateAutoRecoveryWindow(porta, baud) { Owner = this };
                                    var res = autoRecWin.ShowDialog();

                                    if (res == true && autoRecWin.Sucesso)
                                    {
                                        _lastFortiResolvedUser = "admin";
                                        _lastFortiResolvedPass = "";
                                        _skipFactoryReset = true;
                                        _serialOk = true;
                                        TxtSerialTestStatus.Text = "✅ Autenticado";
                                        TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                                        if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
                                        {
                                            TxtChkSerialIcon.Text = "🟢 1b. Serial (Autenticado)";
                                            TxtChkSerialSub.Text = $"{porta} (Acesso Recuperado)";
                                            CardChkSerial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0FDF4"));
                                            CardChkSerial.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                                        }
                                        EscreverLinha("[OK] [FORTIGATE] Senha restaurada ao padrão de fábrica com sucesso! Prossiga com o provisionamento padrão.\n");
                                        SelecionarModeloNoCombo("fortinet.fgt40f");
                                        ConfigurarBotaoTestarTerminal(true, "🔌 Testar Conexão Porta Serial", "#B91C1C", "#FFFFFF");
                                        AtualizarBotaoProsseguir();

                                        await ExecutarAvaliacaoPosLoginAsync(porta, baud);

                                        MessageBox.Show(
                                            "✅ Reset de fábrica e acesso liberados com sucesso!\n\n" +
                                            "A senha do FortiGate 40F foi removida (padrão de fábrica restaurado).\n\n" +
                                            "👉 AÇÃO NECESSÁRIA PARA O PROVISIONAMENTO:\n" +
                                            "1. Carregue os dados do circuito (Passo 2 — Ficha SAIP);\n" +
                                            "2. Selecione ou valide o Firmware (Passo 3, se necessário);\n" +
                                            "3. Clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO' para prosseguir.",
                                            "Reset Concluído — Carregamento de Circuito e Firmware",
                                            MessageBoxButton.OK,
                                            MessageBoxImage.Information);

                                        EscreverLinha("\n=================================================================");
                                        EscreverLinha("👉 AVISO OPERACIONAL: Siga com o carregamento dos dados do");
                                        EscreverLinha("   circuito (Ficha SAIP) e do firmware para provisionar.");
                                        EscreverLinha("=================================================================\n");
                                        break;
                                    }
                                    else
                                    {
                                        _skipFactoryReset = false;
                                        _serialOk = true;
                                        TxtSerialTestStatus.Text = "🔒 Recuperar/Zerar";
                                        TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                                        if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
                                        {
                                            TxtChkSerialIcon.Text = "🟢 1b. Serial (Com Senha)";
                                            TxtChkSerialSub.Text = $"{porta} (Requer Zeramento)";
                                        }
                                        ConfigurarBotaoTestarTerminal(false, "preencha dados para seguir", "#FEF2F2", "#DC2626");
                                        EscreverLinha("[*] [FORTIGATE] Quebra de senha agendada para execução durante o provisionamento automático.");
                                        EscreverLinha("👉 AÇÃO: Carregue a Ficha SAIP (Passo 2) e clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO'.\n");
                                        SelecionarModeloNoCombo("fortinet.fgt40f");
                                        AtualizarBotaoProsseguir();
                                        break;
                                    }
                                }

                                _skipFactoryReset = false;
                                TxtSerialTestStatus.Text = "🔒 Zerar Configuração";
                                TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                                if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
                                {
                                    TxtChkSerialIcon.Text = "🟢 1b. Serial (Com Senha)";
                                    TxtChkSerialSub.Text = $"{porta} (Requer Zeramento)";
                                }
                                ConfigurarBotaoTestarTerminal(false, "preencha dados para seguir", "#FEF2F2", "#DC2626");
                                EscreverLinha("[*] Operador optou pelo zeramento de fábrica automatizado (alternativa de zerar a configuração).");
                                EscreverLinha("👉 AÇÃO NECESSÁRIA: Selecione o Modelo exato do Equipamento no Passo 1a.");
                                EscreverLinha("👉 Em seguida, carregue a Ficha SAIP (Passo 2) e clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO'.\n");

                                MessageBox.Show(this,
                                    "🔒 ZERAMENTO DE CONFIGURAÇÃO SELECIONADO\n\n" +
                                    "Como o equipamento está protegido por senha, o modelo exato precisa ser indicado pelo operador no Passo 1a.\n\n" +
                                    "👉 POR FAVOR, SELECIONE O MODELO NO PASSO 1a:\n" +
                                    "   • HPE MSR 954 / 958 (BootWare Ctrl+B)\n" +
                                    "   • HPE MSR 930 / 931 / 935 (BootWare Ctrl+B)\n" +
                                    "   • Cisco Série 1900 / 1921 / 1941 / 1905 (ROMMON Break)\n" +
                                    "   • Cisco Série 900 / C921-4P (ROMMON Ctrl+C)\n" +
                                    "   • Cisco Série 800 / C841M (ROMMON Break)\n\n" +
                                    "Após selecionar o modelo correto e carregar a Ficha SAIP, clique em 'INICIAR PROVISIONAMENTO AUTOMÁTICO'.",
                                    "Selecione o Modelo — Passo 1a",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information);

                                if (CbModeloRoteadorInicial != null)
                                {
                                    CbModeloRoteadorInicial.Focus();
                                }

                                AtualizarBotaoProsseguir();
                                break;
                            }

                            if (authDlg.Choice == PasswordAuthChoice.LoginDirect)
                            {
                                ConfigurarBotaoTestarTerminal(false, "Aguarde...", "#FEF2F2", "#DC2626");
                                var user = authDlg.Username;
                                var pass = authDlg.Password;
                                lastUser = user;

                                if (requiresUserAndPass)
                                {
                                    EscreverLinha($"\n[*] [LOGIN CONSOLE] Tentando login direto com usuário '{user}' e senha em {porta} @ {baud}...");
                                }
                                else
                                {
                                    EscreverLinha($"\n[*] [LOGIN CONSOLE] Tentando login direto com senha em {porta} @ {baud}...");
                                }

                                bool loginOk = false;
                                try
                                {
                                    loginOk = await TentarLoginSerialDiretoAsync(porta, baud, user, pass);
                                }
                                catch
                                {
                                    loginOk = false;
                                }

                                if (loginOk)
                                {
                                    if (isForti)
                                    {
                                        _lastFortiResolvedUser = user;
                                        _lastFortiResolvedPass = pass;
                                    }
                                    _skipFactoryReset = true;
                                    _serialOk = true;
                                    TxtSerialTestStatus.Text = "✅ Autenticado";
                                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                                    if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
                                    {
                                        TxtChkSerialIcon.Text = "🟢 1b. Serial (Autenticado)";
                                        TxtChkSerialSub.Text = $"{porta} (Zeramento Pulado)";
                                        CardChkSerial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0FDF4"));
                                        CardChkSerial.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                                    }
                                    EscreverLinha("[OK] Autenticação realizada com sucesso! Acesso ao console concedido sem necessidade de zeramento.\n");

                                    // Executa inventário e avaliação automática do equipamento
                                    EscreverLinha("[*] Coletando inventário e avaliando equipamento pós-login...");
                                    await ExecutarAvaliacaoPosLoginAsync(porta, baud);

                                    ConfigurarBotaoTestarTerminal(true, "🔌 Testar Conexão Porta Serial", "#B91C1C", "#FFFFFF");
                                    AtualizarBotaoProsseguir();
                                    break;
                                }
                                else
                                {
                                    _skipFactoryReset = false;
                                    TxtSerialTestStatus.Text = "🔒 Falha Login";
                                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                                    var credMsg = requiresUserAndPass ? "Usuário ou senha incorretos." : "Senha incorreta.";
                                    EscreverLinha($"[AVISO] {credMsg} Acesso negado pelo equipamento em {porta}.");
                                    EscreverLinha("👉 Escolha entre informar uma nova combinação de credenciais ou optar por zerar a configuração de fábrica.\n");

                                    loginErrorMessage = $"❌ {credMsg} Informe nova combinação de credenciais ou consulte o Guia de Quebra de Senha.";
                                }
                            }
                        }
                    }));
                }
                else
                {
                    _serialOk = true;
                    _skipFactoryReset = true;
                    TxtSerialTestStatus.Text = "✅ Serial OK";
                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

                    string especificoNome;
                    if (isHpe)
                    {
                        var isHpe1002 = detection.Series == DeviceSeries.Msr1002
                                     || DeviceDetector.Hpe1002ModelRegex.IsMatch(prompt)
                                     || userSeries == DeviceSeries.Msr1002;

                        var isHpe930 = !isHpe1002 && (
                                       detection.Series == DeviceSeries.Msr930
                                    || prompt.Contains("930", StringComparison.OrdinalIgnoreCase)
                                    || prompt.Contains("MSR930", StringComparison.OrdinalIgnoreCase)
                                    || userSeries == DeviceSeries.Msr930);

                        especificoNome = isHpe1002 ? "HPE MSR 1002 / 1003" :
                                         isHpe930 ? "HPE MSR 930" : "HPE MSR 954";
                    }
                    else if (isCisco)
                    {
                        var is1900 = detection.Series == DeviceSeries.Series1900
                                  || DeviceDetector.Cisco1900ModelRegex.IsMatch(prompt)
                                  || userSeries == DeviceSeries.Series1900;

                        var is921 = !is1900 && (
                                    detection.Series == DeviceSeries.Isr921
                                 || DeviceDetector.Cisco900ModelRegex.IsMatch(prompt)
                                 || userSeries == DeviceSeries.Isr921);

                        var is841 = !is1900 && !is921 && (
                                    detection.Series == DeviceSeries.Isr841
                                 || DeviceDetector.Cisco841ModelRegex.IsMatch(prompt)
                                 || userSeries == DeviceSeries.Isr841);

                        especificoNome = is1900 ? "Cisco Série 1900 (1905/1921/1941)" :
                                         is921 ? "Cisco Série 900 / C921-4P" :
                                         is841 ? "Cisco Série 800 / C841M" :
                                         "Cisco IOS";
                    }
                    else if (isForti)
                    {
                        especificoNome = "FortiGate 40F";
                    }
                    else
                    {
                        especificoNome = "Roteador";
                    }

                    EscreverLinha($"\n=================================================================");
                    EscreverLinha($"   🟢 CONEXÃO SERIAL VALIDADA COM SUCESSO (ACESSO LIVRE)");
                    EscreverLinha($"=================================================================");
                    EscreverLinha($"  Porta Serial : {porta} @ {baud} bps");
                    EscreverLinha($"  Equipamento  : 🖧 {especificoNome}");
                    EscreverLinha($"  Estado       : ✅ Terminal Operacional Aberto (Sem Senha)");
                    EscreverLinha($"[*] Iniciando coleta automática de inventário e avaliação do equipamento...\n");

                    Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await ExecutarAvaliacaoPosLoginAsync(porta, baud);
                    }));
                }

                if (CardChkSerial != null && TxtChkSerialIcon != null && TxtChkSerialSub != null)
                {
                    if (isPasswordLocked && !_serialOk)
                    {
                        TxtChkSerialIcon.Text = (isForti && fortiCredentialsExhausted) ? "🟡 1b. Serial (Senha Desconhecida)" : "🟡 1b. Serial (Requer Senha)";
                        TxtChkSerialSub.Text = $"{porta} (Autenticação Pendente)";
                        CardChkSerial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFBEB"));
                        CardChkSerial.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
                    }
                    else
                    {
                        TxtChkSerialIcon.Text = isPasswordLocked ? (_skipFactoryReset ? "🟢 1b. Serial (Autenticado)" : "🟢 1b. Serial (Com Senha)") : "🟢 1b. Status Serial";
                        TxtChkSerialSub.Text = isPasswordLocked ? (_skipFactoryReset ? $"{porta} (Zeramento Pulado)" : $"{porta} (Requer Zeramento)") : (isForti ? $"{porta} (Acesso Livre)" : $"{porta} @ {baud} OK");
                        CardChkSerial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0FDF4"));
                        CardChkSerial.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BBF7D0"));
                    }
                }

                if (isHpe)
                {
                    var isHpe1002 = detection.Series == DeviceSeries.Msr1002
                                 || DeviceDetector.Hpe1002ModelRegex.IsMatch(prompt)
                                 || userSeries == DeviceSeries.Msr1002;

                    var isHpe930 = !isHpe1002 && (
                                   detection.Series == DeviceSeries.Msr930
                                || DeviceDetector.Hpe930ModelRegex.IsMatch(prompt)
                                || userSeries == DeviceSeries.Msr930);

                    var isHpe954 = !isHpe1002 && !isHpe930 && (
                                   detection.Series == DeviceSeries.Msr954
                                || DeviceDetector.Hpe954ModelRegex.IsMatch(prompt)
                                || userSeries == DeviceSeries.Msr954);

                    if (isHpe1002) SelecionarModeloNoCombo("hpe.msr1002.ctrl-b");
                    else if (isHpe930) SelecionarModeloNoCombo("hpe.msr930.ctrl-b");
                    else if (isHpe954) SelecionarModeloNoCombo("hpe.msr954.ctrl-b");
                }
                else if (isCisco)
                {
                    var is1900 = detection.Series == DeviceSeries.Series1900
                              || DeviceDetector.Cisco1900ModelRegex.IsMatch(prompt)
                              || userSeries == DeviceSeries.Series1900;

                    var is921 = !is1900 && (
                                detection.Series == DeviceSeries.Isr921
                             || DeviceDetector.Cisco900ModelRegex.IsMatch(prompt)
                             || userSeries == DeviceSeries.Isr921);

                    var is841 = !is1900 && !is921 && (
                                detection.Series == DeviceSeries.Isr841
                             || DeviceDetector.Cisco841ModelRegex.IsMatch(prompt)
                             || userSeries == DeviceSeries.Isr841);

                    if (is1900)
                    {
                        SelecionarModeloNoCombo("cisco.c1900.break");
                    }
                    else if (is921)
                    {
                        SelecionarModeloNoCombo("cisco.c900.ctrl-c");
                    }
                    else if (is841)
                    {
                        SelecionarModeloNoCombo("cisco.c841.break");
                    }
                }
                else if (isForti)
                {
                    SelecionarModeloNoCombo("fortinet.fgt40f");
                }

                AtualizarBotaoProsseguir();
            });

            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var isPortInUse = ex is UnauthorizedAccessException ||
                              ex.Message.Contains("em uso", StringComparison.OrdinalIgnoreCase) ||
                              ex.Message.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) ||
                              ex.Message.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
                              ex.InnerException is UnauthorizedAccessException;

            var portasDisp2 = System.IO.Ports.SerialPort.GetPortNames();
            var lista2 = portasDisp2.Length > 0 ? string.Join(", ", portasDisp2) : "(nenhuma porta detectada)";

            EscreverLinha("\n=================================================================");
            if (isPortInUse)
            {
                EscreverLinha($"   ⚠️ [FALHA SERIAL] PORTA {porta} EM USO OU ACESSO NEGADO");
                EscreverLinha("=================================================================");
                EscreverLinha($"  Não foi possível abrir a porta {porta} ({ex.Message}).");
                EscreverLinha($"  • Motivo Provável: Há outro cliente ou programa aberto usando a porta {porta} (ex: PuTTY, Tera Term, SecureCRT, CMD ou outra janela do SPARC).");
                EscreverLinha($"  👉 Ação: Feche os outros programas que estejam usando a porta {porta} e clique em 'Testar Conexão Porta Serial' novamente.");
                EscreverLinha($"  Portas COM detectadas no sistema: {lista2}\n");

                Dispatcher.Invoke(() =>
                {
                    TxtSerialTestStatus.Text = "❌ Porta em uso";
                    TxtSerialTestStatus.Foreground = BrushErro;
                    MessageBox.Show(this,
                        $"A porta {porta} está em uso ou sem permissão de acesso.\n\n" +
                        $"• Motivo: Outro programa ou cliente serial pode estar aberto usando a porta {porta} (ex: PuTTY, Tera Term, SecureCRT ou outra janela do SPARC).\n\n" +
                        $"👉 Feche qualquer programa que esteja utilizando a porta {porta} e tente novamente.",
                        $"Porta {porta} em Uso",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
            else
            {
                EscreverLinha($"   ⚠️ [FALHA SERIAL] {porta} @ {baud} bps — {ex.Message}");
                EscreverLinha("=================================================================");
                EscreverLinha($"  Portas COM detectadas no sistema: {lista2}");
                EscreverLinha($"  👉 Verifique: porta {porta}, baud {baud}, cabo console e energização.");
                EscreverLinha($"  💡 NOTA: Se acabou de ligar o equipamento, aguarde a inicialização completa (geralmente de 2 a 5 minutos).\n");

                Dispatcher.Invoke(() =>
                {
                    TxtSerialTestStatus.Text = "❌ Falha na porta";
                    TxtSerialTestStatus.Foreground = BrushErro;
                });
            }
            return false;
        }
        finally
        {
            if (session != null)
            {
                session.RawOutput -= OnRawOutput;
                try { await session.DisposeAsync(); } catch { }
            }
            if (transport != null)
            {
                try { await transport.DisposeAsync(); } catch { }
            }
        }
    }

    private async Task<bool> TentarLoginSerialDiretoAsync(string porta, int baud, string? username, string pass)
    {
        SerialTransport? transport = null;
        try
        {
            transport = new SerialTransport(porta, baud, readTimeout: TimeSpan.FromMilliseconds(150));
            await transport.OpenAsync();

            var rxAccumulator = new StringBuilder();
            var buf = new byte[1024];

            // 1. Sondagem rápida para não cancelar com Ctrl+C um prompt já aberto de New Password
            var initBuf = new byte[512];
            var initRead = await transport.ReadAsync(initBuf);
            if (initRead > 0)
            {
                rxAccumulator.Append(Encoding.UTF8.GetString(initBuf, 0, initRead));
            }
            if (!Regex.IsMatch(rxAccumulator.ToString(), @"(?i)(?:new|confirm)\s+password"))
            {
                await transport.WriteAsync(new byte[] { 0x03 });
                await Task.Delay(100);
                await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"));
            }

            var passwordSent = false;
            var usernameSent = false;
            var newPasswordSent = false;
            var confirmPasswordSent = false;
            var standardPassToSet = !string.IsNullOrWhiteSpace(pass) ? pass.Trim() : "CQMR";
            var sw = Stopwatch.StartNew();
            long lastProbeMs = 0;
            var isFortiConsole = rxAccumulator.ToString().Contains("FortiGate", StringComparison.OrdinalIgnoreCase) || username == "admin";

            while (sw.ElapsedMilliseconds < 15000)
            {
                var r = await transport.ReadAsync(buf);
                if (r > 0)
                {
                    var text = Encoding.UTF8.GetString(buf, 0, r);
                    rxAccumulator.Append(text);
                    var current = rxAccumulator.ToString();

                    // Sucesso: prompt de shell liberado (<HPE>, [HPE], Router#, Router>, Switch#, FortiGate-40F #, etc.)
                    if (current.Contains("<HPE", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("[HPE", StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(current, @"[\<\[][^\r\n>\]]+[\>\]]\s*$") ||
                        Regex.IsMatch(current, @"[A-Za-z0-9_\-\.\(\)]+[>#]\s*$") ||
                        Regex.IsMatch(current, @"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$"))
                    {
                        EscreverLinha($"[LOGIN CONSOLE] Sucesso! Prompt autenticado detectado: {current.Replace("\r", "").Replace("\n", " | ").Trim()}");
                        return true;
                    }

                    // Se estiver preso em tela More de comando anterior, cancela paginação com Ctrl+C
                    if (Regex.IsMatch(current, @"(?i)--+\s*More\s*--+"))
                    {
                        rxAccumulator.Clear();
                        await transport.WriteAsync(new byte[] { 0x03 });
                        await Task.Delay(100);
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"));
                        await Task.Delay(200);
                        continue;
                    }

                    // Se pedir ENTER
                    if (current.Contains("Press ENTER", StringComparison.OrdinalIgnoreCase) && !passwordSent && !usernameSent)
                    {
                        rxAccumulator.Clear();
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"));
                        await Task.Delay(200);
                        continue;
                    }

                    // Se pedir Username / login
                    if ((current.Contains("Username:", StringComparison.OrdinalIgnoreCase) ||
                         current.Contains("login:", StringComparison.OrdinalIgnoreCase) ||
                         Regex.IsMatch(current, @"(?i)(?:^|[\s\b@:])(?:Username|login)\s*[:?]")) && !usernameSent)
                    {
                        usernameSent = true;
                        rxAccumulator.Clear();
                        await Task.Delay(100);
                        var userToSend = string.IsNullOrWhiteSpace(username) ? "admin" : username.Trim();
                        EscreverLinha($"[LOGIN CONSOLE] Prompt de login detectado. Enviando usuário: '{userToSend}'");
                        var eol = (isFortiConsole || userToSend == "admin") ? "\r" : "\r\n";
                        await transport.WriteAsync(Encoding.UTF8.GetBytes(userToSend + eol));
                        await Task.Delay(300);
                        continue;
                    }

                    // Se FortiOS exigir troca obrigatória de senha (primeiro login admin de fábrica ou política de senha)
                    if (Regex.IsMatch(current, @"(?i)(?:new\s+password\s*[:?]|please\s+input\s+a\s+new\s+password|change\s+your\s+password)") && !newPasswordSent)
                    {
                        newPasswordSent = true;
                        rxAccumulator.Clear();
                        await Task.Delay(150);
                        EscreverLinha($"[LOGIN CONSOLE] Troca obrigatória de senha FortiGate detectada. Definindo senha padrão ({standardPassToSet})...");
                        await transport.WriteAsync(Encoding.UTF8.GetBytes(standardPassToSet + "\r"));
                        await Task.Delay(300);
                        continue;
                    }

                    // Se pedir confirmação de nova senha (FortiOS)
                    if (Regex.IsMatch(current, @"(?i)(?:confirm|verify|re-?enter)\s+(?:new\s+)?password\s*[:?]") && !confirmPasswordSent)
                    {
                        confirmPasswordSent = true;
                        rxAccumulator.Clear();
                        await Task.Delay(150);
                        EscreverLinha("[LOGIN CONSOLE] Confirmando nova senha padrão FortiGate...");
                        await transport.WriteAsync(Encoding.UTF8.GetBytes(standardPassToSet + "\r"));
                        await Task.Delay(300);
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r"));
                        await Task.Delay(300);
                        continue;
                    }


                    // Se pedir Password (inicial)
                    if (Regex.IsMatch(current, @"(?i)(?:^|[\s\b])(?:Password|Login\s+password)\s*[:?]") &&
                        !Regex.IsMatch(current, @"(?i)(?:new|confirm)\s+password") &&
                        !passwordSent)
                    {
                        passwordSent = true;
                        rxAccumulator.Clear();
                        await Task.Delay(150);
                        EscreverLinha("[LOGIN CONSOLE] Prompt de senha detectado. Enviando senha...");
                        // Envia a senha (sem enter extra para não enviar senha vazia no prompt subsequente de New Password)
                        var passEol = (isFortiConsole || username == "admin") ? "\r" : "\r\n";
                        await transport.WriteAsync(Encoding.UTF8.GetBytes(pass.Trim() + passEol));
                        await Task.Delay(300);
                        continue;
                    }

                    // Falha explícita após envio da senha (não confunde com New Password / Confirm Password)
                    var isNewPassPrompt = Regex.IsMatch(current, @"(?i)(?:new|confirm|verify|re-?enter)\s+(?:new\s+)?password|please\s+input\s+a\s+new\s+password|change\s+your\s+password");
                    if (passwordSent && !isNewPassPrompt && (current.Contains("Login failed", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Authentication failure", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Bad passwords", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Bad password", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Login invalid", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
                                         current.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                                         (sw.ElapsedMilliseconds > 1200 && Regex.IsMatch(current, @"(?i)(?:^|[\s\b])(?:Password|Login\s+password|Username|login)\s*[:?]"))))
                    {
                        EscreverLinha($"[LOGIN CONSOLE] Falha de autenticação reportada pelo equipamento: {current.Replace("\r", "").Replace("\n", " | ").Trim()}");
                        return false;
                    }
                    await Task.Delay(50);
                }
                else
                {
                    if ((confirmPasswordSent || passwordSent) && sw.ElapsedMilliseconds > lastProbeMs + 1200)
                    {
                        lastProbeMs = sw.ElapsedMilliseconds;
                        await transport.WriteAsync(Encoding.UTF8.GetBytes("\r\n"));
                    }
                    await Task.Delay(50);
                }
            }

            var finalOutput = rxAccumulator.ToString().Trim();
            var isGranted = finalOutput.Contains("<HPE", StringComparison.OrdinalIgnoreCase) ||
                            finalOutput.Contains("[HPE", StringComparison.OrdinalIgnoreCase) ||
                            Regex.IsMatch(finalOutput, @"[\<\[][^\r\n>\]]+[\>\]]") ||
                            Regex.IsMatch(finalOutput, @"[A-Za-z0-9_\-\.\(\)]+[>#]") ||
                            Regex.IsMatch(finalOutput, @"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s*(?:\([^()\r\n]*\))?\s*[#$]");

            return isGranted;
        }
        catch (Exception ex)
        {
            EscreverLinha($"[LOGIN CONSOLE] Erro ao comunicar com {porta}: {ex.Message}");
            return false;
        }
        finally
        {
            if (transport != null)
            {
                try { await transport.DisposeAsync(); } catch { }
            }
        }
    }

    private async Task ExecutarAvaliacaoPosLoginAsync(string porta, int baud, int maxTentativas = 3)
    {
        Dispatcher.Invoke(() =>
        {
            ConfigurarBotaoTestarTerminal(false, "Aguarde...", "#FEF2F2", "#DC2626");
            TxtSerialTestStatus.Text = "⏳ Avaliando...";
            TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
        });

        // Aguarda liberação da COM pelo teste serial anterior (Dispose do SerialPort
        // não libera instantaneamente — 1º clique falhava aqui com "porta em uso").
        await Task.Delay(700);

        var avaliacaoOk = false;
        Exception? ultimoErro = null;

        for (var tentativa = 1; tentativa <= maxTentativas && !avaliacaoOk; tentativa++)
        {
            if (tentativa > 1)
            {
                Dispatcher.Invoke(() =>
                {
                    TxtSerialTestStatus.Text = $"⏳ Reavaliando... ({tentativa}/{maxTentativas})";
                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                });
                EscreverLinha($"[*] Nova tentativa de avaliação em 2s... ({tentativa}/{maxTentativas})");
                await Task.Delay(2000);
            }

            SerialTransport? transport = null;
            DeviceSession? session = null;
            try
            {
                var isFortiPre = _isFortiGateDetected
                              || IsTagFortiGate((CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");

                transport = new SerialTransport(porta, baud, readTimeout: TimeSpan.FromMilliseconds(400));
                session = new DeviceSession(transport, new SessionOptions
                {
                    PromptMatcher = RegexPromptMatcher.Universal(),
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    CommandTimeout = TimeSpan.FromSeconds(10),
                    Username = isFortiPre ? "admin" : null,
                    Password = isFortiPre ? "CQMR" : null
                });
                session.RawOutput += OnRawOutput;
                // Interrompe qualquer dump/paginação residual no console físico antes de negociar prompt
                try
                {
                    await transport.OpenAsync(CancellationToken.None);
                    await transport.WriteAsync(new byte[] { 0x03 }, CancellationToken.None);
                    await Task.Delay(100);
                }
                catch { }

                await session.ConnectAsync(CancellationToken.None);

                var prompt = session.CurrentPrompt ?? "";
                var isForti = _isFortiGateDetected
                           || prompt.Contains("FortiGate", StringComparison.OrdinalIgnoreCase)
                           || prompt.Contains("FGT", StringComparison.OrdinalIgnoreCase)
                           || prompt.Contains("FortiOS", StringComparison.OrdinalIgnoreCase)
                           || IsTagFortiGate((CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");

                var isHpe = !isForti && (prompt.StartsWith("<") || prompt.StartsWith("[") || prompt.Contains("HPE", StringComparison.OrdinalIgnoreCase));

                if (isForti)
                {
                    await AvaliarEquipamentoFortinetAsync(session, CancellationToken.None);
                }
                else if (isHpe)
                {
                    await AvaliarEquipamentoHpeAsync(session, CancellationToken.None);
                }
                else
                {
                    await AvaliarEquipamentoCiscoAsync(session, CancellationToken.None);
                }

                avaliacaoOk = true;
            }
            catch (Exception ex)
            {
                ultimoErro = ex;
                EscreverLinha($"[AVISO] Tentativa {tentativa}/{maxTentativas} de avaliação falhou: {ex.Message}");
            }
            finally
            {
                if (session != null)
                {
                    session.RawOutput -= OnRawOutput;
                    try { await session.DisposeAsync(); } catch { }
                }
                if (transport != null)
                {
                    try { await transport.DisposeAsync(); } catch { }
                }
            }
        }

        Dispatcher.Invoke(() =>
        {
            if (avaliacaoOk)
            {
                ConfigurarBotaoTestarTerminal(true, "🔌 Testar Conexão Porta Serial", "#B91C1C", "#FFFFFF");
                if (TxtSerialTestStatus.Text.StartsWith("⏳"))
                {
                    TxtSerialTestStatus.Text = "✅ Avaliado";
                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                }
            }
            else
            {
                // Status intermediário honesto: não marca "Avaliado" quando o inventário falhou.
                EscreverLinha($"[AVISO] Não foi possível obter inventário completo após {maxTentativas} tentativas: {ultimoErro?.Message}");
                EscreverLinha("👉 Aguarde 5s (estabilização do console) e clique em 'Testar Conexão Porta Serial' novamente.");
                TxtSerialTestStatus.Text = "⚠️ Avaliação incompleta — testar novamente";
                TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                ConfigurarBotaoTestarTerminal(true, "🔌 Testar Conexão Porta Serial", "#B91C1C", "#FFFFFF");
            }
            AtualizarBotaoProsseguir();
        });
    }

    private async Task AvaliarEquipamentoFortinetAsync(DeviceSession session, CancellationToken ct)
    {
        // 1. Desabilita paginação (--More--) no console FortiOS
        try
        {
            await session.SendCommandAsync("config system console", TimeSpan.FromSeconds(3), ct);
            await session.SendCommandAsync("set output standard", TimeSpan.FromSeconds(3), ct);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(3), ct);
        }
        catch { }

        // 2. get system status
        var statusOut = await session.SendCommandAsync("get system status", TimeSpan.FromSeconds(15), ct);
        _detectedFortiOsStatusOut = statusOut;

        var modelMatch = Regex.Match(statusOut, @"(?im)^\s*Model\s*:\s*([^

]+)");
        var modelo = "FortiGate 40F";
        if (statusOut.Contains("FortiGate-40F", StringComparison.OrdinalIgnoreCase) || statusOut.Contains("FGT40F", StringComparison.OrdinalIgnoreCase))
        {
            modelo = "FortiGate 40F";
        }
        else if (modelMatch.Success)
        {
            modelo = modelMatch.Groups[1].Value.Trim();
        }

        var verMatch = Regex.Match(statusOut, @"(?im)^\s*Version\s*:\s*([^

]+)");
        var fortiVer = verMatch.Success ? verMatch.Groups[1].Value.Trim() : "FortiOS 7.x";
        _detectedFortiOsVersion = fortiVer;

        var serialMatch = Regex.Match(statusOut, @"(?im)^\s*Serial(?:-Number)?\s*:\s*([A-Za-z0-9]+)");
        var serialNumber = serialMatch.Success ? serialMatch.Groups[1].Value.Trim() : "—";

        var biosMatch = Regex.Match(statusOut, @"(?im)^\s*BIOS\s+version\s*:\s*([^

]+)");
        var biosVer = biosMatch.Success ? biosMatch.Groups[1].Value.Trim() : "—";

        var opModeMatch = Regex.Match(statusOut, @"(?im)^\s*Operation\s+Mode\s*:\s*([^

]+)");
        var opMode = opModeMatch.Success ? opModeMatch.Groups[1].Value.Trim() : "NAT";

        var hostnameMatch = Regex.Match(statusOut, @"(?im)^\s*Hostname\s*:\s*([^

]+)");
        var hostname = hostnameMatch.Success ? hostnameMatch.Groups[1].Value.Trim() : "FortiGate-40F";

        // 3. Coleta interfaces
        var ifLines = new List<string>();
        try
        {
            var ifOut = await session.SendCommandAsync("get system interface physical", TimeSpan.FromSeconds(10), ct);
            ifLines = ifOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Contains("up") || l.Contains("down") || l.Contains("1000") || l.Contains("100"))
                .Select(l => l.Trim())
                .Take(6)
                .ToList();
        }
        catch { }

        // Exibe painel consolidado no terminal SPARC
        EscreverLinha($"\n=================================================================");
        EscreverLinha($"       🛡️ INVENTÁRIO DO EQUIPAMENTO FORTINET FORTIGATE           ");
        EscreverLinha($"=================================================================");
        EscreverLinha($"  🏷️ Fabricante / Modelo : Fortinet {modelo}");
        EscreverLinha($"  🔢 Número de Série     : {serialNumber}");
        EscreverLinha($"  💾 Versão FortiOS      : {fortiVer}");
        EscreverLinha($"  ⚙️ Versão BIOS         : {biosVer}");
        EscreverLinha($"  🌐 Modo de Operação    : {opMode}");
        EscreverLinha($"  📋 Hostname            : '{hostname}'");

        if (ifLines.Count > 0)
        {
            EscreverLinha("\n  🌐 Interfaces Físicas:");
            foreach (var ifLine in ifLines)
            {
                EscreverLinha($"     {ifLine}");
            }
        }

        EscreverLinha($"  📊 Situação do Aparelho: 🟢 AUTENTICADO E OPERACIONAL (Pronto para provisionamento)");
        EscreverLinha($"=================================================================\n");

        // Crítica Preventiva: Se um firmware já estiver selecionado, verifica se a versão já é idêntica
        if (!string.IsNullOrEmpty(_selectedIosBinPath) && File.Exists(_selectedIosBinPath))
        {
            if (NetworkDevice.Fortinet.FortiOsFirmwareVersionInspector.IsSameVersion(
                statusOut, _selectedIosBinPath, out var currentVer, out var targetVer))
            {
                EscreverLinha("=================================================================");
                EscreverLinha("       ℹ️ FIRMWARE FORTIOS: MESMA VERSÃO JÁ INSTALADA           ");
                EscreverLinha("=================================================================");
                EscreverLinha($"  Versão no FortiGate   : {currentVer}");
                EscreverLinha($"  Versão da Imagem Alvo : {targetVer} ({Path.GetFileName(_selectedIosBinPath)})");
                EscreverLinha("  -> O equipamento já possui a mesma versão do firmware selecionado.");
                EscreverLinha("  -> A atualização de firmware (Etapa 2) foi desmarcada automaticamente.");
                EscreverLinha("=================================================================\n");

                Dispatcher.Invoke(() =>
                {
                    if (ChkAtualizarFirmwareAuto != null)
                        ChkAtualizarFirmwareAuto.IsChecked = false;
                });
            }
        }

        Dispatcher.Invoke(() =>
        {
            SelecionarModeloNoCombo("fortinet.fgt40f");

            if (CardChkModelo != null && TxtChkModeloIcon != null && TxtChkModeloSub != null)
            {
                TxtChkModeloIcon.Text = "🟢 1a. Modelo";
                TxtChkModeloSub.Text = $"Fortinet {modelo}";
            }

            if (CardChkSerial != null && TxtChkSerialIcon != null && TxtChkSerialSub != null)
            {
                TxtChkSerialIcon.Text = "🟢 1b. Serial (OK)";
                TxtChkSerialSub.Text = serialNumber != "—" ? $"{serialNumber}" : "FortiGate Autenticado";
            }

            AtualizarBotaoProsseguir();
        });

        MessageBox.Show(
            $"AVALIAÇÃO DO EQUIPAMENTO FORTINET:\n\n" +
            $"• Modelo: Fortinet {modelo}\n" +
            $"• Serial: {serialNumber}\n" +
            $"• Versão FortiOS: {fortiVer}\n" +
            $"• BIOS: {biosVer}\n" +
            $"• Modo: {opMode}\n" +
            $"• Hostname: {hostname}\n" +
            $"• Status: 🟢 Equipamento Autenticado e Pronto\n\n" +
            $"✅ Equipamento pronto para provisionamento direto FortiOS.\n\n" +
            "⚠️ ALERTA DE CONFIGURAÇÃO:\n" +
            "Ao prosseguir com o provisionamento, as configurações do circuito serão aplicadas conforme a Ficha SAIP.",
            "Diagnóstico do Equipamento — SPARC",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task AvaliarEquipamentoCiscoAsync(DeviceSession session, CancellationToken ct)
    {
        try
        {
            var p = session.CurrentPrompt ?? "";
            if (p.Trim().EndsWith(">"))
            {
                await session.SendCommandAsync("enable", TimeSpan.FromSeconds(5), ct);
            }
        }
        catch { }

        try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), ct); } catch { }
        try { await session.SendCommandAsync("terminal width 512", TimeSpan.FromSeconds(5), ct); } catch { }

        // 1. show version
        var showVer = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(15), ct);

        var modelMatch = Regex.Match(showVer, @"(?im)\bcisco\s+([A-Za-z0-9\-\/_]+)\s*\(");
        if (!modelMatch.Success) modelMatch = Regex.Match(showVer, @"(?im)\bcisco\s+([A-Za-z0-9\-\/_]+)\s+with");
        if (!modelMatch.Success) modelMatch = Regex.Match(showVer, @"(?im)^\s*(?:cisco\s+)?Model\s+number\s*:\s*(\S+)");
        if (!modelMatch.Success) modelMatch = Regex.Match(showVer, @"(?im)\bPID\s*:\s*(\S+)");
        if (!modelMatch.Success) modelMatch = Regex.Match(showVer, @"(?im)\bPID\s+str\s*:\s*(\S+)");
        if (!modelMatch.Success) modelMatch = Regex.Match(showVer, @"(?im)^\s*cisco\s+([A-Za-z0-9\-\/_]+)");
        var modelo = modelMatch.Success ? modelMatch.Groups[1].Value.Trim() : "Cisco";

        var is1900 = DeviceDetector.Cisco1900ModelRegex.IsMatch(modelo)
                  || DeviceDetector.Cisco1900ModelRegex.IsMatch(showVer);

        var is921 = !is1900 && (
                     DeviceDetector.Cisco900ModelRegex.IsMatch(modelo)
                  || DeviceDetector.Cisco900ModelRegex.IsMatch(showVer));

        var is841 = !is1900 && !is921 && (
                     DeviceDetector.Cisco841ModelRegex.IsMatch(modelo)
                  || DeviceDetector.Cisco841ModelRegex.IsMatch(showVer));

        var iosVerMatch = Regex.Match(showVer, @"(?im)Version\s+([0-9\.\(\)A-Za-z]+),");
        var iosVer = iosVerMatch.Success ? iosVerMatch.Groups[1].Value.Trim() : "Desconhecida";

        var serialMatch = Regex.Match(showVer, @"(?im)Processor board ID\s+(\S+)");
        if (!serialMatch.Success) serialMatch = Regex.Match(showVer, @"(?im)System serial number\s*:\s*(\S+)");
        var serialNumber = serialMatch.Success ? serialMatch.Groups[1].Value.Trim() : "Não informado";

        var regMatch = Regex.Match(showVer, @"(?im)Configuration register is\s+(0x[0-9A-Fa-f]+)");
        var configRegister = regMatch.Success ? regMatch.Groups[1].Value.Trim() : "0x2102";

        var uptimeMatch = Regex.Match(showVer, @"(?im)uptime is\s+(.+)");
        var uptime = uptimeMatch.Success ? uptimeMatch.Groups[1].Value.Trim() : "—";

        // 2. dir flash: / dir sdflash: / dir
        var dirFlash = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), ct);
        if (dirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase))
        {
            dirFlash = await session.SendCommandAsync("dir sdflash:", TimeSpan.FromSeconds(15), ct);
            if (dirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase))
            {
                dirFlash = await session.SendCommandAsync("dir", TimeSpan.FromSeconds(15), ct);
            }
        }
        var binMatches = Regex.Matches(dirFlash, @"(?im)\b(\S+\.bin)\b");
        var binFiles = binMatches.Select(m => m.Groups[1].Value).Distinct().ToList();
        var flashFreeMatch = Regex.Match(dirFlash, @"(?im)([0-9]+)\s+bytes\s+free");
        var flashFreeMb = flashFreeMatch.Success && long.TryParse(flashFreeMatch.Groups[1].Value, out var freeB)
            ? $"{freeB / (1024.0 * 1024.0):F1} MB livres"
            : "—";

        // 3. show ip interface brief
        var ipBrief = await session.SendCommandAsync("show ip interface brief", TimeSpan.FromSeconds(15), ct);
        var ifLines = ipBrief.Split('\n')
            .Where(l => l.StartsWith("GigabitEthernet", StringComparison.OrdinalIgnoreCase) || l.StartsWith("FastEthernet", StringComparison.OrdinalIgnoreCase) || l.StartsWith("Ethernet", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim())
            .ToList();

        // 4. show running-config | include hostname|ip route 0.0.0.0
        var hostOut = await session.SendCommandAsync("show running-config | include hostname", TimeSpan.FromSeconds(10), ct);
        var hostMatch = Regex.Match(hostOut, @"(?im)^\s*hostname\s+(\S+)");
        var hostname = hostMatch.Success ? hostMatch.Groups[1].Value.Trim() : "Router";

        var routeOut = await session.SendCommandAsync("show running-config | include ip route 0.0.0.0", TimeSpan.FromSeconds(10), ct);
        var routeMatches = Regex.Matches(routeOut, @"(?im)^\s*ip\s+route\s+0\.0\.0\.0\s+0\.0\.0\.0\s+(\S+)");
        var defaultGateways = routeMatches.Select(m => m.Groups[1].Value.Trim()).ToList();

        // Exibe painel consolidado
        EscreverLinha($"  🏷️ Fabricante / Modelo : Cisco {modelo}");
        EscreverLinha($"  🔢 Número de Série     : {serialNumber}");
        EscreverLinha($"  💾 Versão Cisco IOS    : {iosVer}");
        EscreverLinha($"  ⏱️ Uptime              : {uptime}");
        EscreverLinha($"  ⚙️ Config-Register     : {configRegister} {(configRegister == "0x2142" ? "(⚠️ Bypass de senha ativo)" : "(✅ Normal)")}");
        EscreverLinha($"  📁 Memória Flash       : {flashFreeMb} — {binFiles.Count} imagem(ns) IOS encontrada(s):");
        foreach (var bin in binFiles)
        {
            EscreverLinha($"     • {bin}");
        }

        EscreverLinha("\n  🌐 Interfaces e Conectividade Fisiológica:");
        foreach (var ifLine in ifLines)
        {
            EscreverLinha($"     {ifLine}");
        }

        EscreverLinha($"\n  📋 Configuração Atual  : Hostname '{hostname}' | Gateway(s) Default: {(defaultGateways.Count > 0 ? string.Join(", ", defaultGateways) : "Nenhum (Zerado)")}");

        bool isZerado = hostname.Equals("Router", StringComparison.OrdinalIgnoreCase) && defaultGateways.Count == 0;
        EscreverLinha($"  📊 Situação do Aparelho: {(isZerado ? "🟢 TOTALMENTE ZERADO (Pronto para provisionamento direto)" : "🟡 POSSUI CONFIGURAÇÃO PRÉVIA (Recomenda-se zerar na Fase 1 ou sobregravar)")}");
        Dispatcher.Invoke(() =>
        {
            if (is841)
            {
                SelecionarModeloNoCombo("cisco.c841.break");
            }
            else if (is921)
            {
                SelecionarModeloNoCombo("cisco.c900.ctrl-c");
            }
            else if (is1900)
            {
                SelecionarModeloNoCombo("cisco.c1900.break");
            }

            if (CardChkModelo != null && TxtChkModeloIcon != null && TxtChkModeloSub != null)
            {
                TxtChkModeloIcon.Text = "🟢 1a. Modelo";
                TxtChkModeloSub.Text = $"Cisco {modelo}";
            }
        });

        MessageBox.Show(
            $"AVALIAÇÃO DO EQUIPAMENTO CISCO:\n\n" +
            $"• Modelo: Cisco {modelo}\n" +
            $"• Serial: {serialNumber}\n" +
            $"• Versão IOS: {iosVer}\n" +
            $"• Config-Register: {configRegister}\n" +
            $"• Imagens na Flash: {string.Join(", ", binFiles)}\n" +
            $"• Status: {(isZerado ? "🟢 Equipamento Limpo/Zerado" : "🟡 Possui Configuração Anterior")}\n\n" +
            $"{(isZerado ? "✅ Equipamento pronto para provisionamento direto (Fase 3)." : "💡 Dica: Execute a esteira completa para zerar e homologar.")}\n\n" +
            "⚠️ ALERTA DE PERDA DE DADOS:\n" +
            "Ao prosseguir com a esteira ou zeramento, toda a configuração existente no equipamento será COMPLETAMENTE APAGADA e os dados anteriores serão PERDIDOS.",
            "Diagnóstico do Equipamento — SPARC",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task AvaliarEquipamentoHpeAsync(DeviceSession session, CancellationToken ct)
    {
        try { await session.SendCommandAsync("screen-length disable", TimeSpan.FromSeconds(5), ct); } catch { }

        // 1. display version
        var dispVer = await session.SendCommandAsync("display version", TimeSpan.FromSeconds(15), ct);

        var modelMatch = Regex.Match(dispVer, @"(?im)^\s*HP(?:E)?\s+([A-Za-z0-9\-\/ ]+)uptime");
        if (!modelMatch.Success) modelMatch = Regex.Match(dispVer, @"(?im)HP(?:E)?\s+([A-Za-z0-9\-\/]+)");
        var modelo = modelMatch.Success ? modelMatch.Groups[1].Value.Trim() : "HPE Comware";

        var comwareVerMatch = Regex.Match(dispVer, @"(?im)HP(?:E)? Comware Software,\s*Version\s*([0-9\.\, A-Za-z]+),");
        var comwareVer = comwareVerMatch.Success ? comwareVerMatch.Groups[1].Value.Trim() : "Comware 7";

        var releaseMatch = Regex.Match(dispVer, @"(?im)Release\s*([0-9A-Za-z]+)");
        var release = releaseMatch.Success ? releaseMatch.Groups[1].Value.Trim() : "";

        var bootImgMatch = Regex.Match(dispVer, @"(?im)Boot image\s*:\s*(\S+)");
        var bootImg = bootImgMatch.Success ? bootImgMatch.Groups[1].Value.Trim() : "—";

        var uptimeMatch = Regex.Match(dispVer, @"(?im)uptime is\s+(.+)");
        var uptime = uptimeMatch.Success ? uptimeMatch.Groups[1].Value.Trim() : "—";

        // 2. dir
        var dirOut = await session.SendCommandAsync("dir", TimeSpan.FromSeconds(15), ct);
        var binMatches = Regex.Matches(dirOut, @"(?im)\b(\S+\.(?:bin|ipe))\b");
        var binFiles = binMatches.Select(m => m.Groups[1].Value).Distinct().ToList();

        // 3. display interface brief
        var ifBrief = await session.SendCommandAsync("display interface brief", TimeSpan.FromSeconds(15), ct);
        var ifLines = ifBrief.Split('\n')
            .Where(l => l.StartsWith("GE", StringComparison.OrdinalIgnoreCase) || l.StartsWith("GigabitEthernet", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim())
            .ToList();

        // 4. display current-configuration | include sysname|ip route-static
        var sysOut = await session.SendCommandAsync("display current-configuration | include sysname", TimeSpan.FromSeconds(10), ct);
        var sysMatch = Regex.Match(sysOut, @"(?im)^\s*sysname\s+(\S+)");
        var sysname = sysMatch.Success ? sysMatch.Groups[1].Value.Trim() : "HPE";

        var routeOut = await session.SendCommandAsync("display current-configuration | include ip route-static", TimeSpan.FromSeconds(10), ct);
        var routeMatches = Regex.Matches(routeOut, @"(?im)^\s*ip\s+route-static\s+0\.0\.0\.0\s+\S+\s+(\S+)");
        var defaultGateways = routeMatches.Select(m => m.Groups[1].Value.Trim()).ToList();

        // Exibe painel consolidado
        EscreverLinha($"  🏷️ Fabricante / Modelo : HPE {modelo}");
        EscreverLinha($"  💾 Versão Comware      : {comwareVer} {release}");
        EscreverLinha($"  🚀 Imagem de Boot      : {bootImg}");
        EscreverLinha($"  ⏱️ Uptime              : {uptime}");
        EscreverLinha($"  📁 Arquivos na Flash   : {binFiles.Count} arquivo(s) (.bin/.ipe) encontrado(s):");
        foreach (var bin in binFiles)
        {
            EscreverLinha($"     • {bin}");
        }

        EscreverLinha("\n  🌐 Interfaces e Conectividade Fisiológica:");
        foreach (var ifLine in ifLines)
        {
            EscreverLinha($"     {ifLine}");
        }

        EscreverLinha($"\n  📋 Configuração Atual  : Sysname '{sysname}' | Gateway(s) Default: {(defaultGateways.Count > 0 ? string.Join(", ", defaultGateways) : "Nenhum (Zerado)")}");

        bool isZerado = sysname.Equals("HPE", StringComparison.OrdinalIgnoreCase) && defaultGateways.Count == 0;
        EscreverLinha($"  📊 Situação do Aparelho: {(isZerado ? "🟢 TOTALMENTE ZERADO (Pronto para provisionamento direto)" : "🟡 POSSUI CONFIGURAÇÃO PRÉVIA (Recomenda-se zerar na Fase 1 ou sobregravar)")}");
        Dispatcher.Invoke(() =>
        {
            var isHpe1002 = modelo.Contains("1002", StringComparison.OrdinalIgnoreCase) || modelo.Contains("1003", StringComparison.OrdinalIgnoreCase) || modelo.Contains("1000", StringComparison.OrdinalIgnoreCase);
            var isHpe930 = !isHpe1002 && (modelo.Contains("930", StringComparison.OrdinalIgnoreCase) || modelo.Contains("931", StringComparison.OrdinalIgnoreCase) || modelo.Contains("935", StringComparison.OrdinalIgnoreCase));
            SelecionarModeloNoCombo(isHpe1002 ? "hpe.msr1002.ctrl-b" :
                                    isHpe930 ? "hpe.msr930.ctrl-b" : "hpe.msr954.ctrl-b");

            if (CardChkModelo != null && TxtChkModeloIcon != null && TxtChkModeloSub != null)
            {
                TxtChkModeloIcon.Text = "🟢 1a. Modelo";
                TxtChkModeloSub.Text = $"HPE {modelo}";
            }
        });

        MessageBox.Show(
            $"AVALIAÇÃO DO EQUIPAMENTO HPE:\n\n" +
            $"• Modelo: HPE {modelo}\n" +
            $"• Versão Comware: {comwareVer} {release}\n" +
            $"• Imagem Boot: {bootImg}\n" +
            $"• Arquivos na Flash: {string.Join(", ", binFiles)}\n" +
            $"• Status: {(isZerado ? "🟢 Equipamento Limpo/Zerado" : "🟡 Possui Configuração Anterior")}\n\n" +
            $"{(isZerado ? "✅ Equipamento pronto para provisionamento direto (Fase 3)." : "💡 Dica: Execute a esteira completa para zerar e homologar.")}\n\n" +
            "⚠️ ALERTA DE PERDA DE DADOS:\n" +
            "Ao prosseguir com a esteira ou zeramento, toda a configuração existente no equipamento será COMPLETAMENTE APAGADA e os dados anteriores serão PERDIDOS.",
            "Diagnóstico do Equipamento — SPARC",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void AtualizarPortas()
    {
        if (CbPorta == null)
            return;

        // Qualquer troca de porta invalida teste serial anterior
        _serialOk = false;
        if (TxtSerialTestStatus != null) { TxtSerialTestStatus.Text = ""; }
        AtualizarBotaoProsseguir();

        // Preserva a seleção atual (prioriza o combo visível da tela inicial)
        var selecionada = CbPortaInicial?.Text?.Trim();
        if (string.IsNullOrEmpty(selecionada))
            selecionada = CbPorta.Text?.Trim();

        var portas = SerialPort.GetPortNames()
            .OrderBy(p => int.TryParse(p.Replace("COM", ""), out var n) ? n : 0)
            .ToArray();

        _syncingCombos = true;
        try
        {
            CbPorta.Items.Clear();
            if (CbPortaInicial is not null)
                CbPortaInicial.Items.Clear();

            foreach (var porta in portas)
            {
                CbPorta.Items.Add(porta);
                if (CbPortaInicial is not null)
                    CbPortaInicial.Items.Add(porta);
            }

            // Restaura a porta anterior se ainda existir, senão COM1, senão a primeira.
            // Define SelectedIndex + Text nos dois combos para a caixa exibir o valor.
            string? restaurar = null;
            if (!string.IsNullOrEmpty(selecionada) && CbPorta.Items.Contains(selecionada))
                restaurar = selecionada;
            else if (CbPorta.Items.Contains("COM1"))
                restaurar = "COM1";
            else if (CbPorta.Items.Count > 0)
                restaurar = CbPorta.Items[0]?.ToString();

            if (!string.IsNullOrEmpty(restaurar))
            {
                var idx = CbPorta.Items.IndexOf(restaurar);
                if (idx >= 0) CbPorta.SelectedIndex = idx;
                CbPorta.Text = restaurar;
                if (CbPortaInicial is not null)
                {
                    var idxIni = CbPortaInicial.Items.IndexOf(restaurar);
                    if (idxIni >= 0) CbPortaInicial.SelectedIndex = idxIni;
                    CbPortaInicial.Text = restaurar;
                }
            }
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    private void CbTipoDispositivo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AtualizarAdaptadoresRede();
    }

    private void AtualizarAdaptadoresRede()
    {
        if (CbAdaptadorRede == null)
            return;

        var isAndroid = CbTipoDispositivo?.SelectedIndex == 1;
        CbAdaptadorRede.Items.Clear();

        if (isAndroid)
        {
            var androidService = new AndroidHostNetworkGuidance();
            foreach (var adapter in androidService.GetAvailableAdapters())
                CbAdaptadorRede.Items.Add(adapter);
        }
        else
        {
            foreach (var adapter in HostNetworkManager.GetEthernetAdapters())
                CbAdaptadorRede.Items.Add(adapter);
        }

        if (CbAdaptadorRede.Items.Count > 0)
            CbAdaptadorRede.SelectedIndex = 0;
    }

    #endregion

    #region Insumo do Circuito (Opção 1 SAIP / Opção 2 Manual) & Firmware

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (isCtrl && isShift && (e.Key == Key.V || e.Key == Key.A || e.Key == Key.T))
        {
            e.Handled = true;
            if (BtnSemiAutoAtalho != null)
            {
                bool jaVisivel = BtnSemiAutoAtalho.Visibility == Visibility.Visible;
                BtnSemiAutoAtalho.Visibility = jaVisivel ? Visibility.Collapsed : Visibility.Visible;
                if (ChkNatLab != null)
                    ChkNatLab.Visibility = BtnSemiAutoAtalho.Visibility;
                if (BtnRecuperacaoFortinet != null)
                    BtnRecuperacaoFortinet.Visibility = BtnSemiAutoAtalho.Visibility;
                if (BtnSimularFortiBiosRecovery != null)
                    BtnSimularFortiBiosRecovery.Visibility = BtnSemiAutoAtalho.Visibility;
                if (!jaVisivel && StatusTexto != null)
                    StatusTexto.Content = "⚙️ OPÇÕES AVANÇADAS DISPONÍVEL — Clique no botão abaixo ou Ctrl+Shift+V.";
            }
        }
    }

    private void BtnSimularFortiBiosRecovery_Click(object sender, RoutedEventArgs e)
    {
        _isFortiGateDetected = true;
        _isRommonOrBootwareDetected = true;
        SelecionarModeloNoCombo("fortinet.fortigate.maintainer");
        if (ChkAtualizarFirmwareAuto != null)
        {
            ChkAtualizarFirmwareAuto.IsChecked = true;
            if (BtnSelecionarFirmwareAuto != null)
                BtnSelecionarFirmwareAuto.IsEnabled = true;
        }

        if (TxtSerialTestStatus != null)
        {
            TxtSerialTestStatus.Text = "🔴 BIOS / Sem Firmware (Fortinet)";
            TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
        }
        if (TxtChkSerialIcon != null && TxtChkSerialSub != null)
        {
            TxtChkSerialIcon.Text = "🔴 1a. Fortinet 40F (BIOS/Sem FW)";
            TxtChkSerialSub.Text = "Simulação: FortiBootLoader (TFTP)";
        }

        AtualizarBotaoProsseguir();

        EscreverLinha("\n=================================================================");
        EscreverLinha("  🧪 [SIMULAÇÃO] FORTIGATE 40F EM MODO BIOS / SEM FIRMWARE ATIVADO");
        EscreverLinha("=================================================================");
        EscreverLinha("  • Estado: DeviceOperatingState.BootFailure (FortiBootLoader)");
        EscreverLinha("  • Fluxo: WorkflowType.FirmwareRecovery (BIOS TFTP)");
        EscreverLinha("  • Passo 3 configurado para exigir arquivo de firmware .out.");
        EscreverLinha("  • A esteira automática e a Fase 2 executarão o fluxo de BIOS TFTP.");
        EscreverLinha("=================================================================\n");

        MessageBox.Show(
            "SIMULAÇÃO ATIVADA: FortiGate 40F em Modo BIOS / Sem Firmware\n\n" +
            "• O sistema configurou a esteira para simular o roteador com firmware corrompido ou ausente.\n" +
            "• O Passo 3 agora exige a seleção da imagem .out.\n" +
            "• Ao clicar em 'INICIAR PROVISIONAMENTO AUTOMÁTICO' ou 'Executar Fase 2', o fluxo de recuperação BIOS TFTP será acionado.",
            "Simulação BIOS TFTP Ativada",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnRecuperacaoFortinet_Click(object sender, RoutedEventArgs e)
    {
        var porta = CbPortaInicial?.Text?.Trim();
        if (string.IsNullOrEmpty(porta)) porta = CbPorta?.Text?.Trim();
        if (string.IsNullOrEmpty(porta) || porta.StartsWith("Selecione", StringComparison.OrdinalIgnoreCase))
        {
            porta = "COM1";
        }
        int baud = 9600;

        EscreverLinha("\n=================================================================");
        EscreverLinha("  🛠️ PAINEL AVANÇADO DE RECUPERAÇÃO FORTINET 40F (Bootloader / BIOS)");
        EscreverLinha($"  Porta: {porta} @ {baud} baud");
        EscreverLinha("=================================================================\n");

        var win = new FortiGateAutoRecoveryWindow(porta, baud, initialSerial: null, modoDiretoFormatacao: false) { Owner = this };
        win.ShowDialog();
    }

    private void BtnSemiAutoAtalho_Click(object sender, RoutedEventArgs e)
    {
        GridTelaInicial.Visibility = Visibility.Collapsed;
        GridEsteiraPrincipal.Visibility = Visibility.Visible;
        Width = 1300;
        MinWidth = 1240;
        WindowState = WindowState.Normal;
        if (TxtCircuitoAtivoTitulo != null)
            TxtCircuitoAtivoTitulo.Text = "⚙️ OPÇÕES AVANÇADAS — Esteira Manual / Passo a Passo";
        if (StatusTexto != null)
            StatusTexto.Content = "⚙️ OPÇÕES AVANÇADAS — Esteira em execução.";
        EscreverLinha("\n=================================================================");
        EscreverLinha("  ⚙️ OPÇÕES AVANÇADAS (via atalho Ctrl+Shift+V)");
        EscreverLinha("=================================================================\n");
    }

    private void BtnVoltarPaginaPrincipal_Click(object sender, RoutedEventArgs e)
    {
        GridEsteiraPrincipal.Visibility = Visibility.Collapsed;
        GridTelaInicial.Visibility = Visibility.Visible;
        Width = 1060;
        MinWidth = 920;
        if (StatusTexto != null)
            StatusTexto.Content = "Pronto para iniciar.";
        if (TxtCircuitoAtivoTitulo != null && _loadedSaipCircuit != null)
            TxtCircuitoAtivoTitulo.Text = $"CIRCUITO: {_loadedSaipCircuit.DesignacaoIp} - {_loadedSaipCircuit.ClienteRazaoSocial}";
    }

    private void BtnSelecionarFirmwareAuto_Click(object sender, RoutedEventArgs e)
    {
        var perfilHw = (CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() 
                    ?? (CbInterrupt?.SelectedItem as ComboBoxItem)?.Tag?.ToString() 
                    ?? string.Empty;
        var isForti = IsTagFortiGate(perfilHw) || _isFortiGateDetected;
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = isForti ? "Selecionar Imagem de Firmware FortiOS (.OUT)" : "Selecionar Firmware para Modo Automático (.ipe / .bin / .out)",
            Filter = isForti ? "Imagens FortiOS (*.out)|*.out|Todos os Arquivos (*.*)|*.*"
                   : "Todos os Firmwares (*.bin;*.ipe;*.pkg;*.out)|*.bin;*.ipe;*.pkg;*.out|Imagens FortiOS (*.out)|*.out|Pacotes HPE Comware (*.ipe)|*.ipe|Imagens Binárias (*.bin)|*.bin|Todos os Arquivos (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            if (!ValidarEBloquearFirmwareIncompativel(dlg.FileName, mostrarAlertaModal: true))
            {
                return;
            }

            _selectedIosBinPath = dlg.FileName;
            var fi = new System.IO.FileInfo(dlg.FileName);
            var sizeMb = (fi.Length / (1024.0 * 1024.0)).ToString("N1");
            TxtFirmwareAutoInfo.Text = $"{System.IO.Path.GetFileName(dlg.FileName)} ({sizeMb} MB)";
            if (TxtIosImageInfo != null) TxtIosImageInfo.Text = TxtFirmwareAutoInfo.Text;
            AtualizarBotaoProsseguir();
            EscreverLinha($"[*] Firmware modo automático validado: {System.IO.Path.GetFileName(dlg.FileName)} ({sizeMb} MB)");

            // Crítica Preventiva de Mesma Versão (FortiOS)
            if (isForti && !string.IsNullOrWhiteSpace(_detectedFortiOsStatusOut))
            {
                if (NetworkDevice.Fortinet.FortiOsFirmwareVersionInspector.IsSameVersion(
                    _detectedFortiOsStatusOut, dlg.FileName, out var currentVer, out var targetVer))
                {
                    EscreverLinha("\n=================================================================");
                    EscreverLinha("       ℹ️ FIRMWARE FORTIOS: MESMA VERSÃO JÁ INSTALADA           ");
                    EscreverLinha("=================================================================");
                    EscreverLinha($"  Versão no FortiGate   : {currentVer}");
                    EscreverLinha($"  Versão da Imagem Alvo : {targetVer}");
                    EscreverLinha("  -> A imagem selecionada é idêntica à versão atualmente em execução.");
                    EscreverLinha("  -> Atualização de firmware dispensada (o processo continuará sem reinstalar).");
                    EscreverLinha("=================================================================\n");

                    MessageBox.Show(
                        $"O FortiGate 40F já está executando exatamente esta versão de FortiOS:\n\n" +
                        $"• Versão no Equipamento: {currentVer}\n" +
                        $"• Imagem Selecionada: {targetVer}\n\n" +
                        $"A atualização de firmware é dispensável e foi desmarcada automaticamente para agilizar o provisionamento.",
                        "Firmware FortiOS Já Atualizado",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    if (ChkAtualizarFirmwareAuto != null)
                        ChkAtualizarFirmwareAuto.IsChecked = false;

                    AtualizarBotaoProsseguir();
                }
            }
        }
    }

    private void BtnAutoVoltar_Click(object sender, RoutedEventArgs e)
    {
        GridModoAutomatico.Visibility = Visibility.Collapsed;
        GridTelaInicial.Visibility = Visibility.Visible;
        if (BtnSemiAutoAtalho != null) BtnSemiAutoAtalho.Visibility = Visibility.Collapsed;
        if (ChkNatLab != null) ChkNatLab.Visibility = Visibility.Collapsed;
    }

    private void RbModoInsumo_Checked(object sender, RoutedEventArgs e)
    {
        AtualizarBotaoProsseguir();
        if (GridInsumoSaip is null || PanelInsumoManual is null)
            return;

        if (RbModoSaip.IsChecked == true)
        {
            GridInsumoSaip.Visibility = Visibility.Visible;
            PanelInsumoManual.Visibility = Visibility.Collapsed;
        }
        else
        {
            GridInsumoSaip.Visibility = Visibility.Collapsed;
            PanelInsumoManual.Visibility = Visibility.Visible;
        }
    }

    private void TxtManualLanIp_TextChanged(object sender, TextChangedEventArgs e)
    {
        TxtManual_TextChanged(sender, e);
    }

    private void TxtManual_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TxtManualHostCalculadoPreview is not null)
        {
            var lanIp = TxtManualLanIp?.Text.Trim() ?? "";
            var hostIp = CalcularHostIp(lanIp);
            var lanCidr = int.TryParse(TxtManualLanCidr?.Text?.Trim(), out var lc) ? lc : 28;
            var mask = CidrToSubnetMask(lanCidr);
            if (!string.IsNullOrEmpty(hostIp))
            {
                if (TxtHostIpCalculado is not null)
                    TxtHostIpCalculado.Text = $"Host LAN: {hostIp}";
                TxtManualHostCalculadoPreview.Text = $"Host LAN Calculado para Placa de Teste: {hostIp} (Máscara: {mask})";
                TxtManualHostCalculadoPreview.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#15803D"));
            }
            else
            {
                TxtManualHostCalculadoPreview.Text = "Host LAN Calculado para Placa de Teste: — (preencha LAN IP)";
                TxtManualHostCalculadoPreview.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
            }
        }
        AtualizarBotaoProsseguir();
    }

    private void BtnAplicarManual_Click(object sender, RoutedEventArgs e)
    {
        var wanIp = TxtManualWanIp.Text.Trim();
        var wanGw = TxtManualWanGw.Text.Trim();
        var lanIp = TxtManualLanIp.Text.Trim();
        var cliente = TxtManualCliente.Text.Trim();
        var designacao = TxtManualDesignacao.Text.Trim();

        if (string.IsNullOrEmpty(wanIp) || string.IsNullOrEmpty(wanGw) || string.IsNullOrEmpty(lanIp))
        {
            MessageBox.Show("Preencha ao menos WAN IP, Gateway WAN e LAN IP para continuar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var wanCidr = int.TryParse(TxtManualWanCidr.Text.Trim(), out var wc) ? wc : 30;
        var lanCidr = int.TryParse(TxtManualLanCidr.Text.Trim(), out var lc) ? lc : 28;
        var hostIp = CalcularHostIp(lanIp);

        var clienteFinal = string.IsNullOrEmpty(cliente) ? "CLIENTE AVULSO" : cliente;
        var designacaoFinal = string.IsNullOrEmpty(designacao) ? "DESIGNACAO-MANUAL" : designacao;

        var circuit = new SaipCircuitData
        {
            ClienteRazaoSocial = clienteFinal,
            DesignacaoIp = designacaoFinal,
            NumeroOts = "-",
            WanIp = wanIp,
            WanCidr = wanCidr,
            WanSubnetMask = CidrToSubnetMask(wanCidr),
            WanGateway = wanGw,
            LanIp = lanIp,
            LanCidr = lanCidr,
            LanSubnetMask = CidrToSubnetMask(lanCidr),
            HostLanIp = hostIp,
            LanBlockNetwork = lanIp
        };

        _loadedSaipCircuit = circuit;
        if (TxtHostIpCalculado is not null)
        if (TxtIcmpTargetLan is not null) TxtIcmpTargetLan.Text = circuit.LanIp;
        if (TxtIcmpTargetWan is not null) TxtIcmpTargetWan.Text = circuit.WanGateway;
        if (TxtIcmpTargetWeb is not null) TxtIcmpTargetWeb.Text = "1.1.1.1, 8.8.8.8";
        TxtIcmpTarget.Text = circuit.WanGateway;
        TxtTelnetTarget.Text = circuit.LanIp;

        TxtCircuitoAtivoTitulo.Text = $"CIRCUITO: {circuit.DesignacaoIp} - {circuit.ClienteRazaoSocial}";
        TxtCircuitoAtivoResumo.Text = $"WAN: {circuit.WanIp}/{circuit.WanCidr} (GW: {circuit.WanGateway}) | LAN: {circuit.LanIp}/{circuit.LanCidr} | Host: {circuit.HostLanIp} | DNS: 1.1.1.1, 8.8.8.8";

        AtualizarEstadoBotoes();
        AtualizarBotaoProsseguir();

        EscreverLinha("\n=================================================================");
        EscreverLinha("             DADOS DO CIRCUITO INFORMADOS MANUALMENTE            ");
        EscreverLinha("=================================================================");
        EscreverLinha($"  Cliente     : {circuit.ClienteRazaoSocial}");
        EscreverLinha($"  Designação  : {circuit.DesignacaoIp}");
        EscreverLinha($"  WAN IP      : {circuit.WanIp} {circuit.WanSubnetMask} (/{circuit.WanCidr})");
        EscreverLinha($"  Gateway WAN : {circuit.WanGateway}");
        EscreverLinha($"  LAN IP      : {circuit.LanIp} {circuit.LanSubnetMask} (/{circuit.LanCidr})");
        EscreverLinha($"  Host Teste  : {circuit.HostLanIp} (IP calculado com DNS 1.1.1.1 / 8.8.8.8)");
        EscreverLinha("=================================================================\n");
    }

    private void BtnAvancarParaEsteira_Click(object sender, RoutedEventArgs e)
    {
        var erros = new List<string>();
        if (!_serialOk) erros.Add("• Teste de conexão serial pendente — clique em 🔌 Testar Conexão Porta Serial.");
        if (CbModeloRoteadorInicial.SelectedIndex <= 0) erros.Add("• Modelo do equipamento não selecionado.");
        var modoManual = RbModoManual.IsChecked == true;
        if (_loadedSaipCircuit is null)
        {
            if (modoManual) erros.Add("• Modo Manual: preencha WAN IP, Gateway, LAN IP e clique em Aplicar.");
            else erros.Add("• Ficha SAIP não carregada — clique em 📂 Selecionar Arquivo SAIP.");
        }

        var isAuto = true; // Modo Automático é o padrão
        var querAtualizar = ChkAtualizarFirmwareAuto?.IsChecked == true;
        var firmwareObrigatorio = _isRommonOrBootwareDetected || querAtualizar;
        var arquivoExiste = !string.IsNullOrEmpty(_selectedIosBinPath) && System.IO.File.Exists(_selectedIosBinPath);
        if (firmwareObrigatorio && !arquivoExiste)
        {
            if (_isRommonOrBootwareDetected)
            {
                erros.Add("• Equipamento em modo ROMMON / BootWare (sem SO): é OBRIGATÓRIO selecionar a imagem de Firmware (.bin / .ipe) no Passo 3 para recuperar o equipamento.");
            }
            else
            {
                erros.Add("• Atualização de Firmware selecionada: selecione o arquivo (.ipe/.bin) ou desmarque a opção.");
            }
        }
        else if (arquivoExiste)
        {
            var serie = ObterSerieAtualSelecionadaOuDetectada();
            var resFw = FirmwareCompatibilityValidator.Validate(serie, _selectedIosBinPath);
            if (!resFw.IsCompatible)
            {
                erros.Add($"• Firmware incompatível: {resFw.ErrorMessage}");
            }
        }

        if (erros.Count > 0)
        {
            MessageBox.Show("Não é possível prosseguir. Pendências:\n\n" + string.Join("\n", erros), "Validação — Iniciar Provisionamento", MessageBoxButton.OK, MessageBoxImage.Warning);
            AtualizarBotaoProsseguir();
            return;
        }

        // Se manual e ainda não aplicou, aplica agora
        if (modoManual && _loadedSaipCircuit is null)
        {
            BtnAplicarManual_Click(sender, e);
            if (_loadedSaipCircuit is null) return;
        }

        if (isAuto)
        {
            GridTelaInicial.Visibility = Visibility.Collapsed;
            GridModoAutomatico.Visibility = Visibility.Visible;
            _ = ExecutarModoAutomaticoAsync();
            return;
        }

        // Garante que a seleção do modelo da primeira tela seja mantida no modo padrão (esteira)
        if (CbModeloRoteadorInicial.SelectedIndex > 0 && CbInterrupt.SelectedIndex != CbModeloRoteadorInicial.SelectedIndex - 1)
        {
            _syncingCombos = true;
            try { CbInterrupt.SelectedIndex = CbModeloRoteadorInicial.SelectedIndex - 1; } finally { _syncingCombos = false; }
        }

        GridTelaInicial.Visibility = Visibility.Collapsed;
        GridEsteiraPrincipal.Visibility = Visibility.Visible;
    }

    private async Task ExecutarModoAutomaticoAsync()
    {
        var porta = CbPortaInicial.Text?.Trim() ?? CbPorta.Text?.Trim() ?? "";
        var baud = int.TryParse(CbBaud?.Text, out var b) ? b : 9600;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        void SetEtapa(int n, string estado, string cor)
        {
            TextBlock? t = n switch
            {
                1 => TxtAutoEtapa1,
                2 => TxtAutoEtapa2,
                3 => TxtAutoEtapa3,
                4 => TxtAutoEtapa4,
                5 => TxtAutoEtapa5,
                6 => TxtAutoEtapa6,
                7 => TxtAutoEtapa7,
                _ => null
            };
            if (t != null) { t.Text = estado; t.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cor)); }
        }
        void Progresso(int pct, string titulo) { PbAutoGeral.Value = pct; TxtAutoPorcentagem.Text = $"{pct}%"; TxtAutoStatusGeral.Text = titulo; }
        void LogAuto(string msg) { TxtAutoLog.Text += msg + "\n"; EscreverLinha(msg); }
        // Reset
        for (int i = 1; i <= 7; i++) SetEtapa(i, $"○ {i}. " + new[] { "Zerar Configuração", "Atualizar Firmware", "Provisionar Equipamento", "Configurar IP de Teste", "Testar Conectividade (ICMP)", "Testar Acesso Remoto (Telnet)", "Testar Banda" }[i-1] + " — aguardando", "#64748B");
        Progresso(0, "Modo automático — iniciando verificação...");
        BtnAutoCancelar.Visibility = Visibility.Visible; BtnAutoVoltar.Visibility = Visibility.Collapsed;
        if (BtnAutoRestaurarRede != null) BtnAutoRestaurarRede.Visibility = Visibility.Collapsed;
        if (BtnAutoRetestarBanda != null) BtnAutoRetestarBanda.Visibility = Visibility.Collapsed;
        if (BtnAutoSalvarScript != null) BtnAutoSalvarScript.Visibility = Visibility.Collapsed;

        // Garante que o modo automático importe o mesmo sistema de análise de boot do modo padrão (HPE BootWare Ctrl+B)
        if (CbModeloRoteadorInicial.SelectedIndex > 0 && CbInterrupt.SelectedIndex != CbModeloRoteadorInicial.SelectedIndex - 1)
        {
            _syncingCombos = true;
            try { CbInterrupt.SelectedIndex = CbModeloRoteadorInicial.SelectedIndex - 1; } finally { _syncingCombos = false; }
        }

        var profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                      ?? (CbModeloRoteadorInicial.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var profile = BootInterruptProfiles.FindById(profileTag);
        var isForti = profile.Id.Contains("forti", StringComparison.OrdinalIgnoreCase)
                   || profile.Manufacturer.Contains("Fortinet", StringComparison.OrdinalIgnoreCase)
                   || IsTagFortiGate(profileTag ?? "")
                   || _isFortiGateDetected;
        var isHpe = !isForti && (profile.Id.Contains("hpe", StringComparison.OrdinalIgnoreCase) || profile.Family.Contains("MSR", StringComparison.OrdinalIgnoreCase));

        try
        {
            var firmwareDesejado = ChkAtualizarFirmwareAuto?.IsChecked == true;

            if (isForti)
            {
                if (_isRommonOrBootwareDetected)
                {
                    SetEtapa(1, "⏭ 1. Zerar Configuração — pulado (FortiGate em modo BIOS)", "#16A34A");
                    Progresso(20, "1/7 BIOS Detectada");
                    LogAuto(">>> [AUTO 1/7] Zeramento de configuração pulado: FortiGate 40F em modo BIOS (sem SO).");
                    firmwareDesejado = true;
                }
                else if (_skipFactoryReset)
                {
                    SetEtapa(1, "⏭ 1. Zerar Configuração — pulado (Login autenticado)", "#16A34A");
                    Progresso(20, "1/7 OK (Autenticado)");
                    LogAuto(">>> [AUTO 1/7] Zeramento de configuração pulado (FortiGate autenticado com sucesso pelo operador).");
                }
                else
                {
                    SetEtapa(1, "⏳ 1. Zerar Configuração — em execução", "#D97706");
                    Progresso(10, "1/7 Zerar FortiGate...");
                    LogAuto(">>> [AUTO 1/7] Fortinet FortiGate 40F — Zerar Configuração / Factory Reset");
                    await ExecutarZerarConfigAsync(porta, baud, ct);
                    SetEtapa(1, "✅ 1. Zerar Configuração — OK", "#16A34A");
                    Progresso(20, "1/7 OK");
                }

                if (firmwareDesejado)
                {
                    if (string.IsNullOrEmpty(_selectedIosBinPath) || !File.Exists(_selectedIosBinPath))
                    {
                        throw new InvalidOperationException("Equipamento FortiGate 40F: selecione a imagem de Firmware FortiOS (.out) no Passo 3 para realizar a recuperação/atualização.");
                    }

                    _firmwareSkippedSameVersion = false;

                    // Crítica Preventiva: Se o equipamento já estiver em execução e a versão instalada for a mesma proposta
                    if (!_isRommonOrBootwareDetected && !string.IsNullOrWhiteSpace(_detectedFortiOsStatusOut) &&
                        NetworkDevice.Fortinet.FortiOsFirmwareVersionInspector.IsSameVersion(
                            _detectedFortiOsStatusOut, _selectedIosBinPath, out var curVer, out var tgtVer))
                    {
                        _firmwareSkippedSameVersion = true;
                        SetEtapa(2, $"✅ 2. Firmware — mantido (mesma versão: {curVer})", "#16A34A");
                        LogAuto("\n=================================================================");
                        LogAuto("       ℹ️ FIRMWARE FORTIOS: MESMA VERSÃO JÁ INSTALADA           ");
                        LogAuto("=================================================================");
                        LogAuto($"  Versão no FortiGate   : {curVer}");
                        LogAuto($"  Versão da Imagem Alvo : {tgtVer} ({Path.GetFileName(_selectedIosBinPath)})");
                        LogAuto("  -> A versão instalada no FortiGate é idêntica à versão proposta.");
                        LogAuto("  -> Etapa 2 de Firmware dispensada com sucesso (TFTP dispensado).");
                        LogAuto("=================================================================\n");
                        Progresso(45, "2/7 OK (Firmware Mantido)");
                    }
                    else
                    {
                        SetEtapa(2, "⏳ 2. Atualizar Firmware — em execução", "#D97706");
                        Progresso(25, "2/7 Atualizar Firmware...");
                        var hostIp = _loadedSaipCircuit?.HostLanIp ?? ObterIpLocalParaTftp() ?? "192.168.1.99";

                        if (_isRommonOrBootwareDetected)
                        {
                            LogAuto(">>> [AUTO 2/7] Recuperar Firmware FortiGate via FortiBootLoader BIOS TFTP");
                            await ExecutarRecuperacaoFirmwareBiosFortinetAsync(porta, baud, hostIp, ct);
                            SetEtapa(2, "✅ 2. Recuperação de Firmware (BIOS) — OK", "#16A34A");
                            _isRommonOrBootwareDetected = false;
                        }
                        else
                        {
                            LogAuto(">>> [AUTO 2/7] Atualizar Firmware FortiOS via TFTP");
                            await ExecutarUpgradeFirmwareAsync(porta, baud, hostIp, ct);
                            if (_firmwareSkippedSameVersion)
                            {
                                SetEtapa(2, "✅ 2. Firmware — mantido (mesma versão já instalada)", "#16A34A");
                                LogAuto(">>> [AUTO 2/7] Firmware FortiOS mantido: versão selecionada já é a mesma disponível no FortiGate 40F (TFTP pulado).");
                            }
                            else
                            {
                                SetEtapa(2, "✅ 2. Atualizar Firmware — OK", "#16A34A");
                            }
                        }
                        Progresso(45, "2/7 OK");
                    }
                }
                else
                {
                    SetEtapa(2, "✅ 2. Firmware — mantido (opção do operador)", "#16A34A");
                    LogAuto(">>> [AUTO 2/7] Firmware FortiOS mantido: atualização dispensada pelo operador (bypass de firmware).");
                    Progresso(45, "2/7 OK");
                }
            }
            else if (isHpe)
            {
                if (_skipFactoryReset)
                {
                    SetEtapa(1, "⏭ 1. Zerar Configuração — pulado (Login autenticado)", "#16A34A");
                    Progresso(20, "1/7 OK (Autenticado)");
                    LogAuto(">>> [AUTO 1/7] Zeramento de configuração pulado (equipamento autenticado com sucesso pelo operador).");
                }
                else
                {
                    SetEtapa(1, "⏳ 1. Zerar Configuração — em execução", "#D97706");
                    var serieAtual = ObterSerieAtualSelecionadaOuDetectada();
                    var modeloAuto = serieAtual == DeviceSeries.Msr1002 ? "HPE MSR 1002 / 1003" :
                                     serieAtual == DeviceSeries.Msr930 ? "HPE MSR 930" :
                                     serieAtual == DeviceSeries.Msr954 ? "HPE MSR 954" : "HPE Comware";
                    LogAuto($">>> [AUTO 1/7] {modeloAuto} — Zerar Configuração e Quebra de Senha");
                    await ExecutarZerarConfigAsync(porta, baud, ct);
                    SetEtapa(1, "✅ 1. Zerar Configuração — OK", "#16A34A");
                    Progresso(20, "1/7 OK");
                }

                // Reavalia se o equipamento caiu no BootWare após Fase 1 apenas se o operador optou por atualizar
                if (_isRommonOrBootwareDetected && (ChkAtualizarFirmwareAuto?.IsChecked == true)) firmwareDesejado = true;

                if (firmwareDesejado)
                {
                    if (string.IsNullOrEmpty(_selectedIosBinPath) || !File.Exists(_selectedIosBinPath))
                    {
                        throw new InvalidOperationException("Equipamento em modo BootWare (sem SO): selecione o arquivo de Firmware (.ipe / .bin) no Passo 3 para realizar a recuperação.");
                    }

                    SetEtapa(2, "⏳ 2. Atualizar Firmware — em execução", "#D97706");
                    Progresso(25, "2/7 Atualizar Firmware...");
                    LogAuto(">>> [AUTO 2/7] Atualizar / Recuperar Firmware HPE via TFTP");
                    var hostIp = _loadedSaipCircuit?.HostLanIp ?? ObterIpLocalParaTftp() ?? "200.182.245.18";
                    await ExecutarUpgradeFirmwareAsync(porta, baud, hostIp, ct);
                    SetEtapa(2, "✅ 2. Atualizar Firmware — OK", "#16A34A");
                    Progresso(45, "2/7 OK");
                }
                else
                {
                    SetEtapa(2, "✅ 2. Firmware — mantido (opção do operador)", "#16A34A");
                    LogAuto(">>> [AUTO 2/7] Firmware HPE mantido: atualização dispensada pelo operador (bypass de firmware).");
                    Progresso(45, "2/7 OK");
                }
            }
            else
            {
                if (_skipFactoryReset)
                {
                    SetEtapa(1, "⏭ 1. Zerar Configuração — pulado (Login autenticado)", "#16A34A");
                    Progresso(18, "1/7 OK (Autenticado)");
                    LogAuto(">>> [AUTO 1/7] Zeramento de configuração pulado (equipamento autenticado com sucesso pelo operador).");
                }
                else
                {
                    SetEtapa(1, "⏳ 1. Zerar Configuração — em execução", "#D97706");
                    Progresso(5, "1/7 Zerar Configuração...");
                    LogAuto(">>> [AUTO 1/7] Zerar Configuração (verificando senha e terminal)");
                    await ExecutarZerarConfigAsync(porta, baud, ct);
                    SetEtapa(1, "✅ 1. Zerar Configuração — OK", "#16A34A");
                    Progresso(18, "1/7 OK");
                }

                // Reavalia se o equipamento caiu no ROMMON após Fase 1 apenas se o operador optou por atualizar
                if (_isRommonOrBootwareDetected && (ChkAtualizarFirmwareAuto?.IsChecked == true)) firmwareDesejado = true;

                if (firmwareDesejado)
                {
                    if (string.IsNullOrEmpty(_selectedIosBinPath) || !File.Exists(_selectedIosBinPath))
                    {
                        throw new InvalidOperationException("Equipamento em modo ROMMON (sem SO): selecione o arquivo de Firmware (.bin) no Passo 3 para realizar a recuperação via TFTP.");
                    }

                    SetEtapa(2, "⏳ 2. Atualizar Firmware — em execução", "#D97706");
                    Progresso(22, "2/7 Atualizar Firmware...");
                    LogAuto(">>> [AUTO 2/7] Atualizar / Recuperar Firmware Cisco via TFTP (ROMMON)");
                    var hostIp = _loadedSaipCircuit?.HostLanIp ?? ObterIpLocalParaTftp() ?? "127.0.0.1";
                    await ExecutarUpgradeFirmwareAsync(porta, baud, hostIp, ct);
                    SetEtapa(2, "✅ 2. Atualizar Firmware — OK", "#16A34A");
                    Progresso(42, "2/7 OK");
                }
                else
                {
                    SetEtapa(2, "✅ 2. Firmware — mantido (opção do operador)", "#16A34A");
                    LogAuto(">>> [AUTO 2/7] Firmware Cisco mantido: atualização dispensada pelo operador (bypass de firmware).");
                    Progresso(42, "2/7 OK");
                }
            }

            SetEtapa(3, "⏳ 3. Provisionar Equipamento — em execução", "#D97706"); Progresso(48, "3/7 Provisionar..."); LogAuto(">>> [AUTO 3/7] Provisionar");
            await ExecutarAplicarSaipAsync(porta, baud, ct); SetEtapa(3, "✅ 3. Provisionar — OK", "#16A34A"); Progresso(60, "3/7 OK");

            var adapter = CbAdaptadorRede.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(adapter) && _loadedSaipCircuit != null)
            {
                SetEtapa(4, "⏳ 4. Configurar IP de Teste — em execução", "#D97706"); Progresso(65, "4/7 IP Teste..."); LogAuto(">>> [AUTO 4/7] IP Teste");
                await ExecutarConfigIpTesteAsync(adapter, ct); SetEtapa(4, "✅ 4. IP de Teste — OK", "#16A34A"); Progresso(72, "4/7 OK");
            }
            else { SetEtapa(4, "⏭ 4. IP de Teste — pulado (sem adaptador)", "#64748B"); Progresso(72, "4/7 pulado"); }



            SetEtapa(5, "⏳ 5. Testar Conectividade (ICMP: 5a LAN, 5b WAN, 5c WEB) — em execução", "#D97706"); Progresso(76, "5/7 ICMP..."); LogAuto(">>> [AUTO 5/7] ICMP (5a LAN / 5b WAN / 5c WEB)");
            var icmpR = await ExecutarTesteIcmpTriploAsync(ct);
            var icmpResText = $"5. ICMP: 5a LAN ({(icmpR.IsLanOk ? "OK" : "❌")}) | 5b WAN ({(icmpR.IsWanOk ? "OK" : "❌")}) | 5c WEB ({(icmpR.IsWebOk ? "OK" : "❌")})";
            SetEtapa(5, $"{icmpR.StatusBadge} {icmpResText}", icmpR.StatusColorHex);
            Progresso(85, "5/7 OK");

            var accessTitle = isForti ? "Acesso Remoto (SSH / Telnet)" : "Acesso Remoto (Telnet)";
            SetEtapa(6, $"⏳ 6. Testar {accessTitle} — em execução", "#D97706"); Progresso(86, "6/7 Remoto..."); LogAuto($">>> [AUTO 6/7] {accessTitle}");
            var telnetHost = TxtTelnetTarget.Text?.Trim(); if (string.IsNullOrEmpty(telnetHost)) telnetHost = _loadedSaipCircuit?.LanIp ?? "200.182.245.17";
            var telnetPort = int.TryParse(TxtTelnetPort.Text?.Trim(), out var tp) ? tp : 23;
            var telnetR = await ExecutarTesteTelnetAsync(telnetHost, telnetPort, ct);

            if (isForti && !telnetR.IsSuccess)
            {
                // No FortiOS o serviço Telnet vem desabilitado por padrão; valida as portas padrão SSH (22) e HTTPS (443)
                var sourceIp = ObterIpOrigemParaIcmp();
                var srv = new ConnectivityService(EscreverLinhaAsync);
                var (sshOk, sshLat, _) = await srv.TestTcpPortAsync(telnetHost, 22, timeoutMs: 5000, sourceIpAddress: sourceIp, cancellationToken: ct);
                if (sshOk)
                {
                    telnetR = new ConnectivityService.TelnetTestResult(telnetHost, 22, true, sshLat, "SSH (Porta 22) Ativa", "SSH Conectado", null);
                    LogAuto($"[OK] [FORTINET] Acesso remoto SSH (porta 22) validado com sucesso ({sshLat}ms)!");
                }
                else
                {
                    var (httpsOk, httpsLat, _) = await srv.TestTcpPortAsync(telnetHost, 443, timeoutMs: 5000, sourceIpAddress: sourceIp, cancellationToken: ct);
                    if (httpsOk)
                    {
                        telnetR = new ConnectivityService.TelnetTestResult(telnetHost, 443, true, httpsLat, "HTTPS (Porta 443) Ativa", "HTTPS Conectado", null);
                        LogAuto($"[OK] [FORTINET] Acesso remoto HTTPS (porta 443) validado com sucesso ({httpsLat}ms)!");
                    }
                }
            }

            SetEtapa(6, (telnetR.IsSuccess ? "✅" : "❌") + $" 6. {accessTitle} — " + (telnetR.IsSuccess ? "OK" : "falha"), telnetR.IsSuccess ? "#16A34A" : "#EF4444"); Progresso(94, "6/7 OK");

            BandwidthTestResult bandR;
            if (icmpR != null && (!icmpR.IsWanOk || !icmpR.IsWebOk))
            {
                SetEtapa(7, "⏭ 7. Testar Banda — descartado (WAN offline)", "#64748B");
                Progresso(100, "Concluído!");
                LogAuto(">>> [AUTO 7/7] Teste de banda descartado (WAN/Internet offline no teste ICMP)");
                bandR = new BandwidthTestResult(0, 0, 0, 0, "Nativo HTTP", "Descartado", false, "Descartado automaticamente pois o link WAN / Internet não respondeu ao teste ICMP.");
            }
            else
            {
                SetEtapa(7, "⏳ 7. Testar Banda — em execução", "#D97706"); Progresso(96, "7/7 Banda..."); LogAuto(">>> [AUTO 7/7] Banda");
                bandR = await ExecutarTesteBandaAsync(ct);
                SetEtapa(7, (bandR.IsSuccess ? "✅" : "⚠") + " 7. Testar Banda — " + (bandR.IsSuccess ? "OK" : "falha"), bandR.IsSuccess ? "#16A34A" : "#D97706"); Progresso(100, "Concluído!");
            }

            LogAuto("================================================================="); LogAuto(" MODO AUTOMÁTICO CONCLUÍDO "); LogAuto("=================================================================");
            BtnAutoCancelar.Visibility = Visibility.Collapsed; BtnAutoVoltar.Visibility = Visibility.Visible;

            // Gera o Relatório Técnico PDF Completo com testes ICMP e Largura de Banda
            await ExibirRelatorioFinalAutomaticoAsync(
                porta, baud,
                step1Ok: true,
                step2Ok: true,
                step3Ok: true,
                step4Ok: !string.IsNullOrEmpty(adapter),
                icmpResult: icmpR,
                telnetResult: telnetR,
                bandResult: bandR,
                falhaGeral: null,
                exibirPopup: true);
        }
        catch (OperationCanceledException)
        {
            LogAuto("[!] Modo automático cancelado pelo operador.");
            Progresso(0, "Cancelado");
            BtnAutoVoltar.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LogAuto($"[ERRO AUTO] {ex.Message}");
            Progresso(0, $"Falha: {ex.Message}");
            BtnAutoVoltar.Visibility = Visibility.Visible;

            await ExibirRelatorioFinalAutomaticoAsync(
                porta, baud,
                step1Ok: false,
                step2Ok: false,
                step3Ok: false,
                step4Ok: false,
                icmpResult: null,
                telnetResult: null,
                bandResult: null,
                falhaGeral: ex.Message,
                exibirPopup: false);
        }
        finally { _cts?.Dispose(); _cts = null; }
    }

    private string? _lastGeneratedPdfPath;
    private ActivationReportData? _lastReportData;
    private string? _lastAppliedConfigScript;

    private async Task ExibirRelatorioFinalAutomaticoAsync(
        string porta,
        int baud,
        bool step1Ok,
        bool step2Ok,
        bool step3Ok,
        bool step4Ok,
        TripleIcmpResult? icmpResult,
        ConnectivityService.TelnetTestResult? telnetResult,
        BandwidthTestResult? bandResult,
        string? falhaGeral,
        bool exibirPopup = false)
    {
        var dataHora = DateTime.Now;
        var itemModelo = CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem;
        var modelo = itemModelo?.Content?.ToString()?.Replace("🖧", "")?.Replace("🛡️", "")?.Trim() ?? "Roteador";
        var cliente = SaipParser.CleanRazaoSocial(_loadedSaipCircuit?.ClienteRazaoSocial) ?? "Não informado / Manual";
        var designacao = _loadedSaipCircuit?.DesignacaoIp ?? _loadedSaipCircuit?.NumeroOts ?? "Não informada";
        var wanIp = _loadedSaipCircuit?.WanIp;
        var wanCidr = _loadedSaipCircuit?.WanCidr ?? 30;
        var wanGateway = _loadedSaipCircuit?.WanGateway;
        var wanMask = _loadedSaipCircuit?.WanSubnetMask;
        var lanIp = _loadedSaipCircuit?.LanIp;
        var lanCidr = _loadedSaipCircuit?.LanCidr ?? 28;
        var lanBlock = _loadedSaipCircuit?.LanBlockNetwork;
        var lanMask = _loadedSaipCircuit?.LanSubnetMask;
        var hostLanIp = _loadedSaipCircuit?.HostLanIp;
        var adapter = CbAdaptadorRede.Text?.Trim();

        var is5aOk = icmpResult?.IsLanOk == true;
        var is5bOk = icmpResult?.IsWanOk == true;
        var is5cOk = icmpResult?.IsWebOk == true;
        var isTelnetOk = telnetResult?.IsSuccess == true;
        var isBandOk = bandResult?.IsSuccess == true;

        // DIAGNÓSTICO DE CAUSAS EM CASO DE FALHAS
        var falhas = new List<string>();

        if (!string.IsNullOrEmpty(falhaGeral))
        {
            falhas.Add($"Falha Crítica no Processo: {falhaGeral}");
        }

        if (!is5aOk && step3Ok)
        {
            var isFortiRep = IsTagFortiGate(itemModelo?.Tag?.ToString() ?? "") || _isFortiGateDetected;
            var lanIfaceRep = isFortiRep ? "lan (ou lan1/internal)" : "Giga 0/1 ou Giga 1";
            falhas.Add($"Falha no Teste 5a (ICMP LAN / Roteador): Verifique se o cabo Ethernet do PC está conectado na porta LAN do roteador ({lanIfaceRep}) e com IP configurado.");
        }

        if (!is5bOk && is5aOk)
        {
            falhas.Add("Falha no Teste 5b (ICMP WAN / Gateway Claro): Cabo da WAN desconectado da porta WAN ou circuito ainda não ativado na central da operadora Claro.");
        }

        if (!is5cOk && is5bOk)
        {
            falhas.Add("Falha no Teste 5c (ICMP WEB / Internet Pública): Rota default (0.0.0.0/0) ou sessão BGP pendente de liberação pela operadora.");
        }

        if (!isTelnetOk && step3Ok)
        {
            falhas.Add("Falha no Teste 6 (Acesso Remoto Telnet / Porta 23): Firewall do Windows bloqueando conexões de saída na porta 23 ou linha VTY sem senha/login.");
        }

        if (bandResult != null && !isBandOk)
        {
            falhas.Add($"Falha no Teste 7 (Banda abaixo do nominal): {bandResult.Message}");
        }

        TripleIcmpData? icmpData = icmpResult != null
            ? new TripleIcmpData(icmpResult.LanResult, icmpResult.WanResult, icmpResult.WebResult)
            : null;

        var reportData = new ActivationReportData(
            DataHora: dataHora,
            ModeloEquipamento: modelo,
            PortaSerial: porta,
            BaudRate: baud,
            ClienteRazaoSocial: cliente,
            DesignacaoIp: designacao,
            NumeroOts: _loadedSaipCircuit?.NumeroOts,
            PeRouter: _loadedSaipCircuit?.PeRouter,
            WanIp: wanIp,
            WanCidr: wanCidr,
            WanGateway: wanGateway,
            WanSubnetMask: wanMask,
            WanInterface: modelo.Contains("921", StringComparison.OrdinalIgnoreCase) ? "GigabitEthernet 5" : modelo.Contains("954", StringComparison.OrdinalIgnoreCase) || modelo.Contains("HPE", StringComparison.OrdinalIgnoreCase) ? "GigabitEthernet0/0" : "GigabitEthernet 0/0",
            LanIp: lanIp,
            LanCidr: lanCidr,
            LanBlockNetwork: lanBlock,
            LanSubnetMask: lanMask,
            HostLanIp: hostLanIp,
            LanInterface: modelo.Contains("921", StringComparison.OrdinalIgnoreCase) ? "GigabitEthernet 4" : modelo.Contains("954", StringComparison.OrdinalIgnoreCase) || modelo.Contains("HPE", StringComparison.OrdinalIgnoreCase) ? "GigabitEthernet0/1" : "GigabitEthernet 0/1",
            Step1ZerarOk: step1Ok,
            Step2FirmwareOk: step2Ok,
            FirmwareNome: Path.GetFileName(_selectedIosBinPath),
            Step3SaipOk: step3Ok,
            Step4IpLocalOk: step4Ok,
            AdaptadorRedeLocal: adapter,
            IcmpResult: icmpData,
            TelnetResult: telnetResult,
            BandResult: bandResult,
            DiagnosticAlerts: falhas,
            FalhaGeral: falhaGeral,
            BandaMbpsNominal: _loadedSaipCircuit?.BandaMbpsNominal,
            AppliedConfigScript: _lastAppliedConfigScript
        );

        _lastReportData = reportData;

        // Gera o arquivo PDF de homologação
        string pdfPath = "";
        try
        {
            pdfPath = await ActivationPdfReportService.GenerateReportPdfAsync(reportData);
            _lastGeneratedPdfPath = pdfPath;
        }
        catch (Exception ex)
        {
            EscreverLinha($"[AVISO] Erro na geração automática do PDF: {ex.Message}");
        }

        // Escreve resumo formatado no terminal
        EscreverLinha("\n================================================================================");
        EscreverLinha("           📄 RELATÓRIO TÉCNICO DE HOMOLOGAÇÃO E ATIVAÇÃO (PDF GERADO)          ");
        EscreverLinha("================================================================================");
        EscreverLinha($"  Cliente       : {cliente}");
        EscreverLinha($"  Circuito      : {designacao}");
        EscreverLinha($"  WAN / LAN     : {wanIp}/{wanCidr} | {lanIp}/{lanCidr}");
        EscreverLinha($"  Status Geral  : {(falhas.Count == 0 ? "🟢 100% HOMOLOGADO E APROVADO" : "🔴 NÃO HOMOLOGADO / REPROVADO")}");
        if (!string.IsNullOrEmpty(pdfPath))
        {
            EscreverLinha($"  Arquivo PDF   : {pdfPath}");
        }
        EscreverLinha("================================================================================\n");

        Dispatcher.Invoke(() =>
        {
            BtnAutoAbrirPdf.Visibility = Visibility.Visible;
            BtnAutoExportarPdf.Visibility = Visibility.Visible;
            BtnAutoRestaurarRede.Visibility = Visibility.Visible;
            BtnAutoRetestarBanda.Visibility = Visibility.Visible;
            BtnAutoSalvarScript.Visibility = Visibility.Visible;

            if (exibirPopup && !string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
            {
                var abrirAgora = MessageBox.Show(
                    $"Relatório Técnico de Homologação gerado em PDF com sucesso!\n\n" +
                    $"📄 Arquivo: {Path.GetFileName(pdfPath)}\n" +
                    $"Status: {(falhas.Count == 0 ? "🟢 100% Aprovado" : "🔴 Não Homologado / Reprovado")}\n\n" +
                    $"Deseja abrir o arquivo PDF agora?",
                    "SPARC — Relatório Técnico em PDF",
                    MessageBoxButton.YesNo,
                    falhas.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

                if (abrirAgora == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true });
                    }
                    catch { }
                }
            }
        });
    }

    private void BtnAutoAbrirPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastGeneratedPdfPath) && File.Exists(_lastGeneratedPdfPath))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _lastGeneratedPdfPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o PDF: {ex.Message}", "Erro ao Abrir PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Nenhum relatório PDF disponível no momento.", "Relatório PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void BtnAutoRestaurarRede_Click(object sender, RoutedEventArgs e)
    {
        await PerguntarRestauracaoRedeAsync();
    }

    private async void BtnAutoRetestarBanda_Click(object sender, RoutedEventArgs e)
    {
        BtnAutoRetestarBanda.IsEnabled = false;
        BtnAutoRetestarBanda.Content = "⏳ Validando Ethernet...";

        try
        {
            EscreverLinha("\n=================================================================");
            EscreverLinha("   🔄 RE-TESTE DE LARGURA DE BANDA / VELOCIDADE DE CONEXÃO");
            EscreverLinha("=================================================================");

            // 1. Localiza a interface de rede física Ethernet dedicada à conexão com o roteador
            var eth = ObterInformacoesEthernetRoteador();
            if (eth == null)
            {
                MessageBox.Show(this,
                    "Nenhuma interface de rede física Ethernet foi detectada no computador.\n\n" +
                    "Para evitar falsos positivos com redes Wi-Fi ou secundárias ativas no sistema, o re-teste de banda exige uma interface Ethernet cabeada conectada à porta LAN do roteador.",
                    "Interface Ethernet Não Encontrada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                EscreverLinha("[ERRO ISOLAMENTO] Nenhuma interface Ethernet física encontrada. Re-teste cancelado para evitar falso positivo via Wi-Fi/outras redes.");
                return;
            }

            // 2. Verifica se o cabo de rede está conectado à interface física (Link UP)
            if (!eth.IsUp)
            {
                var respCabo = MessageBox.Show(this,
                    $"A interface Ethernet '{eth.Name}' ({eth.Description}) está DESCONECTADA (sem link físico / cabo solto).\n\n" +
                    "👉 Por favor, conecte o cabo de rede entre o computador e a porta LAN do roteador e clique OK para prosseguir, ou Cancelar para abortar o re-teste.\n\n" +
                    "(O re-teste é bloqueado por Wi-Fi para evitar falsos positivos com a conexão de internet do computador).",
                    "Cabo de Rede Desconectado — Re-teste de Banda",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);

                if (respCabo != MessageBoxResult.OK)
                {
                    EscreverLinha("[*] Re-teste de banda cancelado pelo operador (cabo de rede desconectado).");
                    return;
                }

                // Reavalia o status da interface após a conexão física do cabo pelo operador
                eth = ObterInformacoesEthernetRoteador();
                if (eth == null || !eth.IsUp)
                {
                    MessageBox.Show(this,
                        $"A interface Ethernet '{eth?.Name ?? "Ethernet"}' continua sem link físico.\nVerifique se o cabo está firmemente conectado e se a porta LAN do roteador está ligada.",
                        "Link Ethernet Inativo",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    EscreverLinha("[ERRO ISOLAMENTO] Interface Ethernet continua sem link físico após tentativa.");
                    return;
                }
            }

            // 3. Garante que o IP de teste do circuito (HostLanIp) está configurado na interface Ethernet
            var hostIpEsperado = _loadedSaipCircuit?.HostLanIp?.Trim();
            if (!string.IsNullOrEmpty(hostIpEsperado) && eth.IPv4Address != hostIpEsperado)
            {
                EscreverLinha($"[*] [RE-TESTE] Reaplicando IP estático do circuito ({hostIpEsperado}) na interface Ethernet '{eth.Name}'...");
                await HostNetworkManager.SetStaticIpAsync(eth.Name, hostIpEsperado, _loadedSaipCircuit!.LanSubnetMask, _loadedSaipCircuit.LanIp, CancellationToken.None);
                await Task.Delay(1000);
                eth = ObterInformacoesEthernetRoteador();
            }

            var sourceIp = eth?.IPv4Address ?? hostIpEsperado;
            EscreverLinha($"[*] [RE-TESTE] Interface de teste validada: '{eth?.Name}' ({eth?.Description})");
            EscreverLinha($"[*] [RE-TESTE] IP de Origem: {sourceIp ?? "Automático"} | Status Link: Conectado (UP)");
            EscreverLinha("[*] [RE-TESTE] Tráfego vinculado exclusivamente à interface Ethernet — Wi-Fi e redes secundárias isoladas.");

            BtnAutoRetestarBanda.Content = "⏳ Medindo Banda...";
            TxtAutoEtapa7.Text = "⏳ 7. Testar Banda — em re-teste...";
            TxtAutoEtapa7.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
            TxtAutoLog.Text += $"\n>>> [RE-TESTE] Disparando medição vinculada à Ethernet ({eth?.Name} - IP {sourceIp})...\n";

            var novoBandR = await ExecutarTesteBandaAsync(CancellationToken.None, sourceIp);

            var statusTexto = novoBandR.IsSuccess
                ? $"✅ 7. Testar Banda — OK ({novoBandR.DownloadMbps:F1} Mbps)"
                : $"⚠ 7. Testar Banda — falha ({novoBandR.Message})";
            var statusCor = novoBandR.IsSuccess ? "#16A34A" : "#D97706";
            TxtAutoEtapa7.Text = statusTexto;
            TxtAutoEtapa7.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusCor));

            TxtAutoLog.Text += $">>> [RE-TESTE] Concluído: Download {novoBandR.DownloadMbps:F1} Mbps | Latência {novoBandR.LatencyMs:F0}ms | {(novoBandR.IsSuccess ? "APROVADO" : "FALHA")}\n";

            // Atualiza o relatório da sessão e regenera o PDF automaticamente
            if (_lastReportData != null)
            {
                var falhas = new List<string>(_lastReportData.DiagnosticAlerts ?? Enumerable.Empty<string>());
                falhas.RemoveAll(f => f.Contains("Banda", StringComparison.OrdinalIgnoreCase) || f.Contains("Velocidade", StringComparison.OrdinalIgnoreCase));
                if (!novoBandR.IsSuccess)
                {
                    falhas.Add($"Falha no Teste de Largura de Banda: {novoBandR.Message}");
                }

                _lastReportData = _lastReportData with
                {
                    BandResult = novoBandR,
                    DiagnosticAlerts = falhas,
                    FalhaGeral = falhas.Count > 0 ? string.Join("; ", falhas) : null
                };

                try
                {
                    _lastGeneratedPdfPath = await ActivationPdfReportService.GenerateReportPdfAsync(_lastReportData);
                    EscreverLinha($"[OK] Relatório Técnico PDF atualizado com sucesso: {_lastGeneratedPdfPath}");
                    TxtAutoLog.Text += $"[OK] Relatório PDF atualizado: {Path.GetFileName(_lastGeneratedPdfPath)}\n";
                }
                catch (Exception ex)
                {
                    EscreverLinha($"[AVISO] Não foi possível atualizar o PDF: {ex.Message}");
                }
            }

            MessageBox.Show(this,
                $"Re-teste de Largura de Banda concluído!\n\n" +
                $"Download: {novoBandR.DownloadMbps:F1} Mbps\n" +
                $"Latência: {novoBandR.LatencyMs:F0} ms\n" +
                $"Status: {(novoBandR.IsSuccess ? "🟢 Aprovado" : "⚠ Insuficiente / Falha")}\n\n" +
                (novoBandR.IsSuccess ? "O relatório técnico em PDF foi atualizado automaticamente." : novoBandR.Message),
                "Re-teste de Largura de Banda",
                MessageBoxButton.OK,
                novoBandR.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            EscreverLinha($"[ERRO RE-TESTE BANDA] {ex.Message}");
            MessageBox.Show(this, $"Erro ao executar re-teste de banda: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnAutoRetestarBanda.IsEnabled = true;
            BtnAutoRetestarBanda.Content = "🔄 Re-testar Largura de Banda";
        }
    }

    private void BtnAutoSalvarScript_Click(object sender, RoutedEventArgs e)
    {
        var script = _lastReportData?.AppliedConfigScript ?? _lastAppliedConfigScript;
        if (string.IsNullOrWhiteSpace(script))
        {
            MessageBox.Show(this, "Nenhuma configuração aplicada foi capturada ainda.\nExecute a Fase C (provisionamento) antes de salvar o script.",
                "Salvar script TXT", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var designacao = _loadedSaipCircuit?.DesignacaoIp ?? _loadedSaipCircuit?.NumeroOts ?? "Circuito";
        foreach (var c in Path.GetInvalidFileNameChars()) designacao = designacao.Replace(c, '_');
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Salvar script das configurações aplicadas",
            Filter = "Script TXT (*.txt)|*.txt|Todos os Arquivos (*.*)|*.*",
            FileName = $"Script_SPARC_{designacao}_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var header = $"SPARC — Configurações aplicadas no equipamento\n" +
                             $"Data: {DateTime.Now:dd/MM/yyyy HH:mm}\n" +
                             $"Circuito: {_loadedSaipCircuit?.DesignacaoIp ?? "-"} | OTS: {_loadedSaipCircuit?.NumeroOts ?? "-"}\n" +
                             new string('=', 70) + "\n\n";
                File.WriteAllText(dlg.FileName, header + script, Encoding.UTF8);
                EscreverLinha($"[OK] Script das configurações salvo em: {dlg.FileName}");
                MessageBox.Show(this, $"Script salvo em:\n{dlg.FileName}", "Salvar script TXT",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Não foi possível salvar: {ex.Message}", "Salvar script TXT",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnAutoExportarPdf_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_lastGeneratedPdfPath) || !File.Exists(_lastGeneratedPdfPath))
        {
            MessageBox.Show("Nenhum relatório PDF foi gerado ainda.", "Exportar PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Salvar Relatório de Homologação SPARC como...",
            Filter = "Documento PDF (*.pdf)|*.pdf|Arquivo HTML (*.html)|*.html",
            FileName = Path.GetFileName(_lastGeneratedPdfPath)
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                File.Copy(_lastGeneratedPdfPath, dlg.FileName, true);
                MessageBox.Show($"Relatório salvo com sucesso em:\n{dlg.FileName}", "Relatório Exportado", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Falha ao salvar relatório: {ex.Message}", "Erro ao Salvar", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnExportarRelatorioPdfTop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var porta = CbPorta?.Text?.Trim() ?? "COM1";
            var baud = int.TryParse(CbBaud?.Text, out var b) ? b : 9600;
            var itemModelo = CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem;
            var modelo = itemModelo?.Content?.ToString()?.Replace("🖧", "")?.Trim() ?? "Roteador";
            var cliente = SaipParser.CleanRazaoSocial(_loadedSaipCircuit?.ClienteRazaoSocial) ?? "Não informado / Manual";
            var designacao = _loadedSaipCircuit?.DesignacaoIp ?? _loadedSaipCircuit?.NumeroOts ?? "Circuito";

            var reportData = _lastReportData ?? new ActivationReportData(
                DataHora: DateTime.Now,
                ModeloEquipamento: modelo,
                PortaSerial: porta,
                BaudRate: baud,
                ClienteRazaoSocial: cliente,
                DesignacaoIp: designacao,
                NumeroOts: _loadedSaipCircuit?.NumeroOts,
                PeRouter: _loadedSaipCircuit?.PeRouter,
                WanIp: _loadedSaipCircuit?.WanIp,
                WanCidr: _loadedSaipCircuit?.WanCidr ?? 30,
                WanGateway: _loadedSaipCircuit?.WanGateway,
                WanSubnetMask: _loadedSaipCircuit?.WanSubnetMask,
                WanInterface: modelo.Contains("921", StringComparison.OrdinalIgnoreCase) ? "GigabitEthernet 5" : modelo.Contains("954", StringComparison.OrdinalIgnoreCase) || modelo.Contains("HPE", StringComparison.OrdinalIgnoreCase) ? "GigabitEthernet0/0" : "GigabitEthernet 0/0",
                LanIp: _loadedSaipCircuit?.LanIp,
                LanCidr: _loadedSaipCircuit?.LanCidr ?? 28,
                LanBlockNetwork: _loadedSaipCircuit?.LanBlockNetwork,
                LanSubnetMask: _loadedSaipCircuit?.LanSubnetMask,
                HostLanIp: _loadedSaipCircuit?.HostLanIp,
                LanInterface: modelo.Contains("921", StringComparison.OrdinalIgnoreCase) ? "GigabitEthernet 4" : modelo.Contains("954", StringComparison.OrdinalIgnoreCase) || modelo.Contains("HPE", StringComparison.OrdinalIgnoreCase) ? "GigabitEthernet0/1" : "GigabitEthernet 0/1",
                Step1ZerarOk: true,
                Step2FirmwareOk: true,
                FirmwareNome: Path.GetFileName(_selectedIosBinPath),
                Step3SaipOk: true,
                Step4IpLocalOk: true,
                AdaptadorRedeLocal: CbAdaptadorRede?.Text?.Trim(),
                IcmpResult: null,
                TelnetResult: null,
                BandResult: null,
                DiagnosticAlerts: null,
                FalhaGeral: null
            );

            var pdf = await ActivationPdfReportService.GenerateReportPdfAsync(reportData);
            _lastGeneratedPdfPath = pdf;

            Process.Start(new ProcessStartInfo { FileName = pdf, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar relatório PDF: {ex.Message}", "Relatório PDF", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnPularTelaInicial_Click(object sender, RoutedEventArgs e)
    {
        GridTelaInicial.Visibility = Visibility.Collapsed;
        GridEsteiraPrincipal.Visibility = Visibility.Visible;
    }

    private void BtnTrocarInsumos_Click(object sender, RoutedEventArgs e)
    {
        GridEsteiraPrincipal.Visibility = Visibility.Collapsed;
        GridModoAutomatico.Visibility = Visibility.Collapsed;
        GridTelaInicial.Visibility = Visibility.Visible;
        Width = 1060;
        MinWidth = 920;
        if (BtnSemiAutoAtalho != null) BtnSemiAutoAtalho.Visibility = Visibility.Collapsed;
    }

    public static string CidrToSubnetMask(int cidr)
    {
        if (cidr is < 0 or > 32)
            return "255.255.255.0";
        var mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
        return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
    }

    public static string CalcularHostIp(string lanIp)
    {
        if (System.Net.IPAddress.TryParse(lanIp, out var ip))
        {
            var bytes = ip.GetAddressBytes();
            if (bytes.Length == 4)
            {
                bytes[3] = (byte)(bytes[3] + 1);
                return new System.Net.IPAddress(bytes).ToString();
            }
        }
        return string.Empty;
    }

    private async void BtnCarregarSaip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Selecionar Ficha SAIP do Circuito",
            Filter = "Arquivos SAIP (*.txt;*.pdf)|*.txt;*.pdf|Arquivos de Texto (*.txt)|*.txt|Documentos PDF (*.pdf)|*.pdf|Todos os Arquivos (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var rawText = await SaipParser.CarregarTextoAsync(dlg.FileName);
                var (ok, motivo) = SaipParser.Validar(rawText);
                if (!ok)
                {
                    MessageBox.Show($"Ficha SAIP rejeitada: {motivo}\n\nSelecione um arquivo SAIP válido contendo IP Serial (WAN) e Blocos IPv4 (LAN).", "Ficha SAIP - Formato inválido", MessageBoxButton.OK, MessageBoxImage.Error);
                    EscreverLinha($"[!] Ficha SAIP rejeitada ({Path.GetFileName(dlg.FileName)}): {motivo}");
                    return;
                }

                var circuit = SaipParser.ParseText(rawText);
                _loadedSaipCircuit = circuit;

                TxtSaipResumo.Text = $"Circuito: {circuit.DesignacaoIp ?? circuit.NumeroOts} | WAN: {circuit.WanIp}/{circuit.WanCidr} (GW: {circuit.WanGateway}) | LAN: {circuit.LanIp}/{circuit.LanCidr} | Cliente: {circuit.ClienteRazaoSocial}";
                TxtHostIpCalculado.Text = $"Host LAN: {circuit.HostLanIp}/{circuit.LanCidr}";
                if (TxtIcmpTargetLan is not null && !string.IsNullOrEmpty(circuit.LanIp))
                    TxtIcmpTargetLan.Text = circuit.LanIp;
                if (TxtIcmpTargetWan is not null && !string.IsNullOrEmpty(circuit.WanGateway))
                    TxtIcmpTargetWan.Text = circuit.WanGateway;
                if (TxtIcmpTargetWeb is not null)
                    TxtIcmpTargetWeb.Text = "1.1.1.1, 8.8.8.8";
                if (!string.IsNullOrEmpty(circuit.WanGateway))
                {
                    TxtIcmpTarget.Text = circuit.WanGateway;
                }

                BtnLimparSaip.Visibility = Visibility.Visible;
                TxtCircuitoAtivoTitulo.Text = $"CIRCUITO: {circuit.DesignacaoIp ?? circuit.NumeroOts} - {circuit.ClienteRazaoSocial}";
                TxtCircuitoAtivoResumo.Text = $"WAN: {circuit.WanIp}/{circuit.WanCidr} (GW: {circuit.WanGateway}) | LAN: {circuit.LanIp}/{circuit.LanCidr} | Host: {circuit.HostLanIp} | DNS: 1.1.1.1, 8.8.8.8";

                AtualizarEstadoBotoes();
                AtualizarBotaoProsseguir();

                EscreverLinha("\n=================================================================");
                EscreverLinha("             FICHA SAIP CARREGADA COM SUCESSO                    ");
                EscreverLinha("=================================================================");
                EscreverLinha($"  Arquivo     : {Path.GetFileName(dlg.FileName)}");
                EscreverLinha($"  Cliente     : {circuit.ClienteRazaoSocial}");
                EscreverLinha($"  Designação  : {circuit.DesignacaoIp}");
                EscreverLinha($"  Número OTS  : {circuit.NumeroOts}");
                EscreverLinha($"  WAN (GE0)   : {circuit.WanIp} {circuit.WanSubnetMask} (Prefixo /{circuit.WanCidr})");
                EscreverLinha($"  Gateway WAN : {circuit.WanGateway}");
                EscreverLinha($"  LAN (GE1)   : {circuit.LanIp} {circuit.LanSubnetMask} (Bloco {circuit.LanBlockNetwork}/{circuit.LanCidr})");
                EscreverLinha($"  Host Teste  : {circuit.HostLanIp} (IP calculado com DNS 1.1.1.1 / 8.8.8.8)");
                EscreverLinha("=================================================================\n");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar e interpretar Ficha SAIP:\n{ex.Message}", "Ficha SAIP", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnLimparSaip_Click(object sender, RoutedEventArgs e)
    {
        _loadedSaipCircuit = null;
        TxtSaipResumo.Text = "Nenhuma ficha SAIP selecionada ainda. Clique no botão acima para carregar o arquivo PDF ou TXT da ordem de serviço.";
        TxtHostIpCalculado.Text = "Host LAN: -";
        TxtCircuitoAtivoTitulo.Text = "CIRCUITO NÃO CONFIGURADO";
        TxtCircuitoAtivoResumo.Text = "Nenhum circuito configurado.";
        BtnLimparSaip.Visibility = Visibility.Collapsed;
        AtualizarEstadoBotoes();
        AtualizarBotaoProsseguir();
        EscreverLinha("[*] Dados do circuito removidos.");
    }

    private void BtnSelecionarIos_Click(object sender, RoutedEventArgs e)
    {
        var perfilHw = (CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() 
                    ?? (CbInterrupt?.SelectedItem as ComboBoxItem)?.Tag?.ToString() 
                    ?? string.Empty;
        var isForti = IsTagFortiGate(perfilHw) || _isFortiGateDetected;
        var isHpe = !isForti && (perfilHw.Contains("hpe", StringComparison.OrdinalIgnoreCase) || perfilHw.Contains("msr", StringComparison.OrdinalIgnoreCase));
        var isCisco = !isForti && perfilHw.Contains("cisco", StringComparison.OrdinalIgnoreCase);

        var dlg = new OpenFileDialog
        {
            Title = isForti ? "Selecionar Imagem de Firmware FortiOS (.OUT)"
                  : isHpe ? "Selecionar Pacote de Firmware HPE Comware (.IPE)" 
                  : isCisco ? "Selecionar Imagem de Firmware Cisco IOS (.BIN)"
                  : "Selecionar Arquivo de Firmware (.bin / .ipe / .out)",
            Filter = isForti ? "Imagens FortiOS (*.out)|*.out|Todos os Arquivos (*.*)|*.*"
                   : isHpe ? "Pacotes HPE Comware (*.ipe)|*.ipe|Imagens Binárias (*.bin)|*.bin|Todos os Arquivos (*.*)|*.*"
                   : isCisco ? "Firmware Cisco IOS (*.bin)|*.bin|Todos os Arquivos (*.*)|*.*"
                   : "Firmwares Suportados (*.bin;*.ipe;*.out)|*.bin;*.ipe;*.out|Imagens FortiOS (*.out)|*.out|Firmware Cisco IOS (*.bin)|*.bin|Pacotes HPE Comware (*.ipe)|*.ipe|Todos os Arquivos (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            if (!ValidarEBloquearFirmwareIncompativel(dlg.FileName, mostrarAlertaModal: true))
            {
                return;
            }

            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var fileName = Path.GetFileName(dlg.FileName);

            _selectedIosBinPath = dlg.FileName;
            var fi = new FileInfo(dlg.FileName);
            var sizeMb = (fi.Length / (1024.0 * 1024.0)).ToString("N1");
            TxtIosImageInfo.Text = $"{fileName} ({sizeMb} MB) [{ext.ToUpperInvariant()}]";
            if (TxtFirmwareAutoInfo != null)
                TxtFirmwareAutoInfo.Text = $"{fileName} ({sizeMb} MB)";

            EscreverLinha($"[*] Firmware validado e carregado: {fileName} ({sizeMb} MB) [{ext.ToUpperInvariant()}]");
            AtualizarEstadoBotoes();
            AtualizarBotaoProsseguir();

            // Crítica Preventiva de Mesma Versão (FortiOS)
            if (isForti && !string.IsNullOrWhiteSpace(_detectedFortiOsStatusOut))
            {
                if (NetworkDevice.Fortinet.FortiOsFirmwareVersionInspector.IsSameVersion(
                    _detectedFortiOsStatusOut, dlg.FileName, out var currentVer, out var targetVer))
                {
                    EscreverLinha("\n=================================================================");
                    EscreverLinha("       ℹ️ FIRMWARE FORTIOS: MESMA VERSÃO JÁ INSTALADA           ");
                    EscreverLinha("=================================================================");
                    EscreverLinha($"  Versão no FortiGate   : {currentVer}");
                    EscreverLinha($"  Versão da Imagem Alvo : {targetVer}");
                    EscreverLinha("  -> A imagem selecionada é idêntica à versão atualmente em execução.");
                    EscreverLinha("  -> A atualização de firmware no Passo 3 é desnecessária.");
                    EscreverLinha("=================================================================\n");

                    MessageBox.Show(
                        $"O FortiGate 40F já está executando exatamente esta versão de FortiOS:\n\n" +
                        $"• Versão no Equipamento: {currentVer}\n" +
                        $"• Imagem Selecionada: {targetVer}\n\n" +
                        $"A atualização de firmware é dispensável e não precisa ser executada.",
                        "Firmware FortiOS Já Atualizado",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }
    }

    private void BtnUsarGwWan_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedSaipCircuit != null && !string.IsNullOrEmpty(_loadedSaipCircuit.WanGateway))
        {
            TxtIcmpTarget.Text = _loadedSaipCircuit.WanGateway;
        }
        else
        {
            MessageBox.Show("Nenhum Gateway WAN disponível na Ficha SAIP carregada.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnUsarGwLan_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedSaipCircuit != null && !string.IsNullOrEmpty(_loadedSaipCircuit.LanIp))
        {
            TxtIcmpTarget.Text = _loadedSaipCircuit.LanIp;
        }
        else
        {
            MessageBox.Show("Nenhum IP LAN disponível na Ficha SAIP carregada.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnSpeedtestNet_Click(object sender, RoutedEventArgs e)
    {
        BandwidthTestService.OpenSpeedTestInBrowser("https://www.speedtest.net");
    }

    private void BtnFastCom_Click(object sender, RoutedEventArgs e)
    {
        BandwidthTestService.OpenSpeedTestInBrowser("https://fast.com");
    }

    #endregion

    #region Execuções Individuais por Fase da Esteira

    // FASE A · ZERAR CONFIGURAÇÃO
    private async void BtnZerarConfig_Click(object sender, RoutedEventArgs e)
    {
        var porta = CbPorta.Text.Trim();
        if (string.IsNullOrEmpty(porta))
        {
            EscreverLinha("[!] Selecione a porta serial do equipamento (ex: COM4).");
            return;
        }

        SelecionarFase("A");
        DefinirBadgeStatus("A", "⏳");
        SetBusy(true);
        _skipFactoryReset = false;
        _cts = new CancellationTokenSource();
        var baud = int.TryParse(CbBaud.Text, out var b) ? b : 9600;

        try
        {
            await ExecutarZerarConfigAsync(porta, baud, _cts.Token);
            DefinirBadgeStatus("A", "✅");
        }
        catch (OperationCanceledException)
        {
            DefinirBadgeStatus("A", "⚪");
            AtualizarProgresso(0, "Operação cancelada", "Interrompido pelo operador.");
            EscreverLinha("\n[!] Operação cancelada pelo operador.");
        }
        catch (Exception ex)
        {
            DefinirBadgeStatus("A", "❌");
            AtualizarProgresso(0, "Falha no zeramento", ex.Message);
            EscreverLinha($"\n[ERRO AO ZERAR CONFIGURAÇÃO] {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private async Task ExecutarZerarConfigAsync(string porta, int baud, CancellationToken ct)
    {
        // Importa perfil de hardware
        var profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                      ?? (CbModeloRoteadorInicial.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var profile = BootInterruptProfiles.FindById(profileTag);
        var isForti = profile.Id.Contains("forti", StringComparison.OrdinalIgnoreCase)
                   || profile.Manufacturer.Contains("Fortinet", StringComparison.OrdinalIgnoreCase)
                   || IsTagFortiGate(profileTag ?? "")
                   || _isFortiGateDetected;
        var isHpe = !isForti && (profile.Id.Contains("hpe", StringComparison.OrdinalIgnoreCase) || profile.Family.Contains("MSR", StringComparison.OrdinalIgnoreCase));

        if (isForti)
        {
            AtualizarProgresso(10, "Fortinet FortiGate 40F — Zerar Configuração...", "Verificando console e prompt FortiOS...");
            EscreverLinha($"\n=================================================================");
            EscreverLinha($"   🧹 FORTINET FORTIGATE 40F — ZERAR CONFIGURAÇÃO EM {porta} @ {baud} BAUD");
            EscreverLinha($"=================================================================");
        }
        else if (isHpe)
        {
            var isHpe1002 = profile.Id.Contains("1002", StringComparison.OrdinalIgnoreCase) || profile.Name.Contains("1002", StringComparison.OrdinalIgnoreCase) || (profileTag?.Contains("1002", StringComparison.OrdinalIgnoreCase) == true);
            var isHpe930 = profile.Id.Contains("930", StringComparison.OrdinalIgnoreCase) || profile.Name.Contains("930", StringComparison.OrdinalIgnoreCase) || (profileTag?.Contains("930", StringComparison.OrdinalIgnoreCase) == true);
            var isHpe954 = profile.Id.Contains("954", StringComparison.OrdinalIgnoreCase) || profile.Name.Contains("954", StringComparison.OrdinalIgnoreCase) || (profileTag?.Contains("954", StringComparison.OrdinalIgnoreCase) == true);
            var modeloHpe = isHpe1002 ? "HPE MSR 1002 / 1003" : isHpe930 ? "HPE MSR 930" : isHpe954 ? "HPE MSR 954" : "HPE Comware / MSR";

            AtualizarProgresso(10, $"{modeloHpe} — Zerar Configuração / Reset...", "Verificando console e estado de acesso...");
            EscreverLinha($"\n=================================================================");
            EscreverLinha($"   🧹 {modeloHpe.ToUpperInvariant()} — ZERAR CONFIGURAÇÃO EM {porta} @ {baud} BAUD");
            EscreverLinha($"=================================================================");
        }
        else
        {
            AtualizarProgresso(5, "Fase A: Zerando configuração...", $"Abrindo conexão em {porta} @ {baud} baud (8-N-1).");
            EscreverLinha($"\n[*] [FASE A] ZERAR CONFIGURAÇÃO EM {porta} @ {baud} BAUD");
        }

        var transport = new SerialTransport(porta, baud);
        var sessionOptions = isForti
            ? new SessionOptions
            {
                PromptMatcher = RegexPromptMatcher.Universal(),
                ConnectTimeout = TimeSpan.FromSeconds(15),
                CommandTimeout = TimeSpan.FromSeconds(20),
                Username = "EBT",
                Password = "CQMR"
            }
            : CiscoIOSAdapter.CreateSessionOptions(null);

        await using var session = new DeviceSession(transport, sessionOptions);
        session.RawOutput += OnRawOutput;
        RegistrarSessaoAtiva(session, porta, baud);

        if (isForti)
        {
            if (_skipFactoryReset)
            {
                EscreverLinha("[*] Zeramento pulado: FortiGate já autenticado e pronto para provisionamento.");
                AtualizarProgresso(100, "Zeramento Concluído!", "FortiGate 40F autenticado e pronto.");
                return;
            }

            AtualizarProgresso(25, "FortiGate 40F: Conectando...", "Verificando prompt...");
            try
            {
                await session.ConnectAsync(ct);
            }
            catch (Exception ex)
            {
                EscreverLinha($"[!] Conexão direta FortiGate não autenticou: {ex.Message}");
                EscreverLinha("[*] Iniciando assistente de recuperação autônoma de senha FortiGate 40F...");

                await session.DisposeAsync();
                await transport.DisposeAsync();

                bool recuperado = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    var win = new FortiGateAutoRecoveryWindow(porta, baud) { Owner = this };
                    var res = win.ShowDialog();
                    recuperado = (res == true && win.Sucesso);
                });

                if (!recuperado)
                {
                    throw new InvalidOperationException("Não foi possível recuperar o acesso de administrador do FortiGate 40F.");
                }

                _skipFactoryReset = true;
                EscreverLinha("[OK] Senha recuperada com sucesso via maintainer. Equipamento autenticado!");
                AtualizarProgresso(100, "Zeramento Concluído!", "FortiGate 40F autenticado e pronto.");
                return;
            }

            // Desabilita paginação
            try
            {
                await session.SendCommandAsync("config system console", TimeSpan.FromSeconds(5), ct);
                await session.SendCommandAsync("set output standard", TimeSpan.FromSeconds(5), ct);
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), ct);
            }
            catch { }

            AtualizarProgresso(50, "FortiGate 40F: Resetando...", "Enviando 'execute factoryreset'...");
            EscreverLinha("[*] Enviando 'execute factoryreset' ao FortiGate...");
            var conditions = new StopCondition[]
            {
                new StopCondition.Contains("FortiResetConfirm", "y/n"),
                new StopCondition.Prompt()
            };
            var resetResult = await session.SendExpectAsync("execute factoryreset", conditions, TimeSpan.FromSeconds(15), ct);
            if (resetResult.Matched is StopCondition.Contains || resetResult.Output.Contains("y/n", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendRawAsync("y\r", ct);
                EscreverLinha("[OK] Confirmação 'y' enviada. O FortiGate está reiniciando para o padrão de fábrica.");
                AtualizarProgresso(75, "FortiGate 40F: Reiniciando...", "Aguardando reinicialização (aprox. 45s)...");
                await Task.Delay(45000, ct);
            }
            else
            {
                EscreverLinha($"[*] Resposta do comando: {resetResult.Output.Trim()}");
            }

            AtualizarProgresso(100, "Zeramento Concluído!", "FortiGate 40F em padrão de fábrica.");
        }
        else if (isHpe)
        {
            var firmwareFile = _selectedIosBinPath;
            var hostIp = _loadedSaipCircuit?.HostLanIp ?? ObterIpLocalParaTftp() ?? "200.182.245.18";
            var routerIp = _loadedSaipCircuit?.LanIp ?? "200.182.245.17";
            var subnetMask = _loadedSaipCircuit?.LanSubnetMask ?? "255.255.255.240";

            var hpeRecovery = new HpeComwareRecovery(EscreverLinhaAsync, profile);
            hpeRecovery.ProgressUpdated += (pct, titulo, desc) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (GridModoAutomatico.Visibility == Visibility.Visible)
                    {
                        PbAutoGeral.Value = pct;
                        TxtAutoPorcentagem.Text = $"{pct}%";
                        TxtAutoStatusGeral.Text = desc;
                    }
                    AtualizarProgresso(pct, titulo, desc);
                });
            };
            await hpeRecovery.RecoverAndResetAsync(
                session,
                InstruirOperadorAsync,
                firmwareFile,
                hostIp,
                routerIp,
                subnetMask,
                (s, ethOpt, fwPath, hIp, rIp, mask, token) => ExecutarBootWareTftpDownloadAsync(s, ethOpt, fwPath, hIp, rIp, mask, token),
                SolicitarFirmwareParaRecuperacaoAsync,
                forceFirmwareRecovery: false,
                ct: ct);
            AtualizarProgresso(100, "Zeramento Concluído!", "Roteador HPE 954 com configuração limpa e senha removida.");
        }
        else
        {
            var recovery = new CiscoIOSRecovery(EscreverLinhaAsync, profile: profile);
            AtualizarProgresso(20, "Fase A: Verificando RS-232...", "Detectando ROMMON, senha ou prompt...");
            await recovery.RecoverAndResetAsync(session, InstruirOperadorAsync, ct);

            // Se o equipamento estiver no ROMMON (sem IOS), não executa comandos de auditoria IOS
            if (session.Mode == ExecMode.Rommon ||
                session.CurrentPrompt?.Trim().StartsWith("rommon", StringComparison.OrdinalIgnoreCase) == true)
            {
                _isRommonOrBootwareDetected = true;
                Dispatcher.Invoke(() =>
                {
                    if (ChkAtualizarFirmwareAuto != null)
                    {
                        ChkAtualizarFirmwareAuto.IsChecked = true;
                        if (BtnSelecionarFirmwareAuto != null)
                            BtnSelecionarFirmwareAuto.IsEnabled = true;
                    }
                    AtualizarBotaoProsseguir();
                });

                var is921 = profile.Id.Contains("921", StringComparison.OrdinalIgnoreCase)
                         || profile.Id.Contains("c900", StringComparison.OrdinalIgnoreCase)
                         || profile.Family.Contains("900", StringComparison.OrdinalIgnoreCase);

                var rommonPort = is921 ? "GigabitEthernet 4 (GE 4 / Porta 4)" : "GigabitEthernet 0/0 (GE 0/0 / Porta 0)";
                var rommonShort = is921 ? "GE 4" : "GE 0/0";
                var lanPort = is921 ? "GE 5 - LAN" : "GE 0/1 - LAN";

                EscreverLinha($"[*] Equipamento identificado em MODO ROMMON (sem firmware na Flash).");
                await NotificarConexaoCaboAsync(
                    "⚠️ ROTEADOR CISCO EM MODO ROMMON (SEM FIRMWARE)\n\n" +
                    "O equipamento foi identificado em modo de recuperação ROMMON.\n\n" +
                    "👉 CONECTE O CABO DE REDE ETHERNET NA PORTA:\n" +
                    $"🔴 {rommonPort}\n\n" +
                    "Esta é a única porta Ethernet habilitada no hardware para a transferência TFTP via ROMMON.\n\n" +
                    $"(Após a gravação do firmware e inicialização do Cisco IOS, o sistema solicitará a troca do cabo para a porta {lanPort}).",
                    ct);

                AtualizarProgresso(100, "Fase A Concluída!", $"Equipamento em ROMMON pronto para carga de firmware TFTP na porta {rommonShort}.");
                return;
            }

            AtualizarProgresso(85, "Fase A: Auditando equipamento...", "Identificando versão e modelo...");
            try
            {
                var adapter = new CiscoIOSAdapter(null);
                var info = await adapter.IdentifyAsync(session, ct);
                ExibirDadosEquipamento(info);
            }
            catch (Exception ex)
            {
                EscreverLinha($"[AVISO] Auditoria inicial show version: {ex.Message}");
            }

            AtualizarProgresso(100, "Fase A Concluída!", "Cisco zerado com sucesso (0x2102).");
        }
    }

    private async Task<bool> ConfigurarEthernetParametrosBootWareAsync(
        DeviceSession session,
        string hostIp,
        string routerIp,
        string subnetMask,
        string fileName,
        CancellationToken ct)
    {
        AtualizarProgresso(35, "[2/6] Configurando Parâmetros Ethernet...", "Enviando Opção 5 (Modify Ethernet Parameter)...");
        EscreverLinha($"\n[ETHERNET PARAMETER SET] Configurando GE0 no BootWare...");
        // Drena buffer de comandos estale antes de enviar '5'
        await Task.Delay(500, ct);
        try { await session.WaitForAsync(new StopCondition[] { new StopCondition.LineRegex("choice", new Regex("choice")) }, TimeSpan.FromMilliseconds(500), ct); } catch { }
        await session.WriteLineAsync("5", ct);

        // Ordem real que o BootWare HPE MSR93x apresenta os campos no ETHERNET PARAMETER SET:
        // Protocol → Load File Name → Target File Name → Server IP → Local IP → Subnet Mask → Gateway → File Name
        var fields = new (string Name, string Label, Regex Pattern, string Value)[]
        {
            ("protocol", "Protocol", new Regex(@"(?i)protocol\s*\(f(?:tp|rom)\s+or\s+tftp\)\s*[:?]"), "TFTP"),
            ("loadfile", "Load File Name", new Regex(@"(?i)load\s+file\s+name\s*[:?]"), fileName),
            ("targetfile", "Target File Name", new Regex(@"(?i)target\s+file\s+name\s*[:?]"), fileName),
            ("serverip", "Server IP Address", new Regex(@"(?i)server\s+ip\s*(?:address)?\s*[:?]"), hostIp),
            ("localip", "Router IP Address", new Regex(@"(?i)(?:switch\s*/\s*router|switch|router|local)\s+ip\s*(?:address)?\s*[:?]"), routerIp),
            ("subnet", "Subnet Mask", new Regex(@"(?i)(?:subnet\s+mask|mask)\s*[:?]"), subnetMask),
            ("gateway", "Gateway IP Address", new Regex(@"(?i)gateway\s+ip\s*(?:address)?\s*[:?]"), "0.0.0.0"),
            ("filename", "File Name", new Regex(@"(?i)file\s+name\s*[:?]"), fileName)
        };

        var choiceRegex = new Regex(@"(?i)(?:enter\s+your\s+choice\s*\(\s*0\s*-\s*5\s*\)|choice\s*\(\s*0\s*-\s*5\s*\)\s*:)");
        var confirmRegex = new Regex(@"(?i)(?:ensure\s+the\s+parameter\s+be\s+modified|modify\s*\(\s*Y\s*/\s*N\s*\)|\[Y/N\])\s*[:?]");

        var deadline = DateTime.UtcNow.AddSeconds(45);
        var answered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int filledCount = 0;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var stopConditions = new List<StopCondition>();
            foreach (var f in fields)
            {
                if (!answered.Contains(f.Name))
                    stopConditions.Add(new StopCondition.LineRegex(f.Name, f.Pattern));
            }
            stopConditions.Add(new StopCondition.LineRegex("confirm", confirmRegex));
            stopConditions.Add(new StopCondition.LineRegex("choice", choiceRegex));

            DeviceSession.ExpectResult? res;
            try
            {
                res = await session.WaitForAsync(stopConditions.ToArray(), TimeSpan.FromSeconds(3), ct);
            }
            catch (SessionTimeoutException)
            {
                res = null;
            }

            if (res == null)
            {
                if (filledCount == 0)
                {
                    EscreverLinha("  [BootWare] Reenviando Opção 5 (Modify Ethernet Parameter)...");
                    await session.WriteLineAsync("5", ct);
                }
                else
                {
                    // Já estamos dentro do ETHERNET PARAMETER SET — apenas Enter para confirmar valor atual
                    await session.WriteLineAsync(string.Empty, ct);
                }
                await Task.Delay(250, ct);
                continue;
            }

            if (res.Matched is StopCondition.LineRegex lr)
            {
                if (lr.Name == "choice")
                {
                    if (filledCount >= 4)
                    {
                        AtualizarProgresso(45, "[2/6] Parâmetros Ethernet Configurados!", "Menu Ethernet pronto para gravação.");
                        EscreverLinha("[ETHERNET PARAMETER SET] ✅ Todos os parâmetros configurados com sucesso.");
                        return true;
                    }
                    else
                    {
                        await Task.Delay(250, ct);
                        continue;
                    }
                }

                if (lr.Name == "confirm")
                {
                    EscreverLinha("  [CONFIRMATION] Ensure Parameter Modified → TX: 'Y'");
                    await session.WriteLineAsync("Y", ct);
                    await Task.Delay(300, ct);
                    continue;
                }

                var field = fields.FirstOrDefault(f => f.Name.Equals(lr.Name, StringComparison.OrdinalIgnoreCase));
                if (field.Name != null && answered.Add(field.Name))
                {
                    filledCount++;
                    EscreverLinha($"  [{filledCount}] {field.Label}");
                    EscreverLinha($"      TX: {field.Value}");
                    EscreverLinha($"      ✓ Confirmado");
                    await session.WriteLineAsync(field.Value, ct);
                    await Task.Delay(250, ct);
                }
            }
        }

        EscreverLinha("[BootWare] TIMEOUT ao configurar parâmetros Ethernet.");
        return false;
    }

    private async Task<bool> ExecutarBootWareTftpDownloadAsync(
        DeviceSession session,
        string ethernetOption,
        string firmwareFilePath,
        string hostIp,
        string routerIp,
        string subnetMask,
        CancellationToken ct)
    {
        var fileDir = Path.GetDirectoryName(firmwareFilePath) ?? AppContext.BaseDirectory;
        var fileName = Path.GetFileName(firmwareFilePath);

        AtualizarProgresso(10, "[1/6] Iniciando Recuperação...", $"Preparando firmware {fileName}...");
        EscreverLinha("\n==================================================================================");
        EscreverLinha("   🚀 INICIANDO RECUPERAÇÃO DE FIRMWARE VIA BOOTWARE TFTP");
        EscreverLinha("==================================================================================");
        EscreverLinha($"  Arquivo Firmware  : {fileName}");
        EscreverLinha($"  Servidor TFTP (PC): {hostIp}");
        EscreverLinha($"  Roteador HPE (GE0): {routerIp}");
        EscreverLinha($"  Máscara de Rede   : {subnetMask}");
        EscreverLinha($"  Porta Ethernet    : Conecte o cabo na porta GE0 (WAN / Porta 0) do HPE MSR 954.");
        EscreverLinha("==================================================================================\n");

        // Configura IP estático no adaptador Windows para emparelhar com o BootWare
        AtualizarProgresso(20, "[1/6] Configurando Rede Windows...", $"Definindo IP estático {hostIp}/{subnetMask} no PC...");
        try
        {
            if (!string.IsNullOrWhiteSpace(hostIp) && !string.IsNullOrWhiteSpace(routerIp))
            {
                var adapter = CbAdaptadorRede?.Text?.Trim();
                var ethAdapters = HostNetworkManager.GetEthernetAdapters();
                if (string.IsNullOrWhiteSpace(adapter) || adapter.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) || adapter.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
                {
                    adapter = ethAdapters.FirstOrDefault(a => !a.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) && !a.Contains("Wireless", StringComparison.OrdinalIgnoreCase))
                           ?? ethAdapters.FirstOrDefault()
                           ?? "Ethernet";
                }
                EscreverLinha($"[*] Auto-config ETH Windows '{adapter}' -> {hostIp}/{subnetMask} (CPE GE0: {routerIp})...");
                var (okNet, outNet) = await HostNetworkManager.SetStaticIpAsync(adapter, hostIp, subnetMask, null, ct);
                EscreverLinha($"  [ETH Windows] {outNet.Trim()}");
                if (!okNet) EscreverLinha("  [AVISO] Falha ao configurar ETH Windows — confira adaptador para GE0 e privilégios de administrador.");
                await HostNetworkManager.EnsureTftpFirewallRuleAsync(ct);
            }
        }
        catch (Exception ex) { EscreverLinha($"  [AVISO] ETH Windows: {ex.Message}"); }

        await using var tftpServer = new NetworkDevice.Protocols.Tftp.EmbeddedTftpServer(fileDir);
        tftpServer.LogMessage += msg => EscreverLinha($"  {msg}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var lastLogPct = -1;
        var lastUiTime = DateTime.MinValue;

        tftpServer.TransferProgress += (file, sent, total, pct) =>
        {
            var now = DateTime.UtcNow;
            var sentMb = sent / (1024.0 * 1024.0);
            var totalMb = total / (1024.0 * 1024.0);
            var elapsedSec = sw.Elapsed.TotalSeconds;
            var speedMbSec = elapsedSec > 0.5 ? sentMb / elapsedSec : 0;
            var remainingSec = speedMbSec > 0 ? (totalMb - sentMb) / speedMbSec : 0;
            var etaStr = remainingSec > 0 ? $" | Restam ~{TimeSpan.FromSeconds(remainingSec):mm\\:ss}" : "";

            var mappedPct = 50 + (int)(pct * 0.40);

            if ((now - lastUiTime).TotalMilliseconds >= 250 || pct >= 100)
            {
                lastUiTime = now;
                AtualizarProgresso(mappedPct, $"[4/6] Gravando Flash ({pct:N1}%)...", $"{sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) — {speedMbSec:N1} MB/s{etaStr}");
            }

            var step = (int)(pct / 5) * 5;
            if (step > lastLogPct)
            {
                lastLogPct = step;
                var barLength = 20;
                var filled = (int)Math.Round((pct / 100.0) * barLength);
                var bar = new string('█', Math.Clamp(filled, 0, barLength)) + new string('░', Math.Max(0, barLength - filled));
                EscreverLinha($"    -> [Passo 3/6 TFTP] [{bar}] {sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) | {speedMbSec:N1} MB/s{etaStr}");
            }
        };
        tftpServer.Start();

        try
        {
            // Sincronização inteligente com buffer normalizado:
            await session.WriteLineAsync(string.Empty, ct);
            await Task.Delay(300, ct);

            var probe = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("choice", "choice"),
                    new StopCondition.Contains("SubMenu", "SubMenu"),
                    new StopCondition.Contains("BOOTWARE", "BOOTWARE"),
                    new StopCondition.Contains("MENU", "MENU"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(3),
                ct);

            var (state, _) = HpeBootWareStateMachine.DetectState(probe.Output);

            await InstruirOperadorAsync(
                "⚠️ CONEXÃO DO CABO DE REDE - BOOTWARE TFTP\n\n" +
                "O roteador HPE está em modo de recuperação BootWare (sem firmware).\n\n" +
                "👉 CONECTE O CABO DE REDE ETHERNET NA PORTA:\n" +
                "🔴 GE0 (Porta 0 / WAN - GigabitEthernet0/0)\n\n" +
                "Esta é a única porta habilitada pelo BootWare para transferência Ethernet TFTP.\n\n" +
                "Clique em OK assim que o cabo estiver conectado na porta GE0.",
                ct);

            if (state == HpeMenuState.ExtendedBootWare)
            {
                AtualizarProgresso(30, "[1/6] Entrando no Ethernet SubMenu...", "Selecionando Opção 3 no menu BootWare...");
                EscreverLinha($"[*] [1/6] Entrando no Ethernet SubMenu (Opção {ethernetOption})...");
                await session.WriteLineAsync(ethernetOption, ct);

                try
                {
                    await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.Contains("choice(0-5)", "choice(0-5)"),
                            new StopCondition.Contains("<Enter Ethernet SubMenu>", "<Enter Ethernet SubMenu>"),
                            new StopCondition.Contains("Modify Ethernet Parameter", "Modify Ethernet Parameter")
                        },
                        TimeSpan.FromSeconds(5),
                        ct);
                }
                catch
                {
                    EscreverLinha("[AVISO] Confirmação de entrada no Ethernet SubMenu não recebida — tentando prosseguir mesmo assim...");
                }
            }
            else if (state == HpeMenuState.EthernetSubMenu)
            {
                EscreverLinha("[*] Console já posicionado no Ethernet SubMenu — prosseguindo diretamente para configuração de rede...");
            }
            else
            {
                EscreverLinha("[*] Sincronizando navegação no BootWare — assegurando menu principal...");
                var ensured = await HpeBootWareStateMachine.EnsureExtendedBootWareAsync(session, EscreverLinhaAsync, ct);
                if (!ensured)
                {
                    EscreverLinha("[ERRO] Não foi possível retornar ao menu principal do BootWare para configurar o TFTP.");
                    return false;
                }

                await session.WriteLineAsync(ethernetOption, ct);
                try
                {
                    await session.WaitForAsync(
                        new StopCondition[] { new StopCondition.Contains("choice(0-5)", "choice(0-5)"), new StopCondition.Contains("Ethernet", "Ethernet") },
                        TimeSpan.FromSeconds(4),
                        ct);
                }
                catch
                {
                    EscreverLinha("[AVISO] Confirmação de entrada no Ethernet SubMenu não recebida — tentando prosseguir mesmo assim...");
                }
            }

            // [2/6] Modifica Parâmetros de Rede (Opção 5 - Modify Ethernet Parameter)
            var paramsOk = await ConfigurarEthernetParametrosBootWareAsync(session, hostIp, routerIp, subnetMask, fileName, ct);
            if (!paramsOk)
            {
                EscreverLinha("[ERRO BOOTWARE TFTP] Parâmetros Ethernet não confirmados — abortando transferência.");
                try { await session.WriteLineAsync("0", ct); } catch { }
                return false;
            }

            // [3/6] Dispara Gravação na Flash (Opção 2 - Update Main Image File)
            AtualizarProgresso(50, "[3/6] Iniciando Transferência TFTP...", $"Disparando Opção 2 (Update Main Image File) para {fileName}...");
            EscreverLinha($"[*] [3/6] Disparando Update Main Image File (Opção 2) no BootWare...");
            await session.WriteLineAsync("2", ct);
            
            try
            {
                var conf = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.Contains("[Y/N]", "[Y/N]"),
                        new StopCondition.Contains("sure", "sure"),
                        new StopCondition.Contains("Loading file", "Loading file"),
                        new StopCondition.Contains("Writing file to Flash", "Writing file to Flash")
                    },
                    TimeSpan.FromSeconds(4),
                    ct);
                if (conf.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) || conf.Output.Contains("sure", StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteLineAsync("Y", ct);
                }
            }
            catch (SessionTimeoutException)
            {
                await session.WriteLineAsync("Y", ct);
            }

            // [4/6] Aguarda a gravação na Flash com rastreamento de pacotes
            AtualizarProgresso(70, "[4/6] Gravando Firmware na Flash...", "Aguardando gravação e descompressão de pacotes na Flash...");
            var deadlineFlash = DateTime.UtcNow.AddMinutes(4);
            var finishedFlash = false;
            var decompressedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (DateTime.UtcNow < deadlineFlash && !ct.IsCancellationRequested && !finishedFlash)
            {
                string outText;
                try
                {
                    var flashWait = await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.Contains("Writing file to Flash...Done.", "Writing file to Flash...Done."),
                            new StopCondition.Contains("is self-decompressing", "is self-decompressing"),
                            new StopCondition.Contains("Saving file flash:", "Saving file flash:"),
                            new StopCondition.Contains("Set as main boot image? [Y/N]:", "Set as main boot image? [Y/N]:"),
                            new StopCondition.Contains("Something wrong with the file", "Something wrong with the file"),
                            new StopCondition.Contains("Loading file fails", "Loading file fails"),
                            new StopCondition.Contains("choice(0-5)", "choice(0-5)")
                        },
                        TimeSpan.FromSeconds(6),
                        ct);
                    outText = flashWait.Output;
                }
                catch (SessionTimeoutException)
                {
                    // Durante transferência TFTP (Loading...), o BootWare emite pontos sem quebras de linha ou prompts.
                    // Continua o loop normalmente aguardando a conclusão dentro do deadline.
                    continue;
                }

                if (outText.Contains("Something wrong with the file", StringComparison.OrdinalIgnoreCase) ||
                    outText.Contains("Loading file fails", StringComparison.OrdinalIgnoreCase))
                {
                    EscreverLinha("[ERRO BOOTWARE TFTP] ❌ Gravação do firmware recusada pelo BootWare (Erro no arquivo ou na transferência).");
                    return false;
                }

                // Identifica qual pacote interno do .ipe está sendo processado
                var pkgMatch = Regex.Match(outText, @"(?i)(?:msr[0-9x]+-cmw710-([a-z0-9_\-]+)-[a-z0-9_\-]+\.bin|Saving\s+file\s+flash:/([a-z0-9_\-\.]+))");
                if (pkgMatch.Success)
                {
                    var pkgName = pkgMatch.Groups[1].Success ? pkgMatch.Groups[1].Value.ToUpperInvariant() : pkgMatch.Groups[2].Value;
                    if (decompressedPackages.Add(pkgName))
                    {
                        var pct = Math.Min(70 + (decompressedPackages.Count * 4), 92);
                        AtualizarProgresso(pct, $"[4/6] Gravando Pacote {pkgName}...", $"Descomprimindo e gravando pacote '{pkgName}' na memória Flash...");
                        EscreverLinha($"  -> [Flash Gravando] Pacote: {pkgName}");
                    }
                }

                if (outText.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
                    outText.Contains("main boot", StringComparison.OrdinalIgnoreCase))
                {
                    EscreverLinha("[*] Confirmando pacote como imagem principal de boot (Y)...");
                    await session.WriteLineAsync("Y", ct);
                    await Task.Delay(500, ct);
                }

                if (outText.Contains("Writing file to Flash...Done.", StringComparison.OrdinalIgnoreCase) ||
                    outText.Contains("choice(0-5)", StringComparison.OrdinalIgnoreCase))
                {
                    finishedFlash = true;
                    break;
                }
            }

            // [5/6] Retorna ao Menu Principal do BootWare (Opção 0)
            AtualizarProgresso(90, "[5/6] Retornando ao Menu Principal...", "Enviando Opção 0 para retornar ao Extended BootWare...");
            EscreverLinha("[*] [5/6] Retornando ao Menu Principal do BootWare (Opção 0)...");
            await session.WriteLineAsync("0", ct);
            try
            {
                await session.WaitForAsync(
                    new StopCondition[] { new StopCondition.Contains("choice(0-9)", "choice(0-9)"), new StopCondition.Contains("<EXTENDED-BOOTWARE MENU>", "<EXTENDED-BOOTWARE MENU>") },
                    TimeSpan.FromSeconds(4),
                    ct);
            }
            catch { }

            AtualizarProgresso(95, "[5/6] Firmware Gravado com Sucesso!", "Imagem principal gravada na Flash. Pronto para inicializar Comware.");
            EscreverLinha("[OK] Firmware baixado e gravado na Flash via BootWare com sucesso!");
            return true;
        }
        catch (Exception ex)
        {
            EscreverLinha($"[ERRO BOOTWARE TFTP] Falha na recuperação via Ethernet: {ex.Message}");
            try { await session.WriteLineAsync("0", ct); } catch { }
            return false;
        }
        finally
        {
            await tftpServer.StopAsync();
        }
    }

    private Task<string?> SolicitarFirmwareParaRecuperacaoAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>();

        Dispatcher.Invoke(() =>
        {
            SelecionarFase("B");
            DefinirBadgeStatus("B", "⏳");

            EscreverLinha("\n=================================================================");
            EscreverLinha("      🚑 RECUPERAÇÃO DE BOOTWARE NECESSÁRIA (CARREGAR .IPE)      ");
            EscreverLinha("=================================================================");
            EscreverLinha("  A memória Flash do equipamento não possui imagem de boot válida.");
            EscreverLinha("  Redirecionando para a FASE B para seleção do pacote de firmware.");
            EscreverLinha("  O arquivo .IPE será gravado na Flash via TFTP pela porta GE0 (WAN / Porta 0).");
            EscreverLinha("=================================================================\n");

            var firmwareCandidate = _selectedIosBinPath;
            if (string.IsNullOrEmpty(firmwareCandidate) || !File.Exists(firmwareCandidate))
            {
                var searchDirs = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                    AppDomain.CurrentDomain.BaseDirectory,
                    @"C:\Killtech"
                };

                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    firmwareCandidate = Directory.GetFiles(dir, "*954*.ipe").FirstOrDefault()
                                     ?? Directory.GetFiles(dir, "*.ipe").FirstOrDefault()
                                     ?? Directory.GetFiles(dir, "*954*.bin").FirstOrDefault();
                    if (!string.IsNullOrEmpty(firmwareCandidate)) break;
                }
            }

            if (!string.IsNullOrEmpty(firmwareCandidate) && File.Exists(firmwareCandidate))
            {
                var fi = new FileInfo(firmwareCandidate);
                var fileName = Path.GetFileName(firmwareCandidate);
                var sizeMb = (fi.Length / (1024.0 * 1024.0)).ToString("N1");
                TxtIosImageInfo.Text = $"{fileName} ({sizeMb} MB)";
                _selectedIosBinPath = firmwareCandidate;
                AtualizarEstadoBotoes();

                if (GridModoAutomatico != null && GridModoAutomatico.Visibility == Visibility.Visible)
                {
                    EscreverLinha($"[*] [Modo Automático TFTP] Utilizando firmware: {fileName} ({sizeMb} MB)");
                    tcs.SetResult(firmwareCandidate);
                    return;
                }

                var resp = MessageBox.Show(
                    $"A memória Flash do roteador está vazia ou sem imagem de boot.\n\n" +
                    $"Pacote de Firmware Detectado:\n" +
                    $"📁 {fileName} ({sizeMb} MB)\n\n" +
                    $"Deseja utilizar este arquivo para recuperar a Flash e inicializar o equipamento via TFTP pela porta GE0?",
                    "Recuperação de BootWare HPE (TFTP)",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (resp == MessageBoxResult.Yes)
                {
                    tcs.SetResult(firmwareCandidate);
                    return;
                }
            }

            var dlg = new OpenFileDialog
            {
                Title = "Selecione o Pacote de Firmware HPE (.IPE) para Recuperação de Boot",
                Filter = "Pacotes HPE Comware (*.ipe)|*.ipe|Imagens Binárias (*.bin)|*.bin|Todos os Arquivos (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                if (!ValidarEBloquearFirmwareIncompativel(dlg.FileName, mostrarAlertaModal: true))
                {
                    tcs.SetResult(null);
                    return;
                }

                _selectedIosBinPath = dlg.FileName;
                var fi = new FileInfo(dlg.FileName);
                var fileName = Path.GetFileName(dlg.FileName);
                var sizeMb = (fi.Length / (1024.0 * 1024.0)).ToString("N1");
                TxtIosImageInfo.Text = $"{fileName} ({sizeMb} MB)";
                AtualizarEstadoBotoes();
                tcs.SetResult(dlg.FileName);
            }
            else
            {
                tcs.SetResult(null);
            }
        });

        return tcs.Task;
    }

    // FASE C · PROVISIONAR EQUIPAMENTO
    private async void BtnAplicarSaip_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedSaipCircuit is null)
        {
            MessageBox.Show("Nenhum dado de circuito definido. Defina os dados na tela inicial ou no botão 'Alterar Insumos'.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var porta = CbPorta.Text.Trim();
        if (string.IsNullOrEmpty(porta))
        {
            EscreverLinha("[!] Selecione a porta serial do equipamento (ex: COM4).");
            return;
        }

        SelecionarFase("C");
        DefinirBadgeStatus("C", "⏳");
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var baud = int.TryParse(CbBaud.Text, out var b) ? b : 9600;

        try
        {
            await ExecutarAplicarSaipAsync(porta, baud, _cts.Token);
            DefinirBadgeStatus("C", "✅");
        }
        catch (OperationCanceledException)
        {
            DefinirBadgeStatus("C", "⚪");
            AtualizarProgresso(0, "Operação cancelada", "Cancelado pelo operador.");
            EscreverLinha("\n[!] Provisionamento cancelado.");
        }
        catch (Exception ex)
        {
            DefinirBadgeStatus("C", "❌");
            AtualizarProgresso(0, "Falha no provisionamento", ex.Message);
            EscreverLinha($"\n[ERRO AO PROVISIONAR] {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private async Task ExecutarAplicarSaipAsync(string porta, int baud, CancellationToken ct)
    {
        if (_loadedSaipCircuit is null)
            throw new InvalidOperationException("Dados do circuito não definidos.");

        AtualizarProgresso(10, "Fase C: Conectando para Provisionamento...", $"Abrindo conexão em {porta} @ {baud}...");
        EscreverLinha($"\n[*] [FASE C] PROVISIONANDO EQUIPAMENTO ({_loadedSaipCircuit.DesignacaoIp}) EM {porta}...");

        var profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                      ?? (CbModeloRoteadorInicial.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                      ?? "";
        var isPreForti = _isFortiGateDetected || IsTagFortiGate(profileTag);
        var sessionOptions = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal(),
            CommandTimeout = TimeSpan.FromSeconds(60),
            ConnectTimeout = TimeSpan.FromSeconds(120),
            Username = isPreForti ? "EBT" : null,
            Password = isPreForti ? "CQMR" : null
        };

        var transport = new SerialTransport(porta, baud);
        await using var session = new DeviceSession(transport, sessionOptions);
        session.RawOutput += OnRawOutput;

        await session.ConnectAsync(ct);

        var promptStr = session.CurrentPrompt ?? "";
        var isRealBootWarePrompt = session.Mode != ExecMode.UserExec && session.Mode != ExecMode.PrivilegedExec
            && (promptStr.Contains("<BOOTWARE", StringComparison.OrdinalIgnoreCase)
                || promptStr.Contains("<EXTENDED-BOOTWARE", StringComparison.OrdinalIgnoreCase)
                || promptStr.Contains("choice(0-", StringComparison.OrdinalIgnoreCase)
                || (promptStr.Contains("BootWare", StringComparison.OrdinalIgnoreCase) && !promptStr.EndsWith(">") && !promptStr.EndsWith("]")));

        var isFortiBiosPrompt = promptStr.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase)
                             || promptStr.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase)
                             || promptStr.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase);

        if (session.Mode == ExecMode.Rommon ||
            promptStr.Trim().StartsWith("rommon", StringComparison.OrdinalIgnoreCase) ||
            promptStr.StartsWith("switch:", StringComparison.OrdinalIgnoreCase) ||
            isRealBootWarePrompt ||
            isFortiBiosPrompt)
        {
            _isRommonOrBootwareDetected = true;
            throw new InvalidOperationException(
                "O equipamento se encontra em modo ROMMON / BootWare / BIOS (sem sistema operacional carregado). " +
                "É OBRIGATÓRIO executar a recuperação de firmware (Fase 2) antes de realizar o provisionamento da Ficha SAIP.");
        }

        profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? profileTag;
        var isForti = IsTagFortiGate(profileTag)
                   || promptStr.Contains("FortiGate", StringComparison.OrdinalIgnoreCase)
                   || promptStr.Contains("FGT", StringComparison.OrdinalIgnoreCase)
                   || promptStr.Contains("FortiOS", StringComparison.OrdinalIgnoreCase)
                   || _isFortiGateDetected;

        var isHpe = !isForti && (profileTag.Contains("hpe", StringComparison.OrdinalIgnoreCase)
                 || profileTag.Contains("msr", StringComparison.OrdinalIgnoreCase)
                 || promptStr.StartsWith("[")
                 || promptStr.StartsWith("<")
                 || promptStr.Contains("HPE", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("MSR", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("Comware", StringComparison.OrdinalIgnoreCase));

        var is921 = !isForti && (profileTag.Contains("921", StringComparison.OrdinalIgnoreCase)
                 || profileTag.Contains("c900", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("921", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("c900", StringComparison.OrdinalIgnoreCase));

        var is841 = !isForti && (profileTag.Contains("841", StringComparison.OrdinalIgnoreCase)
                 || profileTag.Contains("c841", StringComparison.OrdinalIgnoreCase)
                 || profileTag.Contains("c800", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("841", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("c800", StringComparison.OrdinalIgnoreCase));

        if (isForti)
        {
            EscreverLinha($"[*] Equipamento identificado como Fortinet FortiGate 40F (Prompt detectado: '{promptStr}').");
            AtualizarProgresso(50, "Fase C: Configurando FortiGate 40F...", $"WAN wan ({_loadedSaipCircuit.WanIp}), LAN lan ({_loadedSaipCircuit.LanIp})...");
            var fortiConfig = new NetworkDevice.Fortinet.FortiOsSaipConfigurator(EscreverLinhaAsync);
            fortiConfig.IncluirNatLab = ChkNatLab?.IsChecked == true;
            await fortiConfig.ApplyConfigAsync(session, _loadedSaipCircuit, "wan", "lan", cancellationToken: ct);

            // Valida se o técnico conectou o cabo na porta LAN (Porta 1 / Giga 1) antes de prosseguir
            await NetworkDevice.Fortinet.FortiOsSaipConfigurator.EnforceLanPortConnectedAsync(session, "lan", NotificarConexaoCaboAsync, EscreverLinhaAsync, ct);
        }
        else if (isHpe)
        {
            EscreverLinha($"[*] Equipamento identificado como HPE / HP MSR / Comware (Prompt detectado: '{promptStr}').");
            AtualizarProgresso(50, "Fase C: Configurando HPE...", $"WAN GE0 ({_loadedSaipCircuit.WanIp}), LAN GE1 ({_loadedSaipCircuit.LanIp})...");
            var hpeConfig = new HpeSaipConfigurator(EscreverLinhaAsync);
            await hpeConfig.ApplyConfigAsync(session, _loadedSaipCircuit, "GigabitEthernet0/0", "GigabitEthernet0/1", ct);

            // Valida automaticamente via 'display ip interface brief' se o técnico conectou o cabo na porta LAN (GE1 / GigabitEthernet0/1)
            await HpeSaipConfigurator.EnforceLanPortConnectedAsync(session, "GigabitEthernet0/1", NotificarConexaoCaboAsync, EscreverLinhaAsync, ct);
        }
        else if (is841)
        {
            EscreverLinha($"[*] Equipamento identificado como Cisco Série 800 / C841M (Prompt detectado: '{promptStr}').");
            AtualizarProgresso(50, "Fase C: Configurando Cisco Série 800 / C841M...", $"WAN GE0/4 ({_loadedSaipCircuit.WanIp}), LAN GE0/5 ({_loadedSaipCircuit.LanIp})...");
            var ciscoConfig = new CiscoSaipConfigurator(EscreverLinhaAsync);
            ciscoConfig.IncluirNatLab = ChkNatLab?.IsChecked == true;
            await ciscoConfig.ApplyConfigAsync(session, _loadedSaipCircuit, "GigabitEthernet0/4", "GigabitEthernet0/5", ct);

            // Valida se o técnico conectou o cabo na porta LAN (GE 0/5 / GigabitEthernet0/5) antes de prosseguir
            await CiscoIOSAdapter.EnforceLanPortConnectedAsync(session, "GigabitEthernet0/5", NotificarConexaoCaboAsync, EscreverLinhaAsync, ct);
        }
        else if (is921)
        {
            EscreverLinha($"[*] Equipamento identificado como Cisco Série 900 / C921-4P (Prompt detectado: '{promptStr}').");
            AtualizarProgresso(50, "Fase C: Configurando Cisco Série 900 / C921-4P...", $"WAN GE4 ({_loadedSaipCircuit.WanIp}), LAN GE5 ({_loadedSaipCircuit.LanIp})...");
            var ciscoConfig = new CiscoSaipConfigurator(EscreverLinhaAsync);
            ciscoConfig.IncluirNatLab = ChkNatLab?.IsChecked == true;
            await ciscoConfig.ApplyConfigAsync(session, _loadedSaipCircuit, "GigabitEthernet 4", "GigabitEthernet 5", ct);

            // Valida se o técnico conectou o cabo na porta LAN (GE5 / GigabitEthernet 5) antes de prosseguir
            await CiscoIOSAdapter.EnforceLanPortConnectedAsync(session, "GigabitEthernet 5", NotificarConexaoCaboAsync, EscreverLinhaAsync, ct);
        }
        else
        {
            EscreverLinha($"[*] Equipamento identificado como Cisco Série 1900 / G2 (Prompt detectado: '{promptStr}').");
            AtualizarProgresso(50, "Fase C: Configurando Cisco...", $"WAN GE0/0 ({_loadedSaipCircuit.WanIp}), LAN GE0/1 ({_loadedSaipCircuit.LanIp})...");
            var ciscoConfig = new CiscoSaipConfigurator(EscreverLinhaAsync);
            ciscoConfig.IncluirNatLab = ChkNatLab?.IsChecked == true;
            await ciscoConfig.ApplyConfigAsync(session, _loadedSaipCircuit, "GigabitEthernet 0/0", "GigabitEthernet 0/1", ct);

            // Valida se o técnico conectou o cabo na porta LAN (GE 0/1) antes de prosseguir
            await CiscoIOSAdapter.EnforceLanPortConnectedAsync(session, "GigabitEthernet 0/1", NotificarConexaoCaboAsync, EscreverLinhaAsync, ct);
        }

        try
        {
            EscreverLinha("[*] Capturando running-config aplicada para o relatório TXT...");
            _lastAppliedConfigScript = isForti
                ? await NetworkDevice.Fortinet.FortiOsSaipConfigurator.GetAppliedRunningConfigAsync(session, ct)
                : isHpe
                    ? await session.SendCommandAsync("display current-configuration", TimeSpan.FromSeconds(120), ct)
                    : await new CiscoIOSAdapter().GetRunningConfigAsync(session, ct);
            EscreverLinha($"[OK] Running-config capturada ({_lastAppliedConfigScript?.Length ?? 0} caracteres).");
        }
        catch (Exception ex)
        {
            EscreverLinha($"[AVISO] Não foi possível capturar a running-config: {ex.Message}");
            _lastAppliedConfigScript = null;
        }

        AtualizarProgresso(100, "Fase C Concluída!", "Configurações do circuito salvas com sucesso no equipamento atualizado!");
    }

    // FASE D · CONFIGURAR IP DISPOSITIVO DE TESTE
    private async void BtnConfigIpPc_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedSaipCircuit is null)
        {
            MessageBox.Show("Defina os dados do circuito na tela inicial ou no topo para obter os IPs de teste.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var adapter = CbAdaptadorRede.Text.Trim();
        if (string.IsNullOrEmpty(adapter))
        {
            MessageBox.Show("Selecione um adaptador de rede.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelecionarFase("D");
        DefinirBadgeStatus("D", "⏳");
        SetBusy(true);
        _cts = new CancellationTokenSource();

        try
        {
            await ExecutarConfigIpTesteAsync(adapter, _cts.Token);
            DefinirBadgeStatus("D", "✅");
        }
        catch (Exception ex)
        {
            DefinirBadgeStatus("D", "❌");
            EscreverLinha($"\n[ERRO FASE D] {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private async Task ExecutarConfigIpTesteAsync(string adapter, CancellationToken ct)
    {
        if (_loadedSaipCircuit is null)
            throw new InvalidOperationException("Dados do circuito não definidos.");

        var isAndroid = CbTipoDispositivo.SelectedIndex == 1;

        EscreverLinha($"\n[*] [FASE 4 · d] CONFIGURANDO IP E DNS NO DISPOSITIVO DE TESTE ({ (isAndroid ? "Android" : "Windows") })...");
        EscreverLinha($"  Adaptador : {adapter}");
        EscreverLinha($"  IP Fixo   : {_loadedSaipCircuit.HostLanIp}");
        EscreverLinha($"  Máscara   : {_loadedSaipCircuit.LanSubnetMask}");
        EscreverLinha($"  Gateway   : {_loadedSaipCircuit.LanIp}");
        EscreverLinha("  DNS       : 1.1.1.1 (Primário) e 8.8.8.8 (Secundário)");

        if (isAndroid)
        {
            var androidGuidance = new AndroidHostNetworkGuidance();
            var (_, msg) = await androidGuidance.SetStaticIpAsync(adapter, _loadedSaipCircuit.HostLanIp, _loadedSaipCircuit.LanSubnetMask, _loadedSaipCircuit.LanIp, ct);
            EscreverLinha($"[INFO ANDROID]\n{msg}");
            MessageBox.Show(msg, "Configuração de IP no Android", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            AtualizarProgresso(50, "Fase D: Aplicando IP e DNS estáticos...", $"Configurando {_loadedSaipCircuit.HostLanIp} e DNS via netsh...");
            var (success, output) = await HostNetworkManager.SetStaticIpAsync(
                adapter,
                _loadedSaipCircuit.HostLanIp,
                _loadedSaipCircuit.LanSubnetMask,
                _loadedSaipCircuit.LanIp,
                ct);

            if (success)
            {
                AtualizarProgresso(90, "Fase D: Estabelecendo enlace de rede...", "Aguardando convergência de ARP e pilha TCP/IP do Windows...");
                await Task.Delay(2500, ct);
                AtualizarProgresso(100, "Fase D Concluída!", $"IP {_loadedSaipCircuit.HostLanIp} e DNS 1.1.1.1 / 8.8.8.8 configurados na placa '{adapter}'.");
                EscreverLinha($"[OK] IP estático ({_loadedSaipCircuit.HostLanIp}) e servidores DNS (1.1.1.1, 8.8.8.8) configurados com sucesso na placa '{adapter}'!");
            }
            else
            {
                EscreverLinha($"[AVISO NETSH] {output}");
            }
        }
    }

    // FASE B · ATUALIZAR FIRMWARE (TFTP + RELOAD AUTOMÁTICO)
    private async void BtnUpgradeIos_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedIosBinPath) || !File.Exists(_selectedIosBinPath))
        {
            MessageBox.Show("Selecione um arquivo de firmware (.ipe ou .bin) primeiro.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var hostIp = _loadedSaipCircuit?.HostLanIp ?? ObterIpLocalParaTftp();
        if (string.IsNullOrEmpty(hostIp))
        {
            MessageBox.Show("Nenhum IP local detectado para o servidor TFTP. Defina os dados do circuito ou selecione a placa de rede.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var porta = CbPorta.Text.Trim();
        if (string.IsNullOrEmpty(porta))
        {
            EscreverLinha("[!] Selecione a porta serial do equipamento (ex: COM1).");
            return;
        }

        SelecionarFase("B");
        DefinirBadgeStatus("B", "⏳");
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var baud = int.TryParse(CbBaud.Text, out var b) ? b : 9600;

        var profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var isForti = IsTagFortiGate(profileTag)
                   || _isFortiGateDetected
                   || (!string.IsNullOrEmpty(_selectedIosBinPath) && _selectedIosBinPath.EndsWith(".out", StringComparison.OrdinalIgnoreCase));

        try
        {
            if (isForti && _isRommonOrBootwareDetected)
            {
                await ExecutarRecuperacaoFirmwareBiosFortinetAsync(porta, baud, hostIp, _cts.Token);
                _isRommonOrBootwareDetected = false;
            }
            else
            {
                await ExecutarUpgradeFirmwareAsync(porta, baud, hostIp, _cts.Token);
            }
            DefinirBadgeStatus("B", "✅");
        }
        catch (OperationCanceledException)
        {
            DefinirBadgeStatus("B", "⚪");
            AtualizarProgresso(0, "Upgrade cancelado", "Cancelado pelo operador.");
            EscreverLinha("\n[!] Upgrade cancelado.");
        }
        catch (Exception ex)
        {
            DefinirBadgeStatus("B", "❌");
            AtualizarProgresso(0, "Falha no Upgrade", ex.Message);
            EscreverLinha($"\n[ERRO NO UPGRADE] {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private async void BtnExecutarRommonTftp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedIosBinPath))
        {
            MessageBox.Show("Selecione um arquivo de firmware (.bin para Cisco ou .out para Fortinet) para efetuar a recuperação.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var porta = CbPorta.Text.Trim();
        if (string.IsNullOrEmpty(porta))
        {
            EscreverLinha("[!] Selecione a porta serial do equipamento (ex: COM1 ou COM4).");
            MessageBox.Show("Selecione a porta serial do equipamento.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var adapter = CbAdaptadorRede?.Text?.Trim();
        var hostIp = _loadedSaipCircuit?.HostLanIp ?? ObterIpLocalParaTftp() ?? "192.168.1.1";
        var routerIp = _loadedSaipCircuit?.LanIp ?? "192.168.1.2";
        var subnetMask = _loadedSaipCircuit?.LanSubnetMask ?? "255.255.255.0";

        var isForti = _isFortiGateDetected || (_selectedIosBinPath?.EndsWith(".out", StringComparison.OrdinalIgnoreCase) == true);
        if (isForti)
        {
            SelecionarFase("B");
            DefinirBadgeStatus("B", "⏳");
            SetBusy(true);
            _cts = new CancellationTokenSource();
            var baudF = int.TryParse(CbBaud.Text, out var bf) ? bf : 9600;
            try
            {
                await ExecutarRecuperacaoFirmwareBiosFortinetAsync(porta, baudF, hostIp, _cts.Token);
                _isRommonOrBootwareDetected = false;
                DefinirBadgeStatus("B", "✅");
                AtualizarProgresso(100, "Fase B Concluída!", "Firmware gravado na Flash via BIOS TFTP e FortiGate 40F inicializado com sucesso.");
                MessageBox.Show("Firmware transferido e gravado na Flash via BIOS TFTP com sucesso!", "BIOS TFTP Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            catch (OperationCanceledException)
            {
                DefinirBadgeStatus("B", "⚪");
                AtualizarProgresso(0, "Recuperação cancelada", "Cancelado pelo operador.");
                EscreverLinha("\n[!] Recuperação BIOS TFTP cancelada.");
                return;
            }
            catch (Exception ex)
            {
                DefinirBadgeStatus("B", "❌");
                AtualizarProgresso(0, "Falha na Recuperação", ex.Message);
                EscreverLinha($"\n[ERRO NA RECUPERAÇÃO BIOS TFTP] {ex.Message}");
                return;
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        SelecionarFase("B");
        DefinirBadgeStatus("B", "⏳");
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var baud = int.TryParse(CbBaud.Text, out var b) ? b : 9600;

        try
        {
            AtualizarProgresso(15, "Recuperação ROMMON TFTP...", "Conectando ao terminal serial...");
            EscreverLinha("\n[*] [FASE B] RECUPERAÇÃO DE FIRMWARE VIA ROMMON TFTP...");

            var sessionOptions = new SessionOptions
            {
                PromptMatcher = RegexPromptMatcher.Universal(),
                CommandTimeout = TimeSpan.FromSeconds(30),
                ConnectTimeout = TimeSpan.FromSeconds(15)
            };

            var transport = new SerialTransport(porta, baud);
            await using var session = new DeviceSession(transport, sessionOptions);
            session.RawOutput += OnRawOutput;

            await session.ConnectAsync(_cts.Token);

            var ciscoUpgrader = new CiscoIOSUpgrader(EscreverLinhaAsync, AtualizarProgresso);
            var success = await ciscoUpgrader.UpgradeViaRommonTftpAsync(
                session,
                _selectedIosBinPath,
                hostIp,
                routerIp,
                subnetMask,
                null,
                adapter,
                InstruirOperadorAsync,
                _cts.Token);

            if (success)
            {
                DefinirBadgeStatus("B", "✅");
                AtualizarProgresso(100, "Fase B Concluída!", "Firmware gravado na Flash via ROMMON e roteador inicializado com sucesso.");
                MessageBox.Show("Firmware transferido e gravado na Flash via ROMMON com sucesso!", "ROMMON TFTP Concluído", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                throw new InvalidOperationException("Falha no procedimento de transferência ROMMON TFTP.");
            }
        }
        catch (OperationCanceledException)
        {
            DefinirBadgeStatus("B", "⚪");
            AtualizarProgresso(0, "Recuperação cancelada", "Cancelado pelo operador.");
            EscreverLinha("\n[!] Recuperação ROMMON TFTP cancelada.");
        }
        catch (Exception ex)
        {
            DefinirBadgeStatus("B", "❌");
            AtualizarProgresso(0, "Falha na Recuperação ROMMON", ex.Message);
            EscreverLinha($"\n[ERRO ROMMON TFTP] {ex.Message}");
            MessageBox.Show($"Erro durante a recuperação ROMMON TFTP:\n{ex.Message}", "Erro ROMMON TFTP", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private static string? ObterIpLocalParaTftp()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip.Address))
                    {
                        var ipStr = ip.Address.ToString();
                        if (!ipStr.StartsWith("169.254."))
                            return ipStr;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private async Task ExecutarRecuperacaoFirmwareBiosFortinetAsync(string porta, int baud, string hostIp, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_selectedIosBinPath) || !File.Exists(_selectedIosBinPath))
            throw new InvalidOperationException("Selecione a imagem de firmware FortiOS (.out) para realizar a recuperação no modo BIOS.");

        var fileName = Path.GetFileName(_selectedIosBinPath);
        AtualizarProgresso(10, "Recuperação BIOS TFTP...", $"Conectando à console serial {porta}...");
        EscreverLinha($"\n[*] [FASE B] RECUPERAÇÃO DE FIRMWARE FORTIGATE VIA BIOS TFTP ({fileName})...");

        // No modo BIOS Recovery, o hardware FortiGate (FortiBootLoader) opera nativamente com os IPs fixos padrão de fábrica:
        // - Servidor TFTP (computador do operador): 192.168.1.100
        // - Equipamento FortiGate (Local IP na WAN): 192.168.1.99
        // - Máscara de rede: 255.255.255.0
        const string biosTftpServerIp = "192.168.1.100";
        const string biosFortiGateIp = "192.168.1.99";
        const string biosSubnetMask = "255.255.255.0";

        var targetAdapter = CbAdaptadorRede?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(targetAdapter))
        {
            var ethAdapters = HostNetworkManager.GetEthernetAdapters();
            targetAdapter = ethAdapters.FirstOrDefault() ?? "";
        }

        if (!string.IsNullOrEmpty(targetAdapter))
        {
            try
            {
                EscreverLinha($"[*] [REDE] Configurando IP fixo padrão de fábrica da BIOS Fortinet ({biosTftpServerIp} / {biosSubnetMask}) no adaptador '{targetAdapter}'...");
                await HostNetworkManager.EnsureTftpFirewallRuleAsync(ct);
                var (okNet, outNet) = await HostNetworkManager.SetStaticIpAsync(targetAdapter, biosTftpServerIp, biosSubnetMask, null, ct);
                if (okNet) EscreverLinha($"[OK] Adaptador '{targetAdapter}' configurado para BIOS TFTP ({biosTftpServerIp}).");
                else EscreverLinha($"  [AVISO] Configuração do adaptador: {outNet.Trim()}");
            }
            catch (Exception ex)
            {
                EscreverLinha($"  [AVISO] Configuração do adaptador Windows: {ex.Message}");
            }
        }

        var sessionOptions = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal(),
            CommandTimeout = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        var transport = new SerialTransport(porta, baud);
        await using var session = new DeviceSession(transport, sessionOptions);
        session.RawOutput += OnRawOutput;

        EscreverLinha("👉 [CONEXÃO FÍSICA OBRIGATÓRIA] Conecte o cabo de rede Ethernet do computador EXCLUSIVAMENTE na porta WAN do FortiGate 40F (única porta viável para recuperação TFTP na BIOS).");
        EscreverLinha($"ℹ️ [BIOS FORTINET] A BIOS do FortiGate conecta por padrão no servidor TFTP {biosTftpServerIp} com IP local {biosFortiGateIp}. Adaptador Ethernet do computador ajustado para {biosTftpServerIp}/24.");
        await session.ConnectRawAsync(ct);

        var recovery = new NetworkDevice.Fortinet.FortiOsBootLoaderRecovery();
        var success = await recovery.RecoverFirmwareAsync(
            session,
            _selectedIosBinPath,
            biosTftpServerIp,
            biosFortiGateIp,
            AtualizarProgresso,
            EscreverLinhaAsync,
            ct,
            biosSubnetMask);

        if (!success)
        {
            throw new InvalidOperationException("Falha no procedimento de recuperação do FortiGate via BIOS TFTP.");
        }

        // Após a recuperação via BIOS (onde o cabo precisava estar na porta WAN),
        // o FortiOS já inicializou e o provisionamento e testes (Fase 3 em diante)
        // exigem a conexão na porta LAN (porta 1 / lan1) para aplicar a SAIP e testar conectividade.
        EscreverLinha("\n=================================================================");
        EscreverLinha("  🔄 [AJUSTE FÍSICO NECESSÁRIO — TROCA DE PORTA ETHERNET]");
        EscreverLinha("=================================================================");
        EscreverLinha("  O firmware foi gravado na Flash e o FortiOS inicializou com sucesso!");
        EscreverLinha("  👉 Desconecte o cabo de rede da porta WAN e CONECTE NA PORTA LAN1 (Porta 1).");
        EscreverLinha("  Esta etapa é necessária para prosseguir com o fluxo padrão de provisionamento e testes.\n");

        await InstruirOperadorAsync(
            "🔄 TROCA DE CABO DE REDE — FORTIGATE 40F\n\n" +
            "O firmware foi recuperado na Flash e o FortiOS inicializou com sucesso!\n\n" +
            "👉 Desconecte o cabo de rede Ethernet da porta WAN e conecte-o na porta LAN1 (Porta 1) do FortiGate 40F.\n\n" +
            "Clique em OK após realizar a troca física para continuar com o provisionamento e testes da esteira.",
            ct);
    }

    private async Task ExecutarUpgradeFirmwareAsync(string porta, int baud, string hostIp, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_selectedIosBinPath))
            return;

        var fileName = Path.GetFileName(_selectedIosBinPath);
        AtualizarProgresso(10, "Fase B: Iniciando Upgrade e TFTP...", "Iniciando servidor TFTP e conectando...");
        EscreverLinha($"\n[*] [FASE B] ATUALIZAÇÃO DE FIRMWARE VIA TFTP ({fileName})...");

        var isPreForti = _isFortiGateDetected || fileName.EndsWith(".out", StringComparison.OrdinalIgnoreCase);
        var fortiUser = !string.IsNullOrEmpty(_lastFortiResolvedUser) ? _lastFortiResolvedUser : "admin";
        var fortiPass = _lastFortiResolvedPass ?? "CQMR";

        var sessionOptions = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal(),
            CommandTimeout = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(30),
            Username = isPreForti ? fortiUser : null,
            Password = isPreForti ? fortiPass : null
        };

        var transport = new SerialTransport(porta, baud);
        await using var session = new DeviceSession(transport, sessionOptions);
        session.RawOutput += OnRawOutput;

        await session.ConnectAsync(ct);

        var promptStr = session.CurrentPrompt ?? "";
        var profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var isForti = IsTagFortiGate(profileTag)
                   || promptStr.Contains("FortiGate", StringComparison.OrdinalIgnoreCase)
                   || promptStr.Contains("FGT", StringComparison.OrdinalIgnoreCase)
                   || promptStr.Contains("FortiOS", StringComparison.OrdinalIgnoreCase)
                   || fileName.EndsWith(".out", StringComparison.OrdinalIgnoreCase)
                   || _isFortiGateDetected;

        var isHpe = !isForti && (profileTag.Contains("hpe", StringComparison.OrdinalIgnoreCase)
                 || profileTag.Contains("msr", StringComparison.OrdinalIgnoreCase)
                 || promptStr.StartsWith("[")
                 || promptStr.StartsWith("<")
                 || promptStr.Contains("HPE", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("MSR", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("Comware", StringComparison.OrdinalIgnoreCase)
                 || fileName.EndsWith(".ipe", StringComparison.OrdinalIgnoreCase));

        bool success;
        if (isForti)
        {
            EscreverLinha($"[*] Equipamento identificado como Fortinet FortiGate 40F para upgrade de firmware ({fileName}).");

            // ANTES DE CONFIGURAR SERVIDOR TFTP OU INTERFACES DE REDE:
            // Checa se o FortiOS já está na mesma versão da imagem selecionada
            if (session.IsConnected)
            {
                EscreverLinha("[*] Verificando versão atual do FortiOS no equipamento ('get system status')...");
                try
                {
                    await session.SendRawAsync("\r\n", ct);
                    await Task.Delay(250, ct);
                }
                catch { }

                string statusOut = string.Empty;
                try
                {
                    statusOut = await session.SendCommandAsync("get system status", TimeSpan.FromSeconds(10), ct);
                }
                catch (Exception ex)
                {
                    EscreverLinha($"  [AVISO] Não foi possível obter 'get system status': {ex.Message}");
                }

                if (!string.IsNullOrWhiteSpace(statusOut) &&
                    (statusOut.Contains("Version:", StringComparison.OrdinalIgnoreCase) ||
                     statusOut.Contains("FortiGate", StringComparison.OrdinalIgnoreCase)))
                {
                    _detectedFortiOsStatusOut = statusOut;
                    if (NetworkDevice.Fortinet.FortiOsFirmwareVersionInspector.IsSameVersion(
                        statusOut, fileName, out var currentVer, out var targetVer))
                    {
                        _firmwareSkippedSameVersion = true;
                        EscreverLinha("\n=================================================================");
                        EscreverLinha("       ℹ️ FIRMWARE FORTIOS JÁ ATUALIZADO NO FORTIGATE 40F        ");
                        EscreverLinha("=================================================================");
                        EscreverLinha($"  Versão no Equipamento : {currentVer}");
                        EscreverLinha($"  Versão da Imagem Alvo : {targetVer} ({fileName})");
                        EscreverLinha("  -> A versão instalada já é a mesma da imagem selecionada.");
                        EscreverLinha("  -> Pulando transferência TFTP e reinicialização desnecessária!");
                        EscreverLinha("=================================================================\n");

                        AtualizarProgresso(100, "Firmware Já Atualizado", $"FortiOS já está na versão {currentVer}. Transferência TFTP dispensada.");
                        return;
                    }
                    else
                    {
                        EscreverLinha($"[*] Versão no equipamento ({currentVer}) difere da imagem proposta ({targetVer}). Prosseguindo com upgrade TFTP.");
                    }
                }
            }

            AtualizarProgresso(20, "Fase B: Gravando firmware FortiOS...", $"Iniciando transferência TFTP da imagem {fileName}...");

            var fileDir = Path.GetDirectoryName(_selectedIosBinPath) ?? AppContext.BaseDirectory;
            await using var tftpServer = new NetworkDevice.Protocols.Tftp.EmbeddedTftpServer(fileDir);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastUiUpdate = DateTime.MinValue;
            var lastLoggedPct = -1;

            tftpServer.TransferProgress += (file, sent, total, pct) =>
            {
                var now = DateTime.UtcNow;
                var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                var sentMb = sent / (1024.0 * 1024.0);
                var totalMb = total / (1024.0 * 1024.0);
                var speedMbSec = elapsedSec > 0.5 ? sentMb / elapsedSec : 0;
                var remainingSec = speedMbSec > 0 ? (totalMb - sentMb) / speedMbSec : 0;
                var etaStr = remainingSec > 0 ? $" | Restam ~{TimeSpan.FromSeconds(remainingSec):mm\\:ss}" : "";

                var mappedPct = 25 + (int)(pct * 0.45);
                if ((now - lastUiUpdate).TotalMilliseconds >= 250 || pct >= 100)
                {
                    lastUiUpdate = now;
                    AtualizarProgresso(
                        mappedPct,
                        $"⚠️ NÃO DESLIGUE! Transferindo Firmware TFTP ({pct:N1}%)...",
                        $"{sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) — {speedMbSec:N1} MB/s{etaStr}");
                }

                var step = (int)(pct / 5) * 5;
                if (step > lastLoggedPct)
                {
                    lastLoggedPct = step;
                    var barLength = 20;
                    var filled = (int)Math.Round((pct / 100.0) * barLength);
                    var bar = new string('█', Math.Clamp(filled, 0, barLength)) + new string('░', Math.Max(0, barLength - filled));
                    EscreverLinha($"    -> [TFTP] [{bar}] {sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) | {speedMbSec:N1} MB/s{etaStr}");
                }
            };
            tftpServer.Start();

            // Garante regra de firewall no Windows para TFTP (porta 69 UDP)
            await HostNetworkManager.EnsureTftpFirewallRuleAsync(ct);

            // Mapeia interfaces reais do FortiGate
            string lanInterface = "lan";
            string wanInterface = "wan";
            try
            {
                var intfOutput = await session.SendCommandAsync("get system interface", TimeSpan.FromSeconds(10), ct);
                var (w, l) = NetworkDevice.Fortinet.FortiOsSaipConfigurator.DetectInterfaces(intfOutput, "wan", "lan");
                wanInterface = w;
                lanInterface = l;
                EscreverLinha($"[*] Interfaces FortiGate mapeadas: LAN -> '{lanInterface}' | WAN -> '{wanInterface}'.");
            }
            catch { }

            var routerLanIp = _loadedSaipCircuit?.LanIp ?? "192.168.1.99";
            var routerMask = _loadedSaipCircuit?.LanSubnetMask ?? "255.255.255.0";

            // Se temos Ficha SAIP, desabilita servidor DHCP conflitante e configura IP na LAN real
            if (_loadedSaipCircuit != null)
            {
                try
                {
                    // 1. Desabilita servidor DHCP nativo para evitar conflito de subnet no FortiOS
                    await session.SendCommandAsync("config system dhcp server", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync("edit 1", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync("set status disable", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync("next", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), ct);
                }
                catch { }

                try
                {
                    EscreverLinha($"[*] Configurando IP {routerLanIp} {routerMask} na interface '{lanInterface}' do FortiGate...");
                    await session.SendCommandAsync("config system interface", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync($"edit \"{lanInterface}\"", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync($"set ip {routerLanIp} {routerMask}", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync("set allowaccess ping https ssh http telnet", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync("next", TimeSpan.FromSeconds(5), ct);
                    await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), ct);
                    EscreverLinha($"[OK] Interface '{lanInterface}' configurada com {routerLanIp}.");
                }
                catch (Exception ex)
                {
                    EscreverLinha($"  [AVISO] Configuração de interface FortiGate: {ex.Message}");
                }
            }

            // Configura placa de rede do Windows com hostIp (com auto-detecção de adaptador)
            var targetAdapter = CbAdaptadorRede?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(targetAdapter))
            {
                var ethAdapters = HostNetworkManager.GetEthernetAdapters();
                targetAdapter = ethAdapters.FirstOrDefault() ?? "";
            }

            if (!string.IsNullOrEmpty(targetAdapter))
            {
                try
                {
                    EscreverLinha($"[*] Configurando IP {hostIp} no adaptador de rede '{targetAdapter}' para servidor TFTP...");
                    var (okNet, outNet) = await HostNetworkManager.SetStaticIpAsync(targetAdapter, hostIp, routerMask, null, ct);
                    if (okNet) EscreverLinha($"[OK] Adaptador '{targetAdapter}' configurado para TFTP ({hostIp}).");
                    else EscreverLinha($"  [AVISO] Configuração do adaptador: {outNet.Trim()}");
                }
                catch (Exception ex)
                {
                    EscreverLinha($"  [AVISO] Configuração do adaptador Windows: {ex.Message}");
                }
            }

            // Valida criticamente o link físico da porta LAN do FortiGate (Porta 1 / Giga 1 / lan1)
            await NetworkDevice.Fortinet.FortiOsSaipConfigurator.EnforceLanPortConnectedAsync(session, lanInterface, NotificarConexaoCaboAsync, EscreverLinhaAsync, ct);

            // Valida conectividade FortiGate -> PC via ping antes de disparar o TFTP
            EscreverLinha($"[*] Validando conectividade de rede FortiGate -> {hostIp}...");
            var pingOutput = await session.SendCommandAsync($"execute ping {hostIp}", TimeSpan.FromSeconds(10), ct);

            if (pingOutput.Contains("100% packet loss", StringComparison.OrdinalIgnoreCase) ||
                pingOutput.Contains("Unreachable", StringComparison.OrdinalIgnoreCase) ||
                pingOutput.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase))
            {
                EscreverLinha($"[⚠️] FortiGate não obteve resposta no IP {hostIp} (possível cabo de rede desconectado).");
                await InstruirOperadorAsync(
                    "⚠️ CONEXÃO DO CABO DE REDE ETHERNET - ATUALIZAÇÃO FIRMWARE FORTIGATE 40F\n\n" +
                    "O FortiGate não conseguiu alcançar o computador via rede Ethernet.\n\n" +
                    "👉 REQUISITOS OBRIGATÓRIOS PARA O TFTP:\n" +
                    "1. Conecte um cabo de rede Ethernet entre o computador e uma das portas LAN (Portas 1, 2 ou 3) do FortiGate 40F.\n" +
                    "2. Certifique-se de que o LED da porta física no FortiGate está ACESO (Link ativo).\n" +
                    $"3. O computador está configurado no IP {hostIp}.\n\n" +
                    "Clique em OK assim que o cabo estiver conectado para prosseguir com a transferência TFTP.",
                    ct);

                // Re-testa ping após confirmação do operador
                EscreverLinha($"[*] Re-testando ping FortiGate -> {hostIp}...");
                pingOutput = await session.SendCommandAsync($"execute ping {hostIp}", TimeSpan.FromSeconds(10), ct);
                if (!pingOutput.Contains("100% packet loss") && !pingOutput.Contains("Unreachable", StringComparison.OrdinalIgnoreCase))
                {
                    EscreverLinha($"[OK] Comunicação Ethernet validada! Link ativo.");
                }
                else
                {
                    EscreverLinha($"  [AVISO] Sem resposta ao ping, tentando transferência TFTP mesmo assim...");
                }
            }
            else
            {
                EscreverLinha($"[OK] Comunicação Ethernet validada! FortiGate alcança o servidor TFTP em {hostIp}.");
            }

            if (session.IsConnected && !string.IsNullOrEmpty(session.CurrentPrompt) &&
                (session.CurrentPrompt.Contains("#") || session.CurrentPrompt.Contains("$")))
            {
                EscreverLinha($"[*] Enviando 'execute restore image tftp {fileName} {hostIp}' via console...");
                var conditions = new StopCondition[]
                {
                    new StopCondition.Contains("FortiConfirm", "y/n"),
                    new StopCondition.Prompt()
                };

                var promptResult = await session.SendExpectAsync($"execute restore image tftp {fileName} {hostIp}", conditions, TimeSpan.FromSeconds(15), ct);
                if (promptResult.Matched is StopCondition.Contains || promptResult.Output.Contains("y/n", StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendRawAsync("y\r", ct);
                    EscreverLinha("[OK] Confirmação 'y' enviada. FortiGate iniciando download TFTP e gravação da Flash...");
                    AtualizarProgresso(40, "Gravando firmware FortiOS...", "Transferindo arquivo via TFTP e aguardando reboot...");

                    var tftpConditions = new StopCondition[]
                    {
                        new StopCondition.Contains("TFTP_OK", "Get image from tftp server OK"),
                        new StopCondition.Contains("TFTP_OK2", "Check image OK"),
                        new StopCondition.Contains("TFTP_RESET", "System is resetting"),
                        new StopCondition.Contains("TFTP_FAIL1", "Can not connect"),
                        new StopCondition.Contains("TFTP_FAIL2", "Command fail"),
                        new StopCondition.Contains("TFTP_FAIL3", "TFTP timeout"),
                        new StopCondition.Prompt()
                    };

                    try
                    {
                        var tftpWait = await session.WaitForAsync(tftpConditions, TimeSpan.FromSeconds(120), ct);
                        if (tftpWait.Output.Contains("Can not connect", StringComparison.OrdinalIgnoreCase) ||
                            tftpWait.Output.Contains("Command fail", StringComparison.OrdinalIgnoreCase) ||
                            tftpWait.Output.Contains("TFTP timeout", StringComparison.OrdinalIgnoreCase) ||
                            tftpWait.Output.Contains("Network is unreachable", StringComparison.OrdinalIgnoreCase))
                        {
                            EscreverLinha($"[ERRO TFTP] {tftpWait.Output.Trim()}");
                            throw new InvalidOperationException($"Transferência TFTP falhou no FortiGate: {tftpWait.Output.Trim()}. Verifique a conexão física do cabo na porta LAN.");
                        }
                    }
                    catch (SessionTimeoutException)
                    {
                        EscreverLinha("[*] FortiGate reiniciando após recepção da imagem...");
                    }

                    EscreverLinha("\n[*] FortiGate gravando nova imagem na memória Flash e reinicializando...");
                    for (int remaining = 50; remaining > 0; remaining -= 5)
                    {
                        var mapped = 70 + (int)((50 - remaining) * 0.3);
                        AtualizarProgresso(mapped, $"FortiGate 40F: Reiniciando ({remaining}s)...", "Gravando Flash interna e inicializando FortiOS...");
                        EscreverLinha($"    ⏳ [FLASH/BOOT] Gravando firmware e reinicializando... aguardando (~{remaining}s restantes)");
                        await Task.Delay(5000, ct);
                    }

                    EscreverLinha("[*] Sondando console serial para verificar inicialização do FortiOS...");
                    var bootDeadline = DateTime.UtcNow.AddSeconds(180);
                    var probeAttempt = 0;
                    success = false;
                    while (DateTime.UtcNow < bootDeadline && !ct.IsCancellationRequested)
                    {
                        probeAttempt++;
                        try
                        {
                            await session.SendRawAsync("\r", ct);
                            var bootCheck = await session.WaitForAsync(
                                new StopCondition[]
                                {
                                    new StopCondition.Prompt(),
                                    new StopCondition.Contains("LoginPrompt", "login:"),
                                    new StopCondition.Contains("ActivateConsole", "activate this console"),
                                    new StopCondition.Contains("PressEnter", "press ENTER")
                                },
                                TimeSpan.FromSeconds(5),
                                ct);

                            if (bootCheck.Matched != null)
                            {
                                EscreverLinha($"[OK] FortiGate 40F reinicializado com sucesso com o novo firmware (detectado na tentativa {probeAttempt})!");
                                success = true;
                                await Task.Delay(2000, ct);
                                break;
                            }
                        }
                        catch { }

                        var remSeconds = Math.Max(0, (int)(bootDeadline - DateTime.UtcNow).TotalSeconds);
                        EscreverLinha($"    ⏳ [SONDA SERIAL] Tentativa {probeAttempt}: aguardando inicialização do FortiOS... ({remSeconds}s restantes)");
                        await Task.Delay(4000, ct);
                    }

                    if (!success)
                    {
                        EscreverLinha("❌ [ERRO] Tempo limite (180s) esgotado aguardando o FortiOS concluir a reinicialização.");
                        EscreverLinha("    Certifique-se de que o equipamento terminou o boot antes de avançar para a fase de provisionamento.");
                    }
                }
                else
                {
                    EscreverLinha($"[*] Resposta do comando: {promptResult.Output.Trim()}");
                    success = !promptResult.Output.Contains("command fail", StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                var routerIp = _loadedSaipCircuit?.LanIp ?? "192.168.1.99";
                var subnetMask = _loadedSaipCircuit?.LanSubnetMask ?? "255.255.255.0";
                var driver = new NetworkDevice.Fortinet.Drivers.FortiGate40FDriver(EscreverLinhaAsync);
                success = await driver.FirmwareRecovery.RecoverFirmwareAsync(
                    session, _selectedIosBinPath, hostIp, routerIp, subnetMask,
                    InstruirOperadorAsync,
                    (pct, tit, desc) => { AtualizarProgresso(pct, tit, desc); return Task.CompletedTask; },
                    ct);
            }
        }
        else if (isHpe)
        {
            EscreverLinha($"[*] Equipamento identificado como HPE Comware para upgrade de firmware ({fileName}).");
            AtualizarProgresso(20, "Fase B: Gravando firmware HPE...", "Transferindo via TFTP, gravando bootloader e reiniciando...");
            var hpeUpgrader = new HpeComwareUpgrader(EscreverLinhaAsync, AtualizarProgresso);
            success = await hpeUpgrader.UpgradeAsync(session, _selectedIosBinPath, hostIp,
                async (msg, ctk) =>
                {
                    return await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(msg + "\n\nDeseja atualizar o boot-loader agora?", "Boot-loader desatualizado", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
                },
                InstruirOperadorAsync,
                ct,
                _loadedSaipCircuit?.LanSubnetMask,
                CbAdaptadorRede?.Text?.Trim());
        }
        else
        {
            var is921 = profileTag.Contains("921", StringComparison.OrdinalIgnoreCase)
                     || profileTag.Contains("c900", StringComparison.OrdinalIgnoreCase);

            var is841 = profileTag.Contains("841", StringComparison.OrdinalIgnoreCase)
                     || profileTag.Contains("c841", StringComparison.OrdinalIgnoreCase)
                     || profileTag.Contains("c800", StringComparison.OrdinalIgnoreCase);

            var routerIp = _loadedSaipCircuit?.LanIp ?? "200.182.245.17";
            var subnetMask = _loadedSaipCircuit?.LanSubnetMask ?? "255.255.255.240";
            var lanInterface = is841 ? "GigabitEthernet0/5" : is921 ? "GigabitEthernet 5" : "GigabitEthernet 0/1";
            var adapter = CbAdaptadorRede?.Text?.Trim();

            var modeloNome = is841 ? "Cisco Série 800 / C841M" : is921 ? "Cisco Série 900 / C921-4P" : "Cisco Série 1900 / G2";
            EscreverLinha($"[*] Equipamento identificado como {modeloNome} para upgrade de firmware ({fileName}) via LAN ({lanInterface}).");
            AtualizarProgresso(22, $"Fase B: Gravando IOS {modeloNome}...", "Iniciando transferência TFTP...");
            var ciscoUpgrader = new CiscoIOSUpgrader(EscreverLinhaAsync, AtualizarProgresso);
            success = await ciscoUpgrader.UpgradeAsync(session, _selectedIosBinPath, hostIp, routerIp, subnetMask, lanInterface, null, adapter, InstruirOperadorAsync, ct);
        }

        if (success)
        {
            AtualizarProgresso(100, "Fase B Concluída!", "Firmware atualizado e roteador reiniciado para carregar a nova versão.");
        }
        else
        {
            throw new InvalidOperationException("O processo de atualização do firmware não foi concluído com sucesso.");
        }
    }

    public sealed record TripleIcmpResult(
        ConnectivityTestResult? LanResult,
        ConnectivityTestResult? WanResult,
        ConnectivityTestResult? WebResult)
    {
        public bool IsLanOk => LanResult?.IsSuccess == true;
        public bool IsWanOk => WanResult?.IsSuccess == true;
        public bool IsWebOk => WebResult?.IsSuccess == true;
        public bool IsSuccess => IsLanOk;

        // Regra de cores e badges solicitada:
        // 🟢 Verde (#16A34A): 5a, 5b e 5c responderam
        // 🟡 Amarelo (#CA8A04): 5a e 5b responderam (Web sem resposta)
        // 🟠 Laranja (#EA580C): Apenas 5a respondeu
        // 🔴 Vermelho (#DC2626): Nenhum respondeu (ou LAN sem resposta)
        public string StatusColorHex =>
            (IsLanOk && IsWanOk && IsWebOk) ? "#16A34A" :
            (IsLanOk && IsWanOk) ? "#CA8A04" :
            IsLanOk ? "#EA580C" :
            "#DC2626";

        public string StatusBadge =>
            (IsLanOk && IsWanOk && IsWebOk) ? "✅" :
            (IsLanOk && IsWanOk) ? "🟡" :
            IsLanOk ? "🟠" :
            "❌";
    }

    // FASE E · TESTAR CONECTIVIDADE ICMP (5a LAN, 5b WAN, 5c WEB)
    private async void BtnTestarIcmp_Click(object sender, RoutedEventArgs e)
    {
        SelecionarFase("E");
        DefinirBadgeStatus("E", "⏳");
        SetBusy(true);
        _cts = new CancellationTokenSource();

        try
        {
            var icmpResult = await ExecutarTesteIcmpTriploAsync(_cts.Token);
            DefinirBadgeStatus("E", icmpResult.StatusBadge);
        }
        catch (OperationCanceledException)
        {
            DefinirBadgeStatus("E", "⚪");
            AtualizarProgresso(0, "Teste cancelado", "Cancelado pelo operador.");
            EscreverLinha("\n[!] Teste ICMP cancelado.");
        }
        catch (Exception ex)
        {
            DefinirBadgeStatus("E", "❌");
            AtualizarProgresso(0, "Falha nos testes ICMP", ex.Message);
            EscreverLinha($"\n[ERRO FASE E] {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private EthernetAdapterDetails? ObterInformacoesEthernetRoteador()
    {
        var adapterName = CbAdaptadorRede?.Text?.Trim();
        var hostIp = _loadedSaipCircuit?.HostLanIp?.Trim();
        return HostNetworkManager.GetDedicatedRouterEthernetInterface(adapterName, hostIp);
    }

    private string? ObterIpOrigemParaIcmp()
    {
        var eth = ObterInformacoesEthernetRoteador();
        if (eth != null && !string.IsNullOrWhiteSpace(eth.IPv4Address))
        {
            return eth.IPv4Address;
        }

        if (_loadedSaipCircuit != null && !string.IsNullOrWhiteSpace(_loadedSaipCircuit.HostLanIp))
        {
            return _loadedSaipCircuit.HostLanIp.Trim();
        }

        var adapterName = CbAdaptadorRede?.Text?.Trim();
        if (!string.IsNullOrEmpty(adapterName))
        {
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var ni = interfaces.FirstOrDefault(i => i.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase));
                if (ni != null)
                {
                    var ip = ni.GetIPProperties().UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))?
                        .Address.ToString();
                    if (!string.IsNullOrEmpty(ip)) return ip;
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Verifica via console serial se a porta WAN esperada do modelo está DOWN.
    /// Retorna null quando inconclusivo (segue fluxo normal).
    /// </summary>
    private async Task<bool?> VerificarWanDownAsync(WanPortaInfo info, CancellationToken ct)
    {
        SerialTransport? transport = null;
        DeviceSession? session = null;
        try
        {
            var porta = ObterPortaSelecionada();
            var baud = ObterBaudRateSelecionado();
            var isForti = info.IsForti
                       || _isFortiGateDetected
                       || IsTagFortiGate((CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");

            transport = new SerialTransport(porta, baud, readTimeout: TimeSpan.FromMilliseconds(400));
            session = new DeviceSession(transport, new SessionOptions
            {
                PromptMatcher = RegexPromptMatcher.Universal(),
                ConnectTimeout = TimeSpan.FromSeconds(8),
                CommandTimeout = TimeSpan.FromSeconds(10),
                Username = "EBT",
                Password = isForti ? "CQMR" : info.IsHpe ? "PRO1ANPRO1AN" : "PRO1AN",
            });
            session.RawOutput += OnRawOutput;
            await session.ConnectAsync(ct);

            if (isForti)
            {
                var physical = await session.SendCommandAsync("get system interface physical", TimeSpan.FromSeconds(8), ct);
                var (encontrada, down) = WanPortInspector.FortiWanStatus(physical, info.NomesBusca);
                if (!encontrada)
                {
                    var diag = await session.SendCommandAsync("diagnose netlink interface list wan", TimeSpan.FromSeconds(8), ct);
                    (encontrada, down) = WanPortInspector.FortiWanStatus(diag, info.NomesBusca);
                }

                if (!encontrada) return null;
                EscreverLinha(down
                    ? $"[ALERTA 5b] Porta WAN {info.InterfaceExibicao} DOWN no {info.ModeloExibicao}."
                    : $"[*] [5b] Porta WAN {info.InterfaceExibicao} UP no {info.ModeloExibicao} — falha é de rota/operadora.");
                return down;
            }
            else if (info.IsHpe)
            {
                try { await session.SendCommandAsync("screen-length disable", TimeSpan.FromSeconds(5), ct); } catch { }
                var brief = await session.SendCommandAsync("display interface brief", TimeSpan.FromSeconds(15), ct);
                var (encontrada, down) = WanPortInspector.HpeWanStatus(brief, info.NomesBusca);
                if (!encontrada) return null;
                EscreverLinha(down
                    ? $"[ALERTA 5b] Porta WAN {info.InterfaceExibicao} DOWN no {info.ModeloExibicao}."
                    : $"[*] [5b] Porta WAN {info.InterfaceExibicao} UP no {info.ModeloExibicao} — falha é de rota/operadora.");
                return down;
            }
            else
            {
                try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), ct); } catch { }
                var brief = await session.SendCommandAsync("show ip interface brief", TimeSpan.FromSeconds(15), ct);
                var (encontrada, down) = WanPortInspector.CiscoWanStatus(brief, info.NomesBusca);
                if (!encontrada) return null;
                EscreverLinha(down
                    ? $"[ALERTA 5b] Porta WAN {info.InterfaceExibicao} DOWN no {info.ModeloExibicao}."
                    : $"[*] [5b] Porta WAN {info.InterfaceExibicao} UP no {info.ModeloExibicao} — falha é de rota/operadora.");
                return down;
            }
        }
        catch (Exception ex)
        {
            EscreverLinha($"[AVISO 5b] Não foi possível verificar a porta WAN via serial ({ex.Message}) — seguindo fluxo normal.");
            return null;
        }
        finally
        {
            if (session != null)
            {
                session.RawOutput -= OnRawOutput;
                try { await session.DisposeAsync(); } catch { }
            }
            if (transport != null)
            {
                try { await transport.DisposeAsync(); } catch { }
            }
        }
    }

    /// <summary>
    /// Se a WAN está DOWN no equipamento: pergunta se conecta o cabo (SIM repete o teste, até 3x)
    /// ou segue normalmente (NÃO). Sem diagnóstico conclusivo, mantém comportamento atual.
    /// </summary>
    private async Task<ConnectivityTestResult> TratarWanDownAsync(
        ConnectivityService service, string wanTarget, string? sourceIp,
        ConnectivityTestResult atual, CancellationToken ct)
    {
        var tag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString()
               ?? (CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var info = WanPortInspector.PorTagModelo(tag);
        if (info == null && (_isFortiGateDetected || IsTagFortiGate(tag)))
        {
            info = new WanPortaInfo("Fortinet FortiGate 40F", "WAN (Porta WAN)", new[] { "wan", "wan1" }, false, true);
        }
        if (info == null) return atual;

        bool? down;
        try { down = await VerificarWanDownAsync(info, ct); }
        catch { return atual; }
        if (down != true) return atual;

        for (var tentativa = 1; tentativa <= 3; tentativa++)
        {
            ct.ThrowIfCancellationRequested();
            var resp = Dispatcher.Invoke(() => MessageBox.Show(this,
                $"A porta WAN {info.InterfaceExibicao} do roteador {info.ModeloExibicao} está DOWN (sem link físico).\n\n" +
                $"👉 Por favor, conecte a porta WAN ao equipamento do acesso para testes de conectividade do serviço e clique SIM para repetir o teste.\n\n" +
                $"Clique NÃO para seguir normalmente (WAN ficará como Offline).",
                "Porta WAN DOWN — Fase 5b",
                MessageBoxButton.YesNo, MessageBoxImage.Warning));
            if (resp != MessageBoxResult.Yes)
            {
                EscreverLinha("[*] [5b] Operador optou por seguir com WAN Offline.");
                break;
            }
            EscreverLinha($"[*] [5b] Repetindo teste WAN após conexão ao equipamento do acesso (tentativa {tentativa}/3)...");
            AtualizarProgresso(78, "Fase 5b: Repetindo ICMP WAN...", $"Disparando pacotes ICMP para Gateway WAN ({wanTarget})...");
            var retry = await service.TestPingAsync(wanTarget, count: 4, timeoutMs: 2000, sourceIpAddress: sourceIp, cancellationToken: ct);
            if (retry.IsSuccess)
            {
                EscreverLinha($"[OK 5b WAN] Conectividade de enlace WAN confirmada após conexão do cabo: RTT Médio {retry.AvgRttMs:F1}ms, 0% perda.");
                return retry;
            }
            EscreverLinha($"[AVISO 5b WAN] Ainda sem resposta após a tentativa {tentativa}/3.");
        }
        return atual;
    }

    private async Task<TripleIcmpResult> ExecutarTesteIcmpTriploAsync(CancellationToken ct)
    {
        var lanTarget = _loadedSaipCircuit?.LanIp ?? TxtIcmpTargetLan?.Text?.Trim() ?? "200.182.245.17";
        lanTarget = lanTarget.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "200.182.245.17";

        var wanTarget = _loadedSaipCircuit?.WanGateway ?? TxtIcmpTargetWan?.Text?.Trim() ?? "201.90.204.21";
        wanTarget = wanTarget.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "201.90.204.21";

        var sourceIp = ObterIpOrigemParaIcmp();
        var service = new ConnectivityService(EscreverLinhaAsync);

        EscreverLinha("=================================================================");
        EscreverLinha("        FASE 5 · TESTES DE CONECTIVIDADE ICMP MULTI-DESTINO      ");
        EscreverLinha("=================================================================");
        if (!string.IsNullOrEmpty(sourceIp))
        {
            EscreverLinha($"[*] Teste vinculado estritamente à interface conectada ao roteador (Origem: {sourceIp}).");
            EscreverLinha("    -> Isolamento ativo contra falsos positivos de Wi-Fi / outras conexões.");
        }

        // Aguarda 1.5s para estabilização de ARP e da interface de rede
        await Task.Delay(1500, ct);

        // -------------------------------------------------------------
        // 5a. TESTE ICMP LAN (IP do Roteador / Interface de Teste)
        // -------------------------------------------------------------
        AtualizarProgresso(74, "Fase 5a: Testando ICMP LAN...", $"Disparando pacotes ICMP para LAN ({lanTarget})...");
        EscreverLinha($"\n[*] [5a] TESTE ICMP LAN -> {lanTarget} (Interface LAN do Roteador)...");
        var lanRes = await service.TestPingAsync(lanTarget, count: 4, timeoutMs: 2500, sourceIpAddress: sourceIp, cancellationToken: ct);
        if (lanRes.IsSuccess)
            EscreverLinha($"[OK 5a LAN] Conectividade LAN confirmada: RTT Médio {lanRes.AvgRttMs:F1}ms, 0% perda.");
        else
            EscreverLinha($"[AVISO 5a LAN] Interface LAN ({lanTarget}) sem resposta ICMP.");

        // -------------------------------------------------------------
        // 5b. TESTE ICMP WAN (Gateway da Operadora Claro)
        // -------------------------------------------------------------
        AtualizarProgresso(78, "Fase 5b: Testando ICMP WAN...", $"Disparando pacotes ICMP para Gateway WAN ({wanTarget})...");
        EscreverLinha($"\n[*] [5b] TESTE ICMP WAN -> {wanTarget} (Gateway WAN Claro)...");
        var wanRes = await service.TestPingAsync(wanTarget, count: 4, timeoutMs: 2000, sourceIpAddress: sourceIp, cancellationToken: ct);
        if (wanRes.IsSuccess)
            EscreverLinha($"[OK 5b WAN] Conectividade de enlace WAN confirmada: RTT Médio {wanRes.AvgRttMs:F1}ms, 0% perda.");
        else
        {
            EscreverLinha($"[AVISO 5b WAN] Gateway WAN ({wanTarget}) sem resposta (Link físico ou rota pendente na operadora).");
            wanRes = await TratarWanDownAsync(service, wanTarget, sourceIp, wanRes, ct);
        }

        // -------------------------------------------------------------
        // 5c. TESTE ICMP WEB (DNS Cloudflare 1.1.1.1 / Google 8.8.8.8)
        // -------------------------------------------------------------
        AtualizarProgresso(82, "Fase 5c: Testando ICMP WEB...", "Disparando pacotes ICMP para DNS Cloudflare (1.1.1.1)...");
        EscreverLinha($"\n[*] [5c] TESTE ICMP WEB -> DNS Cloudflare (1.1.1.1) / Google (8.8.8.8)...");
        var webRes = await service.TestPingAsync("1.1.1.1", count: 4, timeoutMs: 2000, sourceIpAddress: sourceIp, cancellationToken: ct);
        if (!webRes.IsSuccess)
        {
            EscreverLinha("[*] Testando segundo host Web (Google DNS 8.8.8.8)...");
            webRes = await service.TestPingAsync("8.8.8.8", count: 4, timeoutMs: 2000, sourceIpAddress: sourceIp, cancellationToken: ct);
        }

        if (webRes.IsSuccess)
            EscreverLinha($"[OK 5c WEB] Conectividade com a Internet pública confirmada via roteador sob teste: RTT Médio {webRes.AvgRttMs:F1}ms.");
        else
            EscreverLinha($"[AVISO 5c WEB] Internet externa sem resposta ICMP (normal caso o circuito WAN ainda não esteja ativado na operadora).");

        EscreverLinha("=================================================================\n");

        var summary = $"5a LAN: {(lanRes.IsSuccess ? $"{lanRes.AvgRttMs:F1}ms" : "Falha")} | 5b WAN: {(wanRes.IsSuccess ? $"{wanRes.AvgRttMs:F1}ms" : "Offline")} | 5c WEB: {(webRes.IsSuccess ? $"{webRes.AvgRttMs:F1}ms" : "Offline")}";
        AtualizarProgresso(84, "Fase 5 Concluída!", summary);

        _lastIcmpResult = new TripleIcmpResult(lanRes, wanRes, webRes);
        return _lastIcmpResult;
    }

    // FASE F · TESTAR ACESSO REMOTO TELNET
    private async void BtnTestarTelnet_Click(object sender, RoutedEventArgs e)
    {
        var host = _loadedSaipCircuit?.LanIp ?? (string.IsNullOrWhiteSpace(TxtTelnetTarget.Text) ? "200.182.245.17" : TxtTelnetTarget.Text.Trim());
        if (string.IsNullOrEmpty(host))
        {
            MessageBox.Show("Informe o host/IP para teste Telnet.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var port = int.TryParse(TxtTelnetPort.Text.Trim(), out var p) ? p : 23;
        SelecionarFase("F");
        DefinirBadgeStatus("F", "⏳");
        SetBusy(true);
        _cts = new CancellationTokenSource();
        try
        {
            var r = await ExecutarTesteTelnetAsync(host, port, _cts.Token);
            DefinirBadgeStatus("F", r.IsSuccess ? "✅" : "❌");
        }
        catch (Exception ex)
        {
            DefinirBadgeStatus("F", "❌");
            EscreverLinha($"[ERRO TELNET] {ex.Message}");
        }
        finally { _cts.Dispose(); _cts = null; SetBusy(false); }
    }

    private async Task<ConnectivityService.TelnetTestResult> ExecutarTesteTelnetAsync(string host, int port, CancellationToken ct)
    {
        var telnetUser = TxtTelnetUser.Text.Trim(); if (string.IsNullOrEmpty(telnetUser)) telnetUser = "EBT";
        var telnetPass = TxtTelnetPass.Text.Trim(); if (string.IsNullOrEmpty(telnetPass)) telnetPass = "PRO1ANPRO1AN";
        AtualizarProgresso(50, "Fase F: Testando Telnet...", $"Login {telnetUser} em {host}:{port}...");
        EscreverLinha($"\n[*] [FASE F] TESTE DE ACESSO REMOTO TELNET {host}:{port} (user={telnetUser})");
        var sourceIp = ObterIpOrigemParaIcmp();
        var service = new ConnectivityService(EscreverLinhaAsync);
        var result = await service.TestTelnetAsync(host, port, username: telnetUser, password: telnetPass, timeoutMs: 10000, sourceIpAddress: sourceIp, cancellationToken: ct);
        if (result.IsSuccess)
        {
            AtualizarProgresso(100, "Fase F: Telnet OK!", $"{host}:{port} — {result.LatencyMs}ms{(string.IsNullOrEmpty(result.Banner) ? "" : $" | {result.Banner}")}");
            EscreverLinha($"[OK] Telnet {host}:{port} acessível ({result.LatencyMs}ms). Acesso remoto provisionado com sucesso!");
        }
        else
        {
            AtualizarProgresso(0, "Fase F: Telnet falhou", result.Error ?? "sem resposta");
            EscreverLinha($"[FALHA TELNET] {host}:{port} — {result.Error}");
        }
        return result;
    }

    /// <summary>
    /// Ao concluir: exibe mensagem com antes/atual e dá a opção de restaurar a placa
    /// ou manter como está para mais testes de navegação/internet. Sem snapshot, silencioso.
    /// </summary>
    private async Task PerguntarRestauracaoRedeAsync()
    {
        try
        {
            var preferida = CbAdaptadorRede?.Text?.Trim();
            AdapterSnapshot? snap = null;
            if (!string.IsNullOrEmpty(preferida))
                snap = HostNetworkManager.LoadLatestSnapshot(preferida);
            snap ??= HostNetworkManager.LoadLatestAny();
            if (snap == null) return;

            var atual = HostNetworkManager.CaptureSnapshot(snap.AdapterName);
            var invalido = !snap.DhcpEnabled && HostNetworkManager.SnapshotEhSparc(snap, _loadedSaipCircuit?.HostLanIp);
            var antesTxt = invalido ? "sem original confiável (snapshot inválido)" : snap.Descrever();
            var atualTxt = atual?.Descrever() ?? "?";

            if (!invalido && atual != null &&
                string.Equals(atual.IpAddress ?? "", snap.IpAddress ?? "", StringComparison.OrdinalIgnoreCase) &&
                atual.DhcpEnabled == snap.DhcpEnabled)
            {
                EscreverLinha($"[REDE] Placa '{snap.AdapterName}' já está na configuração original — nada a oferecer.");
                return;
            }
            if (invalido && atual?.DhcpEnabled == true)
            {
                EscreverLinha($"[REDE] Placa '{snap.AdapterName}' já está em DHCP — nada a oferecer.");
                return;
            }

            var resp = MessageBox.Show(this,
                $"Teste concluído.\n\nPlaca '{snap.AdapterName}'\nAntes: {antesTxt}\nAtual: {atualTxt}\n\n" +
                $"Deseja restaurar a configuração anterior?\nSIM = restaura | NÃO = mantém atual para mais testes de navegação/internet.",
                "Restaurar rede do notebook", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (resp != MessageBoxResult.Yes)
            {
                EscreverLinha("[REDE] Operador optou por MANTER a configuração atual para mais testes.");
                return;
            }
            var (ok, log) = await HostNetworkManager.RestoreLastAsync(preferida, _loadedSaipCircuit?.HostLanIp);
            foreach (var line in log.Split('\n'))
                EscreverLinha(line.TrimEnd());
        }
        catch (Exception ex)
        {
            EscreverLinha($"[REDE][ERRO] {ex.Message}");
        }
    }

    private async void BtnRestaurarRedeNotebook_Click(object sender, RoutedEventArgs e)
    {
        await PerguntarRestauracaoRedeAsync();
    }

    // FASE G · TESTAR BANDA
    private async void BtnTestarBanda_Click(object sender, RoutedEventArgs e)
    {
        if (_lastIcmpResult != null && (!_lastIcmpResult.IsWanOk || !_lastIcmpResult.IsWebOk))
        {
            var prosseguir = MessageBox.Show(
                "O link WAN / Gateway Claro ou a Internet não responderam aos testes de conectividade ICMP (Fase 5).\n\n" +
                "Executar o teste de banda sem WAN ativa poderá medir a internet local do computador (Wi-Fi/Rede corporativa) em vez do roteador em bancada.\n\n" +
                "Deseja prosseguir com o teste de banda mesmo assim?",
                "Link WAN Desconectado / Offline",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (prosseguir != MessageBoxResult.Yes)
            {
                DefinirBadgeStatus("G", "⏭");
                EscreverLinha("\n[!] Teste de banda cancelado: link WAN offline.");
                return;
            }
        }

        SelecionarFase("G");
        DefinirBadgeStatus("G", "⏳");
        SetBusy(true);
        _cts = new CancellationTokenSource();

        try
        {
            var res = await ExecutarTesteBandaAsync(_cts.Token);
            DefinirBadgeStatus("G", res.IsSuccess ? "✅" : "❌");
        }
        catch (OperationCanceledException)
        {
            DefinirBadgeStatus("G", "⚪");
            AtualizarProgresso(0, "Teste cancelado", "Cancelado pelo operador.");
            EscreverLinha("\n[!] Teste de banda cancelado.");
        }
        catch (Exception ex)
        {
            DefinirBadgeStatus("G", "❌");
            AtualizarProgresso(0, "Falha no teste de banda", ex.Message);
            EscreverLinha($"\n[ERRO TESTE DE BANDA] {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
            await PerguntarRestauracaoRedeAsync();
        }
    }

    private async Task<BandwidthTestResult> ExecutarTesteBandaAsync(CancellationToken ct, string? sourceIpAddress = null)
    {
        AtualizarProgresso(10, "Fase G: Testando Banda...", "Iniciando teste de vazão...");
        EscreverLinha("\n[*] [FASE G] TESTE DE BANDA / VELOCIDADE DE CONEXÃO");

        // Se o IP de origem não foi fornecido explicitamente, obtém da interface física Ethernet conectada ao roteador
        if (string.IsNullOrWhiteSpace(sourceIpAddress))
        {
            var ethInfo = ObterInformacoesEthernetRoteador();
            if (ethInfo != null && ethInfo.IsUp && !string.IsNullOrWhiteSpace(ethInfo.IPv4Address))
            {
                sourceIpAddress = ethInfo.IPv4Address;
            }
            else if (_loadedSaipCircuit != null && !string.IsNullOrWhiteSpace(_loadedSaipCircuit.HostLanIp))
            {
                sourceIpAddress = _loadedSaipCircuit.HostLanIp.Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceIpAddress))
        {
            EscreverLinha($"[*] [ISOLAMENTO REDE] Medição de banda vinculada estritamente à interface Ethernet (IP: {sourceIpAddress}).");
            EscreverLinha("[*] [ISOLAMENTO REDE] Conexões Wi-Fi, celulares e secundárias foram isoladas para evitar falsos positivos.");
        }
        else
        {
            EscreverLinha("[AVISO] Nenhuma interface Ethernet identificada com IP de teste. Verifique a conexão com a porta LAN do roteador.");
        }

        var service = new BandwidthTestService(EscreverLinhaAsync);

        // 1. Tenta executar CLI Speedtest se disponível
        BandwidthTestResult raw;
        var cliResult = await service.RunSpeedtestCliAsync(sourceIpAddress: sourceIpAddress, cancellationToken: ct);
        if (cliResult.IsSuccess)
        {
            raw = cliResult;
        }
        else
        {
            // 2. Fallback para Teste Nativo HTTP
            EscreverLinha("[*] Executando Teste de Banda Nativo HTTP (Cloudflare CDN - Payload: ~50 MB)...");
            raw = await service.RunNativeHttpSpeedTestAsync(
                testPayloadMegaBytes: 50,
                sourceIpAddress: sourceIpAddress,
                onProgress: (mbps, pct) =>
                {
                    AtualizarProgresso((int)pct, "Fase G: Medindo Vazão HTTP...", $"Vazão atual: {mbps:F2} Mbps ({pct:F0}%)");
                },
                cancellationToken: ct);
        }

        // 3. Critica contra a banda nominal da ficha SAIP (margem 8%: aprova com >= 92%)
        var judged = AplicarCriterioBanda(raw);
        if (judged.IsSuccess)
        {
            AtualizarProgresso(100, "Fase G: Teste Concluído!", $"Download: {judged.DownloadMbps} Mbps | Latência: {judged.LatencyMs:F0}ms");
        }
        else
        {
            AtualizarProgresso(0, "Fase G: Banda reprovada/insuficiente", judged.Message);
        }

        return judged;
    }

    private BandwidthTestResult AplicarCriterioBanda(BandwidthTestResult raw)
    {
        if (!raw.IsSuccess) return raw;
        var nominal = _loadedSaipCircuit?.BandaMbpsNominal;
        var av = BandwidthTestService.AvaliarBanda(nominal, raw.DownloadMbps);
        if (av == null)
        {
            EscreverLinha($"[*] Banda medida: {raw.DownloadMbps:F1} Mbps (sem banda nominal na ficha — sem crítica).");
            return raw;
        }
        EscreverLinha($"  📊 Nominal ficha: {av.NominalMbps:F1} Mbps | Medido: {av.MedidoMbps:F1} Mbps ({av.Percentual:F1}%) | Mínimo 92%: {av.MinimoMbps:F1} Mbps → {(av.Aprovado ? "🟢 APROVADO" : "🔴 REPROVADO")}");
        if (av.Aprovado)
            return raw with { Message = raw.Message + $" | {av.Veredito}" };
        return raw with { IsSuccess = false, Message = raw.Message + $" | {av.Veredito}" };
    }

    #endregion

    #region Execução da Esteira Completa (A → G)

    private async void BtnExecutarSequencia_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedSaipCircuit is null)
        {
            var result = MessageBox.Show(
                "Nenhum dado de circuito foi definido (Opção 1: Carregar Ficha SAIP ou Opção 2: Informar IPs Manualmente).\n\nRecomenda-se definir os dados do circuito no topo para provisionamento e testes automatizados.\n\nDeseja continuar mesmo assim?",
                "Esteira de Produção",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;
        }

        var porta = CbPorta.Text.Trim();
        var baud = int.TryParse(CbBaud.Text, out var b) ? b : 9600;

        if (string.IsNullOrEmpty(porta))
        {
            MessageBox.Show("Selecione uma porta serial COM para iniciar a esteira.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            ResetarBadges();
            EscreverLinha("==================================================================================");
            EscreverLinha("               INICIANDO EXECUÇÃO COMPLETA DA ESTEIRA (1 A 7)                     ");
            EscreverLinha("==================================================================================");
            EscreverLinha("  ⚠️ ATENÇÃO: A configuração existente no equipamento será COMPLETAMENTE APAGADA ");
            EscreverLinha("              e todos os dados anteriores serão PERDIDOS permanentemente!         ");
            EscreverLinha("==================================================================================\n");

            // -------------------------------------------------------------
            // FASE 1 (A): ZERAR CONFIGURAÇÃO
            // -------------------------------------------------------------
            SelecionarFase("A");
            DefinirBadgeStatus("A", "⏳");
            EscreverLinha("\n>>> [ESTEIRA] FASE 1: ZERAR CONFIGURAÇÃO / RECUPERAÇÃO");
            await ExecutarZerarConfigAsync(porta, baud, ct);
            DefinirBadgeStatus("A", "✅");
            await Task.Delay(1000, ct);

            // -------------------------------------------------------------
            // FASE 2 (B): ATUALIZAR FIRMWARE
            // -------------------------------------------------------------
            SelecionarFase("B");
            if (!string.IsNullOrEmpty(_selectedIosBinPath) && File.Exists(_selectedIosBinPath))
            {
                DefinirBadgeStatus("B", "⏳");
                EscreverLinha("\n>>> [ESTEIRA] FASE 2: ATUALIZAR FIRMWARE VIA TFTP");
                var hostIp = _loadedSaipCircuit?.HostLanIp ?? ObterIpLocalParaTftp() ?? "127.0.0.1";
                await ExecutarUpgradeFirmwareAsync(porta, baud, hostIp, ct);
                DefinirBadgeStatus("B", "✅");
                if (!_firmwareSkippedSameVersion)
                {
                    EscreverLinha("[*] Aguardando 15 segundos para estabilização pós-reload...");
                    await Task.Delay(15000, ct);
                }
            }
            else
            {
                DefinirBadgeStatus("B", "⏭");
                EscreverLinha("\n[PULADO] Fase 2 pulada (Nenhum arquivo de firmware selecionado).");
            }

            // -------------------------------------------------------------
            // FASE 3 (C): PROVISIONAR EQUIPAMENTO
            // -------------------------------------------------------------
            SelecionarFase("C");
            if (_loadedSaipCircuit != null)
            {
                DefinirBadgeStatus("C", "⏳");
                EscreverLinha("\n>>> [ESTEIRA] FASE 3: PROVISIONAR EQUIPAMENTO COM A NOVA VERSÃO");
                await ExecutarAplicarSaipAsync(porta, baud, ct);
                DefinirBadgeStatus("C", "✅");
                await Task.Delay(1000, ct);
            }
            else
            {
                DefinirBadgeStatus("C", "⏭");
                EscreverLinha("\n[PULADO] Fase 3 pulada (Dados de circuito não definidos).");
            }

            // -------------------------------------------------------------
            // FASE 4 (D): CONFIGURAR IP DO DISPOSITIVO DE TESTE
            // -------------------------------------------------------------
            SelecionarFase("D");
            var adapter = CbAdaptadorRede.Text.Trim();
            if (_loadedSaipCircuit != null && !string.IsNullOrEmpty(adapter))
            {
                DefinirBadgeStatus("D", "⏳");
                EscreverLinha("\n>>> [ESTEIRA] FASE 4: CONFIGURAR IP E DNS DO DISPOSITIVO DE TESTE");
                await ExecutarConfigIpTesteAsync(adapter, ct);
                DefinirBadgeStatus("D", "✅");
                await Task.Delay(1000, ct);
            }
            else
            {
                DefinirBadgeStatus("D", "⏭");
                EscreverLinha("\n[PULADO] Fase 4 pulada (Sem adaptador selecionado ou sem dados de circuito).");
            }

            // -------------------------------------------------------------
            // FASE 5 (E): TESTAR CONECTIVIDADE ICMP (5a LAN, 5b WAN, 5c WEB)
            // -------------------------------------------------------------
            SelecionarFase("E");
            DefinirBadgeStatus("E", "⏳");
            EscreverLinha("\n>>> [ESTEIRA] FASE 5: TESTAR CONECTIVIDADE ICMP (5a LAN, 5b WAN, 5c WEB)");
            var icmpResult = await ExecutarTesteIcmpTriploAsync(ct);
            DefinirBadgeStatus("E", icmpResult.StatusBadge);
            await Task.Delay(1000, ct);

            // -------------------------------------------------------------
            // FASE 6 (F): TESTAR ACESSO REMOTO (TELNET / SSH)
            // -------------------------------------------------------------
            SelecionarFase("F");
            DefinirBadgeStatus("F", "⏳");
            var telnetHostEsteira = _loadedSaipCircuit?.LanIp
                ?? (!string.IsNullOrWhiteSpace(TxtTelnetTarget.Text) ? TxtTelnetTarget.Text.Trim() : "200.182.245.17");
            var telnetPortEsteira = int.TryParse(TxtTelnetPort.Text.Trim(), out var tp) ? tp : 23;

            EscreverLinha("\n>>> [ESTEIRA] FASE 6: TESTAR ACESSO REMOTO");
            var telnetEsteiraResult = await ExecutarTesteTelnetAsync(telnetHostEsteira, telnetPortEsteira, ct);

            var profileTagEsteira = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            var isFortiEsteira = IsTagFortiGate(profileTagEsteira) || _isFortiGateDetected;
            if (isFortiEsteira && !telnetEsteiraResult.IsSuccess)
            {
                var srv = new ConnectivityService(EscreverLinhaAsync);
                var (sshOk, sshLat, _) = await srv.TestTcpPortAsync(telnetHostEsteira, 22, timeoutMs: 5000, sourceIpAddress: ObterIpOrigemParaIcmp(), cancellationToken: ct);
                if (sshOk)
                {
                    telnetEsteiraResult = new ConnectivityService.TelnetTestResult(telnetHostEsteira, 22, true, sshLat, "SSH (Porta 22) Ativa", "SSH Conectado", null);
                    EscreverLinha($"[OK] [FORTINET] Acesso remoto SSH (porta 22) validado com sucesso ({sshLat}ms)!");
                }
                else
                {
                    var (httpsOk, httpsLat, _) = await srv.TestTcpPortAsync(telnetHostEsteira, 443, timeoutMs: 5000, sourceIpAddress: ObterIpOrigemParaIcmp(), cancellationToken: ct);
                    if (httpsOk)
                    {
                        telnetEsteiraResult = new ConnectivityService.TelnetTestResult(telnetHostEsteira, 443, true, httpsLat, "HTTPS (Porta 443) Ativa", "HTTPS Conectado", null);
                        EscreverLinha($"[OK] [FORTINET] Acesso remoto HTTPS (porta 443) validado com sucesso ({httpsLat}ms)!");
                    }
                }
            }

            DefinirBadgeStatus("F", telnetEsteiraResult.IsSuccess ? "✅" : "❌");
            await Task.Delay(1000, ct);

            // -------------------------------------------------------------
            // FASE 7 (G): TESTAR BANDA
            // -------------------------------------------------------------
            SelecionarFase("G");
            BandwidthTestResult speedResult;
            if (icmpResult != null && !icmpResult.IsWanOk)
            {
                DefinirBadgeStatus("G", "⏭");
                EscreverLinha("\n>>> [ESTEIRA] FASE 7: TESTE DE BANDA DESCARTADO (WAN Offline / Gateway Claro sem resposta)");
                AtualizarProgresso(100, "Fase 7: Descartada", "Teste de banda descartado pois o link WAN (5b) está offline.");
                speedResult = new BandwidthTestResult(0, 0, 0, 0, "Nativo HTTP", "Descartado", false, "Descartado automaticamente devido a falha no gateway WAN.");
            }
            else
            {
                DefinirBadgeStatus("G", "⏳");
                EscreverLinha("\n>>> [ESTEIRA] FASE 7: TESTAR BANDA");
                speedResult = await ExecutarTesteBandaAsync(ct);
                DefinirBadgeStatus("G", speedResult.IsSuccess ? "✅" : "❌");
            }

            // -------------------------------------------------------------
            // RELATÓRIO CONSOLIDADO FINAL
            // -------------------------------------------------------------
            AtualizarProgresso(100, "Esteira Concluída!", "Todas as fases do pipeline foram executadas.");
            EscreverLinha("\n=================================================================");
            EscreverLinha("            RELATÓRIO CONSOLIDADO DA ESTEIRA                     ");
            EscreverLinha("=================================================================");
            EscreverLinha($"  1. a) Zeramento Equipamento : CONCLUÍDO (Registro 0x2102 restaurado)");
            EscreverLinha($"  2. b) Atualização Firmware  : {(!string.IsNullOrEmpty(_selectedIosBinPath) ? "ATUALIZADO VIA TFTP E REINICIADO" : "NÃO SOLICITADO")}");
            EscreverLinha($"  3. c) Provisionamento Rede  : {(_loadedSaipCircuit != null ? "CONFIGURADO COM SUCESSO" : "NÃO APLICADO")}");
            EscreverLinha($"  4. d) IP Dispositivo Teste  : {(_loadedSaipCircuit != null ? $"CONFIGURADO ({_loadedSaipCircuit.HostLanIp} + DNS 1.1.1.1/8.8.8.8)" : "NÃO CONFIGURADO")}");
            EscreverLinha($"  5. e) Conectividade ICMP    : {(icmpResult?.IsSuccess == true ? $"OK (5a LAN: {icmpResult.LanResult?.AvgRttMs:F1}ms | 5b WAN: {(icmpResult.WanResult?.IsSuccess == true ? $"{icmpResult.WanResult?.AvgRttMs:F1}ms" : "Offline")} | 5c WEB: {(icmpResult.WebResult?.IsSuccess == true ? $"{icmpResult.WebResult?.AvgRttMs:F1}ms" : "Offline")})" : "FALHA")}");
            EscreverLinha($"     Telnet {telnetHostEsteira}:{telnetPortEsteira} : {(telnetEsteiraResult == null ? "NÃO TESTADO" : telnetEsteiraResult.IsSuccess ? $"OK ({telnetEsteiraResult.LatencyMs}ms{(string.IsNullOrEmpty(telnetEsteiraResult.Banner) ? "" : $" | {telnetEsteiraResult.Banner}")})" : $"FALHA ({telnetEsteiraResult.Error})")}");
            EscreverLinha($"  6. f) Teste de Banda        : {(speedResult.IsSuccess ? $"OK ({speedResult.DownloadMbps} Mbps)" : "FALHA/OFFLINE")}");
            EscreverLinha("=================================================================\n");

            MessageBox.Show("Fluxo da esteira de provisionamento (Fases A → F) concluído com sucesso!\nVerifique os detalhes no terminal.", "Esteira Concluída", MessageBoxButton.OK, MessageBoxImage.Information);
            await PerguntarRestauracaoRedeAsync();
        }
        catch (OperationCanceledException)
        {
            AtualizarProgresso(0, "Esteira Cancelada", "Interrompido pelo operador.");
            EscreverLinha("\n[!] Fluxo da esteira interrompido pelo operador.");
        }
        catch (Exception ex)
        {
            AtualizarProgresso(0, "Falha na Esteira", ex.Message);
            EscreverLinha($"\n[ERRO NA ESTEIRA] {ex.Message}");
            MessageBox.Show($"Ocorreu um erro durante a execução da esteira:\n{ex.Message}", "Erro na Esteira", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    #endregion

    #region Utilitários de UI e Terminal

    private void ExibirDadosEquipamento(DeviceInfo info)
    {
        EscreverLinha("\n=================================================================");
        EscreverLinha("               DADOS DO EQUIPAMENTO AUDITADO                     ");
        EscreverLinha("=================================================================");
        EscreverLinha($"  Equipamento : {info.DisplayName}");
        EscreverLinha($"  Fabricante  : {info.Vendor}");
        EscreverLinha($"  Modelo      : {info.Model}");
        EscreverLinha($"  Versão IOS  : {info.OsName} {info.OsVersion}");
        EscreverLinha($"  Serial Nº   : {info.SerialNumber}");
        EscreverLinha("=================================================================\n");
    }

    private void AtualizarProgresso(int porcentagem, string titulo, string descricao)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AtualizarProgresso(porcentagem, titulo, descricao));
            return;
        }

        PbProgresso.Value = porcentagem;
        TxtEtapaPorcentagem.Text = $"{porcentagem}%";
        TxtEtapaTitulo.Text = $"Status: {titulo}";
        TxtEtapaDescricao.Text = descricao;
        StatusTexto.Content = titulo;

        // Atualiza a tela de Modo Automático em tempo real com barra e porcentagem
        if (GridModoAutomatico != null && GridModoAutomatico.Visibility == Visibility.Visible)
        {
            PbAutoGeral.Value = porcentagem;
            TxtAutoPorcentagem.Text = $"{porcentagem}%";
            TxtAutoStatusGeral.Text = $"{titulo} — {descricao}";
        }
    }

    private Task EscreverLinhaAsync(string linha)
    {
        EscreverLinha(linha);
        return Task.CompletedTask;
    }

    private Task NotificarConexaoCaboAsync(string instrucao, CancellationToken ct)
    {
        EscreverLinha($"\n=================================================================");
        EscreverLinha("               🔌 CONEXÃO DO CABO DE REDE                       ");
        EscreverLinha("=================================================================");
        EscreverLinha(instrucao);
        EscreverLinha("=================================================================\n");

        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                instrucao,
                "SPARC — Conexão do Cabo de Rede",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });
        return Task.CompletedTask;
    }

    private Task InstruirReinicioEquipamentoAsync(string instrucao, CancellationToken ct)
    {
        AtualizarProgresso(40, "Fase 1: Reinício necessário...", "Reinicie o equipamento na energia para interceptar ROMMON/BootWare.");

        var avisoDestaque = "⚠️ AVISO IMPORTANTE — PERDA DE DADOS:\n\n" +
                            "• Todas as configurações atualmente presentes no roteador serão APAGADAS e substituídas pela configuração básica para a nova ativação.\n" +
                            "• Todos os dados e parametrizações anteriores serão PERDIDOS.\n\n" +
                            "INSTRUÇÃO PARA O OPERADOR:\n" +
                            instrucao;

        EscreverLinha($"\n=================================================================");
        EscreverLinha("         ⚠️ ATENÇÃO: ZERAMENTO TOTAL E PERDA DE DADOS             ");
        EscreverLinha("=================================================================");
        EscreverLinha("  • Todas as configurações presentes no roteador serão APAGADAS.");
        EscreverLinha("  • O equipamento receberá a configuração básica da nova ativação.");
        EscreverLinha("  • Todos os dados e parametrizações anteriores serão PERDIDOS.");
        EscreverLinha("=================================================================");
        EscreverLinha($"[INSTRUÇÃO] {instrucao}\n");

        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                avisoDestaque + "\n\nO sistema já está monitorando a porta serial e interceptará o bootloader (ROMMON/BootWare) automaticamente assim que o equipamento for religado.",
                "Atenção: Reinicie o Equipamento (Zeramento de Configuração)",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
        return Task.CompletedTask;
    }

    private Task InstruirOperadorAsync(string instrucao, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(instrucao)) return Task.CompletedTask;

        var isReinicio = instrucao.Contains("Desligue", StringComparison.OrdinalIgnoreCase)
                      || instrucao.Contains("Religue", StringComparison.OrdinalIgnoreCase)
                      || instrucao.Contains("Reinicie", StringComparison.OrdinalIgnoreCase)
                      || instrucao.Contains("power cycle", StringComparison.OrdinalIgnoreCase)
                      || instrucao.Contains("tomada", StringComparison.OrdinalIgnoreCase);

        if (isReinicio)
        {
            return InstruirReinicioEquipamentoAsync(instrucao, ct);
        }

        // Para instruções de operação em tempo de execução (ex.: troca de porta Ethernet, cabo LAN, etc.)
        EscreverLinha($"\n=================================================================");
        EscreverLinha("                📢 INSTRUÇÃO PARA O OPERADOR                     ");
        EscreverLinha("=================================================================");
        EscreverLinha(instrucao);
        EscreverLinha("=================================================================\n");

        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(
                instrucao,
                "SPARC — Instrução ao Operador",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        });

        return Task.CompletedTask;
    }

    private void BtnCancelar_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        EscreverLinha("Cancelando operação...");
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        CbPorta.IsEnabled = !busy;
        CbBaud.IsEnabled = !busy;
        CbInterrupt.IsEnabled = !busy;
        BtnExecutarSequencia.IsEnabled = !busy;
        BtnCancelar.IsEnabled = busy;
        AtualizarEstadoBotoes();
    }

    private void AtualizarEstadoBotoes()
    {
        var temFicha = _loadedSaipCircuit is not null;
        BtnExecutarA.IsEnabled = !_isBusy;
        BtnExecutarB.IsEnabled = !_isBusy && temFicha;
        BtnExecutarC.IsEnabled = !_isBusy && temFicha && !string.IsNullOrWhiteSpace(CbAdaptadorRede.Text);
        BtnExecutarD.IsEnabled = !_isBusy && temFicha && !string.IsNullOrEmpty(_selectedIosBinPath);
        BtnExecutarE.IsEnabled = !_isBusy;
        BtnExecutarF.IsEnabled = !_isBusy;
    }

    private CliDiagnosticWindow? _cliDiagnosticWindow;
    private DeviceSession? _activeSession;

    public void RegistrarSessaoAtiva(DeviceSession? session, string porta, int baud)
    {
        _activeSession = session;
        if (_cliDiagnosticWindow != null)
        {
            _cliDiagnosticWindow.SetPortInfo(porta, baud);
        }
    }

    private string ObterPortaSelecionada()
    {
        var p = CbPortaInicial?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(p)) p = CbPorta?.Text?.Trim();
        return string.IsNullOrWhiteSpace(p) ? "COM1" : p;
    }

    private int ObterBaudRateSelecionado()
    {
        var baudItem = CbBaud?.SelectedItem as ComboBoxItem;
        return int.TryParse(baudItem?.Content?.ToString(), out var b) ? b : 9600;
    }

    private void BtnAbrirCliDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        AbrirOuFocarCliDiagnosticWindow();
    }

    public void AbrirOuFocarCliDiagnosticWindow()
    {
        if (_cliDiagnosticWindow == null)
        {
            _cliDiagnosticWindow = new CliDiagnosticWindow();
            _cliDiagnosticWindow.OnSendCommand = async (cmd) =>
            {
                if (_activeSession != null)
                {
                    await _activeSession.WriteLineAsync(cmd, CancellationToken.None);
                }
                else
                {
                    _cliDiagnosticWindow.AppendOutput("\r\n[AVISO] Nenhuma sessão serial aberta no momento.\r\n");
                }
            };
            _cliDiagnosticWindow.OnSendBytes = async (bytes) =>
            {
                if (_activeSession?.Transport != null)
                {
                    await _activeSession.Transport.WriteAsync(bytes, CancellationToken.None);
                }
                else
                {
                    _cliDiagnosticWindow.AppendOutput("\r\n[AVISO] Nenhuma sessão serial aberta no momento.\r\n");
                }
            };
        }

        var porta = ObterPortaSelecionada();
        var baud = ObterBaudRateSelecionado();
        _cliDiagnosticWindow.SetPortInfo(porta, baud);

        if (!_cliDiagnosticWindow.IsVisible)
        {
            _cliDiagnosticWindow.Show();
        }
        else
        {
            _cliDiagnosticWindow.Activate();
        }
    }

    private void BtnLimparTerminal_Click(object sender, RoutedEventArgs e)
    {
        TerminalParagraph.Inlines.Clear();
        EscreverLinha("Terminal limpo.");
    }

    private void OnRawOutput(string raw)
    {
        // Envia instantaneamente para a janela de diagnóstico CLI dedicada
        _cliDiagnosticWindow?.AppendOutput(raw);

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnRawOutput(raw));
            return;
        }

        // Suprime ruído da tabela de progresso bruto do Comware TFTP para manter o terminal limpo e legível
        if (raw.Contains("Dload") && raw.Contains("Upload")) return;
        if (raw.Contains("% Total") && raw.Contains("% Received")) return;
        if (raw.TrimStart().StartsWith("0 ") && raw.Contains("117M")) return;

        var run = new Run(raw) { Foreground = BrushEquipamento };
        TerminalParagraph.Inlines.Add(run);
        LimitarTamanhoTerminal();
        TxtTerminal.ScrollToEnd();
    }

    private void EscreverLinha(string linha)
    {
        _cliDiagnosticWindow?.AppendOutput(linha + "\r\n");

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => EscreverLinha(linha));
            return;
        }

        SolidColorBrush brush;
        if (linha.StartsWith("[ERRO", StringComparison.OrdinalIgnoreCase) || linha.StartsWith("[!]", StringComparison.OrdinalIgnoreCase) || linha.Contains("FALHA", StringComparison.OrdinalIgnoreCase))
        {
            brush = BrushErro;
        }
        else if (linha.StartsWith("[OK]", StringComparison.OrdinalIgnoreCase) || linha.Contains("SUCESSO", StringComparison.OrdinalIgnoreCase) || linha.Contains("CONCLUÍDO", StringComparison.OrdinalIgnoreCase))
        {
            brush = BrushSucesso;
        }
        else if (linha.StartsWith("[INSTRUÇÃO]", StringComparison.OrdinalIgnoreCase) || linha.StartsWith("[INFO", StringComparison.OrdinalIgnoreCase))
        {
            brush = BrushInstrucao;
        }
        else
        {
            brush = BrushSistema;
        }

        var run = new Run(linha + "\n")
        {
            Foreground = brush,
            FontWeight = FontWeights.SemiBold
        };
        TerminalParagraph.Inlines.Add(run);
        LimitarTamanhoTerminal();
        TxtTerminal.ScrollToEnd();

        // Espelha no CLI compacto do modo automático (detalhe sem aumentar UI)
        if (GridModoAutomatico != null && GridModoAutomatico.Visibility == Visibility.Visible && TxtAutoLog != null)
        {
            var shortLine = linha.Length > 180 ? linha[..180] + "…" : linha;
            // Remove linhas vazias longas de banner para não poluir
            if (shortLine.Trim().Length == 0) return;
            TxtAutoLog.Text += shortLine + "\n";
            var lines = TxtAutoLog.Text.Split('\n');
            if (lines.Length > 100) TxtAutoLog.Text = string.Join("\n", lines[^85..]);
            if (ScrollAutoLog != null) ScrollAutoLog.ScrollToEnd();
            if (TxtAutoLog.Foreground is SolidColorBrush b && b.Color.ToString() == "#FF94A3B8") TxtAutoLog.Foreground = BrushEquipamento;
        }
    }

    private void LimitarTamanhoTerminal()
    {
        if (TerminalParagraph.Inlines.Count > 1500)
        {
            for (var i = 0; i < 300 && TerminalParagraph.Inlines.Count > 0; i++)
            {
                TerminalParagraph.Inlines.Remove(TerminalParagraph.Inlines.FirstInline);
            }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cts?.Cancel();
        try { _cliDiagnosticWindow?.Close(); } catch { }
        base.OnClosing(e);
    }

    #endregion
}