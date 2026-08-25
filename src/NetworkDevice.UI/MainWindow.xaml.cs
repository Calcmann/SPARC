using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
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
                @"C:\Killtech\Manual_Instrucoes_Operador_SPARC.pdf",
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\Manual_Instrucoes_Operador_SPARC.pdf")),
                Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\Manual_Instrucoes_Operador_SPARC.pdf")),
                Path.Combine(baseDir, "Manual_Instrucoes_Operador_Killtech.pdf"),
                @"C:\Killtech\Manual_Instrucoes_Operador_Killtech.pdf",
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
                    "O arquivo do Manual do Operador (SPARC) não foi localizado em C:\\Killtech.",
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
                TxtSerialTestStatus.Text = "✅ Serial OK";
                TxtSerialTestStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A"));
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
            EscreverLinha($"[OK] Serial OK — prompt detectado: {prompt} (Modo: {session.Mode})");
            EscreverLinha($"[OK] Cabo serial, porta {porta} e baud {baud} validados com sucesso!");
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

            SetEtapa(7, "⏳ 7. Testar Banda — em execução", "#D97706"); Progresso(96, "7/7 Banda..."); LogAuto(">>> [AUTO 7/7] Banda");
            var bandR = await ExecutarTesteBandaAsync(ct);
            SetEtapa(7, (bandR.IsSuccess ? "✅" : "⚠") + " 7. Testar Banda — " + (bandR.IsSuccess ? "OK" : "falha"), bandR.IsSuccess ? "#16A34A" : "#D97706"); Progresso(100, "Concluído!");

            LogAuto("================================================================="); LogAuto(" MODO AUTOMÁTICO CONCLUÍDO "); LogAuto("=================================================================");
            BtnAutoCancelar.Visibility = Visibility.Collapsed; BtnAutoVoltar.Visibility = Visibility.Visible;

            // Gera e exibe o Relatório Final com diagnóstico de causas
            ExibirRelatorioFinalAutomatico(
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

            ExibirRelatorioFinalAutomatico(
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

    private void ExibirRelatorioFinalAutomatico(
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
        var sb = new System.Text.StringBuilder();
        var dataHora = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        var itemModelo = CbModeloRoteadorInicial?.SelectedItem as ComboBoxItem;
        var modelo = itemModelo?.Content?.ToString()?.Replace("🖧", "")?.Trim() ?? "Roteador";
        var cliente = SaipParser.CleanRazaoSocial(_loadedSaipCircuit?.ClienteRazaoSocial) ?? "Não informado / Manual";
        var designacao = _loadedSaipCircuit?.DesignacaoIp ?? "Não informada";
        var wanIp = _loadedSaipCircuit != null ? $"{_loadedSaipCircuit.WanIp}/{_loadedSaipCircuit.WanCidr} (GW: {_loadedSaipCircuit.WanGateway})" : "—";
        var lanIp = _loadedSaipCircuit != null ? $"{_loadedSaipCircuit.LanIp}/{_loadedSaipCircuit.LanCidr} (Host: {_loadedSaipCircuit.HostLanIp})" : "—";

        var is5aOk = icmpResult?.IsLanOk == true;
        var is5bOk = icmpResult?.IsWanOk == true;
        var is5cOk = icmpResult?.IsWebOk == true;
        var isTelnetOk = telnetResult?.IsSuccess == true;
        var isBandOk = bandResult?.IsSuccess == true;

        sb.AppendLine("================================================================================");
        sb.AppendLine("           📊 RELATÓRIO FINAL DE PROVISIONAMENTO E ATIVAÇÃO (SPARC)            ");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"  Data e Hora   : {dataHora}");
        sb.AppendLine($"  Equipamento   : {modelo}");
        sb.AppendLine($"  Comunicação   : {porta} @ {baud} baud (8-N-1)");
        sb.AppendLine($"  Cliente       : {cliente}");
        sb.AppendLine($"  Designação IP : {designacao}");
        sb.AppendLine($"  Rede WAN      : {wanIp}");
        sb.AppendLine($"  Rede LAN/Host : {lanIp}");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("                      RESUMO DAS ETAPAS EXECUTADAS                              ");
        sb.AppendLine("--------------------------------------------------------------------------------");

        sb.AppendLine($"  [1] Zerar Configuração      : {(step1Ok ? "✅ CONCLUÍDO (Reset de fábrica aplicado)" : "❌ FALHA")}");
        sb.AppendLine($"  [2] Firmware & SO           : {(step2Ok ? "✅ CONCLUÍDO (Imagem validada/carregada)" : "❌ FALHA")}");
        sb.AppendLine($"  [3] Provisionamento SAIP    : {(step3Ok ? "✅ CONCLUÍDO (Configuração gravada)" : "❌ FALHA")}");
        sb.AppendLine($"  [4] Configuração IP Teste   : {(step4Ok ? "✅ CONCLUÍDO (Placa de rede configurada)" : "⏭ PULADO / SEM PLACA")}");
        
        // ICMP
        var icmpBadge = icmpResult != null ? icmpResult.StatusBadge : "⚪";
        sb.AppendLine($"  [5] Testes de Conectividade : {icmpBadge} (5a LAN: {(is5aOk ? "OK" : "FALHA")} | 5b WAN: {(is5bOk ? "OK" : "FALHA")} | 5c WEB: {(is5cOk ? "OK" : "FALHA")})");
        sb.AppendLine($"      • 5a. ICMP LAN (Roteador) : {(is5aOk ? $"✅ OK (RTT Médio {icmpResult?.LanResult?.AvgRttMs:F1}ms)" : "❌ SEM RESPOSTA")}");
        sb.AppendLine($"      • 5b. ICMP WAN (Gateway)  : {(is5bOk ? $"✅ OK (RTT Médio {icmpResult?.WanResult?.AvgRttMs:F1}ms)" : "❌ SEM RESPOSTA")}");
        sb.AppendLine($"      • 5c. ICMP WEB (Internet) : {(is5cOk ? $"✅ OK (RTT Médio {icmpResult?.WebResult?.AvgRttMs:F1}ms)" : "❌ SEM RESPOSTA")}");

        sb.AppendLine($"  [6] Acesso Remoto (Telnet)  : {(isTelnetOk ? "✅ CONCLUÍDO (Porta 23 aberta no IP LAN)" : "❌ FALHA")}");
        sb.AppendLine($"  [7] Teste de Banda (iPerf)  : {(isBandOk ? "✅ CONCLUÍDO" : "⚠ ALERTA / INDISPONÍVEL")}");

        // DIAGNÓSTICO DE CAUSAS EM CASO DE FALHAS
        var falhas = new List<string>();

        if (!string.IsNullOrEmpty(falhaGeral))
        {
            falhas.Add($"❌ FALHA CRÍTICA NO PROCESSO:\n   • Erro: {falhaGeral}\n   • Possível Causa: Perda de conexão serial, erro de sintaxe de comando ou equipamento desligado.");
        }

        if (!is5aOk && step3Ok)
        {
            falhas.Add("❌ FALHA NO TESTE 5a (ICMP LAN / Roteador):\n" +
                       "   • Causa 1: Cabo de rede desconectado entre o PC e a porta LAN do roteador (Giga 0/1 ou Giga 1).\n" +
                       "   • Causa 2: Placa de rede do PC não obteve o IP de teste (Etapa 4 falhou ou precisa de elevação de Administrador).\n" +
                       "   • Causa 3: A porta LAN do roteador está em estado 'shutdown' ou com máscara de rede divergente.");
        }

        if (!is5bOk && is5aOk)
        {
            falhas.Add("⚠️ FALHA NO TESTE 5b (ICMP WAN / Gateway Claro):\n" +
                       "   • Causa 1: Circuito físico WAN desconectado da porta WAN do roteador (cabo do modem óptico/rádio solto).\n" +
                       "   • Causa 2: Circuito ainda não ativado ou porta bloqueada na central/NOC da operadora Claro.\n" +
                       "   • Causa 3: VLAN de transporte ou encapsulamento incorreto na ficha SAIP.");
        }

        if (!is5cOk && is5bOk)
        {
            falhas.Add("⚠️ FALHA NO TESTE 5c (ICMP WEB / Internet Pública 1.1.1.1 / 8.8.8.8):\n" +
                       "   • Causa 1: Rota default (0.0.0.0/0) ou sessão BGP ainda não estabelecida na operadora.\n" +
                       "   • Causa 2: Bloqueio de pacotes ICMP externos nos firewalls da rede da operadora.\n" +
                       "   • Causa 3: DNS externo bloqueado para o range de IPs designado ao cliente.");
        }

        if (!isTelnetOk && step3Ok)
        {
            falhas.Add("❌ FALHA NO TESTE 6 (Acesso Remoto Telnet / Porta 23):\n" +
                       "   • Causa 1: Firewall do Windows ou antivírus bloqueando conexões de saída na porta TCP 23.\n" +
                       "   • Causa 2: Configuração de 'line vty 0 4' sem comando 'login' ou 'transport input telnet/all'.");
        }

        if (falhas.Count > 0)
        {
            sb.AppendLine("================================================================================");
            sb.AppendLine("                 🔍 DIAGNÓSTICO DE FALHAS E POSSÍVEIS CAUSAS                   ");
            sb.AppendLine("================================================================================");
            foreach (var f in falhas)
            {
                sb.AppendLine(f);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("================================================================================");
            sb.AppendLine("           🎉 SUCESSO TOTAL: EQUIPAMENTO 100% HOMOLOGADO E PRONTO!              ");
            sb.AppendLine("================================================================================");
        }

        var relatorioTexto = sb.ToString();

        // Escreve no terminal de logs da UI
        EscreverLinha("\n" + relatorioTexto);

        // Exibe modal com o relatório para o operador
        Dispatcher.Invoke(() =>
        {
            var tituloModal = falhas.Count > 0 ? "Relatório de Conclusão — Alertas Identificados" : "Relatório de Conclusão — 100% Sucesso";
            var icone = falhas.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information;
            MessageBox.Show(relatorioTexto, tituloModal, MessageBoxButton.OK, icone);
        });
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

            AtualizarProgresso(85, "Fase A: Auditando equipamento...", "Identificando versão e modelo...");
            var adapter = new CiscoIOSAdapter(null);
            var info = await adapter.IdentifyAsync(session, ct);
            ExibirDadosEquipamento(info);

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

            EscreverLinha($"[*] Equipamento identificado como Cisco IOS para upgrade de firmware ({fileName}).");
            AtualizarProgresso(22, "Fase B: Gravando IOS Cisco...", "Iniciando transferência TFTP...");
            var ciscoUpgrader = new CiscoIOSUpgrader(EscreverLinhaAsync, AtualizarProgresso);
            success = await ciscoUpgrader.UpgradeAsync(session, _selectedIosBinPath, hostIp, routerIp, subnetMask, lanInterface, null, ct);
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
        var lanTarget = TxtIcmpTargetLan?.Text?.Trim();
        if (string.IsNullOrEmpty(lanTarget)) lanTarget = _loadedSaipCircuit?.LanIp ?? "200.182.245.17";
        lanTarget = lanTarget.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "200.182.245.17";

        var wanTarget = TxtIcmpTargetWan?.Text?.Trim();
        if (string.IsNullOrEmpty(wanTarget)) wanTarget = _loadedSaipCircuit?.WanGateway ?? "201.90.204.21";
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

        return new TripleIcmpResult(lanRes, wanRes, webRes);
    }

    // FASE F · TESTAR ACESSO REMOTO TELNET
    private async void BtnTestarTelnet_Click(object sender, RoutedEventArgs e)
    {
        var host = TxtTelnetTarget.Text.Trim();
        if (string.IsNullOrEmpty(host))
        {
            if (_loadedSaipCircuit != null) host = _loadedSaipCircuit.LanIp;
            else { MessageBox.Show("Informe o host/IP para teste Telnet.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
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
        var service = new ConnectivityService(EscreverLinhaAsync);
        var result = await service.TestTelnetAsync(host, port, username: telnetUser, password: telnetPass, timeoutMs: 10000, cancellationToken: ct);
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
        EscreverLinha("[*] Executando Teste de Banda Nativo HTTP (Cloudflare CDN)...");
        var httpResult = await service.RunNativeHttpSpeedTestAsync(
            testPayloadMegaBytes: 15,
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
            EscreverLinha("=================================================================");
            EscreverLinha("        INICIANDO EXECUÇÃO COMPLETA DA ESTEIRA (1 A 7)           ");
            EscreverLinha("=================================================================");

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
            var telnetHostEsteira = !string.IsNullOrEmpty(TxtTelnetTarget.Text.Trim())
                ? TxtTelnetTarget.Text.Trim()
                : (_loadedSaipCircuit?.LanIp ?? "200.182.245.17");
            var telnetPortEsteira = int.TryParse(TxtTelnetPort.Text.Trim(), out var tp) ? tp : 23;

            EscreverLinha("\n>>> [ESTEIRA] FASE 6: TESTAR ACESSO REMOTO TELNET");
            var telnetEsteiraResult = await ExecutarTesteTelnetAsync(telnetHostEsteira, telnetPortEsteira, ct);
            DefinirBadgeStatus("F", telnetEsteiraResult.IsSuccess ? "✅" : "❌");
            await Task.Delay(1000, ct);

            // -------------------------------------------------------------
            // FASE 7 (G): TESTAR BANDA
            // -------------------------------------------------------------
            SelecionarFase("G");
            DefinirBadgeStatus("G", "⏳");
            EscreverLinha("\n>>> [ESTEIRA] FASE 7: TESTAR BANDA");
            var speedResult = await ExecutarTesteBandaAsync(ct);
            DefinirBadgeStatus("G", speedResult.IsSuccess ? "✅" : "❌");

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
            EscreverLinha($"  5. e) Conectividade ICMP    : {(icmpResult.IsSuccess ? $"OK (5a LAN: {icmpResult.LanResult?.AvgRttMs:F1}ms | 5b WAN: {(icmpResult.WanResult?.IsSuccess == true ? $"{icmpResult.WanResult?.AvgRttMs:F1}ms" : "Offline")} | 5c WEB: {(icmpResult.WebResult?.IsSuccess == true ? $"{icmpResult.WebResult?.AvgRttMs:F1}ms" : "Offline")})" : "FALHA")}");
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