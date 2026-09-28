using System.Text;
using NetworkDevice.Android.Services;

namespace NetworkDevice.Android.Views;

public partial class TerminalPage : ContentPage
{
    private readonly DeviceConnectionManager _connManager = DeviceConnectionManager.Instance;

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
            ConsoleStatusLabel.Text = "Porta Serial: Aberta (9600 8N1)";
            ConsoleStatusLabel.TextColor = Color.FromArgb("#4ADE80");
        }
        else
        {
            ConsoleStatusLabel.Text = "Porta Serial: Desconectada (conecte na aba Provisionamento)";
            ConsoleStatusLabel.TextColor = Color.FromArgb("#EF4444");
        }
    }

    private void OnSerialDataReceived(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TerminalOutput.Text += text;
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
        // Envia código ASCII 0x03 (ETX / Ctrl+C)
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

    private async Task SendToConsoleAsync(string text)
    {
        if (_connManager.CurrentTransport == null || !_connManager.CurrentTransport.IsOpen)
        {
            await DisplayAlert("Aviso", "Console serial desconectado. Conecte o cabo USB na aba Provisionamento.", "OK");
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
