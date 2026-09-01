using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Recovery;

public delegate Task<bool> BootWareTftpDownloader(
    DeviceSession session,
    string ethernetOption,
    string firmwareFilePath,
    string hostIp,
    string routerIp,
    string subnetMask,
    CancellationToken ct);

public sealed class HpeComwareRecovery
{
    private static readonly Regex BootWareMenuPrompt = new(
        @"(?i)(?:BOOT\s*MENU|<(?:EXTENDED-)?BOOTWARE\s*MENU>|<MAIN\s*MENU>|<BASIC\s*BOOT\s*MENU>|<ENTER\s*ETHERNET\s*SUBMENU>|<ETHERNET\s*SUBMENU>|Enter\s+your\s+choice|choice\s*\(\s*0\s*-\s*[0-9]\s*\)|choice\s*:|Select\s+your\s+choice|Enter\s+choice|BootWare\s+Operation\s+Menu|Server\s+IP\s*(?:Address)?\s*[:?]|Local\s+IP\s*(?:Address)?\s*[:?]|Subnet\s+Mask\s*[:?]|Gateway\s+IP\s*(?:Address)?\s*[:?]|Protocol\s*\(|File\s+Name\s*[:?]|Ensure\s+The\s+Parameter)",
        RegexOptions.Compiled);

    private static readonly Regex FileControlMenuPrompt = new(
        @"(?i)(?:FILE\s*CONTROL|choice\s*\(\s*0\s*-\s*[4-9]\s*\)|choice\s*:)",
        RegexOptions.Compiled);

    private static readonly Regex EthernetMenuPrompt = new(
        @"(?i)(?:ETHERNET\s*SUBMENU|choice\s*\(\s*0\s*-\s*[3-9]\s*\)|choice\s*:)",
        RegexOptions.Compiled);

    private static readonly Regex BootWareCountdownPrompt = new(
        @"(?i)(?:Press\s+Ctrl\+[BD]\s+to\s+enter|Press\s+Ctrl\+B\s+to\s+access|Press\s+Ctrl\+B\s+to\s+stop|Press\s+Ctrl\+B|BootWare\s+Validating|Booting\s+Normal|Starting\s+to\s+check\s+the\s+memory|Check\s+memory|SDRAM\s+Memory|Flash\s+Size)",
        RegexOptions.Compiled);

    private static readonly Regex BootWarePasswordPrompt = new(
        @"(?i)(?:(?:bootware|input|please\s+input)?\s*password\s*:)",
        RegexOptions.Compiled);

    private static readonly Regex OsLoginPrompt = new(
        @"(?i)(?:login\s*:|Username\s*:|User\s+Access\s+Verification|SOMENTE\s+USUARIOS|AVISO|CLARO\s*-\s*GRC)",
        RegexOptions.Compiled);

    private static readonly Regex SkipConfigMenuOptionRegex = new(
        @"(?i)[<|\[]?\s*(?<num>\d+)\s*[>|\]]?\s*Skip\s+Current\s+(?:System\s+)?Configuration",
        RegexOptions.Compiled);

    private static readonly Regex SkipAuthMenuOptionRegex = new(
        @"(?i)[<|\[]?\s*(?<num>\d+)\s*[>|\]]?\s*Skip\s+Authentication\s+for\s+Console\s+Login",
        RegexOptions.Compiled);

    private static readonly Regex BootSystemMenuOptionRegex = new(
        @"(?i)[<|\[]?\s*(?<num>\d+)\s*[>|\]]?\s*Boot\s+System",
        RegexOptions.Compiled);

    private static readonly Regex FileControlMenuOptionRegex = new(
        @"(?i)[<|\[]?\s*(?<num>\d+)\s*[>|\]]?\s*File\s+Control",
        RegexOptions.Compiled);

    private static readonly Regex EthernetMenuOptionRegex = new(
        @"(?i)[<|\[]?\s*(?<num>\d+)\s*[>|\]]?\s*Enter\s+Ethernet\s+SubMenu",
        RegexOptions.Compiled);

    private static readonly Regex ConfirmPrompt = new(
        @"(?i)(?:\[Y/N\]|\?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex ComwarePrompt = new(
        @"(?i)(?:^<[A-Za-z0-9_\-\.]+>|^\[[A-Za-z0-9_\-\.]+\])",
        RegexOptions.Compiled);

    private static readonly Regex PressReturnPrompt = new(
        @"(?i)(?:press\s+enter\s+to\s+continue|press\s+enter\s+to\s+get\s+started|press\s+return\s+to\s+get\s+started|Line\s+con0\s+is\s+available|Before\s+pressing\s+ENTER)",
        RegexOptions.Compiled);

