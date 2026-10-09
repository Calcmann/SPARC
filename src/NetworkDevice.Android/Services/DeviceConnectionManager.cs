using Android.Hardware.Usb;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;
using NetworkDevice.Fortinet;
using NetworkDevice.Protocols.Ftp;
using NetworkDevice.Protocols.Http;
using NetworkDevice.Protocols.Hpe;
using NetworkDevice.Protocols.Telnet;
using System.Net.NetworkInformation;
using UsbSerialForAndroid.Net;
using UsbSerialForAndroid.Net.Drivers;
using UsbSerialForAndroid.Net.Helper;

namespace NetworkDevice.Android.Services;

public sealed class DeviceConnectionManager
{
    private static readonly Lazy<DeviceConnectionManager> _instance = new(() => new DeviceConnectionManager());
    public static DeviceConnectionManager Instance => _instance.Value;

    private readonly DeviceDetector _detector = new();
    private ITransport? _transport;
    private DeviceSession? _session;
    private ITransport? _telnetTransport;
    private DeviceSession? _telnetSession;
    private CancellationTokenSource? _readCts;
    private Task? _readLoopTask;
    private readonly SemaphoreSlim _loopLock = new(1, 1);

    public ITransport? CurrentTransport => _transport;
    public DeviceSession? CurrentSession => _session;
    public bool IsConnected => _transport?.IsOpen == true;

    public ITransport? TelnetTransport => _telnetTransport;
    public DeviceSession? TelnetSession => _telnetSession;
    public bool IsTelnetConnected => _telnetSession?.IsConnected == true;

    public DeviceDetectionResult? LastDetectionResult { get; private set; }
    public DeviceSeries ConnectedDeviceSeries => LastDetectionResult?.Series ?? DeviceSeries.Unknown;
    public string? LastDetectedSerial { get; set; }
    public SaipCircuitData? LoadedCircuit { get; set; }

    /// <summary>
    /// Credenciais resolvidas com sucesso durante autenticação automática ou manual (paridade Windows).
    /// </summary>
    public string? LastResolvedUser { get; set; }
    public string? LastResolvedPassword { get; set; }
    public string? LastResolvedEnableSecret { get; set; }

    /// <summary>
    /// Paridade com o checkbox secreto "NAT LAB" do Windows (default desligado).
    /// </summary>
    public bool IncluirNatLab { get; set; }

    /// <summary>
    /// Imagem de firmware indicada pelo operador para boot no provisionamento.
    /// </summary>
    public string? CustomBootImage { get; set; }

    /// <summary>
    /// Running-config capturada após o último provisionamento (paridade com o relatório TXT do Windows).
    /// </summary>
    public string? LastAppliedConfig { get; private set; }

    public event Action<string>? OnTerminalDataReceived;
    public event Action<bool>? OnConnectionStateChanged;
    public event Action<DeviceDetectionResult>? OnDeviceIdentified;
    public event Action<string>? OnProbeProgress;

    private DeviceConnectionManager() { }

    public record FirmwareProgressState(
        double Percentage,
        string Stage,
        string Details,
        long BytesTransferred = 0,
        long TotalBytes = 0,
        bool IsCompleted = false,
        bool HasError = false,
        string? ErrorMessage = null);

    public event Action<FirmwareProgressState>? OnFirmwareProgress;

    public void ReportFirmwareProgress(
        double percentage,
        string stage,
        string details,
        long bytes = 0,
        long total = 0,
        bool completed = false,
        bool hasError = false,
        string? err = null)
    {
        try
        {
            OnFirmwareProgress?.Invoke(new FirmwareProgressState(
                percentage, stage, details, bytes, total, completed, hasError, err));
        }
        catch { }
    }

