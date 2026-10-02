using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkDevice.UI.Beta;

// Janela code-only (sem XAML) para nao alterar nenhum XAML da base.
// Texto 100% ASCII de proposito: evita mojibake em qualquer codepage.
internal sealed class ActivationWindow : Window
{
    private readonly TextBox _txtFirstName;
    private readonly TextBox _txtLastName;
    private readonly TextBox _txtCompany;
    private readonly TextBox _txtEmployeeId;
    private readonly TextBox _txtPhone;
    private readonly TextBox _txtEmail;
    private readonly TextBox _txtCluster;
    private readonly TextBox _txtUf;
    private readonly TextBox _txtReq;
    private readonly Button _btnCopy;
    private readonly Button _btnSolicitarOnline;
    private readonly TextBox _txtLicense;
    private readonly TextBlock _txtStatus;
    private readonly MachineIdentity _machine;
    private readonly string _userProfilePath;
    private System.Windows.Threading.DispatcherTimer? _pollTimer;

    public ActivationWindow(string reason, MachineIdentity machine, DateTime betaExpires)
    {
        _machine = machine;
        _userProfilePath = Path.Combine(BetaConfig.DataDir, "user_profile.json");

        Title = "SPARC " + BetaConfig.Tag + " - Ativacao e Identificacao do Tecnico";
        Width = 660;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1A));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = root;
        Content = scroll;

        root.Children.Add(new TextBlock { Text = "Versao " + BetaConfig.Tag + " - Controle de Copias em Campo", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = "Identificacao obrigatoria do tecnico e ativacao de licenca gerenciada pelo Administrador.", Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(reason))
            root.Children.Add(new TextBlock { Text = reason, Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        // SECAO 1: DADOS OBRIGATORIOS DO TECNICO
        var groupTecnico = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var spTec = new StackPanel();
        spTec.Children.Add(new TextBlock { Text = "1) Identificacao do Tecnico (Registrado no Admin):", Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

        var gridCampos = new Grid();
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Linha 0: Nome e Sobrenome
        var pnlNome = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        pnlNome.Children.Add(new TextBlock { Text = "Nome:", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtFirstName = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlNome.Children.Add(_txtFirstName);
        Grid.SetRow(pnlNome, 0); Grid.SetColumn(pnlNome, 0);
        gridCampos.Children.Add(pnlNome);

        var pnlSobrenome = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        pnlSobrenome.Children.Add(new TextBlock { Text = "Sobrenome:", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtLastName = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlSobrenome.Children.Add(_txtLastName);
        Grid.SetRow(pnlSobrenome, 0); Grid.SetColumn(pnlSobrenome, 2);
        gridCampos.Children.Add(pnlSobrenome);

        // Linha 1: Empresa e Matricula
        var pnlCompany = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        pnlCompany.Children.Add(new TextBlock { Text = "Empresa (ex: Claro, Telemont, Propria...):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtCompany = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlCompany.Children.Add(_txtCompany);
        Grid.SetRow(pnlCompany, 1); Grid.SetColumn(pnlCompany, 0);
        gridCampos.Children.Add(pnlCompany);

        var pnlEmployeeId = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        pnlEmployeeId.Children.Add(new TextBlock { Text = "Matricula:", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtEmployeeId = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2), CharacterCasing = CharacterCasing.Upper };
        pnlEmployeeId.Children.Add(_txtEmployeeId);
        Grid.SetRow(pnlEmployeeId, 1); Grid.SetColumn(pnlEmployeeId, 2);
        gridCampos.Children.Add(pnlEmployeeId);

        // Linha 2: Telefone/WhatsApp e Email (Opcional)
        var pnlTel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        pnlTel.Children.Add(new TextBlock { Text = "Telefone / WhatsApp (com DDD):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtPhone = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlTel.Children.Add(_txtPhone);
        Grid.SetRow(pnlTel, 2); Grid.SetColumn(pnlTel, 0);
        gridCampos.Children.Add(pnlTel);

        var pnlEmail = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        pnlEmail.Children.Add(new TextBlock { Text = "E-mail (opcional):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtEmail = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlEmail.Children.Add(_txtEmail);
        Grid.SetRow(pnlEmail, 2); Grid.SetColumn(pnlEmail, 2);
        gridCampos.Children.Add(pnlEmail);

        // Linha 3: Cluster de Atuacao e UF
        var pnlCluster = new StackPanel();
        pnlCluster.Children.Add(new TextBlock { Text = "Cluster de Atuacao (ex: SP Capital, Interior...):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtCluster = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlCluster.Children.Add(_txtCluster);
        Grid.SetRow(pnlCluster, 3); Grid.SetColumn(pnlCluster, 0);
        gridCampos.Children.Add(pnlCluster);

        var pnlUf = new StackPanel();
        pnlUf.Children.Add(new TextBlock { Text = "UF (ex: SP):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtUf = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2), MaxLength = 2, CharacterCasing = CharacterCasing.Upper };
        pnlUf.Children.Add(_txtUf);
        Grid.SetRow(pnlUf, 3); Grid.SetColumn(pnlUf, 2);
        gridCampos.Children.Add(pnlUf);

        spTec.Children.Add(gridCampos);
        groupTecnico.Child = spTec;
        root.Children.Add(groupTecnico);

        // SECAO 2: CODIGO DE ATIVACAO
        root.Children.Add(new TextBlock { Text = "2) Codigo de ativacao desta maquina (copie e envie ao responsavel):", Foreground = Brushes.LightGray, FontWeight = FontWeights.SemiBold });
        _txtReq = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 60, Margin = new Thickness(0, 4, 0, 4), FontFamily = new FontFamily("Consolas"), FontSize = 10, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(_txtReq);

        _txtStatus = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), FontSize = 11.5 };
        
        var pnlActionsOnline = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _btnSolicitarOnline = new Button 
        { 
            Content = "🌐 Solicitar Ativacao Online (1 Clique)", 
            Width = 260, 
            Height = 32, 
            Margin = new Thickness(0, 0, 8, 0), 
            FontWeight = FontWeights.Bold,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _btnSolicitarOnline.Click += async (_, _) => await SolicitarAtivacaoOnlineAsync();
        pnlActionsOnline.Children.Add(_btnSolicitarOnline);

        var btnCheckOnline = new Button 
        { 
            Content = "🔄 Checar Aprovacao", 
            Width = 160, 
            Height = 32, 
            Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88)), 
            Foreground = Brushes.White, 
            BorderThickness = new Thickness(0), 
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnCheckOnline.Click += async (_, _) => await VerificarLiberacaoOnlineAsync(exibirMensagemSeNaoAprovado: true);
        pnlActionsOnline.Children.Add(btnCheckOnline);

        root.Children.Add(pnlActionsOnline);

        var pnlCopy = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _btnCopy = new Button { Content = "📋 Copiar para Envio Manual / WhatsApp", Width = 260, Height = 28, Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.Normal, FontSize = 11 };
        _btnCopy.Click += (_, _) => CopiarSolicitacao();
        pnlCopy.Children.Add(_btnCopy);

        root.Children.Add(pnlCopy);
        root.Children.Add(_txtStatus);

        // SECAO 3: CHAVE DE ATIVACAO
        root.Children.Add(new TextBlock { Text = "3) Chave de Ativacao (Preenchida automaticamente online ou cole manualmente):", Foreground = Brushes.LightGray, FontWeight = FontWeights.SemiBold });
        _txtLicense = new TextBox { TextWrapping = TextWrapping.Wrap, Height = 56, Margin = new Thickness(0, 4, 0, 10), FontFamily = new FontFamily("Consolas"), FontSize = 10, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(_txtLicense);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnSair = new Button { Content = "Sair", Width = 100, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        btnSair.Click += (_, _) => { DialogResult = false; };
        var btnAtivar = new Button { Content = "Ativar SPARC", Width = 140, Height = 30, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)), Foreground = Brushes.White };
        btnAtivar.Click += (_, _) => TentarAtivar();
        bar.Children.Add(btnSair);
        bar.Children.Add(btnAtivar);
        root.Children.Add(bar);

        // Carregar perfil salvo anteriormente
        CarregarPerfilSalvo();

        // Handlers de digitacao
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

        if (faltando)
        {
            _txtReq.Text = "(Preencha Nome, Sobrenome, Empresa, Matricula, Telefone, Cluster e UF acima para gerar o codigo de ativacao)";
            _txtReq.Foreground = Brushes.Gray;
            _btnCopy.IsEnabled = false;
            _btnSolicitarOnline.IsEnabled = false;
            _txtStatus.Text = "Preencha todos os campos obrigatorios acima para liberar a solicitacao de ativacao.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        }
        else
        {
            SalvarPerfil();
            var req = MachineId.BuildRequest(_machine, fn, ln, ph, em, cl, uf, BetaConfig.Tag, cmp, mat);
            _txtReq.Text = req;
            _txtReq.Foreground = Brushes.White;
            _btnCopy.IsEnabled = true;
            _btnSolicitarOnline.IsEnabled = true;
            _txtStatus.Text = "Cadastro completo! Clique em 'Solicitar Ativacao Online (1 Clique)' ou envie manualmente pelo WhatsApp.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
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
            var req = _txtReq.Text.Trim();

            var msg = $"*Solicitacao de Ativacao SPARC*\n" +
                      $"• *Tecnico:* {fn} {ln}\n" +
                      $"• *Empresa:* {cmp} (Matrícula: {mat})\n" +
                      $"• *Telefone:* {ph}\n" +
                      (string.IsNullOrWhiteSpace(em) ? "" : $"• *E-mail:* {em}\n") +
                      $"• *Cluster:* {cl}\n" +
                      $"• *UF:* {uf}\n" +
                      $"• *Codigo:*\n`{req}`";

            Clipboard.SetText(msg);
            _txtStatus.Text = "Solicitacao completa copiada para a area de transferencia! Envie ao gestor no WhatsApp.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
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
        var reqCode = _txtReq.Text.Trim();

        if (string.IsNullOrWhiteSpace(fn) || string.IsNullOrWhiteSpace(ln) ||
            string.IsNullOrWhiteSpace(cmp) || string.IsNullOrWhiteSpace(mat) ||
            string.IsNullOrWhiteSpace(ph) || string.IsNullOrWhiteSpace(cl) || string.IsNullOrWhiteSpace(uf))
        {
            _txtStatus.Text = "Preencha todos os campos cadastrais obrigatorios antes de solicitar a ativacao online.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            return;
        }

        _btnSolicitarOnline.IsEnabled = false;
        _txtStatus.Text = "Enviando solicitacao de ativacao para o painel do Administrador...";
        _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));

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
                _txtStatus.Text = "✅ Solicitacao online enviada ao Administrador! O SPARC sera ativado automaticamente assim que for aprovado no painel.";
                _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));

                IniciarPollingAprovacao();
            }
            else
            {
                _btnSolicitarOnline.IsEnabled = true;
                _txtStatus.Text = "Aviso ao enviar: " + msg + " Voce pode usar o botao manual de Copiar/WhatsApp.";
                _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            }
        }
        catch (Exception ex)
        {
            _btnSolicitarOnline.IsEnabled = true;
            _txtStatus.Text = "Falha de conexao: " + ex.Message + ". Utilize o envio manual.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
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
            _txtStatus.Text = "Consultando status de aprovacao da sua maquina...";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
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
                    _txtStatus.Text = "🎉 Solicitacao aprovada pelo Administrador! Ativando SPARC...";
                    _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                    _txtLicense.Text = req.ApprovedToken;
                    TentarAtivar();
                    return;
                }
                else if (string.Equals(req.Status, "Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    _pollTimer?.Stop();
                    _btnSolicitarOnline.IsEnabled = true;
                    _btnSolicitarOnline.Content = "🌐 Solicitar Ativacao Online (1 Clique)";
                    _txtStatus.Text = $"❌ Solicitacao rejeitada pelo Administrador: {req.RejectionReason ?? "Sem justificativa informada."}";
                    _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
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
                    _txtStatus.Text = "Licenca revogada pelo Administrador.";
                    _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                    return;
                }

                if (!string.IsNullOrWhiteSpace(dev.AuthorizedToken))
                {
                    _pollTimer?.Stop();
                    _txtStatus.Text = "Liberacao localizada! Ativando...";
                    _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                    _txtLicense.Text = dev.AuthorizedToken;
                    TentarAtivar();
                    return;
                }
            }

            if (exibirMensagemSeNaoAprovado)
            {
                _txtStatus.Text = "Sua maquina ainda esta pendente de aprovacao no painel do Gestor.";
                _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
            }
        }
        catch (Exception ex)
        {
            if (exibirMensagemSeNaoAprovado)
            {
                _txtStatus.Text = "Falha ao verificar online: " + ex.Message;
                _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
            }
        }
    }

    private void TentarAtivar()
    {
        var token = _txtLicense.Text.Trim();
        if (!LicenseCrypto.TryValidate(token, out var info) || info is null)
        { _txtStatus.Text = "Chave invalida (assinatura nao confere)."; return; }
        var now = BetaClock.EffectiveUtcNow();
        if (!LicenseCrypto.IsAuthorizedForThisMachine(info, now, out var reason))
        { _txtStatus.Text = reason; return; }
        try
        {
            Directory.CreateDirectory(BetaConfig.DataDir);
            File.WriteAllText(BetaConfig.LicensePath, token);
            SalvarPerfil();
            BetaClock.Touch(now);
        }
        catch (Exception ex) { _txtStatus.Text = "Chave OK, mas falha ao salvar: " + ex.Message; return; }
        DialogResult = true;
    }
}