    private static readonly Regex AutoConfigPrompt = new(
        @"(?i)(?:automatic\s+configuration|auto-configuration|press\s+ctrl[_\+]c|automatic\s+configuration\s+attempt|terminate\s+automatic\s+configuration|dhcp\s+client\s+on\s+vlan|not\s+ready\s+for\s+automatic\s+configuration|press\s+ctrl_c\s+or\s+ctrl_d)",
        RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;
    private readonly BootInterruptProfile _profile;

    public event Action<int, string, string>? ProgressUpdated;

    public HpeComwareRecovery(
        Func<string, Task>? progress = null,
        BootInterruptProfile? profile = null)
    {
        _progress = progress;
        _profile = profile ?? BootInterruptProfiles.HpeMsr;
    }

    /// <summary>
    /// Executa o procedimento automatizado de recuperação de senha, correção de boot e reset de fábrica no HPE MSR / Comware.
    /// </summary>
    public async Task<bool> RecoverAndResetAsync(
        DeviceSession session,
        Func<string, CancellationToken, Task>? instructOperator = null,
        string? firmwareFilePath = null,
        string? hostIpAddress = null,
        string? routerIpAddress = null,
        string? subnetMask = null,
        BootWareTftpDownloader? tftpDownloader = null,
        Func<CancellationToken, Task<string?>>? requestFirmwareFile = null,
        bool forceFirmwareRecovery = false,
        CancellationToken ct = default)
    {
        ProgressUpdated?.Invoke(6, "1/6 Zerar Configuração...", "Verificando se o equipamento já está acessível ou interceptando BootWare (Ctrl+B)...");
        await ProgressAsync("[*] Abrindo sessão serial e verificando estado atual do equipamento HPE...");
        await session.ConnectRawAsync(ct);

        // Pré-diagnóstico: verifica se o equipamento já está no prompt sem senha ou no BootWare
        var menuCaptured = false;
        var menuText = string.Empty;

        try
        {
            // Envia 'return' para sair de qualquer sub-menu (ex: [HPE-luser-manage-EBT]) antes de verificar o prompt
            await session.WriteLineAsync("return", ct);
            await Task.Delay(300, ct);

            var preCheck = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("prompt", ComwarePrompt),
                    new StopCondition.LineRegex("menu", BootWareMenuPrompt),
                    new StopCondition.LineRegex("password", BootWarePasswordPrompt),
                    new StopCondition.LineRegex("login", OsLoginPrompt)
                },
                TimeSpan.FromSeconds(3),
                ct);

            if (preCheck.Matched is StopCondition.LineRegex preMatch)
            {
                if (preMatch.Name == "prompt")
                {
                    await ProgressAsync("[OK] Equipamento HPE acessível sem senha (prompt aberto). Executando reset direto via CLI...");
                    await ExecuteDirectHpeCliResetAsync(session, ct);
                    ProgressUpdated?.Invoke(100, "1/6 Zerar Configuração Concluído", "HPE zerado diretamente via CLI com sucesso.");
                    return true;
                }
                else if (preMatch.Name == "menu")
                {
                    await ProgressAsync("[OK] Menu do BootWare já ativo no console — prosseguindo diretamente sem reinício...");
                    menuCaptured = true;
                    menuText = preCheck.Output;
                }
            }
        }
        catch (SessionTimeoutException)
        {
            // Equipamento iniciando ou bloqueado — prossegue para agendador de interrupção
        }

        if (!menuCaptured && instructOperator is not null)
        {
            await instructOperator(
                "Ligue ou reinicie o roteador HPE na tomada agora (desligue por 5 segundos se já estiver ligado). O software enviará pulsos contínuos de Ctrl+B para interceptar o BootWare.",
                ct);
        }

        // 1. Inicia o agendador contínuo de Ctrl+B
        using var schedulerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var scheduler = new BootInterruptScheduler(session.Transport, _profile);
        var schedulerTask = !menuCaptured ? Task.Run(() => scheduler.RunAsync(schedulerCts.Token), schedulerCts.Token) : Task.CompletedTask;

        var conditions = new StopCondition[]
        {
            new StopCondition.LineRegex("menu", BootWareMenuPrompt),
            new StopCondition.LineRegex("countdown", BootWareCountdownPrompt),
            new StopCondition.LineRegex("password", BootWarePasswordPrompt),
            new StopCondition.LineRegex("login", OsLoginPrompt),
            new StopCondition.LineRegex("prompt", ComwarePrompt)
        };

        var waitDeadline = DateTime.UtcNow.AddMinutes(3);
        var hasWarnedLogin = false;

        // 2. Loop de espera inteligente pelo BootWare Menu
        while (DateTime.UtcNow < waitDeadline && !ct.IsCancellationRequested && !menuCaptured)
        {
            DeviceSession.ExpectResult? waitResult = null;
            try
            {
                waitResult = await session.WaitForAsync(conditions, TimeSpan.FromSeconds(3), ct);
            }
            catch (SessionTimeoutException)
            {
                // Enquanto o equipamento valida RAM/Flash ("BootWare Validating..."), continua enviando pulsos de Ctrl+B
                await session.SendCtrlBAsync(ct);
                continue;
            }

            if (waitResult?.Matched is StopCondition.LineRegex match)
            {
                if (match.Name == "menu")
                {
                    menuCaptured = true;
                    menuText = waitResult.Output;
                    break;
                }
                else if (match.Name == "countdown")
                {
                    await ProgressAsync("[>] Inicialização/Contagem do BootWare detectada! Enviando rajadas de Ctrl+B...");
                    for (int i = 0; i < 5; i++)
                    {
                        await session.SendCtrlBAsync(ct);
                        await Task.Delay(50, ct);
                    }
                }
                else if (match.Name == "password")
                {
                    await ProgressAsync("[*] Solicitada senha do BootWare. Enviando Enter (padrão em branco)...");
                    await session.WriteLineAsync(string.Empty, ct);
                    await Task.Delay(400, ct);

                    try
                    {
                        var pwRes = await session.WaitForAsync(new StopCondition[] { new StopCondition.LineRegex("menu", BootWareMenuPrompt), new StopCondition.LineRegex("password", BootWarePasswordPrompt) }, TimeSpan.FromSeconds(3), ct);
                        if (pwRes.Matched is StopCondition.LineRegex mr && mr.Name == "menu")
                        {
                            menuCaptured = true;
                            menuText = pwRes.Output;
                            break;
                        }
                    }
                    catch (SessionTimeoutException) { /* continua para senhas padrão */ }

                    var fallbackPasswords = new[] { "admin", "Admin@h3c", "Admin@huawei", "h3c" };
                    foreach (var pw in fallbackPasswords)
                    {
                        try
                        {
                            await ProgressAsync($"[*] Tentando senha alternativa '{pw}'...");
                            await session.WriteLineAsync(pw, ct);
                            await Task.Delay(500, ct);
                            var check = await session.WaitForAsync(new StopCondition[] { new StopCondition.LineRegex("menu", BootWareMenuPrompt) }, TimeSpan.FromSeconds(3), ct);
                            if (check.Matched != null)
                            {
                                menuCaptured = true;
                                menuText = check.Output;
                                break;
                            }
                        }
                        catch (SessionTimeoutException) { /* continua para próxima */ }
                    }
                }
                else if (match.Name == "login" || match.Name == "prompt")
                {
                    if (!hasWarnedLogin)
                    {
                        hasWarnedLogin = true;
                        await ProgressAsync("\n[AVISO] O roteador inicializou no prompt de login do sistema operacional (login:). O BootWare não foi interceptado nesta tentativa.");
                        if (instructOperator is not null)
                        {
                            await instructOperator(
                                "O roteador já estava ligado ou o boot passou direto. POR FAVOR, DESLIGUE A FONTE DA TOMADA, AGUARDE 5 SEGUNDOS E LIGUE NOVAMENTE, CONFIRME CLICANDO EM OK. O software continuará aguardando e enviando Ctrl+B automaticamente.",
                                ct);
                        }
                    }
                }
            }
            else
            {
                await session.SendCtrlBAsync(ct);
            }
        }

