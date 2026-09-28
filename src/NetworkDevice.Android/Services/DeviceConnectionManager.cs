using Android.Hardware.Usb;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;
using NetworkDevice.Fortinet;
using UsbSerialForAndroid.Net.Helper;

namespace NetworkDevice.Android.Services;

public sealed class DeviceConnectionManager
{
    private static readonly Lazy<DeviceConnectionManager> _instance = new(() => new DeviceConnectionManager());
    public static DeviceConnectionManager Instance => _instance.Value;

    private readonly DeviceDetector _detector = new();
    private ITransport? _transport;
    private DeviceSession? _session;
    private CancellationTokenSource? _readCts;

    public ITransport? CurrentTransport => _transport;
    public DeviceSession? CurrentSession => _session;
    public bool IsConnected => _transport?.IsOpen == true;

    public DeviceDetectionResult? LastDetectionResult { get; private set; }
    public SaipCircuitData? LoadedCircuit { get; set; }

    public event Action<string>? OnTerminalDataReceived;
    public event Action<bool>? OnConnectionStateChanged;
    public event Action<DeviceDetectionResult>? OnDeviceIdentified;

    private DeviceConnectionManager() { }

    public IReadOnlyList<UsbDevice> ScanUsbDevices()
    {
        try
        {
            var devices = UsbManagerHelper.GetAllUsbDevices();
            return devices?.ToList() ?? (IReadOnlyList<UsbDevice>)Array.Empty<UsbDevice>();
        }
        catch
        {
            return Array.Empty<UsbDevice>();
        }
    }

    public async Task ConnectUsbAsync(UsbDevice device, int baudRate = 9600)
    {
        await DisconnectAsync();

        if (!UsbManagerHelper.HasPermission(device))
        {
            UsbManagerHelper.RequestPermission(device);
            throw new InvalidOperationException("Solicitação de permissão USB enviada. Confirme o popup na tela do celular e tente novamente.");
        }

        _transport = new AndroidUsbSerialTransport(device, baudRate);
        await _transport.OpenAsync();

        var options = new SessionOptions();
        _session = new DeviceSession(_transport, options);

        OnConnectionStateChanged?.Invoke(true);

        _readCts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_readCts.Token));
    }

    public void ConnectSimulated(ITransport customTransport)
    {
        _transport = customTransport;
        var options = new SessionOptions();
        _session = new DeviceSession(_transport, options);
        OnConnectionStateChanged?.Invoke(true);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[2048];
        while (!ct.IsCancellationRequested && _transport?.IsOpen == true)
        {
            try
            {
                var read = await _transport.ReadAsync(buffer, ct);
                if (read > 0)
                {
                    var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                    OnTerminalDataReceived?.Invoke(text);
                }
                else
                {
                    await Task.Delay(40, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnTerminalDataReceived?.Invoke($"\n[ERRO LEITURA SERIAL: {ex.Message}]\n");
                break;
            }
        }
    }

    public async Task<DeviceDetectionResult> IdentifyDeviceAsync(CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        var result = await _detector.DetectAsync(_transport, ct);
        LastDetectionResult = result;
        OnDeviceIdentified?.Invoke(result);
        return result;
    }

    public async Task ApplyProvisioningAsync(SaipCircuitData circuit, Func<string, Task> progressCallback, CancellationToken ct = default)
    {
        if (_session == null || _transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Conecte a porta serial USB do roteador antes de provisionar.");

        if (LastDetectionResult == null)
        {
            await progressCallback("[*] Identificando equipamento conectado na porta serial...");
            await IdentifyDeviceAsync(ct);
        }

        var detected = LastDetectionResult!;
        await progressCallback($"[*] Fabricante detectado: {detected.Manufacturer} | Série: {detected.Series}");

        if (detected.Manufacturer == DeviceManufacturer.Cisco)
        {
            var configurator = new CiscoSaipConfigurator(progressCallback);
            await configurator.ApplyConfigAsync(_session, circuit, cancellationToken: ct);
        }
        else if (detected.Manufacturer == DeviceManufacturer.Hpe)
        {
            var configurator = new HpeSaipConfigurator(progressCallback);
            await configurator.ApplyConfigAsync(_session, circuit, cancellationToken: ct);
        }
        else if (detected.Manufacturer == DeviceManufacturer.Fortinet)
        {
            var configurator = new FortiOsSaipConfigurator(progressCallback);
            await configurator.ApplyConfigAsync(_session, circuit, cancellationToken: ct);
        }
        else
        {
            // Default fallback para Cisco
            await progressCallback("[AVISO] Fabricante não identificado com certeza. Aplicando perfil padrão Cisco IOS...");
            var configurator = new CiscoSaipConfigurator(progressCallback);
            await configurator.ApplyConfigAsync(_session, circuit, cancellationToken: ct);
        }
    }

    public async Task DisconnectAsync()
    {
        _readCts?.Cancel();
        if (_session != null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        if (_transport != null)
        {
            await _transport.CloseAsync();
            await _transport.DisposeAsync();
            _transport = null;
        }

        OnConnectionStateChanged?.Invoke(false);
    }
}
