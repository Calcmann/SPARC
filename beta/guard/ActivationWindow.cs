using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkDevice.UI.Beta;

// Janela code-only (sem XAML) com abas separadas para ativacao online e ativacao manual por chave.
// Texto 100% ASCII para evitar mojibake em qualquer codepage do Windows.
internal sealed class ActivationWindow : Window
{
    // Campos cadastrais da Aba 1 (Online)
    private readonly TextBox _txtFirstName;
    private readonly TextBox _txtLastName;
    private readonly TextBox _txtCompany;
    private readonly TextBox _txtEmployeeId;
    private readonly TextBox _txtPhone;
    private readonly TextBox _txtEmail;
    private readonly TextBox _txtCluster;
    private readonly TextBox _txtUf;
    private readonly Button _btnSolicitarOnline;
    private readonly Button _btnCheckOnline;
    private readonly Button _btnCopyWhatsOnline;
    private readonly TextBlock _txtStatusOnline;

    // Campos da Aba 2 (Manual por chave)
    private readonly TextBox _txtReqManual;
    private readonly Button _btnCopyMachineCode;
    private readonly TextBox _txtLicenseManual;
    private readonly Button _btnAtivarManual;
    private readonly TextBlock _txtStatusManual;

    private readonly MachineIdentity _machine;
    private readonly string _userProfilePath;
    private System.Windows.Threading.DispatcherTimer? _pollTimer;