    /// <summary>
    /// Identifica se o dispositivo USB conectado é uma placa de rede Ethernet (ex: Realtek, ASIX, TP-Link) ou Hub USB,
    /// para não confundi-lo com o console serial quando conectados simultaneamente via HUB USB-C.
    /// </summary>
    public static bool IsNetworkOrHubDevice(UsbDevice d)
    {
        if (d == null) return false;

        // 1. USB Hub (Classe 0x09)
        if ((int)d.DeviceClass == 9) return true;

        // 2. VIDs conhecidos de adaptadores Ethernet / Rede USB
        // 0x0BDA: Realtek USB Ethernet (RTL8150..8156, Hubs RTL) - Realtek NÃO fabrica chips seriais
        // 0x0B95: ASIX USB Ethernet (AX88172, AX88178, AX88179, AX88179A, AX88772)
        // 0x2357: TP-Link USB Ethernet/Wi-Fi (UE300, UE200, UE300C, etc.)
        // 0x0424: Microchip / SMSC LAN (LAN7500, LAN9500, LAN7800)
        // 0x0FE6, 0x0A46: Davicom DM9601 / DM9620
        // 0x0A47: Corechip SR9700 / SR9900
        // 0x05AC: Apple USB Ethernet (0x1402, 0x1405)
        // 0x050D: Belkin USB Ethernet
        // 0x2001, 0x07D1: D-Link Ethernet
        // 0x0DF6: Sitecom
        // 0x1737, 0x13B1, 0x077B: Linksys Ethernet
        // 0x0846: Netgear Ethernet
        // 0x045E: Microsoft Surface Ethernet (0x07C6, 0x07AB, 0x0927)
        // 0x18DA: Fresco Logic Ethernet
        // 0x05E3, 0x2109, 0x1A40, 0x0409, 0x0451: USB Hub controllers (Genesys, VIA Labs, Terminus, NEC, TI)
        if (d.VendorId == 0x0BDA ||
            d.VendorId == 0x0B95 ||
            d.VendorId == 0x2357 ||
            d.VendorId == 0x0424 ||
            d.VendorId == 0x0FE6 || d.VendorId == 0x0A46 ||
            d.VendorId == 0x0A47 ||
            (d.VendorId == 0x05AC && (d.ProductId == 0x1402 || d.ProductId == 0x1405)) ||
            d.VendorId == 0x050D ||
            d.VendorId == 0x2001 || d.VendorId == 0x07D1 ||
            d.VendorId == 0x0DF6 ||
            d.VendorId == 0x1737 || d.VendorId == 0x13B1 || d.VendorId == 0x077B ||
            d.VendorId == 0x0846 ||
            (d.VendorId == 0x045E && (d.ProductId == 0x07C6 || d.ProductId == 0x07AB || d.ProductId == 0x0927)) ||
            d.VendorId == 0x18DA ||
            d.VendorId == 0x05E3 || d.VendorId == 0x2109 || d.VendorId == 0x1A40 ||
            d.VendorId == 0x0409 || d.VendorId == 0x0451)
        {
            return true;
        }

        // 3. Descrição ou nomes de produto / fabricante indicando Ethernet / Rede / Hub
        var m = (d.ManufacturerName ?? "").ToLowerInvariant();
        var p = (d.ProductName ?? "").ToLowerInvariant();
        if (m.Contains("tp-link") || m.Contains("realtek") || m.Contains("asix") ||
            m.Contains("davicom") || m.Contains("corechip") || m.Contains("terminus") ||
            m.Contains("genesys") || m.Contains("via labs") ||
            p.Contains("ethernet") || p.Contains("gigabit") || p.Contains("lan") ||
            p.Contains("ue300") || p.Contains("ue200") || p.Contains("rtl815") ||
            p.Contains("ax8817") || p.Contains("ax8877") || p.Contains("network") ||
            p.Contains("nic") || p.Contains("fast ethernet") || p.Contains("rj45") ||
            p.Contains("10/100") || p.Contains("rndis") || p.Contains("dm96") ||
            p.Contains("sr9700") || p.Contains("sr9900") || p.Contains("usb hub") ||
            p.Contains("hub") || p.Contains("dock") || p.Contains("multiport"))
        {
            return true;
        }

        // 4. Subclasses CDC Ethernet (Subclasse 0x06 = Ethernet, 0x0D = NCM, 0x0F = MBIM), RNDIS (224/1) ou Hub (9) ou Mass Storage (8)
        for (int i = 0; i < d.InterfaceCount; i++)
        {
            var iface = d.GetInterface(i);
            if (iface != null)
            {
                var cls = (int)iface.InterfaceClass;
                var sub = (int)iface.InterfaceSubclass;
                if (cls == 2 && (sub == 6 || sub == 13 || sub == 14 || sub == 15))
                    return true;
                if (cls == 224 && sub == 1) // RNDIS / Ethernet sem fio
                    return true;
                if (cls == 9) // Hub
                    return true;
                if (cls == 8) // Mass Storage (Pen drive / leitor de cartão)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Identifica se o dispositivo é um conversor Serial / UART / Console suportado.
    /// </summary>
    public static bool IsSupportedSerialDevice(UsbDevice d)
    {
        if (d == null || IsNetworkOrHubDevice(d)) return false;

        // 1. Testa se o UsbDriverFactory possui driver registrado para o dispositivo
        try
        {
            var testDriver = UsbDriverFactory.CreateUsbDriver(d.DeviceId);
            if (testDriver != null) return true;
        }
        catch { }

        // 2. VIDs conhecidos de conversores Serial / UART / Console
        // 0x0403: FTDI (FT232R, FT2232, FT4232, FT230X, etc.)
        // 0x10C4: Silicon Labs (CP2101, CP2102, CP2104, CP2105, etc.)
        // 0x1A86, 0x4348: QinHeng / Winchiphead (CH340/CH341/CH9102)
        // 0x067B: Prolific (PL2303)
        // 0x0557 (PID 0x2008): ATEN / Cisco USB Console
        // 0x05A6, 0x145F: Cisco Systems Console
        // 0x0483: STMicroelectronics Virtual COM
        // 0x2341, 0x2A03: Arduino CDC
        // 0x2E8A: Raspberry Pi Pico (RP2040 UART/CDC)
        // 0x1366: SEGGER J-Link CDC Serial
        // 0x03EB: Atmel CDC
        // 0x16C0: Teensy CDC Serial
        // 0x2047: TI MSP430 CDC
        // 0x1FC9: NXP CDC
        // 0x0D28: ARM mbed CDC
        if (d.VendorId == 0x0403 ||
            d.VendorId == 0x10C4 ||
            d.VendorId == 0x1A86 || d.VendorId == 0x4348 ||
            d.VendorId == 0x067B ||
            (d.VendorId == 0x0557 && d.ProductId == 0x2008) ||
            d.VendorId == 0x05A6 || d.VendorId == 0x145F ||
            d.VendorId == 0x0483 ||
            d.VendorId == 0x2341 || d.VendorId == 0x2A03 ||
            d.VendorId == 0x2E8A ||
            d.VendorId == 0x1366 ||
            d.VendorId == 0x03EB ||
            d.VendorId == 0x16C0 ||
            d.VendorId == 0x2047 ||
            d.VendorId == 0x1FC9 ||
            d.VendorId == 0x0D28)
        {
            return true;
        }

        // 3. Subclasses CDC ACM (Abstract Control Model = Modem/Serial COM)
        for (int i = 0; i < d.InterfaceCount; i++)
        {
            var iface = d.GetInterface(i);
            if (iface != null && (int)iface.InterfaceClass == 2 && (int)iface.InterfaceSubclass == 2)
            {
                return true;
            }
        }

        // 4. Nomes contendo referências explícitas a Serial / Console / UART / RS232
        var m = (d.ManufacturerName ?? "").ToLowerInvariant();
        var p = (d.ProductName ?? "").ToLowerInvariant();
        if (p.Contains("serial") || p.Contains("uart") || p.Contains("rs232") || p.Contains("rs-232") ||
            p.Contains("console") || p.Contains("cp210") || p.Contains("ch340") || p.Contains("ch341") ||
            p.Contains("ch9102") || p.Contains("pl2303") || p.Contains("ft232") || p.Contains("ftdi") ||
            p.Contains("cdc acm") || p.Contains("usb-serial") || p.Contains("usb to serial") ||
            m.Contains("ftdi") || m.Contains("prolific") || m.Contains("silicon labs") || m.Contains("qinheng"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Escaneia dispositivos USB na porta OTG / HUB USB-C.
    /// GARANTIA ESTRITA: O primeiro item da lista é SEMPRE preferencialmente o adaptador serial,
    /// evitando que placas de rede Ethernet assumam o índice 0 e impeçam o acesso CLI inicial.
    /// </summary>
    public IReadOnlyList<UsbDevice> ScanUsbDevices()
    {
        try
        {
            var devices = UsbManagerHelper.GetAllUsbDevices()?.ToList() ?? new List<UsbDevice>();
            if (devices.Count == 0) return Array.Empty<UsbDevice>();

            // 1. Separa estritamente quem é serial confirmado de quem é outro dispositivo e rede
            var serialDevices = devices.Where(IsSupportedSerialDevice).ToList();
            var nonNetworkDevices = devices.Where(d => !IsNetworkOrHubDevice(d) && !IsSupportedSerialDevice(d)).ToList();
            var networkOrHubDevices = devices.Where(IsNetworkOrHubDevice).ToList();

            var result = new List<UsbDevice>();

            // Seriais SEMPRE no topo absoluto da lista
            result.AddRange(serialDevices);

            // Dispositivos desconhecidos que não são rede em segundo lugar
            result.AddRange(nonNetworkDevices);

            // Placas de rede e hubs vão apenas ao final caso não haja nenhum outro dispositivo
            if (result.Count == 0)
            {
                result.AddRange(networkOrHubDevices);
            }

            return result;
        }
        catch
        {
            return Array.Empty<UsbDevice>();
        }
    }

    /// <summary>
    /// Verifica se há algum adaptador serial USB homologado conectado ao barramento OTG (inclusive via HUB USB-C).
    /// </summary>
    public bool HasSupportedSerialConnected()
    {
        try
        {
            var devices = UsbManagerHelper.GetAllUsbDevices();
            return devices != null && devices.Any(IsSupportedSerialDevice);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifica se há algum adaptador Ethernet USB ou HUB USB conectado ao smartphone.
    /// </summary>
    public bool HasEthernetOrHubDeviceConnected()
    {
        try
        {
            var devices = UsbManagerHelper.GetAllUsbDevices()?.ToList() ?? new List<UsbDevice>();
            if (devices.Count == 0) return false;
            return devices.Any(IsNetworkOrHubDevice) || devices.Count > 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolve o dispositivo USB para conexão serial.
    /// GARANTE que adaptadores Ethernet NUNCA sejam selecionados se houver adaptador serial disponível.
    /// </summary>
    public UsbDevice ResolveLiveDevice(UsbDevice? preferredDevice = null)
    {
        var devices = ScanUsbDevices();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("Nenhum adaptador serial USB encontrado na porta OTG. Verifique a conexão do cabo.");
        }

        // Se o usuário selecionou um dispositivo que NÃO seja rede, tenta casá-lo
        if (preferredDevice != null && !IsNetworkOrHubDevice(preferredDevice))
        {
            var exactMatch = devices.FirstOrDefault(d => d.DeviceId == preferredDevice.DeviceId);
            if (exactMatch != null && !IsNetworkOrHubDevice(exactMatch))
                return exactMatch;

            var vidPidMatch = devices.FirstOrDefault(d =>
                d.VendorId == preferredDevice.VendorId && d.ProductId == preferredDevice.ProductId);
            if (vidPidMatch != null && !IsNetworkOrHubDevice(vidPidMatch))
                return vidPidMatch;

            var nameMatch = devices.FirstOrDefault(d => d.DeviceName == preferredDevice.DeviceName);
            if (nameMatch != null && !IsNetworkOrHubDevice(nameMatch))
                return nameMatch;
        }

        // Prioridade absoluta: primeiro adaptador serial suportado
        var bestSerial = devices.FirstOrDefault(IsSupportedSerialDevice);
        if (bestSerial != null)
            return bestSerial;

        // Segundo: primeiro dispositivo que não seja rede nem hub
        var bestNonNetwork = devices.FirstOrDefault(d => !IsNetworkOrHubDevice(d));
        if (bestNonNetwork != null)
            return bestNonNetwork;

        return devices[0];
    }

    public async Task ConnectUsbAsync(UsbDevice? device = null, int baudRate = 9600, Action<string>? statusCallback = null, CancellationToken ct = default)
    {
        await DisconnectAsync();

        var liveDevice = ResolveLiveDevice(device);

        if (!UsbManagerHelper.HasPermission(liveDevice))
        {
            statusCallback?.Invoke("[*] Solicitando permissão USB ao Android... Toque em 'OK' no aviso da tela.");
            UsbManagerHelper.RequestPermission(liveDevice);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            var permissionGranted = false;
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(350, ct);
                try
                {
                    liveDevice = ResolveLiveDevice(liveDevice);
                    if (UsbManagerHelper.HasPermission(liveDevice))
                    {
                        permissionGranted = true;
                        break;
                    }
                }
                catch { }
            }

            if (!permissionGranted)
            {
                throw new InvalidOperationException("Permissão USB pendente. Toque em 'Conectar' e selecione 'OK' na solicitação de acesso USB do Android.");
            }

            statusCallback?.Invoke("[✓] Permissão USB concedida pelo usuário!");
        }

        liveDevice = ResolveLiveDevice(liveDevice);
        var transport = new AndroidUsbSerialTransport(liveDevice, baudRate);
        await transport.OpenAsync(ct);
        _transport = transport;

        var options = new SessionOptions { LeaveOpen = true };
        _session = new DeviceSession(_transport, options);

        OnConnectionStateChanged?.Invoke(true);

        ResumeReadLoop();
    }

    public void ConnectSimulated(ITransport customTransport)
    {
        _transport = customTransport;
        var options = new SessionOptions { LeaveOpen = true };
        _session = new DeviceSession(_transport, options);
        OnConnectionStateChanged?.Invoke(true);
    }

    public async Task ChangeBaudRateAsync(int baudRate)
    {
        if (_transport is AndroidUsbSerialTransport usb)
        {
            if (usb.BaudRate != baudRate)
            {
                await usb.ChangeBaudRateAsync(baudRate);
            }
            if (_session != null)
            {
                _session = null;
            }
            OnConnectionStateChanged?.Invoke(true);
        }
    }

    public async Task PauseReadLoopAsync()
    {
        await _loopLock.WaitAsync();
        try
        {
            if (_readCts != null)
            {
                _readCts.Cancel();
                if (_readLoopTask != null)
                {
                    try
                    {
                        await Task.WhenAny(_readLoopTask, Task.Delay(400));
                    }
                    catch { }
                    _readLoopTask = null;
                }
                _readCts.Dispose();
                _readCts = null;
            }
        }
        finally
        {
            _loopLock.Release();
        }
    }

    public void ResumeReadLoop()
    {
        _loopLock.Wait();
        try
        {
            if (_readCts == null && _transport?.IsOpen == true)
            {
                _readCts = new CancellationTokenSource();
                _readLoopTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
            }
        }
        finally
        {
            _loopLock.Release();
        }
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
                    await Task.Delay(30, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    OnTerminalDataReceived?.Invoke($"\n[ERRO LEITURA SERIAL: {ex.Message}]\n");
                    OnConnectionStateChanged?.Invoke(false);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Identificação Autônoma Multi-Padrão do Equipamento:
    /// 1. Testa primeiro o Padrão 1 (9600 bps - Cisco IOS / HPE Comware).
    /// 2. Na ausência de resposta, testa automaticamente o Padrão 2 (115200 bps - Fortinet FortiGate).
    /// 3. Enriquece modelo (ex: Cisco 1905) consultando o hardware sem intervenção do usuário.
    /// </summary>
    public async Task<DeviceDetectionResult> IdentifyDeviceAsync(CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        await PauseReadLoopAsync();
        try
        {
            // --- ETAPA 1: Padrão 1 (9600 bps - Cisco / HPE) ---
            OnProbeProgress?.Invoke("[*] Testando Padrão 1: 9600 bps (Cisco IOS / HPE Comware)...");
            if ((_transport as AndroidUsbSerialTransport)?.BaudRate != 9600)
            {
                await ChangeBaudRateAsync(9600);
            }

            // Acorda o console com múltiplos retornos de linha
            await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n\r\n"), ct);
            await Task.Delay(300, ct);

            var result = await _detector.DetectAsync(_transport, ct);

            // 841 zerado responde com o setup dialog em vez de prompt — responde 'no'
            // (autoinstall/press-return na sequência) e re-detecta para obter o prompt real.
            if (IsCiscoSetupDialog(result.RawPrompt))
            {
                OnProbeProgress?.Invoke("[*] Diálogo de configuração inicial Cisco detectado — respondendo 'no'...");
                await AnswerCiscoSetupDialogAsync(ct);
                result = await _detector.DetectAsync(_transport, ct);
            }

            // Se o equipamento responder com proteção de senha no Padrão 1, tenta autenticar com credenciais padrão Claro/SPARC
            if (result.OperatingState == DeviceOperatingState.PasswordProtected)
            {
                var autoOk = await TryAutoAuthenticateAsync(result, msg => OnProbeProgress?.Invoke(msg), ct);
                if (autoOk)
                {
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
                    await Task.Delay(250, ct);
                    result = await _detector.DetectAsync(_transport, ct);
                    if (result.OperatingState == DeviceOperatingState.PasswordProtected || result.OperatingState == DeviceOperatingState.Unknown)
                    {
                        result = new DeviceDetectionResult(
                            result.Manufacturer,
                            result.Series,
                            DeviceOperatingState.Ready,
                            WorkflowType.Provisioning,
                            AccessState.Open,
                            result.BootState,
                            result.FirmwareState,
                            result.RawPrompt,
                            "Autenticado automaticamente com credenciais padrão Claro/SPARC.");
                    }
                }
            }

            // Se identificou Cisco no Padrão 1 (mesmo com Series == Unknown, como em "Router>"), enriquece o modelo via show version
            if (result.Manufacturer == DeviceManufacturer.Cisco && result.OperatingState != DeviceOperatingState.PasswordProtected)
            {
                result = await EnrichCiscoModelIfPossibleAsync(result, ct);
            }

            // Se identificou com sucesso no Padrão 1 (Cisco, HPE ou Fortinet a 9600 bps)
            var pattern1Identified = result.Manufacturer == DeviceManufacturer.Cisco ||
                                     result.Manufacturer == DeviceManufacturer.Hpe ||
                                     result.Manufacturer == DeviceManufacturer.Fortinet ||
                                     (result.Manufacturer != DeviceManufacturer.Unknown &&
                                      result.Manufacturer != DeviceManufacturer.Generic &&
                                      result.Series != DeviceSeries.Unknown);

            if (pattern1Identified)
            {
                LastDetectionResult = result;
                OnDeviceIdentified?.Invoke(result);
                return result;
            }

            // --- ETAPA 2: Padrão 2 (115200 bps - Fortinet FortiGate) ---
            OnProbeProgress?.Invoke("[*] Sem resposta conclusiva a 9600 bps. Testando automaticamente Padrão 2: 115200 bps (Fortinet FortiGate)...");
            await ChangeBaudRateAsync(115200);

            await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n\r\n"), ct);
            await Task.Delay(300, ct);

            var resultFortinet = await _detector.DetectAsync(_transport, ct);

            if (resultFortinet.OperatingState == DeviceOperatingState.PasswordProtected)
            {
                var autoFortiOk = await TryAutoAuthenticateAsync(resultFortinet, msg => OnProbeProgress?.Invoke(msg), ct);
                if (autoFortiOk)
                {
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
                    await Task.Delay(250, ct);
                    resultFortinet = await _detector.DetectAsync(_transport, ct);
                    if (resultFortinet.OperatingState == DeviceOperatingState.PasswordProtected || resultFortinet.OperatingState == DeviceOperatingState.Unknown)
                    {
                        resultFortinet = new DeviceDetectionResult(
                            resultFortinet.Manufacturer,
                            resultFortinet.Series,
                            DeviceOperatingState.Ready,
                            WorkflowType.Provisioning,
                            AccessState.Open,
                            resultFortinet.BootState,
                            resultFortinet.FirmwareState,
                            resultFortinet.RawPrompt,
                            "Autenticado com credenciais padrão Claro/SPARC.");
                    }
                }
            }

            if (resultFortinet.Manufacturer != DeviceManufacturer.Unknown && resultFortinet.Manufacturer != DeviceManufacturer.Generic)
            {
                LastDetectionResult = resultFortinet;
                OnDeviceIdentified?.Invoke(resultFortinet);
                return resultFortinet;
            }

            // --- ETAPA 3: Nenhuma resposta em ambos os padrões ---
            // Retorna a 9600 bps como velocidade padrão e devolve diagnóstico
            await ChangeBaudRateAsync(9600);
            LastDetectionResult = result;
            OnDeviceIdentified?.Invoke(result);
            return result;
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Detecta o setup dialog do IOS zerado ("Would you like to enter the initial
    /// configuration dialog? [yes/no]:").
    /// </summary>
    internal static bool IsCiscoSetupDialog(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) &&
        (raw.Contains("initial configuration dialog", StringComparison.OrdinalIgnoreCase) ||
         raw.Contains("System Configuration Dialog", StringComparison.OrdinalIgnoreCase) ||
         (raw.Contains("Would you like to enter", StringComparison.OrdinalIgnoreCase) &&
          raw.Contains("[yes/no]", StringComparison.OrdinalIgnoreCase)) ||
         raw.Contains("terminate autoinstall", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Responde ao setup dialog em nível de transporte: 'no' -> ENTER (terminate
    /// autoinstall [yes]) -> ENTER (Press RETURN to get started) e drena os ecos.
    /// </summary>
    private async Task AnswerCiscoSetupDialogAsync(CancellationToken ct)
    {
        if (_transport == null || !_transport.IsOpen)
            return;

        await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("no\r\n"), ct);
        await Task.Delay(900, ct);
        await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
        await Task.Delay(700, ct);
        await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
        await Task.Delay(700, ct);

        // Drena ecos p/ não contaminar a re-detecção (limitado p/ não prender em boot contínuo)
        var buf = new byte[4096];
        try
        {
            for (var i = 0; i < 20; i++)
            {
                if (await _transport.ReadAsync(buf, ct) <= 0)
                    break;
            }
        }
        catch { }
    }

    private async Task<DeviceDetectionResult> EnrichCiscoModelIfPossibleAsync(DeviceDetectionResult initialResult, CancellationToken ct)
    {
        if (initialResult.Manufacturer != DeviceManufacturer.Cisco || _transport == null)
            return initialResult;

        if (initialResult.Series != DeviceSeries.Unknown)
            return initialResult;

        try
        {
            var isRommon = initialResult.OperatingState == DeviceOperatingState.BootFailure ||
                           initialResult.AccessState == AccessState.RommonOrBootware ||
                           initialResult.BootState == BootState.Rommon ||
                           (initialResult.RawPrompt ?? string.Empty).Contains("rommon", StringComparison.OrdinalIgnoreCase) ||
                           (initialResult.RawPrompt ?? string.Empty).Contains("switch:", StringComparison.OrdinalIgnoreCase);

            string promptAtual = initialResult.RawPrompt ?? string.Empty;
            var isConfigMode = promptAtual.Contains("(config", StringComparison.OrdinalIgnoreCase) ||
                               System.Text.RegularExpressions.Regex.IsMatch(promptAtual, @"\([A-Za-z0-9_\-\.\/]*config[A-Za-z0-9_\-\.\/]*\)\s*[>#]");

            var sb = new System.Text.StringBuilder();

            if (isRommon)
            {
                // Em ROMMON, comandos são 'set' e 'dir flash:'
                await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("set\r\n"), ct);
                await Task.Delay(300, ct);
                await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("dir flash:\r\n"), ct);
            }
            else
            {
                // Se estiver com tela de senha / login pedindo autenticação, não adianta mandar 'show version'
                if (initialResult.OperatingState == DeviceOperatingState.PasswordProtected)
                {
                    return initialResult;
                }

                // Se o terminal estiver preso em modo de configuração (ex: Router(config)#), sai com 'end' para o modo EXEC (#)
                if (isConfigMode)
                {
                    OnProbeProgress?.Invoke("[*] Console Cisco em modo de configuração detectado — retornando ao modo EXEC privilegiado...");
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("end\r\n"), ct);
                    await Task.Delay(250, ct);

                    // Drena logs do console como "%SYS-5-CONFIG_I: Configured from console by console"
                    var flushBufDrain = new byte[1024];
                    while (_transport.IsOpen)
                    {
                        using var flushCts = new CancellationTokenSource(80);
                        try
                        {
                            var fr = await _transport.ReadAsync(flushBufDrain, flushCts.Token);
                            if (fr <= 0) break;
                        }
                        catch { break; }
                    }

                    // Sonda o novo prompt limpo em modo EXEC (#)
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
                    await Task.Delay(200, ct);
                    var pBuf = new byte[1024];
                    using var pCts = new CancellationTokenSource(300);
                    try
                    {
                        var pr = await _transport.ReadAsync(pBuf, pCts.Token);
                        if (pr > 0)
                        {
                            var newP = System.Text.Encoding.UTF8.GetString(pBuf, 0, pr).Trim();
                            var lastLine = newP.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                            if (!string.IsNullOrWhiteSpace(lastLine) && (lastLine.EndsWith("#") || lastLine.EndsWith(">")))
                            {
                                promptAtual = lastLine;
                            }
                        }
                    }
                    catch { }

                    // Limpa marcador de config do prompt se ainda contiver
                    if (promptAtual.Contains("(config", StringComparison.OrdinalIgnoreCase))
                    {
                        promptAtual = System.Text.RegularExpressions.Regex.Replace(promptAtual, @"\([A-Za-z0-9_\-\.\/]*config[A-Za-z0-9_\-\.\/]*\)", "");
                    }
                }

                // 1. Envia terminal length 0 para evitar pausas (--More--)
                await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("terminal length 0\r\n"), ct);
                await Task.Delay(250, ct);

                // Drena eventuais ecos do terminal length 0
                var flushBuf = new byte[1024];
                while (_transport.IsOpen)
                {
                    using var flushCts = new CancellationTokenSource(60);
                    try
                    {
                        var fr = await _transport.ReadAsync(flushBuf, flushCts.Token);
                        if (fr <= 0) break;
                    }
                    catch { break; }
                }

                // 2. Envia show version
                await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("show version\r\n"), ct);
            }

            var buffer = new byte[2048];
            var deadline = DateTime.UtcNow.AddSeconds(4.5);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var r = await _transport.ReadAsync(buffer, ct);
                if (r > 0)
                {
                    var chunk = System.Text.Encoding.UTF8.GetString(buffer, 0, r);
                    sb.Append(chunk);
                    var str = sb.ToString();

                    // Se acusar comando inválido (caso ainda estivesse em config), envia fallback 'do show version'
                    if (str.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase) && !str.Contains("do show version", StringComparison.OrdinalIgnoreCase))
                    {
                        await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("do show version\r\n"), ct);
                        await Task.Delay(200, ct);
                    }

                    // Verifica se já temos informação suficiente para identificar o modelo
                    if (DeviceDetector.Cisco841ModelRegex.IsMatch(str) ||
                        DeviceDetector.Cisco900ModelRegex.IsMatch(str) ||
                        DeviceDetector.Cisco2900ModelRegex.IsMatch(str) ||
                        DeviceDetector.Cisco1900ModelRegex.IsMatch(str) ||
                        str.Contains("Configuration register", StringComparison.OrdinalIgnoreCase) ||
                        (str.Length > 200 && (str.EndsWith(">") || str.EndsWith("#"))))
                    {
                        break;
                    }
                }
                else
                {
                    await Task.Delay(30, ct);
                }
            }

            var fullText = promptAtual + "\n" + sb.ToString();
            var classified = _detector.ClassifyPrompt(fullText);

            if (isRommon)
            {
                // Preserva o estado de ROMMON/BootFailure
                return new DeviceDetectionResult(
                    classified.Manufacturer == DeviceManufacturer.Unknown ? DeviceManufacturer.Cisco : classified.Manufacturer,
                    classified.Series,
                    DeviceOperatingState.BootFailure,
                    WorkflowType.Provisioning,
                    AccessState.RommonOrBootware,
                    BootState.Rommon,
                    FirmwareState.CorruptedOrMissing,
                    fullText,
                    "Cisco detectado em modo ROMMON (sem SO carregado).");
            }

            if (classified.Series != DeviceSeries.Unknown)
            {
                return new DeviceDetectionResult(
                    classified.Manufacturer,
                    classified.Series,
                    DeviceOperatingState.Ready,
                    initialResult.RecommendedWorkflow,
                    AccessState.Open,
                    initialResult.BootState,
                    initialResult.FirmwareState,
                    promptAtual,
                    $"Cisco {classified.Series} identificado com sucesso via show version.");
            }

            if (promptAtual != initialResult.RawPrompt)
            {
                return new DeviceDetectionResult(
                    initialResult.Manufacturer,
                    initialResult.Series,
                    initialResult.OperatingState,
                    initialResult.RecommendedWorkflow,
                    initialResult.AccessState,
                    initialResult.BootState,
                    initialResult.FirmwareState,
                    promptAtual,
                    initialResult.Details);
            }

            return initialResult;
        }
        catch
        {
            return initialResult;
        }
    }

    public async Task ApplyProvisioningAsync(SaipCircuitData circuit, Func<string, Task> progressCallback, CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Conecte a porta serial USB do roteador antes de provisionar.");

        await PauseReadLoopAsync();
        try
        {
            if (LastDetectionResult == null || LastDetectionResult.Manufacturer == DeviceManufacturer.Unknown)
            {
                await progressCallback("[*] Identificando equipamento conectado na porta serial...");
                await IdentifyDeviceAsync(ct);
            }

            var detected = LastDetectionResult!;
            await progressCallback($"[*] Fabricante detectado: {detected.Manufacturer} | Série: {detected.Series}");

            // 1. Crítica impeditiva: Modo ROMMON / BootWare / BIOS (sem SO carregado)
            if (detected.OperatingState == DeviceOperatingState.BootFailure ||
                detected.AccessState == AccessState.RommonOrBootware ||
                detected.BootState == BootState.Rommon ||
                detected.BootState == BootState.Bootware ||
                (detected.RawPrompt ?? string.Empty).Contains("rommon", StringComparison.OrdinalIgnoreCase) ||
                (detected.RawPrompt ?? string.Empty).Contains("switch:", StringComparison.OrdinalIgnoreCase) ||
                (detected.RawPrompt ?? string.Empty).Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase) ||
                (detected.RawPrompt ?? string.Empty).Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "O equipamento se encontra em modo ROMMON / BootWare / BIOS (sem sistema operacional carregado). " +
                    "É OBRIGATÓRIO executar a recuperação de firmware na aba 'Firmware' antes de realizar o provisionamento da Ficha SAIP.");
            }

            // 2. Crítica impeditiva: Equipamento protegido por senha
            if (detected.OperatingState == DeviceOperatingState.PasswordProtected)
            {
                throw new InvalidOperationException(
                    "O equipamento está protegido por senha. É necessário autenticar com usuário e senha ou quebrar a senha antes de provisionar.");
            }

            // Garante que a sessão interativa esteja conectada ao roteador antes do provisionamento
            _session = await EnsureConsoleSessionAsync(progressCallback, ct);

            // Validação de segurança no prompt da sessão ativa
            var sessPrompt = (_session.CurrentPrompt ?? string.Empty).Trim();
            if (_session.Mode == ExecMode.Rommon ||
                sessPrompt.StartsWith("rommon", StringComparison.OrdinalIgnoreCase) ||
                sessPrompt.StartsWith("switch:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "O equipamento se encontra em modo ROMMON (sem sistema operacional carregado). " +
                    "É OBRIGATÓRIO executar a recuperação de firmware na aba 'Firmware' antes de realizar o provisionamento da Ficha SAIP.");
            }

            // Seleção de interfaces WAN/LAN por modelo — paridade fiel com o Windows
            var (wanIface, lanIface) = ResolveCiscoInterfaces(detected.Series);

            // Avaliação de Higienização Pré-Provisionamento (paridade com o Windows)
            try
            {
                var sanitization = detected.Manufacturer == DeviceManufacturer.Fortinet
                    ? await FortiOsSaipConfigurator.DetectSanitizationStatusAsync(_session, ct)
                    : detected.Manufacturer == DeviceManufacturer.Hpe
                        ? await HpeSaipConfigurator.DetectSanitizationStatusAsync(_session, ct)
                        : await CiscoSaipConfigurator.DetectSanitizationStatusAsync(_session, ct);

                await progressCallback($"[*] [AVALIAÇÃO DE CONFIGURAÇÃO] {sanitization.Summary}");
                if (sanitization.IsClean)
                {
                    await progressCallback("  -> Equipamento em padrão limpo/fábrica (zero lixo). Aplicação direta do script BLD sem reload.");
                }
                else
                {
                    await progressCallback("  -> Configuração de serviço anterior identificada. Aplicando higienização e sobreposição oficial BLD.");
                }
            }
            catch (Exception ex)
            {
                await progressCallback($"[*] Avaliação de higienização: {ex.Message}");
            }

            if (detected.Manufacturer == DeviceManufacturer.Cisco)
            {
                var configurator = new CiscoSaipConfigurator(progressCallback)
                {
                    IncluirNatLab = IncluirNatLab,
                    BootImage = CustomBootImage
                };
                if (wanIface is null)
                    await configurator.ApplyConfigAsync(_session, circuit, cancellationToken: ct);
                else
                    await configurator.ApplyConfigAsync(_session, circuit, wanIface, lanIface!, cancellationToken: ct);
            }
            else if (detected.Manufacturer == DeviceManufacturer.Hpe)
            {
                var configurator = new HpeSaipConfigurator(progressCallback);
                await configurator.ApplyConfigAsync(_session, circuit, "GigabitEthernet0/0", "GigabitEthernet0/1", ct);
            }
            else if (detected.Manufacturer == DeviceManufacturer.Fortinet)
            {
                var configurator = new FortiOsSaipConfigurator(progressCallback) { IncluirNatLab = IncluirNatLab };
                await configurator.ApplyConfigAsync(_session, circuit, "wan", "lan", cancellationToken: ct);
            }
            else
            {
                // Default fallback para Cisco
                await progressCallback("[AVISO] Fabricante não identificado com certeza. Aplicando perfil padrão Cisco IOS...");
                var configurator = new CiscoSaipConfigurator(progressCallback)
                {
                    IncluirNatLab = IncluirNatLab,
                    BootImage = CustomBootImage
                };
                await configurator.ApplyConfigAsync(_session, circuit, cancellationToken: ct);
            }

            // Captura da running-config aplicada (paridade com o relatório TXT do Windows)
            try
            {
                await progressCallback("[*] Capturando running-config aplicada...");
                LastAppliedConfig = detected.Manufacturer == DeviceManufacturer.Fortinet
                    ? await FortiOsSaipConfigurator.GetAppliedRunningConfigAsync(_session, ct)
                    : detected.Manufacturer == DeviceManufacturer.Hpe
                        ? await _session.SendCommandAsync("display current-configuration", TimeSpan.FromSeconds(60), ct)
                        : await new CiscoIOSAdapter().GetRunningConfigAsync(_session, ct);
                await progressCallback($"[OK] Running-config capturada ({LastAppliedConfig?.Length ?? 0} caracteres).");
            }
            catch (Exception ex)
            {
                LastAppliedConfig = null;
                await progressCallback($"[AVISO] Não foi possível capturar a running-config: {ex.Message}");
            }
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Sonda a sessão reaproveitada: se o console estiver preso no setup dialog do IOS
    /// zerado (sem prompt válido), descarta a sessão e reconecta do zero — o
    /// <see cref="DeviceSession.ConnectAsync"/> responde 'no'/autoinstall/press-return.
    /// </summary>
    private async Task EnsureNoSetupDialogAsync(Func<string, Task> progress, CancellationToken ct)
    {
        if (_session == null || _transport == null || !_transport.IsOpen)
            return;

        string probe;
        try
        {
            probe = await _session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(6), ct);
        }
        catch (SessionTimeoutException)
        {
            probe = "TIMEOUT SEM PROMPT";
        }

        var text = (_session.CurrentPrompt ?? string.Empty) + "\n" + probe;
        if (!IsCiscoSetupDialog(text) && !text.Contains("TIMEOUT"))
            return;

        await progress("[*] Console preso no diálogo inicial — reconectando e respondendo...");
        await _session.DisposeAsync();
        _session = null;

        try
        {
            await _transport.WriteAsync(new byte[] { 0x03 }, ct);
            await Task.Delay(150, ct);
        }
        catch { }

        _session = new DeviceSession(_transport, new SessionOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(60),
            CommandTimeout = TimeSpan.FromSeconds(60),
            LeaveOpen = true
        });
        await _session.ConnectAsync(ct);
        await progress("[✓] Sessão restabelecida após o diálogo inicial!");
    }

    /// <summary>
    /// Mapeia a série detectada para o par WAN/LAN — espelha os branches do Windows
    /// (is841 / is921 / default 1900). Retorna null quando a série é desconhecida para
    /// que o CiscoSaipConfigurator use o default + auto-detecção via 'show'.
    /// </summary>
    internal static (string? wan, string? lan) ResolveCiscoInterfaces(DeviceSeries series) => series switch
    {
        DeviceSeries.Series1900 => ("GigabitEthernet 0/0", "GigabitEthernet 0/1"),
        DeviceSeries.Series2900 => ("GigabitEthernet 0/0", "GigabitEthernet 0/1"),
        DeviceSeries.Isr921 => ("GigabitEthernet 4", "GigabitEthernet 5"),
        DeviceSeries.Isr841 => ("GigabitEthernet0/4", "GigabitEthernet0/5"),
        _ => (null, null),
    };

    /// <summary>
    /// Testa autenticação direta na porta serial enviando usuário e senha e verificando retorno de prompt shell.
    /// Compatível com Cisco IOS, HPE Comware e Fortinet FortiOS (inclusive primeiro login admin com troca obrigatória de senha).
    /// </summary>
    public async Task<bool> TryAuthenticateSerialDirectAsync(string? username, string? pass, CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            return false;

        var rxAccumulator = new System.Text.StringBuilder();
        var buf = new byte[1024];

        // 1. Sondagem rápida para não cancelar com Ctrl+C um prompt já aberto de New Password
        var initBuf = new byte[512];
        int initRead = 0;
        using (var initCts = new CancellationTokenSource(150))
        {
            try { initRead = await _transport.ReadAsync(initBuf, initCts.Token); } catch { }
        }
        if (initRead > 0)
        {
            rxAccumulator.Append(System.Text.Encoding.UTF8.GetString(initBuf, 0, initRead));
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(rxAccumulator.ToString(), @"(?i)(?:new|confirm)\s+password"))
        {
            await _transport.WriteAsync(new byte[] { 0x03 }, ct); // Ctrl+C
            await Task.Delay(80, ct);
            await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
            await Task.Delay(100, ct);
        }

        var passwordSent = false;
        var usernameSent = false;
        var newPasswordSent = false;
        var confirmPasswordSent = false;
        var standardPassToSet = !string.IsNullOrWhiteSpace(pass) ? pass.Trim() : "CQMR";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < 4500 && !ct.IsCancellationRequested)
        {
            int r = 0;
            using (var readCts = new CancellationTokenSource(150))
            {
                try { r = await _transport.ReadAsync(buf, readCts.Token); } catch { }
            }

            if (r > 0)
            {
                var text = System.Text.Encoding.UTF8.GetString(buf, 0, r);
                rxAccumulator.Append(text);
                var current = rxAccumulator.ToString();

                // Sucesso: prompt de shell liberado (<HPE>, [HPE], Router#, Router>, Switch#, FortiGate #, etc.)
                if (current.Contains("<HPE", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("[HPE", StringComparison.OrdinalIgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(current, @"[\<\[][^\r\n>\]]+[\>\]]\s*$") ||
                    System.Text.RegularExpressions.Regex.IsMatch(current, @"[A-Za-z0-9_\-\.\(\)]+[>#]\s*$") ||
                    System.Text.RegularExpressions.Regex.IsMatch(current, @"(?i)(?:FortiGate|FGT|FG)[A-Za-z0-9_\-]*\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$"))
                {
                    // Se for Cisco em User Exec mode (>), tenta elevar para Privileged Exec (#) com 'enable'
                    if (current.Trim().EndsWith(">") && !current.Contains("<HPE"))
                    {
                        await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("enable\r"), ct);
                        await Task.Delay(200, ct);
                        var enableBuf = new byte[512];
                        using var enCts = new CancellationTokenSource(500);
                        try
                        {
                            var er = await _transport.ReadAsync(enableBuf, enCts.Token);
                            if (er > 0)
                            {
                                var enText = System.Text.Encoding.UTF8.GetString(enableBuf, 0, er);
                                if (enText.Contains("Password:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var secret = !string.IsNullOrWhiteSpace(LastResolvedEnableSecret) ? LastResolvedEnableSecret : "PRO1AN";
                                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes(secret + "\r"), ct);
                                    await Task.Delay(200, ct);
                                }
                            }
                        }
                        catch { }
                    }
                    return true;
                }

                // Se estiver preso em tela More de comando anterior, cancela com Ctrl+C
                if (System.Text.RegularExpressions.Regex.IsMatch(current, @"(?i)--+\s*More\s*--+"))
                {
                    rxAccumulator.Clear();
                    await _transport.WriteAsync(new byte[] { 0x03 }, ct);
                    await Task.Delay(80, ct);
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
                    await Task.Delay(150, ct);
                    continue;
                }

                // Se pedir ENTER
                if (current.Contains("Press ENTER", StringComparison.OrdinalIgnoreCase) && !passwordSent && !usernameSent)
                {
                    rxAccumulator.Clear();
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
                    await Task.Delay(150, ct);
                    continue;
                }

                // Se pedir Username / login
                if ((current.Contains("Username:", StringComparison.OrdinalIgnoreCase) ||
                     current.Contains("login:", StringComparison.OrdinalIgnoreCase) ||
                     System.Text.RegularExpressions.Regex.IsMatch(current, @"(?i)(?:^|[\s\b@:])(?:Username|login)\s*[:?]")) && !usernameSent)
                {
                    usernameSent = true;
                    rxAccumulator.Clear();
                    await Task.Delay(80, ct);
                    var userToSend = string.IsNullOrWhiteSpace(username) ? "EBT" : username.Trim();
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes(userToSend + "\r"), ct);
                    await Task.Delay(200, ct);
                    continue;
                }

                // Se FortiOS exigir troca obrigatória de senha
                if (System.Text.RegularExpressions.Regex.IsMatch(current, @"(?i)(?:new\s+password\s*[:?]|please\s+input\s+a\s+new\s+password|change\s+your\s+password)") && !newPasswordSent)
                {
                    newPasswordSent = true;
                    rxAccumulator.Clear();
                    await Task.Delay(100, ct);
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes(standardPassToSet + "\r"), ct);
                    await Task.Delay(200, ct);
                    continue;
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(current, @"(?i)(?:confirm|verify|re-?enter)\s+(?:new\s+)?password\s*[:?]") && !confirmPasswordSent)
                {
                    confirmPasswordSent = true;
                    rxAccumulator.Clear();
                    await Task.Delay(100, ct);
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes(standardPassToSet + "\r"), ct);
                    await Task.Delay(200, ct);
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r"), ct);
                    await Task.Delay(200, ct);
                    continue;
                }

                // Se pedir Password (inicial)
                if (System.Text.RegularExpressions.Regex.IsMatch(current, @"(?i)(?:^|[\s\b])(?:Password|Login\s+password)\s*[:?]") &&
                    !System.Text.RegularExpressions.Regex.IsMatch(current, @"(?i)(?:new|confirm)\s+password") &&
                    !passwordSent)
                {
                    passwordSent = true;
                    rxAccumulator.Clear();
                    await Task.Delay(100, ct);
                    var p = (pass ?? "").Trim();
                    await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes(p + "\r"), ct);
                    await Task.Delay(200, ct);
                    continue;
                }

                // Falha explícita após envio da senha
                var isNewPassPrompt = System.Text.RegularExpressions.Regex.IsMatch(current, @"(?i)(?:new|confirm|verify|re-?enter)\s+(?:new\s+)?password|please\s+input\s+a\s+new\s+password|change\s+your\s+password");
                if (passwordSent && !isNewPassPrompt && (current.Contains("Login failed", StringComparison.OrdinalIgnoreCase) ||
                                     current.Contains("Wrong password", StringComparison.OrdinalIgnoreCase) ||
                                     current.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) ||
                                     current.Contains("Authentication failure", StringComparison.OrdinalIgnoreCase) ||
                                     current.Contains("Bad passwords", StringComparison.OrdinalIgnoreCase) ||
                                     current.Contains("Bad password", StringComparison.OrdinalIgnoreCase) ||
                                     current.Contains("Login invalid", StringComparison.OrdinalIgnoreCase) ||
                                     current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) ||
                                     current.Contains("Access denied", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }
            }
            else
            {
                await Task.Delay(30, ct);
            }
        }

        return false;
    }

    /// <summary>
    /// Rotação automática de credenciais padrão Claro/SPARC (paridade integral com a versão Windows).
    /// </summary>
    public async Task<bool> TryAutoAuthenticateAsync(DeviceDetectionResult? initialDetection = null, Action<string>? log = null, CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen) return false;

        var detected = initialDetection ?? LastDetectionResult;
        var mfr = detected?.Manufacturer ?? DeviceManufacturer.Unknown;

        var candidates = new List<(string? user, string pass)>();

        if (mfr == DeviceManufacturer.Cisco)
        {
            // 1. Cisco Claro padrão: EBT / CQMR
            candidates.Add(("EBT", "CQMR"));
            // 2. Cisco Claro padrão secundário: EBT / PRO1AN
            candidates.Add(("EBT", "PRO1AN"));
            // 3. cisco / cisco
            candidates.Add(("cisco", "cisco"));
            // 4. admin / CQMR
            candidates.Add(("admin", "CQMR"));
            // 5. admin / admin
            candidates.Add(("admin", "admin"));
            // Senhas avulsas de console
            candidates.Add((null, "CQMR"));
            candidates.Add((null, "PRO1AN"));
            candidates.Add((null, "cisco"));
            candidates.Add((null, "admin"));
        }
        else if (mfr == DeviceManufacturer.Hpe)
        {
            // 1. HPE Claro padrão: EBT / PRO1AN
            candidates.Add(("EBT", "PRO1AN"));
            // 2. admin / admin (Padrão de fábrica Comware)
            candidates.Add(("admin", "admin"));
            // 3. admin / CQMR
            candidates.Add(("admin", "CQMR"));
            // 4. admin / PRO1AN
            candidates.Add(("admin", "PRO1AN"));
            // 5. EBT / CQMR
            candidates.Add(("EBT", "CQMR"));
            // Senha avulsa de console
            candidates.Add((null, "PRO1AN"));
            candidates.Add((null, "admin"));
            candidates.Add((null, ""));
        }
        else if (mfr == DeviceManufacturer.Fortinet)
        {
            // 1. admin / (vazio) - Padrão de fábrica Fortinet
            candidates.Add(("admin", ""));
            // 2. admin / admin
            candidates.Add(("admin", "admin"));
            // 3. admin / CQMR
            candidates.Add(("admin", "CQMR"));
            // 4. admin / PRO1AN
            candidates.Add(("admin", "PRO1AN"));
        }
        else
        {
            // Fabricante genérico / não identificado ainda: testa combinações mais comuns
            candidates.Add(("EBT", "CQMR"));
            candidates.Add(("EBT", "PRO1AN"));
            candidates.Add(("admin", ""));
            candidates.Add(("admin", "admin"));
            candidates.Add(("cisco", "cisco"));
            candidates.Add(("admin", "CQMR"));
            candidates.Add((null, "CQMR"));
            candidates.Add((null, "PRO1AN"));
        }

        log?.Invoke($"[*] Console com autenticação detectado ({mfr}). Testando automaticamente credenciais padrão Claro/SPARC...");

        foreach (var (user, pass) in candidates)
        {
            if (ct.IsCancellationRequested) break;

            var displayUser = user ?? "(nenhum)";
            var displayPass = pass.Length > 0 ? new string('*', pass.Length) : "(vazia)";
            log?.Invoke($"[*] Testando credencial padrão: user='{displayUser}', pass='{displayPass}'...");

            bool ok = false;
            try
            {
                ok = await TryAuthenticateSerialDirectAsync(user, pass, ct);
            }
            catch
            {
                ok = false;
            }

            if (ok)
            {
                LastResolvedUser = user;
                LastResolvedPassword = pass;
                if (mfr == DeviceManufacturer.Cisco && string.IsNullOrWhiteSpace(LastResolvedEnableSecret))
                {
                    LastResolvedEnableSecret = "PRO1AN";
                }

                log?.Invoke($"[✓] Autenticação automática concluída com sucesso (usuário '{user ?? "admin"}')! Acesso liberado sem intervenção manual.");
                return true;
            }

            await Task.Delay(150, ct);
        }

        log?.Invoke("[!] Credenciais padrão Claro/SPARC testadas e recusadas pelo equipamento.");
        return false;
    }

