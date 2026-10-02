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
    public string? LastDetectedSerial { get; set; }
    public SaipCircuitData? LoadedCircuit { get; set; }

    /// <summary>
    /// Paridade com o checkbox secreto "NAT LAB" do Windows (default desligado).
    /// </summary>
    public bool IncluirNatLab { get; set; }

    /// <summary>
    /// Running-config capturada após o último provisionamento (paridade com o relatório TXT do Windows).
    /// </summary>
    public string? LastAppliedConfig { get; private set; }

    public event Action<string>? OnTerminalDataReceived;
    public event Action<bool>? OnConnectionStateChanged;
    public event Action<DeviceDetectionResult>? OnDeviceIdentified;
    public event Action<string>? OnProbeProgress;

    private DeviceConnectionManager() { }

    /// <summary>
    /// Identifica se o dispositivo USB conectado é uma placa de rede Ethernet (ex: TP-Link, Realtek) ou Hub USB,
    /// para não confundi-lo com o console serial quando conectados simultaneamente via HUB USB-C.
    /// </summary>
    public static bool IsNetworkOrHubDevice(UsbDevice d)
    {
        if (d == null) return false;

        // 1. USB Hub (Classe 0x09)
        if ((int)d.DeviceClass == 9) return true;

        // 2. VIDs conhecidos de adaptadores Ethernet / Rede USB
        // 0x2357: TP-Link (UE300, UE200, UE300C, etc.)
        // 0x0BDA: Realtek USB Ethernet (RTL8152, RTL8153, RTL8156)
        // 0x0B95: ASIX USB Ethernet (AX88179, AX88772)
        // 0x0424: Microchip / SMSC LAN
        // 0x05AC: Apple USB Ethernet
        // 0x050D: Belkin USB Ethernet
        if (d.VendorId == 0x2357 ||
            (d.VendorId == 0x0BDA && (d.ProductId >= 0x8150 && d.ProductId <= 0x8159)) ||
            d.VendorId == 0x0B95 ||
            d.VendorId == 0x0424 ||
            (d.VendorId == 0x05AC && d.ProductId == 0x1402) ||
            d.VendorId == 0x050D)
        {
            return true;
        }

        // 3. Descrição ou nomes de produto / fabricante indicando Ethernet / Rede
        var m = (d.ManufacturerName ?? "").ToLowerInvariant();
        var p = (d.ProductName ?? "").ToLowerInvariant();
        if (m.Contains("tp-link") || m.Contains("realtek") || m.Contains("asix") ||
            p.Contains("ethernet") || p.Contains("gigabit") || p.Contains("lan") ||
            p.Contains("ue300") || p.Contains("ue200") || p.Contains("rtl815") ||
            p.Contains("ax8817") || p.Contains("network"))
        {
            return true;
        }

        // 4. Subclasses CDC Ethernet (Subclasse 0x06 = Ethernet, 0x0D = NCM, 0x0F = MBIM) ou Mass Storage
        for (int i = 0; i < d.InterfaceCount; i++)
        {
            var iface = d.GetInterface(i);
            if (iface != null)
            {
                var cls = (int)iface.InterfaceClass;
                var sub = (int)iface.InterfaceSubclass;
                if (cls == 2 && (sub == 6 || sub == 13 || sub == 14 || sub == 15))
                    return true;
                if (cls == 9) // Hub
                    return true;
                if (cls == 8) // Mass Storage
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
        // 0x0403: FTDI (FT232R, FT2232, etc.)
        // 0x10C4: Silicon Labs (CP2102, CP2104, etc.)
        // 0x1A86, 0x4348: QinHeng / Winchiphead (CH340/CH341/CH9102)
        // 0x067B: Prolific (PL2303)
        // 0x0557 (PID 0x2008): ATEN / Cisco USB Console
        // 0x05A6, 0x145F: Cisco Systems Console
        // 0x0483: STMicroelectronics Virtual COM
        // 0x2341, 0x2A03: Arduino CDC
        if (d.VendorId == 0x0403 ||
            d.VendorId == 0x10C4 ||
            d.VendorId == 0x1A86 || d.VendorId == 0x4348 ||
            d.VendorId == 0x067B ||
            (d.VendorId == 0x0557 && d.ProductId == 0x2008) ||
            d.VendorId == 0x05A6 || d.VendorId == 0x145F ||
            d.VendorId == 0x0483 ||
            d.VendorId == 0x2341 || d.VendorId == 0x2A03)
        {
            return true;
        }

        // 3. Nomes contendo referências explícitas a Serial / Console / UART
        var m = (d.ManufacturerName ?? "").ToLowerInvariant();
        var p = (d.ProductName ?? "").ToLowerInvariant();
        if (p.Contains("serial") || p.Contains("uart") || p.Contains("rs232") ||
            p.Contains("console") || p.Contains("cp210") || p.Contains("ch340") ||
            p.Contains("pl2303") || p.Contains("ft232") || p.Contains("ftdi") ||
            m.Contains("ftdi") || m.Contains("prolific") || m.Contains("silicon labs"))
        {
            return true;
        }

        return false;
    }

    public IReadOnlyList<UsbDevice> ScanUsbDevices()
    {
        try
        {
            var devices = UsbManagerHelper.GetAllUsbDevices()?.ToList() ?? new List<UsbDevice>();
            
            // Prioriza exclusivamente adaptadores seriais conhecidos (ignora placas USB/ETH TP-Link, hubs, etc.)
            var serialDevices = devices.Where(IsSupportedSerialDevice).ToList();
            if (serialDevices.Count > 0)
            {
                return serialDevices;
            }

            // Fallback: se nenhum serial conhecido foi detectado, filtra ao menos quem NÃO é rede nem hub
            var nonNetworkDevices = devices.Where(d => !IsNetworkOrHubDevice(d)).ToList();
            return nonNetworkDevices.Count > 0 ? nonNetworkDevices : devices;
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

    public UsbDevice ResolveLiveDevice(UsbDevice? preferredDevice = null)
    {
        var devices = ScanUsbDevices();
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("Nenhum adaptador serial USB encontrado na porta OTG. Verifique a conexão do cabo.");
        }

        if (preferredDevice != null)
        {
            var exactMatch = devices.FirstOrDefault(d => d.DeviceId == preferredDevice.DeviceId);
            if (exactMatch != null)
                return exactMatch;

            var vidPidMatch = devices.FirstOrDefault(d =>
                d.VendorId == preferredDevice.VendorId && d.ProductId == preferredDevice.ProductId);
            if (vidPidMatch != null)
                return vidPidMatch;

            var nameMatch = devices.FirstOrDefault(d => d.DeviceName == preferredDevice.DeviceName);
            if (nameMatch != null)
                return nameMatch;
        }

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

            // Se identificou Cisco no Padrão 1 (mesmo com Series == Unknown, como em "Router>"), enriquece o modelo via show version
            if (result.Manufacturer == DeviceManufacturer.Cisco)
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

                    // Verifica se já temos informação suficiente para identificar o modelo
                    if (DeviceDetector.Cisco841ModelRegex.IsMatch(str) ||
                        DeviceDetector.Cisco900ModelRegex.IsMatch(str) ||
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

            var fullText = initialResult.RawPrompt + "\n" + sb.ToString();
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
                    initialResult.OperatingState,
                    initialResult.RecommendedWorkflow,
                    initialResult.AccessState,
                    initialResult.BootState,
                    initialResult.FirmwareState,
                    initialResult.RawPrompt ?? string.Empty,
                    $"Cisco {classified.Series} identificado com sucesso via show version.");
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

            if (detected.Manufacturer == DeviceManufacturer.Cisco)
            {
                var configurator = new CiscoSaipConfigurator(progressCallback) { IncluirNatLab = IncluirNatLab };
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
                var configurator = new CiscoSaipConfigurator(progressCallback) { IncluirNatLab = IncluirNatLab };
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
        DeviceSeries.Isr921 => ("GigabitEthernet 4", "GigabitEthernet 5"),
        DeviceSeries.Isr841 => ("GigabitEthernet0/4", "GigabitEthernet0/5"),
        _ => (null, null),
    };

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

            var options = new SessionOptions
            {
                PromptMatcher = RegexPromptMatcher.Universal(),
                Username = username,
                Password = password,
                ConnectTimeout = TimeSpan.FromSeconds(30),
                CommandTimeout = TimeSpan.FromSeconds(20),
                LeaveOpen = true
            };
            var session = new DeviceSession(_transport, options);
            try
            {
                await session.ConnectAsync(ct);
            }
            catch (LoginException ex)
            {
                log?.Invoke($"[!] Login recusado: {ex.Message}");
                await session.DisposeAsync();
                return false;
            }

            var prompt = (session.CurrentPrompt ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(prompt) || prompt.EndsWith(":"))
            {
                log?.Invoke("[!] Equipamento ainda pede credenciais após login.");
                await session.DisposeAsync();
                return false;
            }

            _session = session;
            LastDetectionResult = null; // força re-identificação c/ prompt aberto no provisionamento
            log?.Invoke($"[✓] Login direto aceito (prompt '{prompt}').");
            return true;
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
    /// Garante sessão de console conectada: cria nova (com prelude Ctrl+C/Enter, que
    /// atravessa setup dialogs via ConnectAsync) ou reaproveita a existente sondando
    /// diálogo preso. Retorna a sessão conectada.
    /// </summary>
    private async Task<DeviceSession> EnsureConsoleSessionAsync(Func<string, Task> progress, CancellationToken ct)
    {
        if (_transport == null || !_transport.IsOpen)
            throw new InvalidOperationException("Console serial USB não está conectado.");

        if (_session == null || !_session.IsConnected)
        {
            await progress("[*] Estabelecendo sessão interativa no console do roteador...");
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
                ConnectTimeout = TimeSpan.FromSeconds(60),
                CommandTimeout = TimeSpan.FromSeconds(60),
                LeaveOpen = true
            });
            await _session.ConnectAsync(ct);
            await progress("[✓] Sessão interativa conectada com sucesso!");
        }
        else
        {
            // Sessão reaproveitada: sonda setup dialog preso e reconecta se necessário
            // (paridade: o Windows sempre abre sessão nova na Fase C).
            await EnsureNoSetupDialogAsync(progress, ct);
        }

        return _session!;
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

        var transport = new TcpTelnetTransport(routerIp, port);
        await transport.OpenAsync(ct);
        var session = new DeviceSession(transport, new SessionOptions
        {
            PromptMatcher = RegexPromptMatcher.Universal(),
            Username = username,
            Password = password,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            CommandTimeout = TimeSpan.FromSeconds(30),
            LeaveOpen = true
        });

        try
        {
            await session.ConnectAsync(ct);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] Telnet {routerIp}:{port} recusado ({ex.Message}) — usando console serial.");
            await session.DisposeAsync();
            await transport.DisposeAsync();
            return false;
        }

        _telnetTransport = transport;
        _telnetSession = session;
        log?.Invoke($"[✓] Telnet conectado em {routerIp}:{port} (prompt '{session.CurrentPrompt}').");
        return true;
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
        var transport = new TcpTelnetTransport(routerIp, port, connectTimeout: TimeSpan.FromSeconds(6));
        try
        {
            await transport.OpenAsync(ct);
            var session = new DeviceSession(transport, new SessionOptions
            {
                PromptMatcher = RegexPromptMatcher.Universal(),
                Username = username,
                Password = password,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                CommandTimeout = TimeSpan.FromSeconds(20),
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

            log?.Invoke($"[✓] Sessão Telnet conectada em {routerIp}:{port} (prompt '{session.CurrentPrompt}'). Terminal operacional via Ethernet.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[!] Telnet {routerIp}:{port} inacessível ({ex.Message}).");
            try { await transport.CloseAsync(); } catch { }
            try { await transport.DisposeAsync(); } catch { }
            return false;
        }
    }

    private void ThrowIfPasswordLocked()
    {
        if (LastDetectionResult?.OperatingState == DeviceOperatingState.PasswordProtected)
            throw new DeviceSessionException(
                "Equipamento BLOQUEADO por senha — use a aba Quebra de Senha antes do firmware.");
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

    private static void AttachFtpProgress(EmbeddedFtpServer ftp, Func<string, Task> progress)
    {
        var lastPct = -1;
        ftp.TransferProgress += (_, sent, total) =>
        {
            var pct = total > 0 ? (int)(sent * 100 / total) : 0;
            if (pct != lastPct && (pct % 5 == 0 || pct == 100))
            {
                lastPct = pct;
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
        OnConnectionStateChanged?.Invoke(false);
    }
}
