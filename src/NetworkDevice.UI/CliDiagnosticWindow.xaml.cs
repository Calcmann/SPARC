using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace NetworkDevice.UI;

public partial class CliDiagnosticWindow : Window
{
    private readonly StringBuilder _buffer = new();
    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private const int MaxBufferChars = 200_000;

    public Func<string, Task>? OnSendCommand { get; set; }
    public Func<byte[], Task>? OnSendBytes { get; set; }

    public CliDiagnosticWindow()
    {
        InitializeComponent();
    }

    public void SetPortInfo(string porta, int baud)
    {
        Dispatcher.Invoke(() =>
        {
            TxtPortaInfo.Text = $"Porta: {porta} @ {baud} baud (8-N-1)";
        });
    }

    public void AppendOutput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Dispatcher.InvokeAsync(() =>
        {
            _buffer.Append(text);
            if (_buffer.Length > MaxBufferChars)
            {
                _buffer.Remove(0, _buffer.Length - MaxBufferChars);
            }

            TxtLiveTerminal.Text = _buffer.ToString();

            if (ChkAutoScroll.IsChecked == true)
            {
                TxtLiveTerminal.CaretIndex = TxtLiveTerminal.Text.Length;
                TxtLiveTerminal.ScrollToEnd();
            }
        });
    }

    private async void BtnEnviarManual_Click(object sender, RoutedEventArgs e)
    {
        await EnviarComandoAtualAsync();
    }

    private async void TxtComandoManual_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await EnviarComandoAtualAsync();
        }
        else if (e.Key == Key.Up)
        {
            if (_history.Count > 0 && _historyIndex > 0)
            {
                _historyIndex--;
                TxtComandoManual.Text = _history[_historyIndex];
                TxtComandoManual.CaretIndex = TxtComandoManual.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_history.Count > 0 && _historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                TxtComandoManual.Text = _history[_historyIndex];
                TxtComandoManual.CaretIndex = TxtComandoManual.Text.Length;
            }
            else
            {
                _historyIndex = _history.Count;
                TxtComandoManual.Text = string.Empty;
            }
            e.Handled = true;
        }
    }

    private async Task EnviarComandoAtualAsync()
    {
        var cmd = TxtComandoManual.Text;
        TxtComandoManual.Text = string.Empty;

        if (!string.IsNullOrWhiteSpace(cmd))
        {
            _history.Add(cmd);
            _historyIndex = _history.Count;
        }

        if (OnSendCommand != null)
        {
            try
            {
                await OnSendCommand(cmd);
            }
            catch (Exception ex)
            {
                AppendOutput($"\r\n[ERRO TX] {ex.Message}\r\n");
            }
        }
    }

    private async void BtnCtrlB_Click(object sender, RoutedEventArgs e)
    {
        if (OnSendBytes != null)
        {
            try { await OnSendBytes(new byte[] { 0x02 }); }
            catch (Exception ex) { AppendOutput($"\r\n[ERRO TX Ctrl+B] {ex.Message}\r\n"); }
        }
        else if (OnSendCommand != null)
        {
            try { await OnSendCommand("\x02"); } catch { }
        }
    }

    private async void BtnCtrlC_Click(object sender, RoutedEventArgs e)
    {
        if (OnSendBytes != null)
        {
            try { await OnSendBytes(new byte[] { 0x03 }); }
            catch (Exception ex) { AppendOutput($"\r\n[ERRO TX Ctrl+C] {ex.Message}\r\n"); }
        }
        else if (OnSendCommand != null)
        {
            try { await OnSendCommand("\x03"); } catch { }
        }
    }

    private async void BtnEnter_Click(object sender, RoutedEventArgs e)
    {
        if (OnSendCommand != null)
        {
            try { await OnSendCommand(string.Empty); }
            catch (Exception ex) { AppendOutput($"\r\n[ERRO TX Enter] {ex.Message}\r\n"); }
        }
    }

    private async void BtnSendOpt1_Click(object sender, RoutedEventArgs e) => await SendOptionAsync("1");
    private async void BtnSendOpt3_Click(object sender, RoutedEventArgs e) => await SendOptionAsync("3");
    private async void BtnSendOpt5_Click(object sender, RoutedEventArgs e) => await SendOptionAsync("5");
    private async void BtnSendOpt2_Click(object sender, RoutedEventArgs e) => await SendOptionAsync("2");
    private async void BtnSendOpt0_Click(object sender, RoutedEventArgs e) => await SendOptionAsync("0");

    private async Task SendOptionAsync(string opt)
    {
        if (OnSendCommand != null)
        {
            try { await OnSendCommand(opt); }
            catch (Exception ex) { AppendOutput($"\r\n[ERRO TX {opt}] {ex.Message}\r\n"); }
        }
    }

    private void BtnCopiar_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(TxtLiveTerminal.Text);
            MessageBox.Show("Logs do terminal copiados para a área de transferência.", "SPARC CLI", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha ao copiar: {ex.Message}", "SPARC CLI", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnLimpar_Click(object sender, RoutedEventArgs e)
    {
        _buffer.Clear();
        TxtLiveTerminal.Text = string.Empty;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Oculta ao invés de destruir para poder reabrir mantendo o buffer
        e.Cancel = true;
        Hide();
    }
}
