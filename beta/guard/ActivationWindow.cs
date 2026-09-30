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
    private readonly TextBox _txtPhone;
    private readonly TextBox _txtEmail;
    private readonly TextBox _txtCluster;
    private readonly TextBox _txtUf;
    private readonly TextBox _txtReq;
    private readonly Button _btnCopy;
    private readonly TextBox _txtLicense;
    private readonly TextBlock _txtStatus;
    private readonly MachineIdentity _machine;
    private readonly string _userProfilePath;

    public ActivationWindow(string reason, MachineIdentity machine, DateTime betaExpires)
    {
        _machine = machine;
        _userProfilePath = Path.Combine(BetaConfig.DataDir, "user_profile.json");

        Title = "SPARC " + BetaConfig.Tag + " - Ativacao e Identificacao do Tecnico";
        Width = 660;
        Height = 670;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1A));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(18) };
        scroll.Content = root;
        Content = scroll;

        root.Children.Add(new TextBlock { Text = "Versao " + BetaConfig.Tag + " - Controle de Copias em Campo", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = "Build valido ate " + betaExpires.ToString("dd/MM/yyyy") + ". Identificacao obrigatoria do tecnico na primeira execucao.", Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap });
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
        spTec.Children.Add(new TextBlock { Text = "1) Identificacao Obrigatoria do Tecnico (Registrado no Admin):", Foreground = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });

        var gridCampos = new Grid();
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        gridCampos.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gridCampos.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Linha 1: Nome e Sobrenome
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

        // Linha 2: Telefone/WhatsApp e Email
        var pnlTel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        pnlTel.Children.Add(new TextBlock { Text = "Telefone / WhatsApp (com DDD):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtPhone = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlTel.Children.Add(_txtPhone);
        Grid.SetRow(pnlTel, 1); Grid.SetColumn(pnlTel, 0);
        gridCampos.Children.Add(pnlTel);

        var pnlEmail = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        pnlEmail.Children.Add(new TextBlock { Text = "E-mail:", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtEmail = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlEmail.Children.Add(_txtEmail);
        Grid.SetRow(pnlEmail, 1); Grid.SetColumn(pnlEmail, 2);
        gridCampos.Children.Add(pnlEmail);

        // Linha 3: Cluster de Atuacao e UF
        var pnlCluster = new StackPanel();
        pnlCluster.Children.Add(new TextBlock { Text = "Cluster de Atuacao (ex: SP Capital, Interior, Sul...):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtCluster = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2) };
        pnlCluster.Children.Add(_txtCluster);
        Grid.SetRow(pnlCluster, 2); Grid.SetColumn(pnlCluster, 0);
        gridCampos.Children.Add(pnlCluster);

        var pnlUf = new StackPanel();
        pnlUf.Children.Add(new TextBlock { Text = "UF (ex: SP):", Foreground = Brushes.LightGray, FontSize = 11 });
        _txtUf = new TextBox { Height = 26, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(4, 2, 4, 2), MaxLength = 2, CharacterCasing = CharacterCasing.Upper };
        pnlUf.Children.Add(_txtUf);
        Grid.SetRow(pnlUf, 2); Grid.SetColumn(pnlUf, 2);
        gridCampos.Children.Add(pnlUf);

        spTec.Children.Add(gridCampos);
        groupTecnico.Child = spTec;
        root.Children.Add(groupTecnico);

        // SECAO 2: CODIGO DE ATIVACAO
        root.Children.Add(new TextBlock { Text = "2) Codigo de ativacao desta maquina (copie e envie ao responsavel):", Foreground = Brushes.LightGray, FontWeight = FontWeights.SemiBold });
        _txtReq = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 60, Margin = new Thickness(0, 4, 0, 4), FontFamily = new FontFamily("Consolas"), FontSize = 10, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(_txtReq);

        _txtStatus = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6), FontSize = 11.5 };
        
        var pnlCopy = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        _btnCopy = new Button { Content = "📋 Copiar Solicitacao de Ativacao", Width = 230, Height = 28, Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold };
        _btnCopy.Click += (_, _) => CopiarSolicitacao();
        pnlCopy.Children.Add(_btnCopy);

        var btnCheckOnline = new Button { Content = "🔄 Verificar Liberacao Online", Width = 200, Height = 28, Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x94, 0x88)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold };
        btnCheckOnline.Click += async (_, _) => await VerificarLiberacaoOnlineAsync();
        pnlCopy.Children.Add(btnCheckOnline);

        root.Children.Add(pnlCopy);
        root.Children.Add(_txtStatus);

        // SECAO 3: CHAVE DE ATIVACAO
        root.Children.Add(new TextBlock { Text = "3) Cole aqui a chave recebida do Administrador:", Foreground = Brushes.LightGray, FontWeight = FontWeights.SemiBold });
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
        _txtPhone.TextChanged += (_, _) => AtualizarRequisicao();
        _txtEmail.TextChanged += (_, _) => AtualizarRequisicao();
        _txtCluster.TextChanged += (_, _) => AtualizarRequisicao();
        _txtUf.TextChanged += (_, _) => AtualizarRequisicao();

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
                fn = _txtFirstName.Text.Trim(),
                ln = _txtLastName.Text.Trim(),
                ph = _txtPhone.Text.Trim(),
                em = _txtEmail.Text.Trim(),
                cl = _txtCluster.Text.Trim(),
                uf = _txtUf.Text.Trim().ToUpperInvariant()
            };
            File.WriteAllText(_userProfilePath, JsonSerializer.Serialize(obj));
        }
        catch { }
    }

    private void AtualizarRequisicao()
    {
        var fn = _txtFirstName.Text.Trim();
        var ln = _txtLastName.Text.Trim();
        var ph = _txtPhone.Text.Trim();
        var em = _txtEmail.Text.Trim();
        var cl = _txtCluster.Text.Trim();
        var uf = _txtUf.Text.Trim().ToUpperInvariant();

        var faltando = string.IsNullOrWhiteSpace(fn) || string.IsNullOrWhiteSpace(ln) ||
                        string.IsNullOrWhiteSpace(ph) || string.IsNullOrWhiteSpace(em) ||
                        string.IsNullOrWhiteSpace(cl) || string.IsNullOrWhiteSpace(uf);

        if (faltando)
        {
            _txtReq.Text = "(Preencha Nome, Sobrenome, Telefone, E-mail, Cluster e UF acima para gerar o codigo de ativacao)";
            _txtReq.Foreground = Brushes.Gray;
            _btnCopy.IsEnabled = false;
            _txtStatus.Text = "Preencha todos os campos cadastrais acima para liberar a geracao do codigo.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        }
        else
        {
            SalvarPerfil();
            var req = MachineId.BuildRequest(_machine, fn, ln, ph, em, cl, uf, BetaConfig.Tag);
            _txtReq.Text = req;
            _txtReq.Foreground = Brushes.White;
            _btnCopy.IsEnabled = true;
            _txtStatus.Text = "Cadastro preenchido! Clique em 'Copiar Solicitacao de Ativacao' e envie ao gestor.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
        }
    }

    private void CopiarSolicitacao()
    {
        try
        {
            var fn = _txtFirstName.Text.Trim();
            var ln = _txtLastName.Text.Trim();
            var ph = _txtPhone.Text.Trim();
            var em = _txtEmail.Text.Trim();
            var cl = _txtCluster.Text.Trim();
            var uf = _txtUf.Text.Trim().ToUpperInvariant();
            var req = _txtReq.Text.Trim();

            var msg = $"*Solicitacao de Ativacao SPARC*\n" +
                      $"• *Tecnico:* {fn} {ln}\n" +
                      $"• *Telefone:* {ph}\n" +
                      $"• *E-mail:* {em}\n" +
                      $"• *Cluster:* {cl}\n" +
                      $"• *UF:* {uf}\n" +
                      $"• *Codigo:*\n`{req}`";

            Clipboard.SetText(msg);
            _txtStatus.Text = "Solicitacao completa copiada para a area de transferencia! Envie ao gestor no WhatsApp.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
        }
        catch { }
    }

    private async System.Threading.Tasks.Task VerificarLiberacaoOnlineAsync()
    {
        _txtStatus.Text = "Consultando base de licencas no GitHub...";
        _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("User-Agent", "SPARC-Beta");
            var url = "https://raw.githubusercontent.com/Calcmann/repo/main/devices.json";
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                _txtStatus.Text = "Nenhuma liberacao online encontrada ainda. Cole a chave recebida manualmente.";
                _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                return;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var g = item.TryGetProperty("MachineGuid", out var gp) ? gp.GetString() : null;
                var f = item.TryGetProperty("MachineFingerprint", out var fp) ? fp.GetString() : null;
                if (string.Equals(g, _machine.MachineGuid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f, _machine.Fingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    var status = item.TryGetProperty("Status", out var sp) ? sp.GetString() : null;
                    if (string.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase))
                    {
                        _txtStatus.Text = "Licenca revogada pelo Administrador.";
                        _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
                        return;
                    }

                    var token = item.TryGetProperty("AuthorizedToken", out var tp) ? tp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        _txtLicense.Text = token;
                        TentarAtivar();
                        return;
                    }
                }
            }

            _txtStatus.Text = "Dispositivo ainda nao aprovado no painel online do Administrador.";
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24));
        }
        catch (Exception ex)
        {
            _txtStatus.Text = "Falha ao verificar online: " + ex.Message;
            _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71));
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
