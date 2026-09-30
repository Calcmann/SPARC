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

    public UsbDevice ResolveLiveDevice(UsbDevice? preferredDevice = null)
    {
        var devices = UsbManagerHelper.GetAllUsbDevices()?.ToList() ?? new List<UsbDevice>();
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

            // Se identificou com sucesso no Padrão 1
            if (result.Manufacturer != DeviceManufacturer.Unknown)
            {
                result = await EnrichCiscoModelIfPossibleAsync(result, ct);
                LastDetectionResult = result;
                OnDeviceIdentified?.Invoke(result);
                return result;
            }

            // --- ETAPA 2: Padrão 2 (115200 bps - Fortinet FortiGate) ---
            OnProbeProgress?.Invoke("[*] Sem resposta a 9600 bps. Testando automaticamente Padrão 2: 115200 bps (Fortinet FortiGate)...");
            await ChangeBaudRateAsync(115200);

            await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("\r\n\r\n"), ct);
            await Task.Delay(300, ct);

            var resultFortinet = await _detector.DetectAsync(_transport, ct);
            if (resultFortinet.Manufacturer != DeviceManufacturer.Unknown)
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
    /// Responde ao setup dialog em nível de transporte: 'no' -&gt; ENTER (terminate
    /// autoinstall [yes]) -&gt; ENTER (Press RETURN to get started) e drena os ecos.
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
            // Consulta versão para extrair o modelo exato do Cisco (ex: Cisco 1905, 1921, 921)
            await _transport.WriteAsync(System.Text.Encoding.UTF8.GetBytes("terminal length 0\r\nshow version\r\n"), ct);
            var buffer = new byte[2048];
            var sb = new System.Text.StringBuilder();
            var deadline = DateTime.UtcNow.AddSeconds(2.0);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var r = await _transport.ReadAsync(buffer, ct);
                if (r > 0)
                {
                    sb.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, r));
                    var str = sb.ToString();
                    if (str.Contains("bytes of memory", StringComparison.OrdinalIgnoreCase) ||
                        str.Contains("Configuration register", StringComparison.OrdinalIgnoreCase) ||
                        str.EndsWith(">") || str.EndsWith("#"))
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
            return _detector.ClassifyPrompt(fullText);
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

            // Garante que a sessão interativa esteja conectada ao roteador antes do provisionamento
            // (sessão nova atravessa setup dialogs; reaproveitada é sondada e reconectada se presa)
            _session = await EnsureConsoleSessionAsync(progressCallback, ct);

            // Seleção de interfaces WAN/LAN por modelo — paridade fiel com o Windows
            // (MainWindow.ExecutarAplicarSaipAsync): o 1905 (Series1900) usa GE0/0 + GE0/1,
            // o 921 usa 'GigabitEthernet 4/5' e o 841 usa GE0/4 + GE0/5. Sem isso o Android
            // caía no default GE4/GE5 e o 1905 retornava '% Invalid input' em tudo.
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

            // Validação do cabo na porta LAN (paridade com o Windows). No mobile não há
            // modal: sem requestOperatorAction o Enforce apenas registra o progresso e
            // aguarda o link (limitado a 60s para caber no timeout da tela).
            try
            {
                using var lanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                lanCts.CancelAfter(TimeSpan.FromSeconds(60));
                var lanTarget = detected.Manufacturer == DeviceManufacturer.Fortinet ? "lan"
                    : detected.Manufacturer == DeviceManufacturer.Hpe ? "GigabitEthernet0/1"
                    : lanIface ?? "GigabitEthernet0/1";
                if (detected.Manufacturer == DeviceManufacturer.Fortinet)
                    await FortiOsSaipConfigurator.EnforceLanPortConnectedAsync(_session, lanTarget, null, progressCallback, lanCts.Token);
                else if (detected.Manufacturer == DeviceManufacturer.Hpe)
                    await HpeSaipConfigurator.EnforceLanPortConnectedAsync(_session, lanTarget, null, progressCallback, lanCts.Token);
                else
                    await CiscoIOSAdapter.EnforceLanPortConnectedAsync(_session, lanTarget, null, progressCallback, lanCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await progressCallback("[AVISO] Validação da porta LAN atingiu 60s sem link — prosseguindo; confira o cabo na porta LAN.");
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
                    await progress($"[i] Serial capturado: {recovery.DetectedSerial}");
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
