using System.Text;
using NetworkDevice.Android.Services;
using NetworkDevice.Protocols.Telnet;

namespace NetworkDevice.Android.Views;

public partial class TerminalPage : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;
    private int _currentBaudRate = 9600;

    public TerminalPage()
    {
        InitializeComponent();
        _connManager.OnTerminalDataReceived += OnSerialDataReceived;
        _connManager.OnConnectionStateChanged += OnConnectionStateChanged;
        UpdateStatus();
    }

    private void OnConnectionStateChanged(bool isConnected)
    {
        MainThread.BeginInvokeOnMainThread(UpdateStatus);
    }

    private void UpdateStatus()
    {
        if (_connManager.IsConnected)
        {
            if (_connManager.CurrentTransport is AndroidUsbSerialTransport usb)
            {
                var baud = usb.BaudRate;
                ConsoleStatusLabel.Text = $"COM USB OTG @ {baud} baud";
                LiveBadge.Text = "🟢 CONSOLE LIVE";
                LiveBadge.TextColor = Color.FromArgb("#10B981");
            }
            else if (_connManager.CurrentTransport is TcpTelnetTransport telnet)
            {
                ConsoleStatusLabel.Text = $"TELNET TCP: {telnet.Host}:{telnet.Port}";
                LiveBadge.Text = "🟢 TELNET LIVE";
                LiveBadge.TextColor = Color.FromArgb("#38BDF8");
            }
            else
            {
                ConsoleStatusLabel.Text = "Conexão Ativa";
                LiveBadge.Text = "🟢 CONECTADO";
                LiveBadge.TextColor = Color.FromArgb("#10B981");
            }
        }
        else
        {
            ConsoleStatusLabel.Text = "Porta Desconectada";
            LiveBadge.Text = "🔴 OFFLINE";
            LiveBadge.TextColor = Color.FromArgb("#EF4444");
        }
    }

    private void OnSerialDataReceived(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var cur = TerminalOutput.Text ?? "";
            if (cur.Length > 60000)
            {
                cur = cur.Substring(cur.Length - 40000);
            }
            TerminalOutput.Text = cur + text;
            TerminalScrollView.ScrollToAsync(TerminalOutput, ScrollToPosition.End, false);
        });
    }

    private async void OnSendCommandClicked(object? sender, EventArgs e)
    {
        var cmd = CommandInput.Text;
        if (string.IsNullOrEmpty(cmd)) return;

        CommandInput.Text = "";
        await SendToConsoleAsync(cmd + "\r\n");
    }

    private async void OnShortcutClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string cmd)
        {
            await SendToConsoleAsync(cmd + "\r\n");
        }
    }

    private async void OnEnterClicked(object? sender, EventArgs e)
    {
        await SendToConsoleAsync("\r\n");
    }

    private async void OnCtrlCClicked(object? sender, EventArgs e)
    {
        // ASCII 0x03 (ETX / Ctrl+C)
        if (_connManager.CurrentTransport == null || !_connManager.CurrentTransport.IsOpen)
        {
            await DisplayAlert("Aviso", "Console serial desconectado.", "OK");
            return;
        }

        try
        {
            await _connManager.CurrentTransport.WriteAsync(new byte[] { 0x03 });
            await Task.Delay(100);
            await _connManager.CurrentTransport.WriteAsync(new byte[] { 0x0D, 0x0A });
        }
        catch (Exception ex)
        {
            TerminalOutput.Text += $"\n[X] Erro ao enviar Ctrl+C: {ex.Message}\n";
        }
    }

    private async void OnCtrlBClicked(object? sender, EventArgs e)
    {
        // ASCII 0x02 (STX / Ctrl+B para BootWare HPE Comware)
        if (_connManager.CurrentTransport == null || !_connManager.CurrentTransport.IsOpen)
        {
            await DisplayAlert("Aviso", "Console serial desconectado.", "OK");
            return;
        }

        try
        {
            await _connManager.CurrentTransport.WriteAsync(new byte[] { 0x02 });
            await Task.Delay(50);
            TerminalOutput.Text += "\n[>>] Sinal Ctrl+B (BootWare Interruption) transmitido.\n";
        }
        catch (Exception ex)
        {
            TerminalOutput.Text += $"\n[X] Erro ao enviar Ctrl+B: {ex.Message}\n";
        }
    }

    private async void OnToggleBaudClicked(object? sender, EventArgs e)
    {
        // Alterna entre 9600 e 115200
        _currentBaudRate = _currentBaudRate == 9600 ? 115200 : 9600;
        BaudToggleBtn.Text = $"{_currentBaudRate} bps";

        if (_connManager.CurrentTransport is AndroidUsbSerialTransport usbTransport && usbTransport.IsOpen)
        {
            await usbTransport.ChangeBaudRateAsync(_currentBaudRate);
            TerminalOutput.Text += $"\n[i] Velocidade da porta serial reconfigurada para {_currentBaudRate} bps.\n";
        }
        UpdateStatus();
    }

    private async void OnCopyTerminalClicked(object? sender, EventArgs e)
    {
        var text = TerminalOutput.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        await Clipboard.Default.SetTextAsync(text);
        await DisplayAlert("Copiado", "O conteúdo do terminal foi copiado para a Área de Transferência.", "OK");
    }

    private async Task SendToConsoleAsync(string text)
    {
        if (_connManager.CurrentTransport == null || !_connManager.CurrentTransport.IsOpen)
        {
            await DisplayAlert("Aviso", "Console serial desconectado. Conecte o cabo USB na aba Ativação.", "OK");
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await _connManager.CurrentTransport.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            TerminalOutput.Text += $"\n[X] Erro ao transmitir: {ex.Message}\n";
        }
    }

    private void OnClearTerminalClicked(object? sender, EventArgs e)
    {
        TerminalOutput.Text = "";
    }
}
