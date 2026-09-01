using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkDevice.UI;

/// <summary>
/// Popup de desenvolvimento que exibe a saída CLI serial bruta em tempo real.
/// Permite identificar inconsistências entre comandos enviados e respostas do equipamento.
/// </summary>
public class CliDebugWindow : Window
{
    private readonly TextBox _textBox;
    private const int MaxChars = 50_000;

    public CliDebugWindow()
    {
        Title = "🔍 CLI Debug — Saída Serial Bruta (Desenvolvimento)";
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 50;
        Top = 50;
        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0A0E14"));
        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
        Icon = null;
        Topmost = false;

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _textBox = new TextBox
        {
            IsReadOnly = true,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8D6E5")),
            CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(_textBox, 0);
        grid.Children.Add(_textBox);

        // Barra inferior com botões
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(6)
        };

        var btnCopy = new Button
        {
            Content = "📋 Copiar Tudo",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(4),
            FontSize = 12,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnCopy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_textBox.Text); } catch { }
        };

        var btnClear = new Button
        {
            Content = "🗑 Limpar",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(4),
            FontSize = 12,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155")),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnClear.Click += (_, _) => _textBox.Clear();

        toolbar.Children.Add(btnCopy);
        toolbar.Children.Add(btnClear);
        Grid.SetRow(toolbar, 1);
        grid.Children.Add(toolbar);

        Content = grid;
    }

    /// <summary>
    /// Adiciona texto bruto ao terminal de debug.
    /// Seguro para chamar de qualquer thread (faz dispatch automático).
    /// </summary>
    public void AppendRaw(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendRaw(text));
            return;
        }

        _textBox.AppendText(text);

        // Limita buffer para evitar consumo excessivo de memória
        if (_textBox.Text.Length > MaxChars)
        {
            _textBox.Text = _textBox.Text[^(MaxChars - 5000)..];
        }

        _textBox.ScrollToEnd();
    }

    /// <summary>
    /// Adiciona uma linha formatada como separador/marcador de evento.
    /// </summary>
    public void AppendMarker(string label)
    {
        AppendRaw($"\n═══════ {label} ═══════\n");
    }
}