    /// <summary>
    /// OPÇÃO 1 (paridade com o PasswordAuthDialog do Windows): tenta login direto com
    /// credenciais conhecidas. Se autenticar, guarda a sessão e força re-identificação
    /// (o resultado anterior era PasswordProtected/Generic). Retorna true se logado.
    /// </summary>
    public async Task<bool> TryLoginAsync(string? username, string? password, Action<string>? log = null, CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        await PauseReadLoopAsync();
        try
        {
            if (_session != null)
            {
                await _session.DisposeAsync();
                _session = null;
            }

            var ok = await TryAuthenticateSerialDirectAsync(username, password ?? "", ct);
            if (ok)
            {
                LastResolvedUser = username;
                LastResolvedPassword = password;
                if (string.IsNullOrWhiteSpace(LastResolvedEnableSecret))
                    LastResolvedEnableSecret = "PRO1AN";
                LastDetectionResult = null; // força re-identificação c/ prompt aberto no provisionamento
                log?.Invoke($"[✓] Login direto aceito com as credenciais informadas.");
                return true;
            }

            log?.Invoke("[!] Credenciais recusadas pelo equipamento.");
            return false;
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// OPÇÃO 2 (paridade com a quebra de senha do Windows): zera o equipamento bloqueado
    /// via ROMMON (Cisco, com power-cycle guiado), BootWare (HPE) ou reset físico + BIOS
    /// (Fortinet). Ao final, descarta sessão/detecção para o provisionamento re-identificar
    /// o equipamento já em padrão de fábrica (emenda automática da esteira).
    /// </summary>
    public async Task<bool> RecoverPasswordAsync(
        Func<string, Task> progress,
        Func<string, CancellationToken, Task>? instructOperator = null,
        CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        var detected = LastDetectionResult;
        if (detected == null || detected.Manufacturer == DeviceManufacturer.Unknown)
        {
            await progress("[*] Identificando equipamento bloqueado na serial...");
            await IdentifyDeviceAsync(ct);
            await PauseReadLoopAsync(); // Identify religou o loop; pausa p/ recovery exclusivo
            detected = LastDetectionResult!;
        }
        else
        {
            await PauseReadLoopAsync();
        }

        try
        {
            await progress($"[*] Quebra de senha: {detected.Manufacturer} | {detected.Series}");

            if (_session != null)
            {
                await _session.DisposeAsync();
                _session = null;
            }

            bool ok;
            if (detected.Manufacturer == DeviceManufacturer.Hpe)
            {
                var profile = detected.Series switch
                {
                    DeviceSeries.Msr954 => BootInterruptProfiles.HpeMsr954,
                    DeviceSeries.Msr930 => BootInterruptProfiles.HpeMsr930,
                    DeviceSeries.Msr1002 => BootInterruptProfiles.HpeMsr1002,
                    _ => BootInterruptProfiles.HpeMsrGeneric,
                };
                var session = new DeviceSession(_transport, new SessionOptions
                {
                    PromptMatcher = RegexPromptMatcher.Universal(),
                    ConnectTimeout = TimeSpan.FromSeconds(60),
                    CommandTimeout = TimeSpan.FromSeconds(60),
                    LeaveOpen = true
                });
                var recovery = new HpeComwareRecovery(progress, profile);
                ok = await recovery.RecoverAndResetAsync(session, instructOperator, ct: ct);
                await session.DisposeAsync();
            }
            else if (detected.Manufacturer == DeviceManufacturer.Fortinet)
            {
                // Opera direto no transporte (monitor reativo, sem sessão interativa)
                var recovery = new FortiGatePasswordRecovery(progress);
                ok = await recovery.RecoverAndResetAsync(_transport, instructOperator, ct);
                if (!string.IsNullOrWhiteSpace(recovery.DetectedSerial))
                {
                    LastDetectedSerial = recovery.DetectedSerial;
                    await progress($"[i] Serial capturado: {recovery.DetectedSerial}");
                }
            }
            else
            {
                // Cisco (qualquer série) ou fabricante incerto: perfil Dual (Break + Ctrl+C)
                // cobre 1900/841/921 — mesma estratégia do fallback do Windows.
                var profile = detected.Series switch
                {
                    DeviceSeries.Series1900 => BootInterruptProfiles.Cisco1900,
                    DeviceSeries.Series2900 => BootInterruptProfiles.Cisco2900,
                    DeviceSeries.Isr921 => BootInterruptProfiles.Cisco900,
                    DeviceSeries.Isr841 => BootInterruptProfiles.Cisco841,
                    _ => BootInterruptProfiles.CiscoUniversal,
                };
                var session = new DeviceSession(_transport, new SessionOptions
                {
                    PromptMatcher = RegexPromptMatcher.Universal(),
                    ConnectTimeout = TimeSpan.FromSeconds(60),
                    CommandTimeout = TimeSpan.FromSeconds(90),
                    LeaveOpen = true
                });
                var recovery = new CiscoIOSRecovery(msg => progress(msg), profile: profile);
                await recovery.RecoverAndResetAsync(session, instructOperator, ct);
                await session.DisposeAsync();
                ok = true;
            }

            // Equipamento agora em padrão de fábrica: força re-identificação no provisionamento
            _session = null;
            LastDetectionResult = null;
            return ok;
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Garante que a sessão interativa esteja aberta e pronta para comandos CLI.
    /// Retorna a sessão conectada.
    /// </summary>
    public async Task<DeviceSession> EnsureConsoleSessionAsync(Func<string, Task>? progress = null, CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen || !HasSupportedSerialConnected())
            throw new InvalidOperationException("Console serial USB não está conectado.");

        Func<string, Task> prog = progress ?? (_ => Task.CompletedTask);

        if (_session == null || !_session.IsConnected)
        {
            await prog("[*] Estabelecendo sessão interativa no console do roteador...");
            try
            {
                // Envia Ctrl+C e Enter para limpar qualquer comando ou submodo pendente no roteador
                await _transport.WriteAsync(new byte[] { 0x03 }, ct);
                await Task.Delay(150, ct);
                await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n"), ct);
                await Task.Delay(200, ct);
            }
            catch { }

            _session = new DeviceSession(_transport, new SessionOptions
            {
                Username = LastResolvedUser,
                Password = LastResolvedPassword,
                ConnectTimeout = TimeSpan.FromSeconds(60),
                CommandTimeout = TimeSpan.FromSeconds(60),
                LeaveOpen = true
            });
            await _session.ConnectAsync(ct);

            try
            {
                await _session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
            }
            catch { }

            await prog("[✓] Sessão interativa conectada com sucesso!");
        }
        else
        {
            // Sessão reaproveitada: sonda setup dialog preso e reconecta se necessário
            // (paridade: o Windows sempre abre sessão nova na Fase C).
            await EnsureNoSetupDialogAsync(prog, ct);
            try
            {
                await _session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(4), ct).ConfigureAwait(false);
            }
            catch { }
        }

        return _session!;
    }

    /// <summary>
    /// Aplica temporariamente IP e DHCP na LAN do roteador e habilita Telnet para permitir o swap
    /// do adaptador Serial USB pelo adaptador Ethernet no celular durante o upgrade de firmware em bancada.
    /// </summary>
    public async Task ApplyTemporaryLanStagingAsync(DeviceManufacturer mfr, DeviceSeries series, string? lanIface, Func<string, Task> progress, CancellationToken ct = default)
    {
        _session = await EnsureConsoleSessionAsync(progress, ct);
        await EnsureNoSetupDialogAsync(progress, ct);

        await progress("[*] Aplicando configuração de staging temporário no roteador (IP 192.168.1.1 + DHCP + Telnet)...");

        if (mfr == DeviceManufacturer.Fortinet)
        {
            var iface = !string.IsNullOrWhiteSpace(lanIface) ? lanIface : "internal";
            await _session.SendCommandAsync("config system interface", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync($"edit {iface}", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("set ip 192.168.1.1 255.255.255.0", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("set allowaccess ping telnet http https", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("next", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("end", TimeSpan.FromSeconds(5), ct);

            await _session.SendCommandAsync("config system dhcp server", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("edit 99", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync($"set interface {iface}", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("set default-gateway 192.168.1.1", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("set netmask 255.255.255.0", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("config ip-range", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("edit 1", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("set start-ip 192.168.1.10", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("set end-ip 192.168.1.30", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("next", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("end", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("next", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("end", TimeSpan.FromSeconds(5), ct);
        }
        else if (mfr == DeviceManufacturer.Hpe)
        {
            var iface = !string.IsNullOrWhiteSpace(lanIface) ? lanIface : "GigabitEthernet0/1";
            await _session.SendCommandAsync("system-view", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync($"interface {iface}", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("ip address 192.168.1.1 255.255.255.0", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("undo shutdown", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("quit", TimeSpan.FromSeconds(5), ct);

            // Garante link ativo na GigabitEthernet0/0 caso o técnico conecte na outra porta
            try
            {
                await _session.SendCommandAsync("interface GigabitEthernet0/0", TimeSpan.FromSeconds(3), ct);
                await _session.SendCommandAsync("undo shutdown", TimeSpan.FromSeconds(3), ct);
                await _session.SendCommandAsync("quit", TimeSpan.FromSeconds(3), ct);
            }
            catch { }

            await _session.SendCommandAsync("dhcp enable", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("dhcp server ip-pool SPARC_STAGING", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("network 192.168.1.0 mask 255.255.255.0", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("gateway-list 192.168.1.1", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("quit", TimeSpan.FromSeconds(5), ct);

            await _session.SendCommandAsync("telnet server enable", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("user-interface vty 0 4", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("authentication-mode password", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("set authentication password simple PRO1ANPRO1AN", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("user-role level-15", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("return", TimeSpan.FromSeconds(5), ct);
        }
        else // Cisco
        {
            var iface = !string.IsNullOrWhiteSpace(lanIface)
                ? lanIface
                : (series == DeviceSeries.Isr841 ? "Vlan1" : (series == DeviceSeries.Isr921 ? "Vlan1" : "GigabitEthernet0/0"));

            // Garante que o console saia de qualquer submodo com 'end' antes de entrar em config terminal
            try { await _session.SendCommandAsync("end", TimeSpan.FromSeconds(3), ct); } catch { }

            await _session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync($"interface {iface}", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("ip address 192.168.1.1 255.255.255.0", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("no shutdown", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("exit", TimeSpan.FromSeconds(5), ct);

            // Garante link ativo na GigabitEthernet0/1 para modelos modulares (ex: Cisco 1905, 1921, 1941, 2901)
            if (series == DeviceSeries.Series1900 || series == DeviceSeries.Series2900 || series == DeviceSeries.Unknown)
            {
                try
                {
                    await _session.SendCommandAsync("interface GigabitEthernet0/1", TimeSpan.FromSeconds(3), ct);
                    await _session.SendCommandAsync("no shutdown", TimeSpan.FromSeconds(3), ct);
                    await _session.SendCommandAsync("exit", TimeSpan.FromSeconds(3), ct);
                }
                catch { }
            }
            // Para switches integrados (ISR 841 / 921), garante 'no shutdown' nas portas físicas da switch LAN
            else if (series == DeviceSeries.Isr841 || series == DeviceSeries.Isr921)
            {
                try
                {
                    await _session.SendCommandAsync("interface range GigabitEthernet 0 - 3", TimeSpan.FromSeconds(3), ct);
                    await _session.SendCommandAsync("no shutdown", TimeSpan.FromSeconds(3), ct);
                    await _session.SendCommandAsync("exit", TimeSpan.FromSeconds(3), ct);
                }
                catch
                {
                    try
                    {
                        await _session.SendCommandAsync("interface range FastEthernet 0 - 3", TimeSpan.FromSeconds(3), ct);
                        await _session.SendCommandAsync("no shutdown", TimeSpan.FromSeconds(3), ct);
                        await _session.SendCommandAsync("exit", TimeSpan.FromSeconds(3), ct);
                    }
                    catch { }
                }
            }

            await _session.SendCommandAsync("ip dhcp pool SPARC_STAGING", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("network 192.168.1.0 255.255.255.0", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("default-router 192.168.1.1", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("exit", TimeSpan.FromSeconds(5), ct);

            await _session.SendCommandAsync("line vty 0 4", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("transport input telnet", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("privilege level 15", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("password CQMR", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("login", TimeSpan.FromSeconds(5), ct);
            await _session.SendCommandAsync("end", TimeSpan.FromSeconds(5), ct);
        }

        await progress("[✓] Staging temporário aplicado com sucesso (IP 192.168.1.1 / DHCP / Telnet ativo)!");
    }

    /// <summary>
    /// Lista os IPv4 locais das interfaces ativas (ex: eth0 do adaptador Ethernet OTG,
    /// wlan0) para o técnico escolher o IP do celular na LAN do roteador.
    /// </summary>
    public static IReadOnlyList<string> GetLocalIpv4Addresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        list.Add($"{ua.Address} ({ni.Name})");
                }
            }
        }
        catch { }
        return list.Distinct().ToList();
    }

    /// <summary>
    /// Conecta via Telnet ao roteador na LAN (híbrido: CLI pesada no Telnet, console
    /// serial preservado para reload/ROMMON/Break). Credenciais VTY (ex: EBT/PRO1AN).
    /// </summary>
    public async Task<bool> ConnectTelnetAsync(string routerIp, int port, string? username, string? password,
        Action<string>? log = null, CancellationToken ct = default)
    {
        await DisconnectTelnetAsync();

        var candidatePasswords = new List<string>();
        if (!string.IsNullOrWhiteSpace(password)) candidatePasswords.Add(password);
        if (!candidatePasswords.Contains("CQMR")) candidatePasswords.Add("CQMR");
        if (!candidatePasswords.Contains("PRO1ANPRO1AN")) candidatePasswords.Add("PRO1ANPRO1AN");
        if (!candidatePasswords.Contains("PRO1AN")) candidatePasswords.Add("PRO1AN");

        foreach (var pwd in candidatePasswords)
        {
            var transport = new TcpTelnetTransport(routerIp, port, connectTimeout: TimeSpan.FromSeconds(6));
            try
            {
                await transport.OpenAsync(ct);
                var session = new DeviceSession(transport, new SessionOptions
                {
                    PromptMatcher = RegexPromptMatcher.Universal(),
                    Username = username,
                    Password = pwd,
                    ConnectTimeout = TimeSpan.FromSeconds(8),
                    CommandTimeout = TimeSpan.FromSeconds(20),
                    LeaveOpen = true
                });

                await session.ConnectAsync(ct);
                _telnetTransport = transport;
                _telnetSession = session;
                log?.Invoke($"[✓] Telnet conectado em {routerIp}:{port} (user={username}). Prompt '{session.CurrentPrompt}'.");
                return true;
            }
            catch (Exception ex)
            {
                try { await transport.CloseAsync(); } catch { }
                try { await transport.DisposeAsync(); } catch { }
                log?.Invoke($"[!] Telnet {routerIp}:{port} (user={username}) com senha {(pwd.Length > 0 ? new string('*', pwd.Length) : "(vazia)")} recusado: {ex.Message}");
            }
        }

        log?.Invoke($"[!] Telnet {routerIp}:{port} não autenticou com nenhuma credencial conhecida — mantendo console serial.");
        return false;
    }

    /// <summary>
    /// Desconecta especificamente a porta USB serial sem limpar LastDetectionResult ou LoadedCircuit,
    /// permitindo a transição segura para o adaptador USB/Ethernet e sessão Telnet.
    /// </summary>
    public async Task DisconnectUsbOnlyAsync()
    {
        await PauseReadLoopAsync();

        if (_session != null)
        {
            try { await _session.DisposeAsync(); } catch { }
            _session = null;
        }

        if (_transport != null)
        {
            try
            {
                await _transport.CloseAsync();
                await _transport.DisposeAsync();
            }
            catch { }
            _transport = null;
        }

        OnConnectionStateChanged?.Invoke(false);
    }

    /// <summary>
    /// Conecta ao roteador via Telnet e define a sessão Telnet como transporte primário do aplicativo,
    /// reativando o ReadLoop para que a aba Terminal e os scripts operem sobre a interface de rede Ethernet.
    /// </summary>
    public async Task<bool> SwitchToTelnetSessionAsync(string routerIp, int port, string? username, string? password,
        Action<string>? log = null, CancellationToken ct = default)
    {
        var candidatePasswords = new List<string>();
        if (!string.IsNullOrWhiteSpace(password)) candidatePasswords.Add(password);
        if (!candidatePasswords.Contains("CQMR")) candidatePasswords.Add("CQMR");
        if (!candidatePasswords.Contains("PRO1ANPRO1AN")) candidatePasswords.Add("PRO1ANPRO1AN");
        if (!candidatePasswords.Contains("PRO1AN")) candidatePasswords.Add("PRO1AN");

        foreach (var pwd in candidatePasswords)
        {
            var transport = new TcpTelnetTransport(routerIp, port, connectTimeout: TimeSpan.FromSeconds(6));
            try
            {
                await transport.OpenAsync(ct);
                var session = new DeviceSession(transport, new SessionOptions
                {
                    PromptMatcher = RegexPromptMatcher.Universal(),
                    Username = username,
                    Password = pwd,
                    ConnectTimeout = TimeSpan.FromSeconds(8),
                    CommandTimeout = TimeSpan.FromSeconds(15),
                    LeaveOpen = true
                });

                await session.ConnectAsync(ct);

                // Somente após o Telnet autenticar com sucesso desconectamos o serial e ativamos o Telnet como sessão principal
                await DisconnectUsbOnlyAsync();
                await DisconnectTelnetAsync();

                _transport = transport;
                _session = session;
                _telnetTransport = transport;
                _telnetSession = session;

                OnConnectionStateChanged?.Invoke(true);
                ResumeReadLoop();

                log?.Invoke($"[✓] Sessão Telnet conectada em {routerIp}:{port} (user={username}). Terminal operacional via Ethernet.");
                return true;
            }
            catch (Exception ex)
            {
                try { await transport.CloseAsync(); } catch { }
                try { await transport.DisposeAsync(); } catch { }
                log?.Invoke($"[!] Tentativa Telnet com senha {(pwd.Length > 0 ? new string('*', pwd.Length) : "(vazia)")} em {routerIp}:{port} falhou: {ex.Message}");
            }
        }

        return false;
    }

    private void ThrowIfPasswordLocked()
    {
        if (LastDetectionResult?.OperatingState == DeviceOperatingState.PasswordProtected)
            throw new DeviceSessionException(
                "Equipamento BLOQUEADO por senha — use a aba Zerar Configuração antes do firmware.");
    }

    /// <summary>
    /// Upgrade de Comware via FTP do celular (HPE com SO em execução; BootWare segue
    /// por notebook/USB). Sobe o FTP embarcado (:2121), baixa via cliente ftp interativo,
    /// aplica boot-loader + save + reboot com verificação de retorno.
    /// </summary>
    public async Task<bool> UpgradeFirmwareHpeFtpAsync(
        string firmwareFilePath,
        string phoneIp,
        string ftpUser,
        string ftpPass,
        Func<string, Task> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(firmwareFilePath) || !File.Exists(firmwareFilePath))
            throw new FileNotFoundException($"Firmware não encontrado: {firmwareFilePath}");
        ThrowIfPasswordLocked();

        await PauseReadLoopAsync();
        try
        {
            _session = await EnsureConsoleSessionAsync(progress, ct);

            var dir = Path.GetDirectoryName(firmwareFilePath) ?? FileSystem.CacheDirectory;
            await using var ftp = new EmbeddedFtpServer(dir, phoneIp.Trim(), 2121, ftpUser, ftpPass);
            ftp.Start();
            AttachFtpProgress(ftp, progress);
            await progress($"[*] Servidor FTP no ar: {phoneIp.Trim()}:{ftp.ActualPort} (user '{ftpUser}')");

            try
            {
                var size = new FileInfo(firmwareFilePath).Length;
                var upgrader = new HpeFtpUpgrader(progress);
                return await upgrader.UpgradeAsync(_session,
                    Path.GetFileName(firmwareFilePath), phoneIp.Trim(), ftp.ActualPort,
                    ftpUser, ftpPass, size, ct);
            }
            finally
            {
                await ftp.StopAsync();
            }
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Restore de firmware FortiOS via FTP do celular
    /// (<c>execute restore image ftp arquivo ip:porta user pass</c> — grava e reinicia
    /// imediatamente). BootLoader (TFTP :69) segue por notebook/USB.
    /// </summary>
    public async Task<bool> RestoreFirmwareFortiFtpAsync(
        string firmwareFilePath,
        string phoneIp,
        string? ftpUser,
        string? ftpPass,
        Func<string, Task> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(firmwareFilePath) || !File.Exists(firmwareFilePath))
            throw new FileNotFoundException($"Firmware não encontrado: {firmwareFilePath}");
        ThrowIfPasswordLocked();

        await PauseReadLoopAsync();
        try
        {
            _session = await EnsureConsoleSessionAsync(progress, ct);

            var dir = Path.GetDirectoryName(firmwareFilePath) ?? FileSystem.CacheDirectory;
            await using var ftp = new EmbeddedFtpServer(dir, phoneIp.Trim(), 2121, ftpUser, ftpPass);
            ftp.Start();
            AttachFtpProgress(ftp, progress);
            await progress($"[*] Servidor FTP no ar: {phoneIp.Trim()}:{ftp.ActualPort}");

            try
            {
                var upgrader = new FortiGateFirmwareUpgrader(progress);
                return await upgrader.UpgradeAsync(_session,
                    Path.GetFileName(firmwareFilePath), phoneIp.Trim(), ftp.ActualPort,
                    ftpUser, ftpPass, ct);
            }
            finally
            {
                await ftp.StopAsync();
            }
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    private void AttachFtpProgress(EmbeddedFtpServer ftp, Func<string, Task> progress)
    {
        var lastPct = -1;
        ftp.TransferProgress += (_, sent, total) =>
        {
            var pct = total > 0 ? (int)(sent * 100 / total) : 0;
            if (pct != lastPct && (pct % 5 == 0 || pct == 100))
            {
                lastPct = pct;
                ReportFirmwareProgress(pct, "Transferindo via FTP...", $"{sent / 1048576.0:F1} / {total / 1048576.0:F1} MB", sent, total);
                progress($"[*] FTP: {sent}/{total} bytes ({pct}%)...").GetAwaiter().GetResult();
            }
        };
    }

    public async Task DisconnectTelnetAsync()    {
        if (_telnetSession != null)
        {
            try { await _telnetSession.DisposeAsync(); } catch { }
            _telnetSession = null;
        }
        if (_telnetTransport != null)
        {
            try
            {
                await _telnetTransport.CloseAsync();
                await _telnetTransport.DisposeAsync();
            }
            catch { }
            _telnetTransport = null;
        }
    }

    /// <summary>
    /// Upgrade de IOS via HTTP do celular (híbrido Telnet+console): sobe o servidor HTTP
    /// embarcado no firmware, tenta CLI via Telnet (fallback console), executa a cópia
    /// http:// + boot system + reload com verificação pós-boot. Console segue necessário
    /// para monitorar o reload. ROMMON (flash vazia) retorna orientação p/ notebook.
    /// </summary>
    public async Task<bool> UpgradeFirmwareCiscoHttpAsync(
        string firmwareFilePath,
        string phoneIp,
        string routerIp,
        string? telnetUser,
        string? telnetPass,
        string? routerTempIp,
        string? routerTempMask,
        string? lanInterface,
        string? expectedMd5,
        Func<string, Task> progress,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(firmwareFilePath) || !File.Exists(firmwareFilePath))
            throw new FileNotFoundException($"Firmware não encontrado: {firmwareFilePath}");
        if (string.IsNullOrWhiteSpace(phoneIp))
            throw new ArgumentException("IP do celular na LAN inválido.", nameof(phoneIp));

        ThrowIfPasswordLocked();

        await PauseReadLoopAsync();
        try
        {
            _session = await EnsureConsoleSessionAsync(progress, ct);
            await EnsureNoSetupDialogAsync(progress, ct);

            // Servidor HTTP do celular (porta alta — sem root)
            await using var http = new EmbeddedHttpFileServer(firmwareFilePath);
            http.Start();
            var lastPct = -1;
            http.TransferProgress += (_, sent, total) =>
            {
                var pct = total > 0 ? (int)(sent * 100 / total) : 0;
                if (pct != lastPct && (pct % 5 == 0 || pct == 100))
                {
                    lastPct = pct;
                    ReportFirmwareProgress(pct, "Transferindo via HTTP...", $"{sent / 1048576.0:F1} / {total / 1048576.0:F1} MB", sent, total);
                    progress($"[*] HTTP: {sent}/{total} bytes ({pct}%)...").GetAwaiter().GetResult();
                }
            };
            await progress($"[*] Servidor HTTP no ar: http://{phoneIp}:{http.ActualPort}/{Path.GetFileName(firmwareFilePath)}");

            // CLI híbrida: prefere Telnet, cai para console
            DeviceSession cli = _session;
            if (!string.IsNullOrWhiteSpace(routerIp))
            {
                var telnetOk = await ConnectTelnetAsync(routerIp.Trim(), 23, telnetUser, telnetPass,
                    msg => progress(msg).GetAwaiter().GetResult(), ct);
                if (telnetOk && _telnetSession != null)
                    cli = _telnetSession;
            }

            try
            {
                var size = new FileInfo(firmwareFilePath).Length;
                var upgrader = new CiscoHttpUpgrader(progress);
                return await upgrader.UpgradeAsync(cli,
                    Path.GetFileName(firmwareFilePath), phoneIp.Trim(), http.ActualPort,
                    size, routerTempIp, routerTempMask, lanInterface, expectedMd5, ct);
            }
            finally
            {
                await DisconnectTelnetAsync();
            }
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Audita a conformidade de firmware com a porta serial isolada (pausa o loop de background
    /// para garantir leitura CLI determinística e sem perda de pacotes para o terminal).
    /// </summary>
    public async Task<NetworkDevice.Core.Firmware.RouterFirmwareStatus> AuditFirmwareComplianceAsync(
        DeviceSeries series,
        NetworkDevice.Core.Firmware.RemoteFirmwareInfo? officialRemote,
        Func<string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        var log = progress ?? (_ => Task.CompletedTask);

        await PauseReadLoopAsync();
        try
        {
            _session = await EnsureConsoleSessionAsync(log, ct);
            var updater = new NetworkDevice.Core.Firmware.RouterDirectFirmwareUpdater(log);
            return await updater.AuditComplianceAsync(_session, series, officialRemote, ct);
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Diagnostica a porta WAN e conectividade com a internet diretamente pelo roteador,
    /// com a porta serial isolada do loop de leitura de terminal.
    /// </summary>
    public async Task<NetworkDevice.Core.Firmware.WanDiagnosticsResult> CheckWanAndInternetAsync(
        DeviceSeries series,
        Func<string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        var log = progress ?? (_ => Task.CompletedTask);

        await PauseReadLoopAsync();
        try
        {
            _session = await EnsureConsoleSessionAsync(log, ct);
            var updater = new NetworkDevice.Core.Firmware.RouterDirectFirmwareUpdater(log);
            return await updater.CheckWanAndInternetAsync(_session, series, ct);
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Avalia o status de higienização do equipamento (limpo em padrão de fábrica vs residual).
    /// </summary>
    public async Task<DeviceSanitizationStatus> DetectSanitizationAsync(CancellationToken ct = default)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        await PauseReadLoopAsync();
        try
        {
            _session = await EnsureConsoleSessionAsync(_ => Task.CompletedTask, ct);
            var detected = LastDetectionResult;
            if (detected?.Manufacturer == DeviceManufacturer.Fortinet)
                return await FortiOsSaipConfigurator.DetectSanitizationStatusAsync(_session, ct);
            if (detected?.Manufacturer == DeviceManufacturer.Hpe)
                return await HpeSaipConfigurator.DetectSanitizationStatusAsync(_session, ct);
            return await CiscoSaipConfigurator.DetectSanitizationStatusAsync(_session, ct);
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Zera as configurações antigas/senhas residuais e reinicia o equipamento em um único reload.
    /// </summary>
    public async Task<bool> EraseConfigurationAndReloadAsync(
        DeviceManufacturer manufacturer,
        DeviceSeries series,
        Func<string, Task> progress,
        CancellationToken ct)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        await PauseReadLoopAsync();
        try
        {
            _session = await EnsureConsoleSessionAsync(progress, ct);

            if (manufacturer == DeviceManufacturer.Fortinet)
            {
                await progress("[*] Enviando 'execute factoryreset' ao FortiGate...");
                var conds = new StopCondition[]
                {
                    new StopCondition.Contains("FortiResetConfirm", "y/n"),
                    new StopCondition.Prompt()
                };
                var resetRes = await _session.SendExpectAsync("execute factoryreset", conds, TimeSpan.FromSeconds(15), ct);
                if (resetRes.Output.Contains("y/n", StringComparison.OrdinalIgnoreCase))
                {
                    await _session.WriteLineAsync("y", ct);
                    await progress("[OK] Confirmação 'y' enviada. FortiGate reiniciando de fábrica (aprox. 45s)...");
                    await Task.Delay(45000, ct);
                    return true;
                }
                return false;
            }
            else if (manufacturer == DeviceManufacturer.Hpe)
            {
                await progress("[*] Apagando configurações do HPE (reset saved-configuration)...");
                await _session.WriteLineAsync("reset saved-configuration", ct);
                await Task.Delay(500, ct);
                await _session.WriteLineAsync("y", ct);
                await Task.Delay(1000, ct);

                await progress("[*] Reiniciando roteador HPE Comware (reboot)...");
                await _session.WriteLineAsync("reboot", ct);
                await Task.Delay(500, ct);
                // Confirma 'n' para não salvar a config atual
                await _session.WriteLineAsync("n", ct);
                await Task.Delay(500, ct);
                // Confirma 'y' para prosseguir com o reboot
                await _session.WriteLineAsync("y", ct);

                await progress("[*] Aguardando boot do Comware (até 5 min)...");
                var deadline = DateTime.UtcNow.AddMinutes(5);
                while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                {
                    try
                    {
                        var res = await _session.WaitForAsync(
                            new StopCondition[]
                            {
                                new StopCondition.Prompt(),
                                new StopCondition.LineRegex("hpe", new System.Text.RegularExpressions.Regex(@"^[<\[][A-Za-z0-9_\-\.]+[>\]]\s*$")),
                            },
                            TimeSpan.FromSeconds(15), ct);
                        await progress("[OK] HPE Comware reinicializado em padrão de fábrica!");
                        return true;
                    }
                    catch (SessionTimeoutException)
                    {
                        await progress("[*] Aguardando boot do Comware...");
                    }
                }
                return false;
            }
            else
            {
                // Cisco IOS
                await progress("[*] Apagando configurações da NVRAM (write erase)...");
                await _session.WriteLineAsync("write erase", ct);
                await Task.Delay(500, ct);
                await _session.WriteLineAsync(string.Empty, ct); // Confirma [confirm] com Enter
                await Task.Delay(1000, ct);

                await progress("[*] Reiniciando roteador Cisco (reload)...");
                await _session.WriteLineAsync("reload", ct);
                try
                {
                    var reloadRes = await _session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.Contains("Proceed with reload? [confirm]", "Proceed with reload? [confirm]"),
                            new StopCondition.Contains("[confirm]", "[confirm]"),
                            new StopCondition.Contains("System configuration has been modified", "System configuration has been modified"),
                            new StopCondition.Prompt()
                        },
                        TimeSpan.FromSeconds(15), ct);

                    if (reloadRes.Output.Contains("System configuration has been modified", StringComparison.OrdinalIgnoreCase))
                    {
                        await _session.WriteLineAsync("no", ct); // Não salva running-config sobre a NVRAM apagada!
                        await Task.Delay(500, ct);
                        try
                        {
                            await _session.WaitForAsync(
                                new StopCondition[] { new StopCondition.Contains("[confirm]", "[confirm]") },
                                TimeSpan.FromSeconds(10), ct);
                        }
                        catch { }
                    }
                    await _session.WriteLineAsync(string.Empty, ct); // Confirma [confirm] com Enter
                }
                catch { }

                await progress("[*] Aguardando boot do Cisco IOS (até 6 min)...");
                await EnsureNoSetupDialogAsync(progress, ct);
                await progress("[OK] Roteador Cisco reinicializado com NVRAM limpa!");
                return true;
            }
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    /// <summary>
    /// Consulta as interfaces físicas e lógicas do roteador e valida os IPs atribuídos e links UP/DOWN.
    /// </summary>
    public async Task<InterfacesVerificationResult> VerifyInterfacesStatusAsync(
        DeviceSeries series,
        string? expectedWanIp,
        string? expectedLanIp,
        Func<string, Task> progress,
        CancellationToken ct)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        await PauseReadLoopAsync();
        try
        {
            _session = await EnsureConsoleSessionAsync(progress, ct);
            var (wanIface, lanIface) = InterfaceStatusInspector.ResolveExpectedInterfaces(series);
            var detected = LastDetectionResult;

            if (detected?.Manufacturer == DeviceManufacturer.Fortinet)
            {
                var phys = await _session.SendCommandAsync("get system interface physical", TimeSpan.FromSeconds(10), ct);
                var ipOut = await _session.SendCommandAsync("diagnose ip address list", TimeSpan.FromSeconds(10), ct);
                return InterfaceStatusInspector.ParseFortinetInterfaces(phys, ipOut, wanIface, lanIface, expectedWanIp, expectedLanIp);
            }
            else if (detected?.Manufacturer == DeviceManufacturer.Hpe)
            {
                try { await _session.SendCommandAsync("screen-length disable", TimeSpan.FromSeconds(5), ct); } catch { }
                var brief = await _session.SendCommandAsync("display ip interface brief", TimeSpan.FromSeconds(15), ct);
                return InterfaceStatusInspector.ParseHpeBrief(brief, wanIface, lanIface, expectedWanIp, expectedLanIp);
            }
            else
            {
                // Cisco IOS
                try { await _session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), ct); } catch { }
                var brief = await _session.SendCommandAsync("show ip interface brief", TimeSpan.FromSeconds(15), ct);
                return InterfaceStatusInspector.ParseCiscoBrief(brief, wanIface, lanIface, expectedWanIp, expectedLanIp);
            }
        }
        finally
        {
            ResumeReadLoop();
        }
    }

    public async Task DisconnectAsync()
    {
        await PauseReadLoopAsync();
        await DisconnectTelnetAsync();

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

        LastDetectionResult = null;
        LastAppliedConfig = null;
        LastResolvedUser = null;
        LastResolvedPassword = null;
        LastResolvedEnableSecret = null;
        OnConnectionStateChanged?.Invoke(false);
    }
}