    public ActivationWindow(string reason, MachineIdentity machine, DateTime betaExpires, int initialTab = 0)
    {
        _machine = machine;
        _userProfilePath = Path.Combine(BetaConfig.DataDir, "user_profile.json");

        Title = "SPARC " + BetaConfig.Tag + " - Controle de Copias e Ativacao";
        Width = 680;
        Height = 730;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)); // Zinc 900

        var mainPanel = new DockPanel { Margin = new Thickness(16) };
        Content = mainPanel;

        // TOP: Titulo, Subtitulo e Alerta de Motivo
        var topPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(topPanel, Dock.Top);
        mainPanel.Children.Add(topPanel);

        topPanel.Children.Add(new TextBlock
        {
            Text = "SPARC " + BetaConfig.Tag + " — Controle de Copias & Ativacao",
            Foreground = Brushes.White,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap
        });
        topPanel.Children.Add(new TextBlock
        {
            Text = "Identificacao obrigatoria do tecnico e licenciamento gerenciado pelo Administrador corporativo Claro.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 6),
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(reason))
        {
            var alertBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x15, 0x15)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 4, 0, 8)
            };
            alertBorder.Child = new TextBlock
            {
                Text = "⚠️ " + reason,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5)),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };
            topPanel.Children.Add(alertBorder);
        }

        // TAB CONTROL
        var tabControl = new TabControl
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            SelectedIndex = Math.Clamp(initialTab, 0, 1)
        };
        mainPanel.Children.Add(tabControl);

        // ---------------------------------------------------------------------------------
        // ABA 1: CADASTRO & ATIVACAO ONLINE (1 CLIQUE)
        // ---------------------------------------------------------------------------------
        var tabOnline = new TabItem
        {
            Header = "  🌐 Cadastro & Ativacao Online  ",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            Foreground = Brushes.White,
            Padding = new Thickness(12, 6, 12, 6)
        };

        var scrollOnline = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var rootOnline = new StackPanel { Margin = new Thickness(14) };
        scrollOnline.Content = rootOnline;
        tabOnline.Content = scrollOnline;
        tabControl.Items.Add(tabOnline);

        // Explicacao do Metodo Online
        var boxInfoOnline = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x22, 0x33)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x02, 0x84, 0xC7)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        boxInfoOnline.Child = new TextBlock
        {
            Text = "ℹ️ Metodo Automatico (Recomendado):\nPreencha seus dados abaixo e clique em 'Solicitar Ativacao Online'. Assim que o Administrador aprovar no painel, este SPARC ativara automaticamente sem necessidade de digitar chaves manuais.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xBA, 0xE6, 0xFD)),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        };
        rootOnline.Children.Add(boxInfoOnline);

        // Card 1: Dados do Tecnico
        var groupTecnico = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spTec = new StackPanel();
        spTec.Children.Add(new TextBlock
        {
            Text = "1) Identificacao do Tecnico (Registrado no Banco de Dados):",
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var gridCampos = new Grid();
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Linha 0: Nome e Sobrenome
        var pnlNome = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        pnlNome.Children.Add(new TextBlock { Text = "Nome:", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtFirstName = new TextBox { Height = 28, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) };
        pnlNome.Children.Add(_txtFirstName);
        Grid.SetRow(pnlNome, 0); Grid.SetColumn(pnlNome, 0);
        gridCampos.Children.Add(pnlNome);

        var pnlSobrenome = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        pnlSobrenome.Children.Add(new TextBlock { Text = "Sobrenome:", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtLastName = new TextBox { Height = 28, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) };
        pnlSobrenome.Children.Add(_txtLastName);
        Grid.SetRow(pnlSobrenome, 0); Grid.SetColumn(pnlSobrenome, 2);
        gridCampos.Children.Add(pnlSobrenome);

        // Linha 1: Empresa e Matricula
        var pnlCompany = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        pnlCompany.Children.Add(new TextBlock { Text = "Empresa (ex: Claro, Telemont, Propria...):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtCompany = new TextBox { Height = 28, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) };
        pnlCompany.Children.Add(_txtCompany);
        Grid.SetRow(pnlCompany, 1); Grid.SetColumn(pnlCompany, 0);
        gridCampos.Children.Add(pnlCompany);

        var pnlEmployeeId = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        pnlEmployeeId.Children.Add(new TextBlock { Text = "Matricula do Tecnico:", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtEmployeeId = new TextBox { Height = 28, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(6, 4, 6, 4), CharacterCasing = CharacterCasing.Upper, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) };
        pnlEmployeeId.Children.Add(_txtEmployeeId);
        Grid.SetRow(pnlEmployeeId, 1); Grid.SetColumn(pnlEmployeeId, 2);
        gridCampos.Children.Add(pnlEmployeeId);

        // Linha 2: Telefone/WhatsApp e Email
        var pnlTel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        pnlTel.Children.Add(new TextBlock { Text = "Telefone / WhatsApp (com DDD):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtPhone = new TextBox { Height = 28, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) };
        pnlTel.Children.Add(_txtPhone);
        Grid.SetRow(pnlTel, 2); Grid.SetColumn(pnlTel, 0);
        gridCampos.Children.Add(pnlTel);

        var pnlEmail = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        pnlEmail.Children.Add(new TextBlock { Text = "E-mail Corporativo (opcional):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtEmail = new TextBox { Height = 28, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) };
        pnlEmail.Children.Add(_txtEmail);
        Grid.SetRow(pnlEmail, 2); Grid.SetColumn(pnlEmail, 2);
        gridCampos.Children.Add(pnlEmail);

        // Linha 3: Cluster e UF
        var pnlCluster = new StackPanel();
        pnlCluster.Children.Add(new TextBlock { Text = "Cluster de Atuacao (ex: SP Capital, Interior...):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtCluster = new TextBox { Height = 28, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) };
        pnlCluster.Children.Add(_txtCluster);
        Grid.SetRow(pnlCluster, 3); Grid.SetColumn(pnlCluster, 0);
        gridCampos.Children.Add(pnlCluster);

        var pnlUf = new StackPanel();
        pnlUf.Children.Add(new TextBlock { Text = "UF (ex: SP):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtUf = new TextBox { Height = 28, Margin = new Thickness(0, 3, 0, 0), Padding = new Thickness(6, 4, 6, 4), MaxLength = 2, CharacterCasing = CharacterCasing.Upper, Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)) };
        pnlUf.Children.Add(_txtUf);
        Grid.SetRow(pnlUf, 3); Grid.SetColumn(pnlUf, 2);
        gridCampos.Children.Add(pnlUf);

        spTec.Children.Add(gridCampos);
        groupTecnico.Child = spTec;
        rootOnline.Children.Add(groupTecnico);

        // Card 2: Acoes de Solicitacao Online
        var groupAcoesOnline = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spAcoes = new StackPanel();
        spAcoes.Children.Add(new TextBlock
        {
            Text = "2) Envio da Solicitacao ao Administrador:",
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var pnlBotoesOnline = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _btnSolicitarOnline = new Button
        {
            Content = "🌐 Solicitar Ativacao Online (1 Clique)",
            Width = 260,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _btnSolicitarOnline.Click += async (_, _) => await SolicitarAtivacaoOnlineAsync();
        pnlBotoesOnline.Children.Add(_btnSolicitarOnline);

        _btnCheckOnline = new Button
        {
            Content = "🔄 Checar Aprovacao",
            Width = 150,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _btnCheckOnline.Click += async (_, _) => await VerificarLiberacaoOnlineAsync(exibirMensagemSeNaoAprovado: true);
        pnlBotoesOnline.Children.Add(_btnCheckOnline);

        _btnCopyWhatsOnline = new Button
        {
            Content = "📋 Copiar para WhatsApp",
            Width = 160,
            Height = 34,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _btnCopyWhatsOnline.Click += (_, _) => CopiarSolicitacao();
        pnlBotoesOnline.Children.Add(_btnCopyWhatsOnline);

        spAcoes.Children.Add(pnlBotoesOnline);

        _txtStatusOnline = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 11.5
        };
        spAcoes.Children.Add(_txtStatusOnline);

        groupAcoesOnline.Child = spAcoes;
        rootOnline.Children.Add(groupAcoesOnline);

        // Barra inferior da Aba 1 (Botao Sair)
        var barOnline = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var btnSairOnline = new Button { Content = "Sair do SPARC", Width = 120, Height = 30, Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        btnSairOnline.Click += (_, _) => { DialogResult = false; };
        barOnline.Children.Add(btnSairOnline);
        rootOnline.Children.Add(barOnline);


        // ---------------------------------------------------------------------------------
        // ABA 2: ATIVACAO MANUAL VIA CHAVE (OFFLINE / SUPORTE)
        // ---------------------------------------------------------------------------------
        var tabManual = new TabItem
        {
            Header = "  🔑 Ativacao Manual via Chave  ",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            Foreground = Brushes.White,
            Padding = new Thickness(12, 6, 12, 6)
        };

        var scrollManual = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var rootManual = new StackPanel { Margin = new Thickness(14) };
        scrollManual.Content = rootManual;
        tabManual.Content = scrollManual;
        tabControl.Items.Add(tabManual);

        // Explicacao do Metodo Manual
        var boxInfoManual = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x29, 0x1E, 0x08)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12)
        };
        boxInfoManual.Child = new TextBlock
        {
            Text = "🔑 Uso Exclusivo para Ativacao Manual por Chave:\nUtilize esta aba apenas se voce recebeu uma Chave de Ativacao (Token alfanumerico SPB1...) enviada diretamente pelo seu Gestor ou Suporte Tecnico.\n\nSe voce deseja solicitar aprovacao online automatica, utilize a aba '🌐 Cadastro & Ativacao Online'.",
            Foreground = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A)),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap
        };
        rootManual.Children.Add(boxInfoManual);

        // Secao 1 da Aba Manual: Codigo da Maquina para envio ao Suporte
        var groupCodManual = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spCodManual = new StackPanel();
        spCodManual.Children.Add(new TextBlock
        {
            Text = "1) Codigo desta Maquina (Node-Locking):",
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });
        spCodManual.Children.Add(new TextBlock
        {
            Text = "Envie este codigo ao responsavel caso ele precise gerar sua chave manualmente:",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _txtReqManual = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 55,
            Margin = new Thickness(0, 0, 0, 8),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        spCodManual.Children.Add(_txtReqManual);

        _btnCopyMachineCode = new Button
        {
            Content = "📋 Copiar Codigo da Maquina",
            Width = 220,
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _btnCopyMachineCode.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_txtReqManual.Text.Trim());
                _txtStatusManual.Text = "Codigo da maquina copiado para a area de transferencia!";
                _txtStatusManual.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
            }
            catch { }
        };
        spCodManual.Children.Add(_btnCopyMachineCode);

        groupCodManual.Child = spCodManual;
        rootManual.Children.Add(groupCodManual);

        // Secao 2 da Aba Manual: Inserir a Chave Recebida
        var groupKeyManual = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var spKeyManual = new StackPanel();
        spKeyManual.Children.Add(new TextBlock
        {
            Text = "2) Inserir Chave de Ativacao Recebida:",
            Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });
        spKeyManual.Children.Add(new TextBlock
        {
            Text = "Cole abaixo a chave gerada pelo Administrador (comeca com 'SPB1.'):",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 6)
        });

        _txtLicenseManual = new TextBox
        {
            TextWrapping = TextWrapping.Wrap,
            Height = 65,
            Margin = new Thickness(0, 0, 0, 8),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        spKeyManual.Children.Add(_txtLicenseManual);

        _txtStatusManual = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 11.5
        };
        spKeyManual.Children.Add(_txtStatusManual);

        var pnlActionsManual = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        var btnSairManual = new Button { Content = "Sair", Width = 100, Height = 32, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        btnSairManual.Click += (_, _) => { DialogResult = false; };
        pnlActionsManual.Children.Add(btnSairManual);

        _btnAtivarManual = new Button
        {
            Content = "✅ Validar e Ativar SPARC",
            Width = 220,
            Height = 32,
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _btnAtivarManual.Click += (_, _) => TentarAtivarManual();
        pnlActionsManual.Children.Add(_btnAtivarManual);

        spKeyManual.Children.Add(pnlActionsManual);
        groupKeyManual.Child = spKeyManual;
        rootManual.Children.Add(groupKeyManual);

        // Carrega perfil salvo anteriormente
        CarregarPerfilSalvo();

        // Handlers de digitacao na aba online
        _txtFirstName.TextChanged += (_, _) => AtualizarRequisicao();
        _txtLastName.TextChanged += (_, _) => AtualizarRequisicao();
        _txtCompany.TextChanged += (_, _) => AtualizarRequisicao();
        _txtEmployeeId.TextChanged += (_, _) => AtualizarRequisicao();
        _txtPhone.TextChanged += (_, _) => AtualizarRequisicao();
        _txtEmail.TextChanged += (_, _) => AtualizarRequisicao();
        _txtCluster.TextChanged += (_, _) => AtualizarRequisicao();
        _txtUf.TextChanged += (_, _) => AtualizarRequisicao();

        Closed += (_, _) => { _pollTimer?.Stop(); };

        AtualizarRequisicao();
    }

    private void CarregarPerfilSalvo()
    {
        try
        {
            if (File.Exists(_userProfilePath))
            {
                var json = File.ReadAllText(_userProfilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("fn", out var fn)) _txtFirstName.Text = fn.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("ln", out var ln)) _txtLastName.Text = ln.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("cmp", out var cmp)) _txtCompany.Text = cmp.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("mat", out var mat)) _txtEmployeeId.Text = mat.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("ph", out var ph)) _txtPhone.Text = ph.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("em", out var em)) _txtEmail.Text = em.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("cl", out var cl)) _txtCluster.Text = cl.GetString() ?? "";
                if (doc.RootElement.TryGetProperty("uf", out var uf)) _txtUf.Text = uf.GetString() ?? "";
            }
        }
        catch { }
    }

    private void SalvarPerfil()
    {
        try
        {
            Directory.CreateDirectory(BetaConfig.DataDir);
            var obj = new
            {
                fn = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtFirstName.Text),
                ln = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtLastName.Text),
                cmp = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtCompany.Text),
                mat = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatEmployeeId(_txtEmployeeId.Text),
                ph = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPhone(_txtPhone.Text),
                em = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatEmail(_txtEmail.Text),
                cl = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatCluster(_txtCluster.Text),
                uf = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatUf(_txtUf.Text)
            };
            File.WriteAllText(_userProfilePath, JsonSerializer.Serialize(obj));
        }
        catch { }
    }

    private void AtualizarRequisicao()
    {
        var fn = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtFirstName.Text);
        var ln = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtLastName.Text);
        var cmp = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtCompany.Text);
        var mat = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatEmployeeId(_txtEmployeeId.Text);
        var ph = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPhone(_txtPhone.Text);
        var em = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatEmail(_txtEmail.Text);
        var cl = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatCluster(_txtCluster.Text);
        var uf = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatUf(_txtUf.Text);

        var faltando = string.IsNullOrWhiteSpace(fn) || string.IsNullOrWhiteSpace(ln) ||
                        string.IsNullOrWhiteSpace(cmp) || string.IsNullOrWhiteSpace(mat) ||
                        string.IsNullOrWhiteSpace(ph) || string.IsNullOrWhiteSpace(cl) || string.IsNullOrWhiteSpace(uf);

        var req = MachineId.BuildRequest(_machine, fn, ln, ph, em, cl, uf, BetaConfig.Tag, cmp, mat);
        if (_txtReqManual != null)
        {
            _txtReqManual.Text = req;
        }

        if (faltando)
        {
            _btnCopyWhatsOnline.IsEnabled = false;
            _btnSolicitarOnline.IsEnabled = false;
            _txtStatusOnline.Text = "Preencha todos os campos cadastrais obrigatorios acima para liberar a solicitacao.";
            _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        }
        else
        {
            SalvarPerfil();
            _btnCopyWhatsOnline.IsEnabled = true;
            _btnSolicitarOnline.IsEnabled = true;
            _txtStatusOnline.Text = "Cadastro completo! Clique em 'Solicitar Ativacao Online (1 Clique)' para registrar.";
            _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
        }
    }

    private void CopiarSolicitacao()
    {
        try
        {
            var fn = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtFirstName.Text);
            var ln = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtLastName.Text);
            var cmp = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtCompany.Text);
            var mat = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatEmployeeId(_txtEmployeeId.Text);
            var ph = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPhone(_txtPhone.Text);
            var em = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatEmail(_txtEmail.Text);
            var cl = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatCluster(_txtCluster.Text);
            var uf = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatUf(_txtUf.Text);
            var req = _txtReqManual.Text.Trim();

            var msg = $"*Solicitacao de Ativacao SPARC*\n" +
                      $"• *Tecnico:* {fn} {ln}\n" +
                      $"• *Empresa:* {cmp} (Matrícula: {mat})\n" +
                      $"• *Telefone:* {ph}\n" +
                      (string.IsNullOrWhiteSpace(em) ? "" : $"• *E-mail:* {em}\n") +
                      $"• *Cluster:* {cl}\n" +
                      $"• *UF:* {uf}\n" +
                      $"• *Codigo:*\n`{req}`";

            Clipboard.SetText(msg);
            _txtStatusOnline.Text = "Solicitacao completa copiada para a area de transferencia! Envie ao gestor no WhatsApp.";
            _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
        }
        catch { }
    }

    private async System.Threading.Tasks.Task SolicitarAtivacaoOnlineAsync()
    {
        var fn = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtFirstName.Text);
        var ln = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtLastName.Text);
        var cmp = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPersonOrCompanyName(_txtCompany.Text);
        var mat = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatEmployeeId(_txtEmployeeId.Text);
        var ph = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatPhone(_txtPhone.Text);
        var em = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatEmail(_txtEmail.Text);
        var cl = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatCluster(_txtCluster.Text);
        var uf = NetworkDevice.Core.Licensing.SparcTextSanitizer.FormatUf(_txtUf.Text);
        var reqCode = _txtReqManual.Text.Trim();

        if (string.IsNullOrWhiteSpace(fn) || string.IsNullOrWhiteSpace(ln) ||
            string.IsNullOrWhiteSpace(cmp) || string.IsNullOrWhiteSpace(mat) ||
            string.IsNullOrWhiteSpace(ph) || string.IsNullOrWhiteSpace(cl) || string.IsNullOrWhiteSpace(uf))
        {
            _txtStatusOnline.Text = "Preencha todos os campos cadastrais obrigatorios antes de solicitar a ativacao online.";
            _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            return;
        }

        _btnSolicitarOnline.IsEnabled = false;
        _txtStatusOnline.Text = "Enviando solicitacao de ativacao para o painel do Administrador...";
        _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));

        try
        {
            var reqObj = new NetworkDevice.Core.Licensing.OnlineActivationRequest
            {
                MachineGuid = _machine.MachineGuid,
                MachineFingerprint = _machine.Fingerprint,
                RawRequestCode = reqCode,
                FirstName = fn,
                LastName = ln,
                Company = cmp,
                EmployeeId = mat,
                Phone = ph,
                Email = em,
                Cluster = cl,
                Uf = uf,
                ClientVersion = BetaConfig.Tag,
                Platform = "Windows"
            };

            var svc = new NetworkDevice.Core.Licensing.CloudLicenseService(customToken: BetaConfig.GitHubReadOnlyToken);
            var (ok, msg) = await svc.SubmitActivationRequestAsync(reqObj);

            if (ok)
            {
                _btnSolicitarOnline.Content = "⏳ Solicitacao Enviada! Aguardando...";
                _txtStatusOnline.Text = "✅ Solicitacao online enviada ao Administrador! O SPARC sera ativado automaticamente assim que for aprovado no painel.";
                _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));

                IniciarPollingAprovacao();
            }
            else
            {
                _btnSolicitarOnline.IsEnabled = true;
                _txtStatusOnline.Text = "Aviso ao enviar: " + msg + " Voce pode usar o botao de Copiar para WhatsApp.";
                _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            }
        }
        catch (Exception ex)
        {
            _btnSolicitarOnline.IsEnabled = true;
            _txtStatusOnline.Text = "Falha de conexao: " + ex.Message + ". Utilize o envio manual por WhatsApp.";
            _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
        }
    }

    private void IniciarPollingAprovacao()
    {
        if (_pollTimer == null)
        {
            _pollTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _pollTimer.Tick += async (_, _) => await VerificarLiberacaoOnlineAsync(exibirMensagemSeNaoAprovado: false);
        }
        _pollTimer.Start();
    }

    private async System.Threading.Tasks.Task VerificarLiberacaoOnlineAsync(bool exibirMensagemSeNaoAprovado = false)
    {
        if (exibirMensagemSeNaoAprovado)
        {
            _txtStatusOnline.Text = "Consultando status de aprovacao da sua maquina no banco de dados...";
            _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
        }

        try
        {
            var svc = new NetworkDevice.Core.Licensing.CloudLicenseService(customToken: BetaConfig.GitHubReadOnlyToken);

            // 1. Checa a solicitacao individual em requests/{machineGuid}.json
            var (found, req, msg) = await svc.CheckActivationRequestStatusAsync(_machine.MachineGuid);
            if (found && req != null)
            {
                if (string.Equals(req.Status, "Approved", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(req.ApprovedToken))
                {
                    _pollTimer?.Stop();
                    _txtStatusOnline.Text = "🎉 Solicitacao aprovada pelo Administrador! Ativando SPARC...";
                    _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                    _txtLicenseManual.Text = req.ApprovedToken;
                    TentarAtivarManual();
                    return;
                }
                else if (string.Equals(req.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    _pollTimer?.Stop();
                    _btnSolicitarOnline.IsEnabled = true;
                    _btnSolicitarOnline.Content = "🌐 Solicitar Ativacao Online (1 Clique)";
                    _txtStatusOnline.Text = $"❌ Solicitacao rejeitada pelo Administrador: {req.RejectionReason ?? "Sem justificativa informada."}";
                    _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                    return;
                }
            }

            // 2. Fallback: Checa na lista consolidada em devices.json
            var remoteList = await svc.FetchRemoteDevicesAsync();
            var dev = remoteList.FirstOrDefault(d =>
                (!string.IsNullOrEmpty(_machine.MachineGuid) && d.MachineGuid.Equals(_machine.MachineGuid, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(_machine.Fingerprint) && d.MachineFingerprint.Equals(_machine.Fingerprint, StringComparison.OrdinalIgnoreCase)));

            if (dev != null)
            {
                if (string.Equals(dev.Status, "Revoked", StringComparison.OrdinalIgnoreCase))
                {
                    _pollTimer?.Stop();
                    _txtStatusOnline.Text = "Licenca revogada pelo Administrador.";
                    _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                    return;
                }

                if (!string.IsNullOrWhiteSpace(dev.AuthorizedToken))
                {
                    _pollTimer?.Stop();
                    _txtStatusOnline.Text = "🎉 Liberacao localizada no banco de dados! Ativando...";
                    _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                    _txtLicenseManual.Text = dev.AuthorizedToken;
                    TentarAtivarManual();
                    return;
                }
            }

            if (exibirMensagemSeNaoAprovado)
            {
                _txtStatusOnline.Text = "Sua maquina ainda esta pendente de aprovacao no painel do Gestor.";
                _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            }
        }
        catch (Exception ex)
        {
            if (exibirMensagemSeNaoAprovado)
            {
                _txtStatusOnline.Text = "Falha ao verificar online: " + ex.Message;
                _txtStatusOnline.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            }
        }
    }

    private void TentarAtivarManual()
    {
        var token = _txtLicenseManual.Text.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            _txtStatusManual.Text = "Cole a chave de ativacao fornecida no campo acima antes de ativar.";
            _txtStatusManual.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            return;
        }

        if (!LicenseCrypto.TryValidate(token, out var info) || info is null)
        {
            _txtStatusManual.Text = "Chave invalida: a assinatura criptografica nao confere com a chave publica deste aplicativo.";
            _txtStatusManual.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            return;
        }

        var now = BetaClock.EffectiveUtcNow();
        if (!LicenseCrypto.IsAuthorizedForThisMachine(info, now, out var reason))
        {
            _txtStatusManual.Text = reason;
            _txtStatusManual.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xF1, 0x71));
            return;
        }

        try
        {
            Directory.CreateDirectory(BetaConfig.DataDir);
            File.WriteAllText(BetaConfig.LicensePath, token);
            SalvarPerfil();
            BetaClock.Touch(now);
            _txtStatusManual.Text = "✅ Chave validada com sucesso! Acesso liberado.";
            _txtStatusManual.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
        }
        catch (Exception ex)
        {
            _txtStatusManual.Text = "Chave valida, mas falhou ao gravar arquivo local: " + ex.Message;
            _txtStatusManual.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            return;
        }

        DialogResult = true;
    }
}
