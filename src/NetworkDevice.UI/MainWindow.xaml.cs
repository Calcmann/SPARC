using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Backup;
using NetworkDevice.Core.Device;
using NetworkDevice.Core.Diagnostics;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;
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
    private TripleIcmpResult? _lastIcmpResult;

    public MainWindow()
    {
        InitializeComponent();
    }

    private bool _serialOk;
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CbModeloRoteadorInicial.SelectedIndex = -1;
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

    private void CbModeloRoteadorInicial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
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
        AtualizarBotaoProsseguir();
    }

    private void AtualizarBotaoProsseguir()
    {
        if (BtnAvancarParaEsteira == null) return;
        var modeloOk = CbModeloRoteadorInicial?.SelectedIndex > 0;
        var modoManual = RbModoManual?.IsChecked == true;
        bool insumoOk = _loadedSaipCircuit != null;
        var auto = RbExecAuto?.IsChecked == true;
        var firmwareOk = !auto || (!string.IsNullOrEmpty(_selectedIosBinPath) && System.IO.File.Exists(_selectedIosBinPath));
        var ok = _serialOk && modeloOk && insumoOk && firmwareOk;
        BtnAvancarParaEsteira.IsEnabled = ok;

        // Atualiza checklist visual expandido de 4 itens e badges de cada passo
        AtualizarChecklist(_serialOk, modeloOk, insumoOk, firmwareOk, auto, modoManual);
    }

    private void AtualizarChecklist(bool serialOk, bool modeloOk, bool insumoOk, bool firmwareOk, bool isAuto, bool modoManual)
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
            if (isAuto)
            {
                if (firmwareOk)
                {
                    BadgeStep3.Background = brushVerdeBg;
                    TxtBadgeStep3.Text = "🟢 Passo 3 OK (Auto + Firmware)";
                    TxtBadgeStep3.Foreground = brushVerdeTxt;
                }
                else
                {
                    BadgeStep3.Background = brushAmareloBg;
                    TxtBadgeStep3.Text = "🟡 Falta Selecionar Firmware";
                    TxtBadgeStep3.Foreground = brushAmareloTxt;
                }
            }
            else
            {
                BadgeStep3.Background = brushVerdeBg;
                TxtBadgeStep3.Text = "🟢 Passo 3 OK (Semi-Automático)";
                TxtBadgeStep3.Foreground = brushVerdeTxt;
            }
        }

        // 2. CARDS LADO A LADO DO CHECKLIST
        if (CardChkModelo != null && TxtChkModeloIcon != null && TxtChkModeloSub != null)
        {
            CardChkModelo.Background = modeloOk ? brushVerdeBg : brushVermelhoBg;
            CardChkModelo.BorderBrush = modeloOk ? brushVerdeBorder : brushVermelhoBorder;
            TxtChkModeloIcon.Text = modeloOk ? "🟢 1. Modelo OK" : "🔴 1. Modelo";
            TxtChkModeloIcon.Foreground = modeloOk ? brushVerdeTxt : brushVermelhoTxt;
            var item = CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem;
            TxtChkModeloSub.Text = modeloOk ? (item?.Content?.ToString()?.Replace("🖧", "")?.Trim() ?? "Selecionado") : "Pendente: selecione";
        }

        if (CardChkSerial != null && TxtChkSerialIcon != null && TxtChkSerialSub != null)
        {
            CardChkSerial.Background = serialOk ? brushVerdeBg : brushVermelhoBg;
            CardChkSerial.BorderBrush = serialOk ? brushVerdeBorder : brushVermelhoBorder;
            TxtChkSerialIcon.Text = serialOk ? "🟢 2. Serial OK" : "🔴 2. Cabo Serial";
            TxtChkSerialIcon.Foreground = serialOk ? brushVerdeTxt : brushVermelhoTxt;
            TxtChkSerialSub.Text = serialOk ? $"{CbPortaInicial?.Text?.Trim()} conectada" : "Pendente: clique 'Testar'";
        }

        if (CardChkDados != null && TxtChkDadosIcon != null && TxtChkDadosSub != null)
        {
            CardChkDados.Background = insumoOk ? brushVerdeBg : brushVermelhoBg;
            CardChkDados.BorderBrush = insumoOk ? brushVerdeBorder : brushVermelhoBorder;
            TxtChkDadosIcon.Text = insumoOk ? "🟢 3. Circuito OK" : "🔴 3. Ficha SAIP";
            TxtChkDadosIcon.Foreground = insumoOk ? brushVerdeTxt : brushVermelhoTxt;
            TxtChkDadosSub.Text = insumoOk ? (_loadedSaipCircuit?.DesignacaoIp ?? "Circuito carregado") : (modoManual ? "Pendente: aplicar IPs" : "Pendente: selecione SAIP");
        }

        if (CardChkFirmware != null && TxtChkFirmwareIcon != null && TxtChkFirmwareSub != null)
        {
            if (isAuto)
            {
                CardChkFirmware.Background = firmwareOk ? brushVerdeBg : brushVermelhoBg;
                CardChkFirmware.BorderBrush = firmwareOk ? brushVerdeBorder : brushVermelhoBorder;
                TxtChkFirmwareIcon.Text = firmwareOk ? "🟢 4. Firmware OK" : "🔴 4. Firmware";
                TxtChkFirmwareIcon.Foreground = firmwareOk ? brushVerdeTxt : brushVermelhoTxt;
                TxtChkFirmwareSub.Text = firmwareOk ? Path.GetFileName(_selectedIosBinPath) : "Pendente: selecione .ipe/.bin";
            }
            else
            {
                CardChkFirmware.Background = brushCinzaBg;
                CardChkFirmware.BorderBrush = brushCinzaBorder;
                TxtChkFirmwareIcon.Text = "⚪ 4. Firmware";
                TxtChkFirmwareIcon.Foreground = brushCinzaTxt;
                TxtChkFirmwareSub.Text = "Opcional (Modo Passo a Passo)";
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

    private void CbPortaInicial_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos || CbPorta is null || CbPortaInicial is null)
            return;

        _syncingCombos = true;
        try
        {
            CbPorta.Text = CbPortaInicial.Text;
            CbPorta.SelectedItem = CbPortaInicial.SelectedItem;
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
            CbPortaInicial.Text = CbPorta.Text;
            CbPortaInicial.SelectedItem = CbPorta.SelectedItem;
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

    private async void BtnTestarSerialInicial_Click(object sender, RoutedEventArgs e)
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

        _serialTestCts = new CancellationTokenSource();
        var localCts = _serialTestCts;
        BtnTestarSerialInicial.Content = "⏹ Cancelar";
        BtnTestarSerialInicial.Background = BrushErro;
        TxtSerialTestStatus.Text = "⏳ Testando...";
        TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));

        try
        {
            // Timeout duro de 10s com CTS separado - nunca fica eterno
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linkedTimeout = CancellationTokenSource.CreateLinkedTokenSource(localCts.Token, timeoutCts.Token);

            _serialTestTask = TestarConexaoSerialAsync(porta, baud, localCts.Token);
            var completed = await Task.WhenAny(_serialTestTask, Task.Delay(Timeout.Infinite, linkedTimeout.Token));

            // Se timeoutCts disparou (10s sem resposta)
            if (timeoutCts.IsCancellationRequested && !localCts.IsCancellationRequested)
            {
                try { localCts.Cancel(); } catch { }
                try { await _serialTestTask; } catch { }
                var portasDisp = System.IO.Ports.SerialPort.GetPortNames();
                var listaPortas = portasDisp.Length > 0 ? string.Join(", ", portasDisp) : "(nenhuma porta COM/USB detectada)";
                var detalhePortas = portasDisp.Length > 0 ? $"Portas COM disponíveis no sistema: {listaPortas}" : "Nenhuma porta COM/USB foi detectada — verifique driver do conversor USB-Serial";
                EscreverLinha($"[ERRO CONEXÃO SERIAL] {porta} @ {baud} — sem resposta após 10s.");
                EscreverLinha($"    {detalhePortas}");
                EscreverLinha($"    Verifique: cabo console, porta COM correta, baud {baud} e energização.");
                EscreverLinha($"    💡 OBSERVAÇÃO: Se você acabou de ligar o equipamento, aguarde a inicialização completa do mesmo (geralmente de 2 a 5 minutos) antes de testar a conexão.");
                _serialOk = false; AtualizarBotaoProsseguir();
                TxtSerialTestStatus.Text = "❌ Sem resposta (10s)";
                TxtSerialTestStatus.Foreground = BrushErro;
                AtualizarPortas();

                var msg = $"Conexão serial falhou em {porta} @ {baud} após 10s sem resposta.\n\n" +
                          $"{detalhePortas}\n" +
                          $"Porta testada: {porta}\n\n" +
                          $"ORIENTAÇÕES IMPORTANTES:\n" +
                          $"• Se você acabou de ligar o equipamento na energia, aguarde a inicialização completa do mesmo (geralmente de 2 a 5 minutos) antes de testar a conexão serial.\n" +
                          $"• Veja em Gerenciador de Dispositivos > Portas (COM e LPT) se a porta COM correta foi selecionada.\n" +
                          $"• Desconecte/reconecte o cabo USB-Serial para identificar a porta.\n" +
                          $"• Baud {baud} (padrão 9600 8-N-1).\n" +
                          $"• Cabo console firmemente conectado na porta CONSOLE e equipamento energizado.\n\n" +
                          $"Deseja selecionar outra porta agora?";
                var resp = MessageBox.Show(msg, "Erro Conexão Serial — Confirme a Porta", MessageBoxButton.YesNo, MessageBoxImage.Error);
                if (resp == MessageBoxResult.Yes)
                {
                    // Foca no combo para o usuário corrigir
                    CbPortaInicial.Focus();
                    if (CbPortaInicial.IsDropDownOpen == false) CbPortaInicial.IsDropDownOpen = true;
                }
                return;
            }

            // Cancelado pelo usuário
            if (localCts.IsCancellationRequested)
            {
                try { await _serialTestTask; } catch (OperationCanceledException) { }
                EscreverLinha($"[!] Teste serial cancelado pelo usuário.");
                TxtSerialTestStatus.Text = "↺ Cancelado";
                TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                return;
            }

            var ok = await (Task<bool>)_serialTestTask;
            _serialOk = ok;
            AtualizarBotaoProsseguir();
            if (ok)
            {
                if (TxtSerialTestStatus.Text?.Contains("ROMMON") != true)
                {
                    TxtSerialTestStatus.Text = "✅ Serial OK";
                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
                }
            }
            else
            {
                var portasDisp3 = System.IO.Ports.SerialPort.GetPortNames();
                var lista3 = portasDisp3.Length > 0 ? string.Join(", ", portasDisp3) : "(nenhuma porta detectada)";
                EscreverLinha($"    Portas COM disponíveis: {lista3} — confirme se {porta} é a correta.");
                TxtSerialTestStatus.Text = "❌ Sem resposta";
                TxtSerialTestStatus.Foreground = BrushErro;
                AtualizarPortas();
                var msg2 = $"Sem resposta em {porta}. Portas disponíveis: {lista3}\n\nA porta selecionada está correta?";
                var r2 = MessageBox.Show(msg2, "Conexão Serial — Confirme a Porta", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r2 == MessageBoxResult.Yes) { CbPortaInicial.Focus(); CbPortaInicial.IsDropDownOpen = true; }
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
            BtnTestarSerialInicial.Content = "🔌 Testar Conexão Serial";
            BtnTestarSerialInicial.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F766E"));
            BtnTestarSerialInicial.IsEnabled = true;
        }
    }

    private async Task<bool> TestarConexaoSerialAsync(string porta, int baud, CancellationToken ct)
    {
        EscreverLinha($"\n[*] [TESTE SERIAL] Verificando acesso em {porta} @ {baud} baud (8-N-1) — timeout 10s...");
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
                    ConnectTimeout = TimeSpan.FromSeconds(8),
                    CommandTimeout = TimeSpan.FromSeconds(8)
                });
            session.RawOutput += OnRawOutput;
            // Registra cancelamento para fechar porta e desbloquear BaseStream.ReadAsync pendente
            using var reg = ct.Register(() => { try { session.Transport.CloseAsync().Wait(500); } catch { } try { transport.DisposeAsync().AsTask().Wait(500); } catch { } });
            await session.ConnectAsync(ct);
            var prompt = session.CurrentPrompt ?? "(sem prompt)";

            var isRommon = session.Mode == NetworkDevice.Core.Session.ExecMode.Rommon ||
                           prompt.Trim().StartsWith("rommon", StringComparison.OrdinalIgnoreCase);

            if (isRommon)
            {
                EscreverLinha("\n=================================================================");
                EscreverLinha("   ⚠️ ROTEADOR CISCO EM MODO ROMMON (SEM FIRMWARE / FLASH VAZIA) ");
                EscreverLinha("=================================================================");
                EscreverLinha($"  Prompt detectado   : {prompt}");
                EscreverLinha("  Diagnóstico        : O roteador Cisco está no bootloader ROMMON.");
                EscreverLinha("  Possíveis Causas   : • Memória Flash sem arquivo de boot (.bin) válido.");
                EscreverLinha("                       • Imagem IOS existente na Flash ausente ou corrompida.");
                EscreverLinha("                       • Registrador de boot ajustado para modo de manutenção.");
                EscreverLinha("  Status Conexão     : ✅ Cabo Serial e Porta COM comunicando perfeitamente!");
                EscreverLinha("  Ação Recomendada   : O equipamento está pronto para receber novo firmware.");
                EscreverLinha("                       Avance para a Fase B (TFTP) ou Modo Automático.");
                EscreverLinha("=================================================================\n");

                Dispatcher.Invoke(() =>
                {
                    TxtSerialTestStatus.Text = "⚠️ Cisco em ROMMON (Sem Firmware)";
                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));

                    if (CardChkSerial != null && TxtChkSerialIcon != null && TxtChkSerialSub != null)
                    {
                        TxtChkSerialIcon.Text = "🟡 2. Serial (ROMMON)";
                        TxtChkSerialSub.Text = "Cisco sem firmware (ROMMON)";
                    }

                    // Se o modelo ainda não foi selecionado, auto-seleciona Cisco
                    if (CbModeloRoteadorInicial != null && CbModeloRoteadorInicial.SelectedIndex <= 0)
                    {
                        CbModeloRoteadorInicial.SelectedIndex = 2; // Cisco Série 1900
                    }
                });
            }
            else
            {
                EscreverLinha($"[OK] Serial OK — prompt detectado: {prompt} (Modo: {session.Mode})");
                EscreverLinha($"[OK] Cabo serial, porta {porta} e baud {baud} validados com sucesso!");

                Dispatcher.Invoke(() =>
                {
                    TxtSerialTestStatus.Text = "✅ Serial OK";
                    TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));

                    if (CardChkSerial != null && TxtChkSerialIcon != null && TxtChkSerialSub != null)
                    {
                        TxtChkSerialIcon.Text = "🟢 2. Cabo Serial";
                        TxtChkSerialSub.Text = $"Conectado em {porta}";
                    }
                });
            }

            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var portasDisp2 = System.IO.Ports.SerialPort.GetPortNames();
            var lista2 = portasDisp2.Length > 0 ? string.Join(", ", portasDisp2) : "(nenhuma)";
            EscreverLinha($"[FALHA SERIAL] {porta} @ {baud} — {ex.Message}");
            EscreverLinha($"    Portas COM disponíveis: {lista2} | Verifique: porta {porta}, baud {baud}, cabo e energização.");
            EscreverLinha($"    💡 NOTA: Se acabou de ligar o equipamento, aguarde a inicialização completa do mesmo (geralmente de 2 a 5 minutos).");
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

    private async void BtnAvaliarEquipamento_Click(object sender, RoutedEventArgs e)
    {
        var porta = CbPortaInicial?.Text?.Trim();
        if (string.IsNullOrEmpty(porta) || porta == "(nenhuma)")
            porta = CbPorta?.Text?.Trim();

        if (string.IsNullOrEmpty(porta) || porta == "(nenhuma)")
        {
            MessageBox.Show("Selecione a porta serial do equipamento (ex: COM1 ou COM4) antes de iniciar a avaliação.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var baud = int.TryParse(CbBaud?.Text, out var b) ? b : 9600;
        SetBusy(true);
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            AtualizarProgresso(10, "Avaliando Equipamento...", $"Conectando em {porta} @ {baud} bps...");
            EscreverLinha("\n================================================================================");
            EscreverLinha("               🔍 AVALIAÇÃO E DIAGNÓSTICO DO EQUIPAMENTO CONECTADO              ");
            EscreverLinha("================================================================================");
            EscreverLinha($"  Porta Serial : {porta} @ {baud} bps");
            EscreverLinha($"  Data / Hora  : {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n");

            var sessionOptions = new SessionOptions
            {
                PromptMatcher = RegexPromptMatcher.Universal(),
                CommandTimeout = TimeSpan.FromSeconds(15),
                ConnectTimeout = TimeSpan.FromSeconds(20)
            };

            var transport = new SerialTransport(porta, baud);
            await using var session = new DeviceSession(transport, sessionOptions);
            session.RawOutput += OnRawOutput;

            await session.ConnectAsync(ct);

            // 1. Diagnóstico do Estado de Acesso / Senha / ROMMON
            AtualizarProgresso(25, "Diagnosticando Estado de Acesso...", "Verificando terminal e senha...");
            var ciscoRecovery = new CiscoIOSRecovery();
            var (accessState, rommonKind) = await ciscoRecovery.DiagnoseAccessStateAsync(session, ct);

            string accessDesc;
            bool isOpen = false;
            switch (accessState)
            {
                case DeviceAccessState.AlreadyInRommon:
                    accessDesc = $"🚨 MODO ROMMON / BOOTLOADER ({rommonKind}) — Sem Firmware ou em Recuperação";
                    break;
                case DeviceAccessState.UnlockedPrompt:
                    accessDesc = "🟢 PROMPT ABERTO / SEM SENHA — Acesso Direto Liberado (Modo Privilegiado)";
                    isOpen = true;
                    break;
                case DeviceAccessState.PasswordLocked:
                    accessDesc = "🔒 PROTEGIDO POR SENHA — Requer quebra/zeramento na Fase 1 (Break/ROMMON)";
                    break;
                default:
                    accessDesc = "❓ ESTADO NÃO IDENTIFICADO";
                    break;
            }

            EscreverLinha($"  🔑 Estado de Acesso : {accessDesc}");

            if (accessState == DeviceAccessState.AlreadyInRommon)
            {
                EscreverLinha("  ------------------------------------------------------------------------------");
                EscreverLinha("  💡 Diagnóstico      : Equipamento no bootloader ROMMON (sem imagem de boot carregada).");
                EscreverLinha("  💡 Ação Recomendada: Conectar o cabo na porta GE 0/0 e executar a Fase 2 (Firmware TFTP).");
                EscreverLinha("================================================================================\n");

                AtualizarProgresso(100, "Avaliação Concluída!", "Equipamento em modo ROMMON.");
                MessageBox.Show(
                    $"Equipamento conectado em {porta} está em MODO ROMMON / BOOTLOADER ({rommonKind}).\n\n" +
                    "• Flash vazia ou sem imagem de boot configurada.\n" +
                    "• Recomendação: Conecte o cabo na porta GE 0/0 e execute a Fase 2 (Atualização de Firmware via TFTP).",
                    "Diagnóstico do Equipamento — SPARC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Se o prompt estiver aberto, coleta inventário completo e configuração existente
            if (isOpen)
            {
                AtualizarProgresso(50, "Coletando Inventário do Sistema...", "Consultando versão, hardware e interfaces...");

                var promptStr = session.CurrentPrompt ?? "";
                var isHpe = promptStr.StartsWith("<") || promptStr.StartsWith("[") || promptStr.Contains("HPE", StringComparison.OrdinalIgnoreCase);

                if (isHpe)
                {
                    await AvaliarEquipamentoHpeAsync(session, ct);
                }
                else
                {
                    await AvaliarEquipamentoCiscoAsync(session, ct);
                }
            }
            else
            {
                EscreverLinha("  ------------------------------------------------------------------------------");
                EscreverLinha("  💡 Diagnóstico      : Equipamento com senha configurada no console/enable.");
                EscreverLinha("  💡 Ação Recomendada: Executar a Fase 1 (Zerar Configuração) para quebrar a senha via Break/ROMMON.");
                EscreverLinha("  ⚠️ ATENÇÃO          : Toda a configuração existente será APAGADA e os dados atuais serão PERDIDOS!");
                EscreverLinha("================================================================================\n");

                AtualizarProgresso(100, "Avaliação Concluída!", "Equipamento protegido por senha.");
                MessageBox.Show(
                    $"Equipamento conectado em {porta} está PROTEGIDO POR SENHA.\n\n" +
                    "• O CLI exige usuário/senha ou enable com senha restrita.\n" +
                    "• Recomendação: Execute a Fase 1 (Zerar Configuração) na esteira para quebrar a senha via Break/ROMMON e restaurar o config-register para 0x2102.\n\n" +
                    "⚠️ ALERTA DE PERDA DE DADOS:\n" +
                    "Ao executar o zeramento ou provisionamento, toda a configuração existente no equipamento será COMPLETAMENTE APAGADA e os dados configurados serão PERDIDOS.",
                    "Diagnóstico do Equipamento — SPARC",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EscreverLinha($"\n[FALHA NA AVALIAÇÃO] {ex.Message}");
            AtualizarProgresso(0, "Falha na avaliação", ex.Message);
            MessageBox.Show($"Não foi possível avaliar o equipamento na porta {porta}:\n\n{ex.Message}", "Erro de Comunicação Serial", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusy(false);
        }
    }

    private async Task AvaliarEquipamentoCiscoAsync(DeviceSession session, CancellationToken ct)
    {
        // 1. show version
        var showVer = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(15), ct);

        var modelMatch = Regex.Match(showVer, @"(?im)^\s*cisco\s+([A-Za-z0-9\-\/]+).+processor");
        if (!modelMatch.Success) modelMatch = Regex.Match(showVer, @"(?im)^\s*Model number\s*:\s*(\S+)");
        var modelo = modelMatch.Success ? modelMatch.Groups[1].Value.Trim() : "Cisco (modelo não parseado)";

        var iosVerMatch = Regex.Match(showVer, @"(?im)Version\s+([0-9\.\(\)A-Za-z]+),");
        var iosVer = iosVerMatch.Success ? iosVerMatch.Groups[1].Value.Trim() : "Desconhecida";

        var serialMatch = Regex.Match(showVer, @"(?im)Processor board ID\s+(\S+)");
        if (!serialMatch.Success) serialMatch = Regex.Match(showVer, @"(?im)System serial number\s*:\s*(\S+)");
        var serialNumber = serialMatch.Success ? serialMatch.Groups[1].Value.Trim() : "Não informado";

        var regMatch = Regex.Match(showVer, @"(?im)Configuration register is\s+(0x[0-9A-Fa-f]+)");
        var configRegister = regMatch.Success ? regMatch.Groups[1].Value.Trim() : "0x2102";

        var uptimeMatch = Regex.Match(showVer, @"(?im)uptime is\s+(.+)");
        var uptime = uptimeMatch.Success ? uptimeMatch.Groups[1].Value.Trim() : "—";

        // 2. dir flash:
        var dirFlash = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), ct);
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
            if (modelo.Contains("900", StringComparison.OrdinalIgnoreCase) || modelo.Contains("921", StringComparison.OrdinalIgnoreCase))
            {
                if (CbModeloRoteadorInicial != null) CbModeloRoteadorInicial.SelectedIndex = 3;
            }
            else
            {
                if (CbModeloRoteadorInicial != null) CbModeloRoteadorInicial.SelectedIndex = 2;
            }

            if (CardChkModelo != null && TxtChkModeloIcon != null && TxtChkModeloSub != null)
            {
                TxtChkModeloIcon.Text = "🟢 1. Modelo";
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
        // 1. display version
        var dispVer = await session.SendCommandAsync("display version", TimeSpan.FromSeconds(15), ct);

        var modelMatch = Regex.Match(dispVer, @"(?im)^\s*HPE\s+([A-Za-z0-9\-\/ ]+)uptime");
        if (!modelMatch.Success) modelMatch = Regex.Match(dispVer, @"(?im)HPE\s+([A-Za-z0-9\-\/]+)");
        var modelo = modelMatch.Success ? modelMatch.Groups[1].Value.Trim() : "HPE Comware";

        var comwareVerMatch = Regex.Match(dispVer, @"(?im)HPE Comware Software,\s*Version\s*([0-9\.\, A-Za-z]+),");
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
            if (CbModeloRoteadorInicial != null) CbModeloRoteadorInicial.SelectedIndex = 1;

            if (CardChkModelo != null && TxtChkModeloIcon != null && TxtChkModeloSub != null)
            {
                TxtChkModeloIcon.Text = "🟢 1. Modelo";
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

        var selecionada = CbPorta.Text;
        var portas = SerialPort.GetPortNames();

        CbPorta.Items.Clear();
        if (CbPortaInicial is not null)
            CbPortaInicial.Items.Clear();

        foreach (var porta in portas)
        {
            CbPorta.Items.Add(porta);
            if (CbPortaInicial is not null)
                CbPortaInicial.Items.Add(porta);
        }

        if (!string.IsNullOrEmpty(selecionada) && CbPorta.Items.Contains(selecionada))
        {
            CbPorta.Text = selecionada;
            if (CbPortaInicial is not null)
                CbPortaInicial.Text = selecionada;
        }
        else if (CbPorta.Items.Contains("COM1"))
        {
            CbPorta.Text = "COM1";
            if (CbPortaInicial is not null)
                CbPortaInicial.Text = "COM1";
        }
        else if (CbPorta.Items.Count > 0)
        {
            var first = CbPorta.Items[0]?.ToString();
            CbPorta.Text = first;
            if (CbPortaInicial is not null)
                CbPortaInicial.Text = first;
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

    private void CardModoSemiAuto_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RbExecSemiAuto != null) RbExecSemiAuto.IsChecked = true;
    }

    private void CardModoAutomatico_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RbExecAuto != null) RbExecAuto.IsChecked = true;
    }

    private void ModoExecucao_Changed(object sender, RoutedEventArgs e)
    {
        var auto = RbExecAuto?.IsChecked == true;

        if (CardModoAutomatico != null)
        {
            CardModoAutomatico.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(auto ? "#D97706" : "#CBD5E1"));
            CardModoAutomatico.BorderThickness = new Thickness(auto ? 2 : 1);
        }

        if (CardModoSemiAuto != null)
        {
            CardModoSemiAuto.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(auto ? "#CBD5E1" : "#881337"));
            CardModoSemiAuto.BorderThickness = new Thickness(auto ? 1 : 2);
        }

        if (PanelFirmwareObrigatorio != null)
            PanelFirmwareObrigatorio.Visibility = auto ? Visibility.Visible : Visibility.Collapsed;

        if (BtnAvancarParaEsteira != null)
            BtnAvancarParaEsteira.Content = auto ? "⚡ INICIAR PROVISIONAMENTO AUTOMÁTICO" : "🚀 INICIAR PROVISIONAMENTO SEMI-AUTOMÁTICO";

        if (TxtDescricaoBotaoAcao != null)
            TxtDescricaoBotaoAcao.Text = auto
                ? "O SPARC executará as 7 etapas em sequência. Acompanhe o progresso na esteira."
                : "Você acompanhará cada etapa e poderá validar/intervir quando necessário.";

        AtualizarBotaoProsseguir();
    }

    private void BtnSelecionarFirmwareAuto_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Selecionar Firmware para Modo Automático (.ipe / .bin)",
            Filter = "Todos os Firmwares (*.bin;*.ipe;*.pkg)|*.bin;*.ipe;*.pkg|Pacotes HPE Comware (*.ipe)|*.ipe|Imagens Binárias (*.bin)|*.bin|Todos os Arquivos (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _selectedIosBinPath = dlg.FileName;
            var fi = new System.IO.FileInfo(dlg.FileName);
            var sizeMb = (fi.Length / (1024.0 * 1024.0)).ToString("N1");
            TxtFirmwareAutoInfo.Text = $"{System.IO.Path.GetFileName(dlg.FileName)} ({sizeMb} MB)";
            if (TxtIosImageInfo != null) TxtIosImageInfo.Text = TxtFirmwareAutoInfo.Text;
            AtualizarBotaoProsseguir();
            EscreverLinha($"[*] Firmware modo automático: {System.IO.Path.GetFileName(dlg.FileName)} ({sizeMb} MB)");
        }
    }

    private void BtnAutoVoltar_Click(object sender, RoutedEventArgs e)
    {
        GridModoAutomatico.Visibility = Visibility.Collapsed;
        GridTelaInicial.Visibility = Visibility.Visible;
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
        if (!_serialOk) erros.Add("• Teste de conexão serial pendente — clique em 🔌 Testar Conexão.");
        if (CbModeloRoteadorInicial.SelectedIndex <= 0) erros.Add("• Modelo do equipamento não selecionado.");
        var modoManual = RbModoManual.IsChecked == true;
        if (_loadedSaipCircuit is null)
        {
            if (modoManual) erros.Add("• Modo Manual: preencha WAN IP, Gateway, LAN IP e clique em Aplicar.");
            else erros.Add("• Ficha SAIP não carregada — clique em 📂 Selecionar Arquivo SAIP.");
        }

        var isAuto = RbExecAuto?.IsChecked == true;
        if (isAuto && (string.IsNullOrEmpty(_selectedIosBinPath) || !System.IO.File.Exists(_selectedIosBinPath)))
            erros.Add("• Modo Automático: firmware (.ipe/.bin) obrigatório.");

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

        // Garante que o modo automático importe o mesmo sistema de análise de boot do modo padrão (HPE BootWare Ctrl+B)
        if (CbModeloRoteadorInicial.SelectedIndex > 0 && CbInterrupt.SelectedIndex != CbModeloRoteadorInicial.SelectedIndex - 1)
        {
            _syncingCombos = true;
            try { CbInterrupt.SelectedIndex = CbModeloRoteadorInicial.SelectedIndex - 1; } finally { _syncingCombos = false; }
        }

        try
        {
            SetEtapa(1, "⏳ 1. Zerar Configuração — em execução", "#D97706"); Progresso(5, "1/7 Zerar Configuração..."); LogAuto(">>> [AUTO 1/7] Zerar Configuração (verificando senha e terminal)");
            await ExecutarZerarConfigAsync(porta, baud, ct); SetEtapa(1, "✅ 1. Zerar Configuração — OK", "#16A34A"); Progresso(18, "1/7 OK");

            SetEtapa(2, "⏳ 2. Atualizar Firmware — em execução", "#D97706"); Progresso(22, "2/7 Atualizar Firmware..."); LogAuto(">>> [AUTO 2/7] Atualizar Firmware");
            var hostIp = _loadedSaipCircuit?.HostLanIp ?? ObterIpLocalParaTftp() ?? "127.0.0.1";
            await ExecutarUpgradeFirmwareAsync(porta, baud, hostIp, ct); SetEtapa(2, "✅ 2. Atualizar Firmware — OK", "#16A34A"); Progresso(42, "2/7 OK");

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

            SetEtapa(6, "⏳ 6. Testar Acesso Remoto (Telnet) — em execução", "#D97706"); Progresso(86, "6/7 Telnet..."); LogAuto(">>> [AUTO 6/7] Telnet");
            var telnetHost = TxtTelnetTarget.Text?.Trim(); if (string.IsNullOrEmpty(telnetHost)) telnetHost = _loadedSaipCircuit?.LanIp ?? "200.182.245.17";
            var telnetPort = int.TryParse(TxtTelnetPort.Text?.Trim(), out var tp) ? tp : 23;
            var telnetR = await ExecutarTesteTelnetAsync(telnetHost, telnetPort, ct);
            SetEtapa(6, (telnetR.IsSuccess ? "✅" : "❌") + " 6. Acesso Remoto (Telnet) — " + (telnetR.IsSuccess ? "OK" : "falha"), telnetR.IsSuccess ? "#16A34A" : "#EF4444"); Progresso(94, "6/7 OK");

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
                falhaGeral: null);
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
                falhaGeral: ex.Message);
        }
        finally { _cts?.Dispose(); _cts = null; }
    }

    private string? _lastGeneratedPdfPath;
    private ActivationReportData? _lastReportData;

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
        string? falhaGeral)
    {
        var dataHora = DateTime.Now;
        var itemModelo = CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem;
        var modelo = itemModelo?.Content?.ToString()?.Replace("🖧", "")?.Trim() ?? "Roteador";
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
            falhas.Add("Falha no Teste 5a (ICMP LAN / Roteador): Verifique se o cabo Ethernet do PC está conectado na porta LAN do roteador (Giga 0/1 ou Giga 1) e com IP configurado.");
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
            WanInterface: "GigabitEthernet 0/0",
            LanIp: lanIp,
            LanCidr: lanCidr,
            LanBlockNetwork: lanBlock,
            LanSubnetMask: lanMask,
            HostLanIp: hostLanIp,
            LanInterface: "GigabitEthernet 0/1",
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
            FalhaGeral: falhaGeral
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

            if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
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
                WanInterface: "GigabitEthernet 0/0",
                LanIp: _loadedSaipCircuit?.LanIp,
                LanCidr: _loadedSaipCircuit?.LanCidr ?? 28,
                LanBlockNetwork: _loadedSaipCircuit?.LanBlockNetwork,
                LanSubnetMask: _loadedSaipCircuit?.LanSubnetMask,
                HostLanIp: _loadedSaipCircuit?.HostLanIp,
                LanInterface: "GigabitEthernet 0/1",
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
                // Valida formato antes de aceitar: exige IPs necessários
                var rawText = await Task.Run(async () =>
                {
                    var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    if (ext == ".pdf")
                    {
                        using var doc = UglyToad.PdfPig.PdfDocument.Open(dlg.FileName);
                        var sb = new System.Text.StringBuilder();
                        foreach (var p in doc.GetPages()) sb.AppendLine(p.Text);
                        return sb.ToString();
                    }
                    return await File.ReadAllTextAsync(dlg.FileName);
                });
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
        var isHpe = perfilHw.Contains("hpe", StringComparison.OrdinalIgnoreCase) || perfilHw.Contains("msr", StringComparison.OrdinalIgnoreCase);
        var isCisco = perfilHw.Contains("cisco", StringComparison.OrdinalIgnoreCase);

        var dlg = new OpenFileDialog
        {
            Title = isHpe ? "Selecionar Pacote de Firmware HPE Comware (.IPE)" 
                  : isCisco ? "Selecionar Imagem de Firmware Cisco IOS (.BIN)"
                  : "Selecionar Arquivo de Firmware (.bin / .ipe)",
            Filter = isHpe ? "Pacotes HPE Comware (*.ipe)|*.ipe|Imagens Binárias (*.bin)|*.bin|Todos os Arquivos (*.*)|*.*"
                   : isCisco ? "Firmware Cisco IOS (*.bin)|*.bin|Todos os Arquivos (*.*)|*.*"
                   : "Firmwares Suportados (*.bin;*.ipe)|*.bin;*.ipe|Firmware Cisco IOS (*.bin)|*.bin|Pacotes HPE Comware (*.ipe)|*.ipe|Todos os Arquivos (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var fileName = Path.GetFileName(dlg.FileName);

            // CRÍTICA DE FORMATO DE ARQUIVO
            if (isCisco && ext != ".bin")
            {
                var msgCritica = "❌ CRÍTICA DE FORMATO — CISCO IOS:\n\n" +
                                 $"O arquivo selecionado '{fileName}' possui extensão '{ext}'.\n\n" +
                                 "• Roteadores Cisco (Série 1900 / 900 / ISR) aceitam EXCLUSIVAMENTE arquivos no formato executável .BIN (exemplo: c1900-universalk9-mz.SPA.158-3.M7.bin).\n" +
                                 "• Arquivos .ipe, .tar, .zip ou .iso NÃO são aceitos pelo bootloader ROMMON da Cisco.\n\n" +
                                 "Por favor, selecione um arquivo de firmware .bin válido.";

                MessageBox.Show(msgCritica, "Formato de Firmware Incompatível (Cisco)", MessageBoxButton.OK, MessageBoxImage.Error);
                EscreverLinha($"\n[CRÍTICA DE FORMATO] Arquivo '{fileName}' rejeitado. Para Cisco, o firmware deve ser obrigatoriamente .bin");
                return;
            }
            else if (isHpe && ext != ".ipe" && ext != ".bin")
            {
                var msgCritica = "⚠ CRÍTICA DE FORMATO — HPE COMWARE:\n\n" +
                                 $"O arquivo selecionado '{fileName}' possui extensão '{ext}'.\n\n" +
                                 "• Equipamentos HPE MSR (954 / 958) utilizam pacotes integrados no formato .IPE (Image Package Executable, ex.: MSR954-CMW710-R6749P43.ipe).\n" +
                                 "• Arquivos de outros formatos não contêm a estrutura de componentes necessária para a recuperação via BootWare.\n\n" +
                                 "Deseja manter este arquivo mesmo assim?";

                var respCritica = MessageBox.Show(msgCritica, "Aviso de Formato HPE", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (respCritica != MessageBoxResult.Yes)
                {
                    EscreverLinha($"\n[CRÍTICA DE FORMATO] Seleção cancelada pelo operador para escolher um pacote HPE .ipe válido.");
                    return;
                }
            }

            _selectedIosBinPath = dlg.FileName;
            var fi = new FileInfo(dlg.FileName);
            var sizeMb = (fi.Length / (1024.0 * 1024.0)).ToString("N1");
            TxtIosImageInfo.Text = $"{fileName} ({sizeMb} MB) [{ext.ToUpperInvariant()}]";
            if (TxtFirmwareAutoInfo != null)
                TxtFirmwareAutoInfo.Text = $"{fileName} ({sizeMb} MB)";

            EscreverLinha($"[*] Firmware validado e carregado: {fileName} ({sizeMb} MB) [{ext.ToUpperInvariant()}]");
            AtualizarEstadoBotoes();
            AtualizarBotaoProsseguir();
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
        AtualizarProgresso(5, "Fase A: Zerando configuração...", $"Abrindo conexão em {porta} @ {baud} baud (8-N-1).");
        EscreverLinha($"\n[*] [FASE A] ZERAR CONFIGURAÇÃO EM {porta} @ {baud} BAUD");

        // Importa mesmo sistema de análise de boot do modo padrão: tenta CbInterrupt, fallback CbModeloRoteadorInicial (modo automático)
        var profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                      ?? (CbModeloRoteadorInicial.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var profile = BootInterruptProfiles.FindById(profileTag);
        EscreverLinha($"[*] Perfil de hardware: {profile.Name} (Método: {profile.Method}) — {(profile.Id.Contains("hpe") || profile.Family.Contains("MSR", StringComparison.OrdinalIgnoreCase) ? "BootWare HPE" : "ROMMON Cisco")}.");

        var transport = new SerialTransport(porta, baud);
        await using var session = new DeviceSession(
            transport, CiscoIOSAdapter.CreateSessionOptions(null));
        session.RawOutput += OnRawOutput;
        var isHpe = profile.Id.Contains("hpe", StringComparison.OrdinalIgnoreCase) || profile.Family.Contains("MSR", StringComparison.OrdinalIgnoreCase);

        if (isHpe)
        {
            var firmwareFile = _selectedIosBinPath;
            if (string.IsNullOrEmpty(firmwareFile) || !File.Exists(firmwareFile))
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
                    firmwareFile = Directory.GetFiles(dir, "*954*.ipe").FirstOrDefault()
                                ?? Directory.GetFiles(dir, "*.ipe").FirstOrDefault()
                                ?? Directory.GetFiles(dir, "*954*.bin").FirstOrDefault();
                    if (!string.IsNullOrEmpty(firmwareFile)) break;
                }
            }
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
                (s, ethOpt, fwPath, hIp, rIp, mask, token) => ExecutarBootWareTftpDownloadAsync(s, ethOpt, fwPath, hostIp, routerIp, subnetMask, token),
                SolicitarFirmwareParaRecuperacaoAsync,
                ct);
            AtualizarProgresso(100, "Fase A Concluída!", "Roteador HPE zerado/recuperado com sucesso via BootWare.");
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
                EscreverLinha("[*] Equipamento identificado em MODO ROMMON (sem firmware na Flash).");
                await InstruirOperadorAsync(
                    "⚠️ ROTEADOR CISCO EM MODO ROMMON (SEM FIRMWARE)\n\n" +
                    "O equipamento foi identificado em modo de recuperação ROMMON.\n\n" +
                    "👉 CONECTE O CABO DE REDE ETHERNET NA PORTA:\n" +
                    "🔴 GigabitEthernet 0/0 (GE 0/0 / Porta 0)\n\n" +
                    "Esta é a única porta Ethernet habilitada no hardware para a transferência TFTP via ROMMON.\n\n" +
                    "(Após a gravação do firmware e inicialização do Cisco IOS, o sistema solicitará a troca do cabo para a porta GE 0/1 - LAN).",
                    ct);

                AtualizarProgresso(100, "Fase A Concluída!", "Equipamento em ROMMON pronto para carga de firmware TFTP na porta GE 0/0.");
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

        AtualizarProgresso(30, "Fase A: Recuperação BootWare TFTP...", $"Iniciando TFTP para {fileName}...");
        EscreverLinha($"[*] Iniciando servidor TFTP integrado para transferência no BootWare (Porta Ethernet)...");

        await using var tftpServer = new NetworkDevice.Protocols.Tftp.EmbeddedTftpServer(fileDir);
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

            if ((now - lastUiTime).TotalMilliseconds >= 250 || pct >= 100)
            {
                lastUiTime = now;
                AtualizarProgresso((int)pct, $"Fase A: Gravando Flash ({pct:N1}%)...", $"{sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) — {speedMbSec:N1} MB/s{etaStr}");
            }

            var step = (int)(pct / 5) * 5;
            if (step > lastLogPct)
            {
                lastLogPct = step;
                var barLength = 20;
                var filled = (int)Math.Round((pct / 100.0) * barLength);
                var bar = new string('█', Math.Clamp(filled, 0, barLength)) + new string('░', Math.Max(0, barLength - filled));
                EscreverLinha($"    -> [BootWare TFTP] [{bar}] {sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) | {speedMbSec:N1} MB/s{etaStr}");
            }
        };
        tftpServer.Start();

        try
        {
            // 1. Entra no Ethernet SubMenu (Opção 3 do Menu Principal)
            await session.WriteLineAsync(ethernetOption, ct);
            await Task.Delay(1200, ct);

            // 2. Modifica Parâmetros de Rede (Opção 5 - Modify Ethernet Parameter)
            EscreverLinha($"[*] Configurando parâmetros Ethernet no BootWare (Roteador: {routerIp}, Servidor: {hostIp}, Arquivo: {fileName})...");
            await session.WriteLineAsync("5", ct);
            await Task.Delay(800, ct);

            await session.WriteLineAsync("0", ct); // Protocol: 0-TFTP
            await Task.Delay(400, ct);
            await session.WriteLineAsync("0", ct); // DHCP: 0-Disable
            await Task.Delay(400, ct);
            await session.WriteLineAsync(routerIp, ct); // Client IP
            await Task.Delay(400, ct);
            await session.WriteLineAsync(subnetMask, ct); // Subnet Mask
            await Task.Delay(400, ct);
            await session.WriteLineAsync(hostIp, ct); // Server IP (PC)
            await Task.Delay(400, ct);
            await session.WriteLineAsync("0.0.0.0", ct); // Gateway IP
            await Task.Delay(400, ct);
            await session.WriteLineAsync(fileName, ct); // File Name
            await Task.Delay(1000, ct);

            // 3. Dispara Gravação na Flash (Opção 2 - Update Main Image File)
            EscreverLinha($"[*] Iniciando download e gravação do firmware ({fileName}) na Flash pelo BootWare...");
            await session.WriteLineAsync("2", ct);
            await Task.Delay(1500, ct);
            await session.WriteLineAsync("Y", ct); // Confirma download

            // 4. Aguarda a gravação na Flash
            var downloadResult = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("Writing file to Flash...Done.", "Writing file to Flash...Done."),
                    new StopCondition.Contains("Done.", "Done."),
                    new StopCondition.Contains("successfully", "successfully"),
                    new StopCondition.Contains("Set as main boot image? [Y/N]:", "Set as main boot image? [Y/N]:"),
                    new StopCondition.Contains("choice", "choice")
                },
                TimeSpan.FromMinutes(8),
                ct);

            if (downloadResult.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
                downloadResult.Output.Contains("main boot", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(1000, ct);
            }

            // 5. Retorna ao Menu Principal
            await session.WriteLineAsync("0", ct);
            await Task.Delay(1000, ct);
            await session.WriteLineAsync(string.Empty, ct);

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
            EscreverLinha("  O arquivo .IPE será gravado na Flash via TFTP pela porta LAN (Giga 1).");
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

                var resp = MessageBox.Show(
                    $"A memória Flash do roteador está vazia ou sem imagem de boot.\n\n" +
                    $"Pacote de Firmware Detectado:\n" +
                    $"📁 {fileName} ({sizeMb} MB)\n\n" +
                    $"Deseja utilizar este arquivo para recuperar a Flash e inicializar o equipamento via TFTP pela porta LAN (Giga 1)?",
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

        var sessionOptions = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal(),
            CommandTimeout = TimeSpan.FromSeconds(35),
            ConnectTimeout = TimeSpan.FromSeconds(50)
        };

        var transport = new SerialTransport(porta, baud);
        await using var session = new DeviceSession(transport, sessionOptions);
        session.RawOutput += OnRawOutput;

        await session.ConnectAsync(ct);

        var promptStr = session.CurrentPrompt ?? "";
        var profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var isHpe = profileTag.Contains("hpe", StringComparison.OrdinalIgnoreCase)
                 || profileTag.Contains("msr", StringComparison.OrdinalIgnoreCase)
                 || promptStr.StartsWith("[")
                 || promptStr.StartsWith("<")
                 || promptStr.Contains("HPE", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("MSR", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("Comware", StringComparison.OrdinalIgnoreCase);

        if (isHpe)
        {
            EscreverLinha($"[*] Equipamento identificado como HPE / HP MSR / Comware (Prompt detectado: '{promptStr}').");
            AtualizarProgresso(50, "Fase C: Configurando HPE...", $"WAN GE0 ({_loadedSaipCircuit.WanIp}), LAN GE1 ({_loadedSaipCircuit.LanIp})...");
            var hpeConfig = new HpeSaipConfigurator(EscreverLinhaAsync);
            await hpeConfig.ApplyConfigAsync(session, _loadedSaipCircuit, "GigabitEthernet0/0", "GigabitEthernet0/1", ct);
        }
        else
        {
            EscreverLinha($"[*] Equipamento identificado como Cisco IOS (Prompt detectado: '{promptStr}').");
            AtualizarProgresso(50, "Fase C: Configurando Cisco...", $"WAN GE0/0 ({_loadedSaipCircuit.WanIp}), LAN GE0/1 ({_loadedSaipCircuit.LanIp})...");
            var ciscoConfig = new CiscoSaipConfigurator(EscreverLinhaAsync);
            await ciscoConfig.ApplyConfigAsync(session, _loadedSaipCircuit, "GigabitEthernet 0/0", "GigabitEthernet 0/1", ct);

            // Valida se o técnico conectou o cabo na porta LAN (GE 0/1) antes de prosseguir
            await CiscoIOSAdapter.EnforceLanPortConnectedAsync(session, "GigabitEthernet 0/1", InstruirOperadorAsync, EscreverLinhaAsync, ct);
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

        try
        {
            await ExecutarUpgradeFirmwareAsync(porta, baud, hostIp, _cts.Token);
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
            MessageBox.Show("Selecione um arquivo de firmware Cisco (.bin) para efetuar a recuperação no modo ROMMON.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private async Task ExecutarUpgradeFirmwareAsync(string porta, int baud, string hostIp, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_selectedIosBinPath))
            return;

        var fileName = Path.GetFileName(_selectedIosBinPath);
        AtualizarProgresso(10, "Fase B: Iniciando Upgrade e TFTP...", "Iniciando servidor TFTP e conectando...");
        EscreverLinha($"\n[*] [FASE B] ATUALIZAÇÃO DE FIRMWARE VIA TFTP ({fileName})...");

        var sessionOptions = new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal(),
            CommandTimeout = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        var transport = new SerialTransport(porta, baud);
        await using var session = new DeviceSession(transport, sessionOptions);
        session.RawOutput += OnRawOutput;

        await session.ConnectAsync(ct);

        var promptStr = session.CurrentPrompt ?? "";
        var profileTag = (CbInterrupt.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        var isHpe = profileTag.Contains("hpe", StringComparison.OrdinalIgnoreCase)
                 || profileTag.Contains("msr", StringComparison.OrdinalIgnoreCase)
                 || promptStr.StartsWith("[")
                 || promptStr.StartsWith("<")
                 || promptStr.Contains("HPE", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("MSR", StringComparison.OrdinalIgnoreCase)
                 || promptStr.Contains("Comware", StringComparison.OrdinalIgnoreCase)
                 || fileName.EndsWith(".ipe", StringComparison.OrdinalIgnoreCase);

        bool success;
        if (isHpe)
        {
            EscreverLinha($"[*] Equipamento identificado como HPE Comware para upgrade de firmware ({fileName}).");
            AtualizarProgresso(20, "Fase B: Gravando firmware HPE...", "Transferindo via TFTP, gravando bootloader e reiniciando...");
            var hpeUpgrader = new HpeComwareUpgrader(EscreverLinhaAsync, AtualizarProgresso);
            success = await hpeUpgrader.UpgradeAsync(session, _selectedIosBinPath, hostIp,
                async (msg, ctk) =>
                {
                    return await Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(msg + "\n\nDeseja atualizar o boot-loader agora?", "Boot-loader desatualizado", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
                }, ct);
        }
        else
        {
            var routerIp = _loadedSaipCircuit?.LanIp ?? "200.182.245.17";
            var subnetMask = _loadedSaipCircuit?.LanSubnetMask ?? "255.255.255.240";
            var lanInterface = "GigabitEthernet 0/1";
            var adapter = CbAdaptadorRede?.Text?.Trim();

            EscreverLinha($"[*] Equipamento identificado como Cisco IOS para upgrade de firmware ({fileName}).");
            AtualizarProgresso(22, "Fase B: Gravando IOS Cisco...", "Iniciando transferência TFTP...");
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

    private string? ObterIpOrigemParaIcmp()
    {
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
            EscreverLinha($"[AVISO 5b WAN] Gateway WAN ({wanTarget}) sem resposta (Link físico ou rota pendente na operadora).");

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
        var telnetPass = TxtTelnetPass.Text.Trim(); if (string.IsNullOrEmpty(telnetPass)) telnetPass = "PRO1AN";
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
        }
    }

    private async Task<BandwidthTestResult> ExecutarTesteBandaAsync(CancellationToken ct)
    {
        AtualizarProgresso(10, "Fase G: Testando Banda...", "Iniciando teste de vazão...");
        EscreverLinha("\n[*] [FASE G] TESTE DE BANDA / VELOCIDADE DE CONEXÃO");

        var service = new BandwidthTestService(EscreverLinhaAsync);

        // 1. Tenta executar CLI Speedtest se disponível
        var cliResult = await service.RunSpeedtestCliAsync(cancellationToken: ct);
        if (cliResult.IsSuccess)
        {
            AtualizarProgresso(100, "Fase G: Teste Concluído!", $"Download: {cliResult.DownloadMbps} Mbps | Latência: {cliResult.LatencyMs:F0}ms");
            return cliResult;
        }

        // 2. Fallback para Teste Nativo HTTP
        EscreverLinha("[*] Executando Teste de Banda Nativo HTTP (Cloudflare CDN - Payload: ~50 MB)...");
        var httpResult = await service.RunNativeHttpSpeedTestAsync(
            testPayloadMegaBytes: 50,
            onProgress: (mbps, pct) =>
            {
                AtualizarProgresso((int)pct, "Fase G: Medindo Vazão HTTP...", $"Vazão atual: {mbps:F2} Mbps ({pct:F0}%)");
            },
            cancellationToken: ct);

        if (httpResult.IsSuccess)
        {
            AtualizarProgresso(100, "Fase G Concluída!", $"Download: {httpResult.DownloadMbps} Mbps | Latência: {httpResult.LatencyMs:F0}ms");
        }

        return httpResult;
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
                EscreverLinha("[*] Aguardando 15 segundos para estabilização pós-reload...");
                await Task.Delay(15000, ct);
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
            // FASE 6 (F): TESTAR ACESSO REMOTO (TELNET)
            // -------------------------------------------------------------
            SelecionarFase("F");
            DefinirBadgeStatus("F", "⏳");
            var telnetHostEsteira = _loadedSaipCircuit?.LanIp
                ?? (!string.IsNullOrWhiteSpace(TxtTelnetTarget.Text) ? TxtTelnetTarget.Text.Trim() : "200.182.245.17");
            var telnetPortEsteira = int.TryParse(TxtTelnetPort.Text.Trim(), out var tp) ? tp : 23;

            EscreverLinha("\n>>> [ESTEIRA] FASE 6: TESTAR ACESSO REMOTO TELNET");
            var telnetEsteiraResult = await ExecutarTesteTelnetAsync(telnetHostEsteira, telnetPortEsteira, ct);
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
            if (TxtAutoEtapa2 != null && TxtAutoEtapa2.Text.Contains("Atualizar Firmware") && !TxtAutoEtapa2.Text.Contains("✅"))
            {
                TxtAutoEtapa2.Text = $"⏳ 2. Atualizar Firmware — {descricao}";
            }
        }
    }

    private Task EscreverLinhaAsync(string linha)
    {
        EscreverLinha(linha);
        return Task.CompletedTask;
    }

    private Task InstruirOperadorAsync(string instrucao, CancellationToken ct)
    {
        AtualizarProgresso(40, "Fase 1: Reinício necessário...", "Roteador protegido por senha. Reinicie o equipamento na energia.");

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

    private void BtnLimparTerminal_Click(object sender, RoutedEventArgs e)
    {
        TerminalParagraph.Inlines.Clear();
        EscreverLinha("Terminal limpo.");
    }

    private void OnRawOutput(string raw)
    {
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
        base.OnClosing(e);
    }

    #endregion
}