        await schedulerCts.CancelAsync();
        try { await schedulerTask; } catch { /* ignore */ }

        if (!menuCaptured)
        {
            throw new DeviceSessionException("Tempo limite esgotado ao aguardar o Menu do BootWare. Certifique-se de reiniciar o roteador HPE na tomada para que o boot seja interceptado.");
        }

        ProgressUpdated?.Invoke(15, "1/6 Menu BootWare Detectado", "Roteador HPE detectado no menu de recuperação (BootWare).");
        await ProgressAsync("\n==================================================================================");
        await ProgressAsync("   📢 ROTEADOR HPE DETECTADO NO MENU DE RECUPERAÇÃO (BOOTWARE)");
        string restoreFactoryOption = "5";
        string skipConfigOption = "6";
        string skipAuthOption = "8";
        string bootOption = "1";
        string ethernetOption = "3";

        // Se for explicitamente solicitado RECOVERY DE FIRMWARE via BootWare:
        if (forceFirmwareRecovery && tftpDownloader != null && !string.IsNullOrEmpty(firmwareFilePath) && File.Exists(firmwareFilePath))
        {
            await ProgressAsync("==================================================================================");
            await ProgressAsync("  Diagnóstico: Roteador HPE em modo BootWare (Recuperação de Firmware Solicitada).");
            await ProgressAsync("  Processo   : Será realizado o DOWNLOAD e GRAVAÇÃO na Flash via TFTP pela GE0.");
            await ProgressAsync("==================================================================================\n");

            ProgressUpdated?.Invoke(25, "Recuperando Firmware...", $"Iniciando recuperação direta via TFTP ({Path.GetFileName(firmwareFilePath)})...");
            await ProgressAsync("\n==================================================================================");
            await ProgressAsync("   🚑 RECUPERAÇÃO DIRETA DE FIRMWARE VIA BOOTWARE ETHERNET TFTP");
            await ProgressAsync("==================================================================================");
            await ProgressAsync($"  Arquivo Firmware  : {Path.GetFileName(firmwareFilePath)}");
            await ProgressAsync($"  Servidor TFTP (PC): {hostIpAddress ?? "200.182.245.18"}");
            await ProgressAsync($"  Roteador HPE (GE0): {routerIpAddress ?? "200.182.245.17"}");
            await ProgressAsync($"  Máscara de Rede   : {subnetMask ?? "255.255.255.240"}");
            await ProgressAsync("==================================================================================\n");

            var tftpOk = await tftpDownloader(
                session,
                ethernetOption,
                firmwareFilePath,
                hostIpAddress ?? "200.182.245.18",
                routerIpAddress ?? "200.182.245.17",
                subnetMask ?? "255.255.255.240",
                ct);

            if (tftpOk)
            {
                await HpeBootWareStateMachine.EnsureExtendedBootWareAsync(session, _progress, ct);
                await ProgressAsync("[*] [4/4] Disparando inicialização do sistema (Opção 1 - Boot System)...");
                await HpeBootWareStateMachine.ExecuteOptionAsync(session, HpeMenuState.ExtendedBootWare, bootOption, "Boot System", _progress, ct);
            }
        }
        else
        {
            // PROCESSO DE ZERAMENTO / QUEBRA DE SENHA (NÃO BAIXA FIRMWARE)
            await ProgressAsync("==================================================================================");
            await ProgressAsync("  Diagnóstico: Zeramento de Fábrica / Quebra de Senha via BootWare.");
            await ProgressAsync("  Processo   : Restaurando padrões de fábrica e ignorando autenticação/configuração.");
            await ProgressAsync("==================================================================================\n");

            await HpeBootWareStateMachine.EnsureExtendedBootWareAsync(session, _progress, ct);

            // 1. Se o menu tiver opção 5 (Restore to Factory Default Configuration), executa para limpar flash
            if (menuText.Contains("Restore to Factory Default", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync("[*] [1/4] Restaurando configuração de fábrica (Opção 5 - Restore to Factory Default)...");
                await HpeBootWareStateMachine.ExecuteOptionAsync(session, HpeMenuState.ExtendedBootWare, restoreFactoryOption, "Restore to Factory Default Configuration", _progress, ct);
                await Task.Delay(500, ct);
            }

            // 2. Opção 6 (Skip Current System Configuration)
            await ProgressAsync("[*] [2/4] Ignorando configuração atual do sistema (Opção 6 - Skip Config)...");
            await HpeBootWareStateMachine.ExecuteOptionAsync(session, HpeMenuState.ExtendedBootWare, skipConfigOption, "Skip Current System Configuration", _progress, ct);
            await Task.Delay(500, ct);

            // 3. Opção 8 (Skip Authentication for Console Login)
            await ProgressAsync("[*] [3/4] Ignorando autenticação de login de console (Opção 8 - Skip Auth)...");
            await HpeBootWareStateMachine.ExecuteOptionAsync(session, HpeMenuState.ExtendedBootWare, skipAuthOption, "Skip Authentication for Console Login", _progress, ct);
            await Task.Delay(500, ct);

            // 4. Opção 1 (Boot System)
            await ProgressAsync("[*] [4/4] Disparando inicialização do sistema (Opção 1 - Boot System)...");
            await HpeBootWareStateMachine.ExecuteOptionAsync(session, HpeMenuState.ExtendedBootWare, bootOption, "Boot System", _progress, ct);
            await Task.Delay(500, ct);
        }

        // 7. Aguarda o Comware inicializar
        await ProgressAsync("[*] [5/6] Aguardando inicialização do sistema Comware (2 a 4 minutos)...");
        var bootConditions = new StopCondition[]
        {
            new StopCondition.LineRegex("prompt", ComwarePrompt),
            new StopCondition.LineRegex("return", PressReturnPrompt),
            new StopCondition.LineRegex("autoconfig", AutoConfigPrompt),
            new StopCondition.LineRegex("menu", BootWareMenuPrompt),
            new StopCondition.Contains("fail", "Loading images fails"),
            new StopCondition.Contains("not_exist", "The image does not exist!"),
            new StopCondition.Contains("boot_fail", "Loading boot image fails")
        };

        var bootDeadline = DateTime.UtcNow.AddMinutes(7);
        var bootSw = System.Diagnostics.Stopwatch.StartNew();
        var booted = false;

        while (DateTime.UtcNow < bootDeadline && !ct.IsCancellationRequested && !booted)
        {
            var elapsedSec = bootSw.Elapsed.TotalSeconds;
            
            DeviceSession.ExpectResult? res;
            try
            {
                res = await session.WaitForAsync(bootConditions, TimeSpan.FromSeconds(3), ct);
            }
            catch (SessionTimeoutException)
            {
                // Envia Enter periódico para acordar console quando o boot terminar
                if (elapsedSec > 30)
                {
                    await session.WriteLineAsync(string.Empty, ct);
                }
                continue;
            }

            var outText = res.Output;

            // Se detectar ausência de imagem na Flash, solicita o firmware e realiza gravação via TFTP
            if (outText.Contains("Loading images fails", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("The image does not exist", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("Loading boot image fails", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("Image program does not exist", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync("\n[⚠️ ALERTA] Memória Flash sem imagem de sistema operacional (ausência de firmware detectada).");
                if (requestFirmwareFile != null && tftpDownloader != null)
                {
                    await ProgressAsync("[*] Solicitando pacote de firmware (.ipe / .bin) para gravação via BootWare TFTP...");
                    var fwFile = await requestFirmwareFile(ct);
                    if (!string.IsNullOrEmpty(fwFile) && File.Exists(fwFile))
                    {
                        var tftpOk = await tftpDownloader(
                            session,
                            ethernetOption,
                            fwFile,
                            hostIpAddress ?? "200.182.245.18",
                            routerIpAddress ?? "200.182.245.17",
                            subnetMask ?? "255.255.255.240",
                            ct);

                        if (tftpOk)
                        {
                            await HpeBootWareStateMachine.EnsureExtendedBootWareAsync(session, _progress, ct);
                            await ProgressAsync("[*] Disparando inicialização do sistema pós-gravação (Opção 1 - Boot System)...");
                            await HpeBootWareStateMachine.ExecuteOptionAsync(session, HpeMenuState.ExtendedBootWare, bootOption, "Boot System", _progress, ct);
                            bootSw.Restart();
                            continue;
                        }
                    }
                }

                throw new DeviceSessionException("A memória Flash do HPE não possui firmware válido (imagem ausente) e a recuperação não pôde ser realizada.");
            }

            // Se o roteador ainda estiver ou retornou ao menu BootWare, reenvia '1' (Boot System) imediatamente
            if (outText.Contains("choice(0-9)", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("<EXTENDED-BOOTWARE MENU>", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("Enter your choice", StringComparison.OrdinalIgnoreCase) ||
                (res.Matched is StopCondition.LineRegex lrMenu && lrMenu.Name == "menu"))
            {
                await ProgressAsync("[*] [BootWare] Menu detectado durante a inicialização — enviando '1' (Boot System)...");
                await session.WriteLineAsync("1", ct);
                await Task.Delay(1000, ct);
                continue;
            }

            if (outText.Contains("System image is starting", StringComparison.OrdinalIgnoreCase))
            {
                ProgressUpdated?.Invoke(60, "[5/6] Inicializando Kernel Comware...", "Carregando módulos do sistema Comware...");
            }
            else if (outText.Contains("Cryptographic algorithms tests passed", StringComparison.OrdinalIgnoreCase))
            {
                ProgressUpdated?.Invoke(70, "[5/6] Serviços Criptográficos OK", "Iniciando subsistemas e interfaces de rede...");
                await session.WriteLineAsync(string.Empty, ct);
            }

            if (outText.Contains("automatic configuration", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("CTRL_C", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("terminate automatic configuration", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("Auto-Configuration", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("Waiting for the next", StringComparison.OrdinalIgnoreCase))
            {
                ProgressUpdated?.Invoke(80, "[5/6] Interrompendo Auto-Configuração...", "Cancelando tentativa de DHCP/Auto-Config para liberar console...");
                await ProgressAsync("[*] Detectada Auto-Configuration — enviando CTRL+C e confirmando encerramento (Y)...");
                await session.SendCtrlCAsync(ct);
                await Task.Delay(300, ct);
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(400, ct);
                await session.WriteLineAsync(string.Empty, ct);
            }

            if (outText.Contains("Line con0 is available", StringComparison.OrdinalIgnoreCase) ||
                outText.Contains("Press ENTER to get started", StringComparison.OrdinalIgnoreCase) ||
                (res.Matched is StopCondition.LineRegex lrReturn && lrReturn.Name == "return"))
            {
                ProgressUpdated?.Invoke(90, "[6/6] Console Con0 Liberado!", "Pressionando ENTER para ativar prompt <HPE>...");
                await session.WriteLineAsync(string.Empty, ct);
                await Task.Delay(400, ct);
                await session.WriteLineAsync(string.Empty, ct);
            }

            if (res.Matched is StopCondition.LineRegex lrPrompt && lrPrompt.Name == "prompt")
            {
                booted = true;
                ProgressUpdated?.Invoke(100, "[6/6] Comware Pronto!", "Sistema Comware ativo! Prompt <HPE> capturado com sucesso.");
                await ProgressAsync("[OK] Console Comware inicializado com sucesso! Prompt <HPE> pronto.");
                break;
            }
        }

        if (!booted)
        {
            throw new DeviceSessionException("Tempo limite esgotado ao aguardar o boot do sistema Comware. Verifique o console.");
        }

        // 8. Limpa a configuração antiga com reset saved-configuration
        await ProgressAsync("[*] Roteador inicializado sem senha! Limpando configuração antiga (reset saved-configuration)...");
        await session.WriteLineAsync(string.Empty, ct);
        await Task.Delay(800, ct);

        var rst = await session.SendExpectAsync("reset saved-configuration",
            new StopCondition[] { new StopCondition.Contains("[Y/N]", "[Y/N]"), new StopCondition.Prompt() },
            TimeSpan.FromSeconds(8), ct);
        if (rst.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
        {
            await session.WriteLineAsync("Y", ct);
            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(8), ct);
            if (rst.Output.Contains("Before pressing ENTER", StringComparison.OrdinalIgnoreCase) || rst.Output.Contains("n", StringComparison.OrdinalIgnoreCase))
            { await session.WriteLineAsync("Y", ct); await Task.Delay(600, ct); }
        }
        await session.WriteLineAsync(string.Empty, ct);

        await ProgressAsync("[OK] Recuperação de senha HPE concluída com sucesso! Equipamento desbloqueado e pronto para provisionamento.");
        return true;
    }

    private static async Task ExecuteMenuOptionWithOptionalConfirmAsync(DeviceSession session, string option, CancellationToken ct)
    {
        await session.WriteLineAsync(option, ct);
        try
        {
            var res = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("confirm", ConfirmPrompt),
                    new StopCondition.Contains("Flag Set Success", "Flag Set Success"),
                    new StopCondition.LineRegex("menu", BootWareMenuPrompt),
                    new StopCondition.Contains("choice", "choice")
                },
                TimeSpan.FromSeconds(2),
                ct);

            if (res.Matched is StopCondition.LineRegex lr && lr.Name == "confirm" || res.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(400, ct);
            }
        }
        catch (SessionTimeoutException)
        {
            // Prossegue normalmente
        }
    }

    /// <summary>
    /// Acessa o File Control do BootWare, seleciona <3> Set Bin File type, associa todos os arquivos .bin (boot, system, etc) e define como Main Image.
    /// </summary>
    private async Task<bool> RepararBootImageViaFileControlAsync(DeviceSession session, string fileControlOption, CancellationToken ct)
    {
        try
        {
            // Garante que estamos no Menu Principal do BootWare antes de enviar a Opção 4
            await HpeBootWareStateMachine.EnsureExtendedBootWareAsync(session, _progress, ct);

            await ProgressAsync("[*] Acessando Menu de Controle de Arquivos (Opção 4 - File Control)...");
            await session.WriteLineAsync(fileControlOption, ct);

            await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("File CONTROL", "File CONTROL"),
                    new StopCondition.Contains("Set Bin File type", "Set Bin File type"),
                    new StopCondition.Contains("choice(0-6)", "choice(0-6)")
                },
                TimeSpan.FromSeconds(6),
                ct);

            // 2. Executa Opção 3 (Set Bin File type)
            await ProgressAsync("[*] Acessando Opção 3 (Set Bin File type) no BootWare...");
            await session.WriteLineAsync("3", ct);

            DeviceSession.ExpectResult listResult;
            try
            {
                listResult = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.Contains("No right Bin file", "No right Bin file"),
                        new StopCondition.Contains("No right file", "No right file"),
                        new StopCondition.Contains("Enter file No", "Enter file No"),
                        new StopCondition.Contains("selection", "selection"),
                        new StopCondition.Contains("choice(0-6)", "choice(0-6)")
                    },
                    TimeSpan.FromSeconds(6),
                    ct);
            }
            catch (SessionTimeoutException)
            {
                await ProgressAsync("[!] Sem resposta ao listar arquivos .bin — retornando ao Menu Principal...");
                await RetornarAoMenuPrincipalBootWareAsync(session, ct);
                return false;
            }

            var rawText = listResult.Output;
            var tail = rawText.Length > 1200 ? rawText.Substring(rawText.Length - 1200) : rawText;

            if (tail.Contains("No right Bin file", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("No right file", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("No file", StringComparison.OrdinalIgnoreCase) ||
                !tail.Contains("Enter file No", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync("[!] Memória Flash sem pacotes .bin válidos ('No right Bin file in the current device!').");
                await RetornarAoMenuPrincipalBootWareAsync(session, ct);
                return false;
            }

            // Normaliza quebras de linha com pipes que ocorrem por limitação de largura de terminal
            var normalizedText = Regex.Replace(tail, @"\|\s*\r?\n\s*\|?", string.Empty);
            await ProgressAsync($"[PACOTES DETECTADOS NO BOOTWARE]\n{normalizedText.Trim()}");

            // Mapeia cada linha da tabela: |<NO> ... flash:/<nome>.bin
            var rowMatches = Regex.Matches(normalizedText, @"\|?\s*(?<no>\d+)\s+[0-9]+\s+[A-Za-z0-9/: ]+\s+(?:N/A|MAIN|BACKUP)?\s+(?:flash:/)?(?<file>[a-zA-Z0-9_\-\.]+\.bin)", RegexOptions.IgnoreCase);

            var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in rowMatches)
            {
                var no = m.Groups["no"].Value.Trim();
                var file = m.Groups["file"].Value.Trim();
                if (no != "0" && !fileMap.ContainsKey(file))
                {
                    fileMap[file] = no;
                }
            }

            if (fileMap.Count == 0)
            {
                var altMatches = Regex.Matches(normalizedText, @"(?<no>\d+)\s+.*?(?<file>[a-zA-Z0-9_\-\.]+\.bin)", RegexOptions.IgnoreCase);
                foreach (Match m in altMatches)
                {
                    var no = m.Groups["no"].Value.Trim();
                    var file = m.Groups["file"].Value.Trim();
                    if (no != "0" && !fileMap.ContainsKey(file))
                    {
                        fileMap[file] = no;
                    }
                }
            }

            var bootEntry = fileMap.FirstOrDefault(kv => kv.Key.Contains("boot", StringComparison.OrdinalIgnoreCase));
            var systemEntry = fileMap.FirstOrDefault(kv => kv.Key.Contains("system", StringComparison.OrdinalIgnoreCase));

            if (fileMap.Count == 0 || string.IsNullOrEmpty(bootEntry.Value) || string.IsNullOrEmpty(systemEntry.Value))
            {
                await ProgressAsync("[!] Pacotes obrigatórios (.bin boot e system) não foram identificados na Flash.");
                await session.WriteLineAsync("0", ct);
                await Task.Delay(600, ct);
                await RetornarAoMenuPrincipalBootWareAsync(session, ct);
                return false;
            }

            string bootNo = bootEntry.Value;
            string systemNo = systemEntry.Value;

            await ProgressAsync($"[*] Selecionando pacote Boot: #{bootNo} ({bootEntry.Key})");
            await session.WriteLineAsync(bootNo, ct);
            await Task.Delay(600, ct);

            await ProgressAsync($"[*] Selecionando pacote System: #{systemNo} ({systemEntry.Key})");
            await session.WriteLineAsync(systemNo, ct);
            await Task.Delay(600, ct);

            // Seleciona pacotes complementares legítimos existentes na tabela
            var extraNos = new List<string>();
            foreach (var kv in fileMap)
            {
                if (kv.Value != bootNo && kv.Value != systemNo && !extraNos.Contains(kv.Value))
                    extraNos.Add(kv.Value);
            }

            foreach (var no in extraNos)
            {
                await ProgressAsync($"[*] Adicionando pacote complementar: #{no}");
                await session.WriteLineAsync(no, ct);
                await Task.Delay(400, ct);
            }

            // Envia '0' para finalizar a seleção de arquivos
            await ProgressAsync("[*] Finalizando seleção de arquivos (0 - Finish choice)...");
            await session.WriteLineAsync("0", ct);
            await Task.Delay(1000, ct);

            // Responde se pedir atributo: '1' para Main ou 'Y' para confirmar
            var attrPrompt = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("Main", "Main"),
                    new StopCondition.Contains("[Y/N]", "[Y/N]"),
                    new StopCondition.Contains("attribute", "attribute"),
                    new StopCondition.Contains("File CONTROL", "File CONTROL"),
                    new StopCondition.LineRegex("menu", FileControlMenuPrompt)
                },
                TimeSpan.FromSeconds(5),
                ct);

            if (attrPrompt.Output.Contains("Main", StringComparison.OrdinalIgnoreCase) ||
                attrPrompt.Output.Contains("attribute", StringComparison.OrdinalIgnoreCase) ||
                attrPrompt.Output.Contains("1-", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("1", ct); // 1 = Main
                await Task.Delay(600, ct);
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(800, ct);
            }
            else if (attrPrompt.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(800, ct);
            }

            // Retorna ao Menu Principal de forma segura
            await RetornarAoMenuPrincipalBootWareAsync(session, ct);

            await ProgressAsync("[OK] Pacotes de software reassociados e configurados como Main Boot Image com sucesso!");
            return true;
        }
        catch (Exception ex)
        {
            await ProgressAsync($"[AVISO] Falha ao configurar Set Bin File type: {ex.Message}");
            try
            {
                await RetornarAoMenuPrincipalBootWareAsync(session, ct);
            }
            catch { }
            return false;
        }
    }

    public static async Task<bool> EnsureHpeUserViewAsync(DeviceSession session, Func<string, Task>? progress = null, CancellationToken ct = default)
    {
        var prompt = (session.CurrentPrompt ?? string.Empty).Trim();
        if (Regex.IsMatch(prompt, @"\<[^\r\n>]+\>\s*$"))
            return true;

        for (int i = 0; i < 4; i++)
        {
            await session.WriteLineAsync("return", ct);
            await Task.Delay(300, ct);

            var res = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("user-view", new Regex(@"\<[^\r\n>]+\>\s*$", RegexOptions.Compiled)),
                    new StopCondition.LineRegex("sys-view", new Regex(@"\[[^\r\n\]]+\]\s*$", RegexOptions.Compiled))
                },
                TimeSpan.FromSeconds(2),
                ct);

            var outText = res.Output.Trim();
            if (Regex.IsMatch(outText, @"\<[^\r\n>]+\>\s*$"))
            {
                if (progress != null)
                    await progress("[*] Retornado com sucesso à visualização de usuário raiz do HPE (<HPE>).");
                return true;
            }

            if (outText.Contains("[", StringComparison.Ordinal))
            {
                await session.WriteLineAsync("quit", ct);
                await Task.Delay(300, ct);
            }
        }
        return false;
    }

    private async Task ExecuteDirectHpeCliResetAsync(DeviceSession session, CancellationToken ct)
    {
        // Garante que o equipamento saiu de qualquer sub-menu/system-view e está na User View (<HPE>)
        await EnsureHpeUserViewAsync(session, _progress, ct);

        await ProgressAsync("[*] Apagando configuração salva do HPE (reset saved-configuration)...");
        // Usa SendExpect para garantir captura do prompt [Y/N] mesmo quando <HPE> já casou primeiro
        var confRes = await session.SendExpectAsync(
            "reset saved-configuration",
            new StopCondition[]
            {
                new StopCondition.Contains("The action will delete", "The action will delete"),
                new StopCondition.Contains("Continue?", "Continue?"),
                new StopCondition.Contains("[Y/N]", "[Y/N]"),
                new StopCondition.LineRegex("confirm", ConfirmPrompt),
                new StopCondition.LineRegex("prompt", ComwarePrompt)
            },
            TimeSpan.FromSeconds(8),
            ct);

        // Se já caiu direto no prompt, o comando pode não ter sido processado — reenvia
        if (confRes.Matched is StopCondition.LineRegex lr && lr.Name == "prompt" && !confRes.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
        {
            confRes = await session.SendExpectAsync(
                "reset saved-configuration",
                new StopCondition[] { new StopCondition.Contains("[Y/N]", "[Y/N]"), new StopCondition.LineRegex("confirm", ConfirmPrompt) },
                TimeSpan.FromSeconds(8), ct);
        }

        if (confRes.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
            confRes.Output.Contains("Continue", StringComparison.OrdinalIgnoreCase) ||
            confRes.Output.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
            confRes.Matched is not null)
        {
            await session.WriteLineAsync("Y", ct);
            await Task.Delay(800, ct);
            // HPE re-exibe "Before pressing ENTER you must choose 'YES' or 'NO'" se Y não foi aceito — reenvia Y
            var confirm2 = await session.WaitForAsync(
                new StopCondition[] { new StopCondition.Contains("Before pressing ENTER", "Before pressing ENTER"), new StopCondition.LineRegex("prompt", ComwarePrompt) },
                TimeSpan.FromSeconds(4), ct);
            if (confirm2.Output.Contains("Before pressing ENTER", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(800, ct);
                await session.WaitForAsync(new StopCondition[] { new StopCondition.LineRegex("prompt", ComwarePrompt) }, TimeSpan.FromSeconds(4), ct);
            }
        }

        await session.WriteLineAsync(string.Empty, ct);
        await ProgressAsync("[OK] Configurações apagadas do HPE via CLI com sucesso.");
    }

    public static async Task<string> RetornarAoMenuPrincipalBootWareAsync(DeviceSession session, CancellationToken ct)
    {
        var ok = await HpeBootWareStateMachine.EnsureExtendedBootWareAsync(session, null, ct);
        return ok ? "<EXTENDED-BOOTWARE MENU> Enter your choice(0-9):" : string.Empty;
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }
}

public enum HpeMenuState
{
    Unknown = 0,
    ExtendedBootWare,   // <EXTENDED-BOOTWARE MENU> / <BASIC BOOT MENU> (choice 0-9)
    EthernetSubMenu,    // <ETHERNET SUBMENU> (choice 0-5)
    FileControl,        // <File CONTROL> (choice 0-6)
    SerialSubMenu,      // <SERIAL SUBMENU> (choice 0-3)
    FileSelection,      // Enter file No.:
    ConfirmYesNo,       // [Y/N]
    ComwareCli          // <HPE>, [HPE]
}

public static class HpeBootWareStateMachine
{
    private static readonly Regex ConfirmPrompt = new(@"\[Y/N\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static (HpeMenuState State, string Prompt) DetectState(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (HpeMenuState.Unknown, string.Empty);

        var tail = text.Length > 600 ? text.Substring(text.Length - 600) : text;

        if (Regex.IsMatch(tail, @"(?i)(?:^<[A-Za-z0-9_\-\.]+>|^\[[A-Za-z0-9_\-\.]+\])\s*$"))
            return (HpeMenuState.ComwareCli, "<HPE>");

        if (Regex.IsMatch(tail, @"(?i)\[Y/N\]\s*[:?]?\s*$"))
            return (HpeMenuState.ConfirmYesNo, "[Y/N]");

        if (tail.Contains("Enter file No", StringComparison.OrdinalIgnoreCase))
            return (HpeMenuState.FileSelection, "Enter file No.:");

        // 1. EXTENDED BOOTWARE (Menu Principal 0-9) - Verificar antes para não casar com "<3> Enter Ethernet SubMenu"
        if (tail.Contains("<EXTENDED-BOOTWARE MENU>", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("<BASIC BOOT MENU>", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("<MAIN MENU>", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("choice(0-9)", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("(0-9):", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(tail, @"(?i)choice\s*\(\s*0\s*-\s*9\s*\)"))
        {
            return (HpeMenuState.ExtendedBootWare, "Enter your choice(0-9):");
        }

        // 2. ETHERNET SUBMENU (Submenu Ethernet 0-5)
        if (tail.Contains("<Enter Ethernet SubMenu>", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("<ETHERNET SUBMENU>", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("choice(0-5)", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("(0-5):", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(tail, @"(?i)choice\s*\(\s*0\s*-\s*5\s*\)"))
        {
            return (HpeMenuState.EthernetSubMenu, "Enter your choice(0-5):");
        }

        // 3. FILE CONTROL (Submenu File Control 0-6)
        if (tail.Contains("<File CONTROL>", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("choice(0-6)", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("(0-6):", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(tail, @"(?i)choice\s*\(\s*0\s*-\s*6\s*\)"))
        {
            return (HpeMenuState.FileControl, "Enter your choice(0-6):");
        }

        // 4. SERIAL SUBMENU (Submenu Serial 0-3)
        if (tail.Contains("<SERIAL SUBMENU>", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("choice(0-3)", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("(0-3):", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(tail, @"(?i)choice\s*\(\s*0\s*-\s*3\s*\)"))
        {
            return (HpeMenuState.SerialSubMenu, "Enter your choice(0-3):");
        }

        // 5. PARÂMETROS ETHERNET (Opção 5 em andamento)
        if (tail.Contains("Server IP", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("Local IP", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("Subnet Mask", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("Gateway IP", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("Protocol (FTP", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("Load File Name", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("Target File Name", StringComparison.OrdinalIgnoreCase))
        {
            return (HpeMenuState.EthernetSubMenu, "Ethernet Parameters");
        }

        return (HpeMenuState.Unknown, string.Empty);
    }

    public static async Task<bool> EnsureExtendedBootWareAsync(DeviceSession session, Func<string, Task>? progress, CancellationToken ct)
    {
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            await session.WriteLineAsync(string.Empty, ct);
            await Task.Delay(250, ct);

            DeviceSession.ExpectResult res;
            try
            {
                res = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.Contains("choice", "choice"),
                        new StopCondition.Contains("BOOTWARE", "BOOTWARE"),
                        new StopCondition.Contains("MENU", "MENU"),
                        new StopCondition.Contains("Enter file No", "Enter file No"),
                        new StopCondition.Contains("[Y/N]", "[Y/N]"),
                        new StopCondition.Prompt()
                    },
                    TimeSpan.FromSeconds(3),
                    ct);
            }
            catch (SessionTimeoutException)
            {
                continue;
            }

            var (state, prompt) = DetectState(res.Output);

            if (state == HpeMenuState.ExtendedBootWare)
            {
                if (progress != null)
                    await progress($"[{DateTime.Now:HH:mm:ss}] [STATE: EXTENDED_BOOTWARE] [PROMPT: {prompt}] — Menu Principal confirmado.");
                return true;
            }

            if (state == HpeMenuState.EthernetSubMenu || state == HpeMenuState.FileControl || state == HpeMenuState.SerialSubMenu || state == HpeMenuState.FileSelection)
            {
                if (progress != null)
                    await progress($"[{DateTime.Now:HH:mm:ss}] [STATE: {state}] [PROMPT: {prompt}] -> TX: '0' (Retornando ao Menu Principal)...");
                await session.WriteLineAsync("0", ct);
                await Task.Delay(800, ct);
            }
            else if (state == HpeMenuState.ConfirmYesNo)
            {
                if (progress != null)
                    await progress($"[{DateTime.Now:HH:mm:ss}] [STATE: ConfirmYesNo] -> TX: 'N' (Cancelando diálogo)...");
                await session.WriteLineAsync("N", ct);
                await Task.Delay(400, ct);
            }
        }

        return false;
    }

    public static async Task<bool> ExecuteOptionAsync(
        DeviceSession session,
        HpeMenuState expectedMenu,
        string option,
        string reason,
        Func<string, Task>? progress,
        CancellationToken ct)
    {
        if (progress != null)
            await progress($"[{DateTime.Now:HH:mm:ss}] [ACTION] TargetMenu: {expectedMenu} | TX: '{option}' | Motivo: {reason}");

        await session.WriteLineAsync(option, ct);
        await Task.Delay(300, ct);

        try
        {
            var res = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("Flag Set Success", "Flag Set Success"),
                    new StopCondition.Contains("[Y/N]", "[Y/N]"),
                    new StopCondition.Contains("choice", "choice"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(3),
                ct);

            if (res.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
            {
                if (progress != null)
                    await progress($"[{DateTime.Now:HH:mm:ss}] [PROMPT: [Y/N]] -> TX: 'Y' (Confirmando ação)...");
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(400, ct);
            }

            if (progress != null && res.Output.Contains("Flag Set Success", StringComparison.OrdinalIgnoreCase))
            {
                await progress($"[{DateTime.Now:HH:mm:ss}] [VERIFY] {reason}: Flag Set Success (OK).");
            }
        }
        catch (SessionTimeoutException)
        {
            // Prossegue normalmente
        }

        return true;
    }
}
