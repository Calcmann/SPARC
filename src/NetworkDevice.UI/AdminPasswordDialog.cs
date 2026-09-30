using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NetworkDevice.UI;

/// <summary>
/// Diálogo modal de autenticação para desbloqueio do Painel de Administração do Gestor SPARC.
/// Acionado via sequência de teclas restrita (Ctrl + Shift + F12).
/// </summary>
public sealed class AdminPasswordDialog : Window
{
    private const string ExpectedPassword = "CR@PS";
    private readonly PasswordBox _txtPassword;
    private readonly TextBlock _txtErro;

    public bool IsAuthenticated { get; private set; }

    public AdminPasswordDialog(Window? owner = null)
    {
        Owner = owner;
        Title = "Acesso Restrito — Gestor SPARC";
        Width = 400;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B));
        WindowStyle = WindowStyle.ToolWindow;

        var root = new StackPanel { Margin = new Thickness(20) };
        Content = root;

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(new TextBlock { Text = "🔒", FontSize = 16, Margin = new Thickness(0, 0, 8, 0) });
        header.Children.Add(new TextBlock
        {
            Text = "Autenticação de Administrador",
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(header);

        root.Children.Add(new TextBlock
        {
            Text = "Informe a chave de segurança para desbloquear o painel:",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        _txtPassword = new PasswordBox
        {
            Height = 34,
            FontSize = 14,
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x52, 0x52, 0x5B)),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 4)
        };
        _txtPassword.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) TentarConfirmar();
            if (e.Key == Key.Escape) { DialogResult = false; }
        };
        root.Children.Add(_txtPassword);

        _txtErro = new TextBlock
        {
            Text = "",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(_txtErro);

        var pnlButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnCancelar = new Button
        {
            Content = "Cancelar",
            Width = 90,
            Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        btnCancelar.Click += (_, _) => { DialogResult = false; };

        var btnEntrar = new Button
        {
            Content = "Desbloquear",
            Width = 110,
            Height = 30,
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand
        };
        btnEntrar.Click += (_, _) => TentarConfirmar();

        pnlButtons.Children.Add(btnCancelar);
        pnlButtons.Children.Add(btnEntrar);
        root.Children.Add(pnlButtons);

        Loaded += (_, _) => _txtPassword.Focus();
    }

    private void TentarConfirmar()
    {
        if (_txtPassword.Password == ExpectedPassword)
        {
            IsAuthenticated = true;
            DialogResult = true;
        }
        else
        {
            _txtErro.Text = "Senha incorreta. Acesso não autorizado.";
            _txtPassword.Clear();
            _txtPassword.Focus();
        }
    }
}
