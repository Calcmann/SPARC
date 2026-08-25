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
        @"(?i)(?:BOOT\s*MENU|<(?:EXTENDED-)?BOOTWARE\s*MENU>|<MAIN\s*MENU>|<BASIC\s*BOOT\s*MENU>|Enter\s+your\s+choice|choice\(0-9\):|Select\s+your\s+choice|Enter\s+choice)",
        RegexOptions.Compiled);

    private static readonly Regex FileControlMenuPrompt = new(
        @"(?i)(?:FILE\s*CONTROL|choice\(0-[4-9]\):)",
        RegexOptions.Compiled);

    private static readonly Regex EthernetMenuPrompt = new(
        @"(?i)(?:ETHERNET\s*SUBMENU|choice\(0-[3-9]\):)",
        RegexOptions.Compiled);

    private static readonly Regex BootWareCountdownPrompt = new(
        @"(?i)(?:Press\s+Ctrl\+[BD]\s+to\s+enter|Press\s+Ctrl\+B\s+to\s+access|Press\s+Ctrl\+B\s+to\s+stop)",
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
        BootWareTftpDownloader? tftpDownloader = null,
        Func<CancellationToken, Task<string?>>? requestFirmwareFile = null,
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
            await session.WriteLineAsync(string.Empty, ct);
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
            var waitResult = await session.WaitForAsync(conditions, TimeSpan.FromSeconds(15), ct);

            if (waitResult.Matched is StopCondition.LineRegex match)
            {
                if (match.Name == "menu")
                {
                    menuCaptured = true;
                    menuText = waitResult.Output;
                    break;
                }
                else if (match.Name == "countdown")
                {
                    await ProgressAsync("[>] Contagem regressiva do BootWare detectada! Enviando rajadas de Ctrl+B...");
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

                    var pwRes = await session.WaitForAsync(new StopCondition[] { new StopCondition.LineRegex("menu", BootWareMenuPrompt), new StopCondition.LineRegex("password", BootWarePasswordPrompt) }, TimeSpan.FromSeconds(3), ct);
                    if (pwRes.Matched is StopCondition.LineRegex mr && mr.Name == "menu")
                    {
                        menuCaptured = true;
                        menuText = pwRes.Output;
                        break;
                    }
                    else
                    {
                        var fallbackPasswords = new[] { "admin", "Admin@h3c", "Admin@huawei", "h3c" };
                        foreach (var pw in fallbackPasswords)
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

        ProgressUpdated?.Invoke(10, "1/6 Zerar Configuração...", "Menu do BootWare acessado com sucesso!");
        await ProgressAsync("[OK] Menu do BootWare acessado com sucesso!");
        await Task.Delay(500, ct);

        string skipConfigOption = "6";
        string skipAuthOption = "8";
        string bootOption = "1";
        string fileControlOption = "4";
        string ethernetOption = "3";

        var skipConfigMatch = SkipConfigMenuOptionRegex.Match(menuText);
        if (skipConfigMatch.Success)
            skipConfigOption = skipConfigMatch.Groups["num"].Value;

        var skipAuthMatch = SkipAuthMenuOptionRegex.Match(menuText);
        if (skipAuthMatch.Success)
            skipAuthOption = skipAuthMatch.Groups["num"].Value;

        var bootMatch = BootSystemMenuOptionRegex.Match(menuText);
        if (bootMatch.Success)
            bootOption = bootMatch.Groups["num"].Value;

        var fileControlMatch = FileControlMenuOptionRegex.Match(menuText);
        if (fileControlMatch.Success)
            fileControlOption = fileControlMatch.Groups["num"].Value;

        var ethernetMatch = EthernetMenuOptionRegex.Match(menuText);
        if (ethernetMatch.Success)
            ethernetOption = ethernetMatch.Groups["num"].Value;

        // 4. Executa Opção 'Skip Current Configuration'
        ProgressUpdated?.Invoke(12, "1/6 Zerar Configuração...", "Configurando Skip Config e Skip Auth no BootWare...");
        await ProgressAsync($"[*] Selecionando Opção {skipConfigOption} (Skip Current System Configuration)...");
        await session.WriteLineAsync(skipConfigOption, ct);
        await Task.Delay(800, ct);
        await session.WriteLineAsync("Y", ct);
        await Task.Delay(800, ct);

        // 5. Executa Opção 'Skip Authentication for Console Login' (se disponível)
        if (skipAuthMatch.Success)
        {
            await ProgressAsync($"[*] Selecionando Opção {skipAuthOption} (Skip Authentication for Console Login)...");
            await session.WriteLineAsync(skipAuthOption, ct);
            await Task.Delay(800, ct);
            await session.WriteLineAsync("Y", ct);
            await Task.Delay(800, ct);
        }

        // 6. Executa Boot System
        ProgressUpdated?.Invoke(14, "1/6 Zerar Configuração...", "Inicializando sistema Comware (aguarde de 2 a 5 min)...");
        await ProgressAsync($"[*] Inicializando o sistema Comware (Opção {bootOption} - Boot System)...");
        await session.WriteLineAsync(bootOption, ct);
        await Task.Delay(800, ct);
        await session.WriteLineAsync("Y", ct);
        await Task.Delay(3000, ct);

        // 7. Aguarda o Comware inicializar
        await ProgressAsync("[*] Aguardando inicialização do sistema Comware (2 a 5 minutos)...");
        var bootConditions = new StopCondition[]
        {
            new StopCondition.LineRegex("prompt", ComwarePrompt),
            new StopCondition.LineRegex("return", PressReturnPrompt),
            new StopCondition.LineRegex("autoconfig", AutoConfigPrompt),
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
            var bootPct = (int)Math.Clamp(14 + (elapsedSec / 25.0), 14, 19);
            ProgressUpdated?.Invoke(bootPct, "1/6 Zerar Configuração...", $"Inicializando sistema Comware ({elapsedSec:F0}s decorridos)...");

            var res = await session.WaitForAsync(bootConditions, TimeSpan.FromSeconds(6), ct);
            await session.WriteLineAsync(string.Empty, ct);

            if (res.Output.Contains("automatic configuration", StringComparison.OrdinalIgnoreCase) ||
                res.Output.Contains("CTRL_C", StringComparison.OrdinalIgnoreCase) ||
                res.Output.Contains("terminate automatic configuration", StringComparison.OrdinalIgnoreCase) ||
                res.Output.Contains("Auto-Configuration", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync("[*] Detectada tentativa de Auto-Configuration — enviando CTRL+C e confirmando encerramento (Y)...");
                await session.SendCtrlCAsync(ct);
                await Task.Delay(400, ct);
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(600, ct);
                await session.WriteLineAsync(string.Empty, ct);
            }

            if (res.Matched is StopCondition.LineRegex lr)
            {
                if (lr.Name == "autoconfig")
                {
                    await ProgressAsync("[*] Cancelando diálogo de Auto-Configuration com CTRL+C e confirmando (Y)...");
                    await session.SendCtrlCAsync(ct);
                    await Task.Delay(400, ct);
                    await session.WriteLineAsync("Y", ct);
                    await Task.Delay(600, ct);
                    await session.WriteLineAsync(string.Empty, ct);
                }
                else if (lr.Name == "return")
                {
                    await session.WriteLineAsync(string.Empty, ct);
                    await Task.Delay(500, ct);
                }
                else if (lr.Name == "prompt")
                {
                    booted = true;
                    ProgressUpdated?.Invoke(18, "1/7 Zerar Configuração OK!", "Sistema Comware inicializado com sucesso! Prompt <HPE> detectado.");
                    break;
                }
            }
            else if (res.Matched is StopCondition.Contains)
            {
                ProgressUpdated?.Invoke(13, "1/6 Zerar Configuração...", "File Control: Vinculando imagens de boot na Flash...");
                await ProgressAsync("\n=================================================================");
                await ProgressAsync("     🚑 ETAPA 1: CONFIGURAÇÃO DE IMAGEM DE BOOT (FILE CONTROL)   ");
                await ProgressAsync("=================================================================");
                await ProgressAsync("  Detectada falha de boot (pacotes na Flash estão como 'Not Assigned').");
                await ProgressAsync("  Configurando arquivos boot e system existentes como Imagem Principal...");
                await ProgressAsync("=================================================================\n");

                var repaired = await RepararBootImageViaFileControlAsync(session, fileControlOption, ct);

                if (repaired)
                {
                    await ProgressAsync($"[*] Reaplicando Opção {skipConfigOption} (Skip Current System Configuration)...");
                    await session.WriteLineAsync(skipConfigOption, ct);
                    await Task.Delay(800, ct);
                    await session.WriteLineAsync("Y", ct);
                    await Task.Delay(800, ct);

                    ProgressUpdated?.Invoke(15, "1/6 Zerar Configuração...", "Reinicializando Comware com novas imagens...");
                    await ProgressAsync($"[*] Inicializando o sistema Comware com as imagens configuradas (Opção {bootOption})...");
                    await session.WriteLineAsync(bootOption, ct);
                    await Task.Delay(800, ct);
                    await session.WriteLineAsync("Y", ct);
                    await Task.Delay(3000, ct);
                    continue;
                }

                // ETAPA 2: Se o File Control não conseguir ou a Flash estiver vazia, tenta recuperação via BootWare Ethernet TFTP
                if (tftpDownloader != null)
                {
                    if (requestFirmwareFile != null)
                    {
                        await ProgressAsync("\n[*] Redirecionando para a Fase B para seleção do pacote de firmware .IPE...");
                        firmwareFilePath = await requestFirmwareFile(ct);
                    }

                    if (!string.IsNullOrEmpty(firmwareFilePath) && File.Exists(firmwareFilePath))
                    {
                        await ProgressAsync("\n=================================================================");
                        await ProgressAsync("           🚑 ETAPA 2: RECUPERAÇÃO VIA BOOTWARE ETHERNET TFTP    ");
                        await ProgressAsync("=================================================================");
                        await ProgressAsync("  A memória Flash não possui imagens válidas para boot.");
                        await ProgressAsync($"  Firmware selecionado: {Path.GetFileName(firmwareFilePath)}");
                        await ProgressAsync("  Iniciando servidor TFTP integrado e baixando firmware diretamente pelo BootWare...");
                        await ProgressAsync("=================================================================\n");

                        var tftpRecovered = await tftpDownloader(
                            session,
                            ethernetOption,
                            firmwareFilePath,
                            hostIpAddress ?? "200.182.245.18",
                            "200.182.245.17",
                            "255.255.255.240",
                            ct);

                        if (tftpRecovered)
                        {
                            await ProgressAsync($"[*] Reaplicando Opção {skipConfigOption} (Skip Current System Configuration)...");
                            await session.WriteLineAsync(skipConfigOption, ct);
                            await Task.Delay(800, ct);
                            await session.WriteLineAsync("Y", ct);
                            await Task.Delay(800, ct);

                            await ProgressAsync($"[*] Inicializando sistema com firmware recuperado (Opção {bootOption})...");
                            await session.WriteLineAsync(bootOption, ct);
                            await Task.Delay(800, ct);
                            await session.WriteLineAsync("Y", ct);
                            await Task.Delay(3000, ct);
                            continue;
                        }
                    }
                }

                throw new DeviceSessionException("O BootWare não conseguiu inicializar a imagem na Flash. Conecte o cabo de rede Ethernet na porta LAN (Giga 1) e selecione o arquivo de firmware (.ipe/.bin) para recuperação via TFTP.");
            }
        }

        // 8. Limpa a configuração antiga com reset saved-configuration
        await ProgressAsync("[*] Roteador inicializado sem senha! Limpando configuração antiga (reset saved-configuration)...");
        await session.WriteLineAsync(string.Empty, ct);
        await Task.Delay(800, ct);

        await session.WriteLineAsync("reset saved-configuration", ct);
        await Task.Delay(800, ct);
        await session.WriteLineAsync("Y", ct);
        await Task.Delay(1000, ct);
        await session.WriteLineAsync(string.Empty, ct);

        await ProgressAsync("[OK] Recuperação de senha HPE concluída com sucesso! Equipamento desbloqueado e pronto para provisionamento.");
        return true;
    }

    /// <summary>
    /// Acessa o File Control do BootWare, seleciona <3> Set Bin File type, associa todos os arquivos .bin (boot, system, etc) e define como Main Image.
    /// </summary>
    private async Task<bool> RepararBootImageViaFileControlAsync(DeviceSession session, string fileControlOption, CancellationToken ct)
    {
        try
        {
            // 1. Aguarda estabilização e entra no File Control Menu (Opção 4)
            await Task.Delay(1500, ct);
            await session.WriteLineAsync(string.Empty, ct);
            await Task.Delay(500, ct);

            await ProgressAsync("[*] Acessando Menu de Controle de Arquivos (Opção 4 - File Control)...");
            await session.WriteLineAsync(fileControlOption, ct);

            await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("File CONTROL", "File CONTROL"),
                    new StopCondition.Contains("Set Bin File type", "Set Bin File type"),
                    new StopCondition.Contains("Enter your choice", "Enter your choice")
                },
                TimeSpan.FromSeconds(6),
                ct);

            // 2. Executa Opção 3 (Set Bin File type)
            await ProgressAsync("[*] Acessando Opção 3 (Set Bin File type) no BootWare...");
            await session.WriteLineAsync("3", ct);

            var listResult = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("Enter file No", "Enter file No"),
                    new StopCondition.Contains("selection", "selection"),
                    new StopCondition.Contains("Select .bin files", "Select .bin files")
                },
                TimeSpan.FromSeconds(8),
                ct);

            var rawText = listResult.Output;
            // Normaliza quebras de linha com pipes que ocorrem por limitação de largura de terminal
            var normalizedText = Regex.Replace(rawText, @"\|\s*\r?\n\s*\|?", string.Empty);
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

            // Fallback para mapeamento padrão do HPE MSR954 se regex não extrair (boot=6, system=5, data=2, security=4, voice=3, wifidog=1)
            string bootNo = bootEntry.Value ?? (normalizedText.Contains("boot") ? "6" : string.Empty);
            string systemNo = systemEntry.Value ?? (normalizedText.Contains("system") ? "5" : string.Empty);

            if (string.IsNullOrEmpty(bootNo) || string.IsNullOrEmpty(systemNo))
            {
                await ProgressAsync("[!] Pacotes obrigatórios (boot e system) não foram identificados na Flash.");
                await session.WriteLineAsync("0", ct);
                await Task.Delay(800, ct);
                await session.WriteLineAsync("0", ct);
                await Task.Delay(800, ct);
                return false;
            }

            await ProgressAsync($"[*] Selecionando pacote Boot: #{bootNo}");
            await session.WriteLineAsync(bootNo, ct);
            await Task.Delay(600, ct);

            await ProgressAsync($"[*] Selecionando pacote System: #{systemNo}");
            await session.WriteLineAsync(systemNo, ct);
            await Task.Delay(600, ct);

            // Seleciona pacotes complementares
            var extraNos = new List<string>();
            if (fileMap.Count > 0)
            {
                foreach (var kv in fileMap)
                {
                    if (kv.Value != bootNo && kv.Value != systemNo && !extraNos.Contains(kv.Value))
                        extraNos.Add(kv.Value);
                }
            }
            else
            {
                // Fallback de numeração
                extraNos.AddRange(new[] { "2", "4", "3", "1" });
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

            // Retorna ao Menu Principal (Opção 0)
            await session.WriteLineAsync("0", ct);
            await Task.Delay(1000, ct);
            await session.WriteLineAsync(string.Empty, ct);

            await ProgressAsync("[OK] Pacotes de software reassociados e configurados como Main Boot Image com sucesso!");
            return true;
        }
        catch (Exception ex)
        {
            await ProgressAsync($"[AVISO] Falha ao configurar Set Bin File type: {ex.Message}");
            try
            {
                await session.WriteLineAsync("0", ct);
                await Task.Delay(600, ct);
                await session.WriteLineAsync("0", ct);
                await Task.Delay(600, ct);
            }
            catch { }
            return false;
        }
    }

    private async Task ExecuteDirectHpeCliResetAsync(DeviceSession session, CancellationToken ct)
    {
        await session.WriteLineAsync(string.Empty, ct);
        await Task.Delay(200, ct);
        await ProgressAsync("[*] Apagando configuração salva do HPE (reset saved-configuration)...");
        await session.WriteLineAsync("reset saved-configuration", ct);
        await Task.Delay(600, ct);
        
        var confRes = await session.WaitForAsync(
            new StopCondition[]
            {
                new StopCondition.Contains("The action will delete", "The action will delete"),
                new StopCondition.Contains("Continue?", "Continue?"),
                new StopCondition.Contains("[Y/N]", "[Y/N]"),
                new StopCondition.LineRegex("confirm", ConfirmPrompt),
                new StopCondition.LineRegex("prompt", ComwarePrompt)
            },
            TimeSpan.FromSeconds(5),
            ct);

        if (confRes.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
            confRes.Output.Contains("Continue", StringComparison.OrdinalIgnoreCase) ||
            confRes.Output.Contains("delete", StringComparison.OrdinalIgnoreCase))
        {
            await session.WriteLineAsync("Y", ct);
            await Task.Delay(600, ct);
        }

        await session.WriteLineAsync(string.Empty, ct);
        await ProgressAsync("[OK] Configurações apagadas do HPE via CLI com sucesso.");
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }
}
