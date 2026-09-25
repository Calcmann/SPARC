using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkDevice.UI.Beta;

// Janela code-only (sem XAML) para nao alterar nenhum XAML da base.
// Texto 100% ASCII de proposito: evita mojibake em qualquer codepage.
internal sealed class ActivationWindow : Window
{
    private readonly TextBox _txtLicense;
    private readonly TextBlock _txtStatus;

    public ActivationWindow(string reason, MachineIdentity machine, DateTime betaExpires)
    {
        Title = "SPARC " + BetaConfig.Tag + " - Ativacao necessaria";
        Width = 620;
        Height = 490;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1A));

        var root = new StackPanel { Margin = new Thickness(18) };
        Content = root;

        root.Children.Add(new TextBlock { Text = "Versao " + BetaConfig.Tag + " - uso controlado por chave (bloqueio total sem ativacao)", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new TextBlock { Text = "Build valido ate " + betaExpires.ToString("dd/MM/yyyy") + ". Cada chave dura 30 dias nesta maquina.", Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 8), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(reason))
            root.Children.Add(new TextBlock { Text = reason, Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });

        root.Children.Add(new TextBlock { Text = "1) Codigo de ativacao desta maquina (copie e envie ao responsavel):", Foreground = Brushes.LightGray, FontWeight = FontWeights.SemiBold });
        var txtReq = new TextBox { Text = MachineId.BuildRequest(machine), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = 74, Margin = new Thickness(0, 4, 0, 4), FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(txtReq);

        _txtStatus = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var btnCopy = new Button { Content = "Copiar codigo de ativacao", Width = 220, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), FontWeight = FontWeights.SemiBold };
        btnCopy.Click += (_, _) => { try { Clipboard.SetText(txtReq.Text); _txtStatus.Text = "Codigo copiado para a area de transferencia! Envie-o ao responsavel."; _txtStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)); } catch { } };
        root.Children.Add(btnCopy);
        root.Children.Add(_txtStatus);

        root.Children.Add(new TextBlock { Text = "2) Cole aqui a chave recebida:", Foreground = Brushes.LightGray, FontWeight = FontWeights.SemiBold });
        _txtLicense = new TextBox { TextWrapping = TextWrapping.Wrap, Height = 74, Margin = new Thickness(0, 4, 0, 10), FontFamily = new FontFamily("Consolas"), FontSize = 11, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(_txtLicense);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnSair = new Button { Content = "Sair", Width = 100, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
        btnSair.Click += (_, _) => { DialogResult = false; };
        var btnAtivar = new Button { Content = "Ativar SPARC", Width = 140, Height = 30, FontWeight = FontWeights.Bold };
        btnAtivar.Click += (_, _) => TentarAtivar();
        bar.Children.Add(btnSair);
        bar.Children.Add(btnAtivar);
        root.Children.Add(bar);
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
            BetaClock.Touch(now);
        }
        catch (Exception ex) { _txtStatus.Text = "Chave OK, mas falha ao salvar: " + ex.Message; return; }
        DialogResult = true;
    }
}
