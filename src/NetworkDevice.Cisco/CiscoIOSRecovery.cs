using System.Text.RegularExpressions;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Cisco;

public enum InterruptMethod
{
    /// <summary>Sinal Break no pino TX (plataformas tradicionais).</summary>
    Break,

    /// <summary>Ctrl+C (0x03) — usado pela série Cisco 900 (C921-4P etc.).</summary>
    CtrlC
}

public sealed class CiscoIOSRecovery
{
    private static readonly Regex RommonRouterPrompt = new(
        @"(?i)(?:rommon\s*\S*\s*>|common\s*\S*\s*>|cannot determine first executable|readonly rommon initialized|cannot load image)",
        RegexOptions.Compiled);

    private static readonly Regex RommonSwitchPrompt = new(
        @"(?i)^switch\s*[:>]",
        RegexOptions.Compiled);

    private static readonly Regex BootDialogPrompt = new(
        @"(?i)(?:system\s+configuration\s+dialog|would\s+you\s+like\s+to\s+enter\s+the\s+initial\s+configuration\s+dialog|\[yes/no\])",
        RegexOptions.Compiled);

    private static readonly Regex PressReturnPrompt = new(
        @"(?i)press\s+return\s+to\s+get\s+started",
        RegexOptions.Compiled);

    private static readonly Regex EraseConfirm = new(
        @"(?i)(?:continue\?|\[confirm\])",
        RegexOptions.Compiled);

    public delegate Task ProgressHandler(string message);

    private readonly ProgressHandler? _progress;
    private readonly TimeSpan _bootWait;
    private readonly TimeSpan _commandTimeout;
    private readonly TimeSpan _verifyTimeout;
    private readonly BootInterruptProfile _profile;

    public CiscoIOSRecovery(
        ProgressHandler? progress = null,
        TimeSpan? bootWait = null,
        TimeSpan? commandTimeout = null,
        TimeSpan? verifyTimeout = null,
        BootInterruptProfile? profile = null)
    {
        _progress = progress;
        _bootWait = bootWait ?? TimeSpan.FromMinutes(5);
        _commandTimeout = commandTimeout ?? TimeSpan.FromSeconds(90);
        _verifyTimeout = verifyTimeout ?? TimeSpan.FromSeconds(10);
        _profile = profile ?? BootInterruptProfiles.CiscoStandardBreak;
    }

    public CiscoIOSRecovery(
        ProgressHandler? progress,
        TimeSpan? bootWait,
        TimeSpan? commandTimeout,
        TimeSpan? verifyTimeout,
        InterruptMethod interruptMethod)
        : this(
            progress,
            bootWait,
            commandTimeout,
            verifyTimeout,
            interruptMethod == InterruptMethod.CtrlC
                ? BootInterruptProfiles.Cisco900
                : BootInterruptProfiles.CiscoStandardBreak)
    {
    }

    public BootInterruptProfile Profile => _profile;

    public event Action<RecoveryState, string>? StateChanged;

    public async Task RecoverAndResetAsync(
        DeviceSession session,
        Func<string, CancellationToken, Task>? requestReload = null,
        CancellationToken cancellationToken = default)
    {
        var stateMachine = new RecoveryStateMachine(session.Transport, _profile);
        stateMachine.StateChanged += (state, msg) =>
        {
            StateChanged?.Invoke(state, msg);
            _ = ProgressAsync($"[{state}] {msg}", cancellationToken);
        };

        stateMachine.OutputReceived += text => session.EmitRawOutput(text);

        stateMachine.TransitionTo(RecoveryState.Connecting, "Abrindo conexão com o equipamento...");
        await session.ConnectRawAsync(cancellationToken);
        await ProgressAsync("Sessão serial aberta.", cancellationToken);

        // 1) Diagnóstico de atividade e estado de segurança inicial (Com Senha / Sem Senha / ROMMON)
        stateMachine.TransitionTo(RecoveryState.VerifyingTerminal, "Verificando atividade do terminal e estado de segurança...");
        var (accessState, rommonKind) = await DiagnoseAccessStateAsync(session, cancellationToken);

        if (accessState == DeviceAccessState.AlreadyInRommon && rommonKind.HasValue)
        {
            stateMachine.TransitionTo(RecoveryState.RommonDetected, "Equipamento já está no ROMMON...");
            await ProgressAsync("Equipamento já está no ROMMON (sem firmware na Flash ou em bootloader).", cancellationToken);
            try { await SendRommonCommandAsync(session, "confreg 0x2142", cancellationToken, requirePromptReturn: false); await Task.Delay(300, cancellationToken); } catch { }
            stateMachine.TransitionTo(RecoveryState.Completed, "Equipamento pronto no ROMMON para carga de firmware TFTP.");
            await ProgressAsync("\n=================================================================", cancellationToken);
            await ProgressAsync("  [OK] ROTEADOR NO MODO ROMMON (SEM FIRMWARE / FLASH VAZIA)", cancellationToken);
            await ProgressAsync("  • Equipamento já se encontra no ROMMON aguardando gravação de imagem.", cancellationToken);
            await ProgressAsync("  • Fase 1 concluída: Avançando para Atualização de Firmware via TFTP (Fase 2)...", cancellationToken);
            await ProgressAsync("=================================================================\n", cancellationToken);
            return;
        }

        if (accessState == DeviceAccessState.UnlockedPrompt)
        {
            stateMachine.TransitionTo(RecoveryState.ExecutingRecovery, "Equipamento acessível sem senha. Executando zeramento direto via CLI...");
            await ProgressAsync("Equipamento acessível sem senha (prompt aberto). Zeramento direto via CLI (sem ROMMON)...", cancellationToken);
            await ExecuteDirectCliResetAsync(session, cancellationToken);
            stateMachine.TransitionTo(RecoveryState.Completed, "Procedimento de zeramento concluído diretamente via CLI.");
            await ProgressAsync("Equipamento zerado e registrador 0x2102 garantido. Pronto para provisionamento!", cancellationToken);
            return;
        }

        // 2) Equipamento bloqueado por senha (PasswordLocked): Inicia o agendador e solicita reload
        await ProgressAsync("Equipamento bloqueado por senha ou não inicializado. Iniciando processo de quebra via ROMMON...", cancellationToken);

        using var interruptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var interruptTask = stateMachine.RunInterruptPhaseAsync(interruptCts.Token);

        try
        {
            stateMachine.TransitionTo(RecoveryState.WaitingReload, "Solicitando reload do equipamento...");
            if (requestReload is not null)
            {
                var reloadInstruction = _profile.RequiresManualIntervention && !string.IsNullOrEmpty(_profile.ManualInterventionPrompt)
                    ? _profile.ManualInterventionPrompt
                    : _profile.Method == BootInterruptMethod.CtrlC
                        ? "Desligue e religue (reload / power-cycle) o equipamento agora na chave de energia ou cabo de força. O agendador de interrupção (Ctrl+C) JÁ ESTÁ ATIVO e capturará o ROMMON durante o boot. Clique em OK assim que o equipamento for religado."
                        : "Desligue e religue (reload / power-cycle) o equipamento agora na chave de energia ou cabo de força. O agendador de interrupção (Break) JÁ ESTÁ ATIVO e capturará o ROMMON durante o boot. Clique em OK assim que o equipamento for religado.";

                await ProgressAsync($"Solicitando reload do equipamento ({_profile.Name} — interrupções já ativas)...", cancellationToken);
                await requestReload(reloadInstruction, cancellationToken);
            }
            else
            {
                await ProgressAsync($"Solicitado reload do equipamento. Monitorando boot...", cancellationToken);
            }

            // 3) Aguarda a conclusão da interrupção
            var rommonPrompt = await interruptTask;
            var capturedRommonKind = RommonSwitchPrompt.IsMatch(rommonPrompt) ? RommonKind.Switch : RommonKind.Router;

            // 4) Executa os passos de recuperação e zeramento
            await RunRecoveryStepsAsync(session, capturedRommonKind, stateMachine, cancellationToken);
        }
        catch
        {
            await interruptCts.CancelAsync();
            throw;
        }
    }

    public async Task<(DeviceAccessState State, RommonKind? RommonKind)> DiagnoseAccessStateAsync(DeviceSession session, CancellationToken ct)
    {
        if (!session.IsConnected)
            await session.ConnectRawAsync(ct);
        else
            await session.WriteLineAsync(string.Empty, ct);

        await ProgressAsync("Verificando atividade do terminal RS-232...", ct);

        try
        {
            var result = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("rommon-router", RommonRouterPrompt),
                    new StopCondition.LineRegex("rommon-switch", RommonSwitchPrompt),
                    new StopCondition.LineRegex("dialog", BootDialogPrompt),
                    new StopCondition.LineRegex("return", PressReturnPrompt),
                    new StopCondition.LineRegex("password", new Regex(@"(?i)(?:user|username|login|password|user access verification|secret)\s*[:?]")),
                    new StopCondition.LineRegex("prompt-priv", new Regex(@"#\s*$")),
                    new StopCondition.LineRegex("prompt-user", new Regex(@">\s*$")),
                    new StopCondition.LineRegex("any-output", new Regex(@"\S"))
                },
                _verifyTimeout,
                ct);

            var rawOutput = result.Output;
            var tail = rawOutput.Replace("\r", "").Replace("\n", " ").Trim();
            await ProgressAsync($"Conexão RS-232 ativa. Resposta: {Truncate(tail)}", ct);

            // 1. ROMMON
            if (result.Matched is StopCondition.LineRegex lr && lr.Name == "rommon-switch")
                return (DeviceAccessState.AlreadyInRommon, RommonKind.Switch);
            if (result.Matched is StopCondition.LineRegex lrR && lrR.Name == "rommon-router" || RommonRouterPrompt.IsMatch(rawOutput))
                return (DeviceAccessState.AlreadyInRommon, RommonKind.Router);

            // 2. Diálogo de Inicialização — responde "no" + ENTER para forçar início e verifica logs
            if (rawOutput.Contains("initial configuration dialog", StringComparison.OrdinalIgnoreCase) || rawOutput.Contains("System Configuration Dialog", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync("Detectado diálogo de configuração inicial. Respondendo 'no' + ENTER...", ct);
                await session.WriteLineAsync("no", ct);
                await Task.Delay(800, ct);
                await session.WriteLineAsync(string.Empty, ct); // ENTER para forçar "Press RETURN to get started"
                await Task.Delay(800, ct);
                // Verifica o que o IOS retornou após "no"
                try
                {
                    var after = await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.LineRegex("press-return", PressReturnPrompt),
                            new StopCondition.LineRegex("prompt-priv", new Regex(@"#\s*$")),
                            new StopCondition.LineRegex("prompt-user", new Regex(@">\s*$")),
                            new StopCondition.LineRegex("any", new Regex(@"\S"))
                        }, TimeSpan.FromSeconds(4), ct);
                    rawOutput += "\n" + after.Output;
                    tail = rawOutput.Replace("\r", "").Replace("\n", " ").Trim();
                    await ProgressAsync($"Após 'no': {Truncate(tail)}", ct);
                    if (after.Output.Contains("Press RETURN", StringComparison.OrdinalIgnoreCase))
                    {
                        await session.WriteLineAsync(string.Empty, ct);
                        await Task.Delay(500, ct);
                    }
                } catch { }
            }
            else if (rawOutput.Contains("Press RETURN to get started", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync(string.Empty, ct);
                await Task.Delay(500, ct);
            }

            // 3. Prompt aberto com # (Privilegiado)
            if (tail.EndsWith("#"))
            {
                return (DeviceAccessState.UnlockedPrompt, null);
            }

            // 4. Prompt de usuário (>) -> Testa se enable é livre
            if (tail.EndsWith(">"))
            {
                await session.WriteLineAsync("enable", ct);
                try
                {
                    var enableResult = await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.LineRegex("password", new Regex(@"(?i)(?:password|secret)\s*[:?]")),
                            new StopCondition.LineRegex("prompt-priv", new Regex(@"#\s*$")),
                            new StopCondition.LineRegex("prompt-user", new Regex(@">\s*$"))
                        },
                        TimeSpan.FromSeconds(3),
                        ct);

                    var enableTail = enableResult.Output.Replace("\r", "").Replace("\n", " ").Trim();
                    if (enableTail.EndsWith("#"))
                    {
                        return (DeviceAccessState.UnlockedPrompt, null);
                    }
                }
                catch (SessionTimeoutException) { }

                await ProgressAsync("Equipamento exige senha em modo privilegiado (enable). Prosseguindo para quebra via ROMMON...", ct);
                return (DeviceAccessState.PasswordLocked, null);
            }

            // 5. Exige senha ou login inicial
            if (Regex.IsMatch(rawOutput, @"(?i)(?:user|username|login|password|user access verification)\s*[:?]"))
            {
                return (DeviceAccessState.PasswordLocked, null);
            }

            return (DeviceAccessState.PasswordLocked, null);
        }
        catch (SessionTimeoutException)
        {
            // Tenta enviar um Enter de renovação caso a console esteja em silêncio
            try
            {
                await session.WriteLineAsync(string.Empty, ct);
                var retryResult = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("rommon-router", RommonRouterPrompt),
                        new StopCondition.LineRegex("rommon-switch", RommonSwitchPrompt),
                        new StopCondition.LineRegex("password", new Regex(@"(?i)(?:user|username|login|password|user access verification)\s*[:?]")),
                        new StopCondition.LineRegex("prompt-priv", new Regex(@"#\s*$")),
                        new StopCondition.LineRegex("prompt-user", new Regex(@">\s*$")),
                        new StopCondition.LineRegex("any-output", new Regex(@"\S"))
                    },
                    TimeSpan.FromSeconds(2),
                    ct);

                var tail2 = retryResult.Output.Replace("\r", "").Replace("\n", " ").Trim();
                if (retryResult.Matched is StopCondition.LineRegex rlr)
                {
                    if (rlr.Name == "rommon-switch")
                        return (DeviceAccessState.AlreadyInRommon, RommonKind.Switch);
                    if (rlr.Name == "rommon-router" || RommonRouterPrompt.IsMatch(tail2))
                        return (DeviceAccessState.AlreadyInRommon, RommonKind.Router);
                    if (tail2.EndsWith("#"))
                        return (DeviceAccessState.UnlockedPrompt, null);
                    if (tail2.EndsWith(">"))
                        return (DeviceAccessState.PasswordLocked, null);
                }
            }
            catch { }

            throw new DeviceSessionException(
                "Conexão RS-232 inativa: sem resposta do equipamento. Verifique se o cabo serial está conectado (ex: COM4 para FTDI) e se o equipamento está ligado.");
        }
    }

    public static async Task<bool> EnsurePrivilegedExecViewAsync(DeviceSession session, ProgressHandler? progress = null, CancellationToken ct = default)
    {
        for (int i = 0; i < 4; i++)
        {
            // Envia 'end' (ou Ctrl+Z) para sair de qualquer sub-menu / (config-if) / (config) direto para Privileged EXEC #
            await session.WriteLineAsync("end", ct);
            await Task.Delay(300, ct);

            var res = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("priv-prompt", new Regex(@"[^\(\r\n]+#\s*$", RegexOptions.Compiled)),
                    new StopCondition.LineRegex("config-prompt", new Regex(@"\([^\)\r\n]+\)#\s*$", RegexOptions.Compiled)),
                    new StopCondition.LineRegex("user-prompt", new Regex(@"[^\r\n]+>\s*$", RegexOptions.Compiled))
                },
                TimeSpan.FromSeconds(2),
                ct);

            var outText = res.Output.Trim();
            // Se estiver em prompt não-config terminado em # (ex: Router#)
            if (Regex.IsMatch(outText, @"^[^\(\r\n]+#\s*$"))
            {
                if (progress != null && i > 0)
                    await progress("[*] Retornado com sucesso ao menu privilegiado do Cisco (Privileged EXEC #).");
                return true;
            }

            if (outText.Contains("(", StringComparison.Ordinal) && outText.EndsWith("#"))
            {
                await session.WriteLineAsync("exit", ct);
                await Task.Delay(300, ct);
            }
            else if (outText.EndsWith(">"))
            {
                await session.WriteLineAsync("enable", ct);
                await Task.Delay(300, ct);
            }
        }
        return false;
    }

    private async Task ExecuteDirectCliResetAsync(DeviceSession session, CancellationToken ct)
    {
        // Garante que o equipamento saiu de qualquer sub-menu / (config) e está no modo privilegiado raiz (#)
        await EnsurePrivilegedExecViewAsync(session, _progress, ct);

        // Garante modo privilegiado (enable) antes de comandos administrativos
        if (session.Mode is not (ExecMode.PrivilegedExec or ExecMode.GlobalConfig) && (session.CurrentPrompt == null || !session.CurrentPrompt.EndsWith("#")))
        {
            await ProgressAsync("Entrando em modo privilegiado (enable)...", ct);
            await session.WriteLineAsync("enable", ct);
            await Task.Delay(300, ct);
            await WaitForPromptAsync(session, ct);
        }

        await session.WriteLineAsync("end", ct);
        await Task.Delay(200, ct);

        await ProgressAsync("Apagando configuração e removendo senha antiga (write erase)...", ct);
        await SendConfirmAsync(session, "write erase", EraseConfirm, waitForPrompt: true, ct);

        await ProgressAsync("Garantindo config-register 0x2102 (boot normal)...", ct);
        await session.WriteLineAsync("configure terminal", ct);
        await WaitForPromptAsync(session, ct);
        await session.WriteLineAsync("config-register 0x2102", ct);
        await WaitForPromptAsync(session, ct);
        await session.WriteLineAsync("end", ct);
        await WaitForPromptAsync(session, ct);

        await ProgressAsync("Salvando configuração limpa (write memory)...", ct);
        await session.WriteLineAsync("write memory", ct);
        await WaitForPromptAsync(session, ct);
    }

    private async Task<RommonKind> RunRecoveryStepsAsync(
        DeviceSession session,
        RommonKind rommon,
        RecoveryStateMachine stateMachine,
        CancellationToken ct)
    {
        stateMachine.TransitionTo(RecoveryState.ExecutingRecovery, "Executando procedimentos de limpeza de senha...");

        if (rommon == RommonKind.Switch)
        {
            await ProgressAsync("ROMMON detectado (switch). Preparando filesystem flash...", ct);
            await SendRommonCommandAsync(session, "flash_init", ct);
            await SendRommonCommandAsync(session, "load_helper", ct);
            await SendRommonCommandAsync(session, "dir flash:", ct);
            await SendRommonCommandAsync(session, "rename flash:config.text flash:config.text.old", ct);
            await SendRommonCommandAsync(session, "boot", ct, requirePromptReturn: false);
        }
        else
        {
            await ProgressAsync("ROMMON detectado (roteador Cisco 1900 / ISR). Definindo config-register 0x2142 (ignora startup-config)...", ct);
            await SendRommonCommandAsync(session, "confreg 0x2142", ct);
            await Task.Delay(500, ct);
            await ProgressAsync("Reiniciando equipamento a partir do ROMMON (reset)...", ct);
            await SendRommonCommandAsync(session, "reset", ct, requirePromptReturn: false);
        }

        await ProgressAsync("Aguardando boot da IOS sem startup-config (isso pode levar ~60-90s)...", ct);
        await SkipInitialDialogAsync(session, ct);

        // Aguarda estabilização do boot
        await Task.Delay(2000, ct);

        await ProgressAsync("Entrando em modo privilegiado (enable)...", ct);
        await session.WriteLineAsync(string.Empty, ct);
        await Task.Delay(200, ct);
        await session.WriteLineAsync("enable", ct);
        await Task.Delay(300, ct);
        await WaitForPromptAsync(session, ct);

        await ProgressAsync("Apagando configuração e removendo senha antiga (write erase)...", ct);
        await SendConfirmAsync(session, "write erase", EraseConfirm, waitForPrompt: true, ct);

        await ProgressAsync("Restaurando config-register 0x2102 (boot normal)...", ct);
        await session.WriteLineAsync("configure terminal", ct);
        await WaitForPromptAsync(session, ct);
        await session.WriteLineAsync("config-register 0x2102", ct);
        await WaitForPromptAsync(session, ct);
        await session.WriteLineAsync("end", ct);
        await WaitForPromptAsync(session, ct);

        await ProgressAsync("Salvando configuração limpa (write memory)...", ct);
        await session.WriteLineAsync("write memory", ct);
        await WaitForPromptAsync(session, ct);

        stateMachine.TransitionTo(RecoveryState.Completed, "Procedimento de quebra de senha e restauração de registrador concluído com sucesso.");
        await ProgressAsync("Procedimento concluído com sucesso! Equipamento desbloqueado, registrador 0x2102 restaurado e pronto para provisionamento.", ct);
        return rommon;
    }

    private async Task SkipInitialDialogAsync(DeviceSession session, CancellationToken ct)
    {
        var bootTimeout = _bootWait; // Timeout amplo para descompressão e boot do IOS (geralmente 1 a 3 min)
        var deadline = DateTime.UtcNow.Add(bootTimeout);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        await ProgressAsync("Aguardando inicialização do Cisco IOS e liberação do console (1 a 3 min)...", ct);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var elapsedSec = (int)sw.Elapsed.TotalSeconds;

            // Envia Enter para acordar console e desobstruir mensagens de syslog
            await session.WriteLineAsync(string.Empty, ct);

            try
            {
                var result = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("dialog", BootDialogPrompt),
                        new StopCondition.LineRegex("autoinstall", new Regex(@"(?i)terminate\s+autoinstall|autoinstall")),
                        new StopCondition.LineRegex("press-return", PressReturnPrompt),
                        new StopCondition.LineRegex("cisco-prompt", new Regex(@"(?i)^[A-Za-z0-9_.+()/-]+[>#]")),
                        new StopCondition.Prompt()
                    },
                    TimeSpan.FromSeconds(3),
                    ct);

                if (result.Matched is StopCondition.LineRegex lr)
                {
                    if (lr.Name == "dialog")
                    {
                        await ProgressAsync("[*] Diálogo inicial detectado — enviando 'no'...", ct);
                        await session.WriteLineAsync("no", ct);
                        await Task.Delay(800, ct);
                    }
                    else if (lr.Name == "autoinstall")
                    {
                        await ProgressAsync("[*] Diálogo autoinstall detectado — enviando 'yes'...", ct);
                        await session.WriteLineAsync("yes", ct);
                        await Task.Delay(800, ct);
                    }
                    else if (lr.Name == "press-return")
                    {
                        await ProgressAsync("[*] 'Press RETURN to get started' detectado — enviando ENTER...", ct);
                        await session.WriteLineAsync(string.Empty, ct);
                        await Task.Delay(500, ct);
                    }
                    else if (lr.Name == "cisco-prompt")
                    {
                        await ProgressAsync("[OK] Prompt do Cisco IOS detectado. Console pronto.", ct);
                        return;
                    }
                }
                else if (result.Matched is StopCondition.Prompt)
                {
                    await ProgressAsync("[OK] Prompt do Cisco IOS detectado. Console pronto.", ct);
                    return;
                }
            }
            catch (SessionTimeoutException)
            {
                await ProgressAsync($"Aguardando boot e prontidão do console Cisco IOS... ({elapsedSec}s)", ct);
                // Envia Enter para renovar prompt
                await session.WriteLineAsync(string.Empty, ct);
            }

            if (session.CurrentPrompt != null && (session.CurrentPrompt.EndsWith(">") || session.CurrentPrompt.EndsWith("#")))
            {
                await ProgressAsync($"[OK] Prompt do IOS detectado ({session.CurrentPrompt}). Console pronto.", ct);
                return;
            }
        }
    }

    private async Task WaitForPromptAsync(DeviceSession session, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(_commandTimeout);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.Prompt(),
                        new StopCondition.LineRegex("cisco-prompt", new Regex(@"(?i)^[A-Za-z0-9_.+()/-]+[>#]"))
                    },
                    TimeSpan.FromSeconds(3),
                    ct);

                return;
            }
            catch (SessionTimeoutException)
            {
                var sec = (int)sw.Elapsed.TotalSeconds;
                await ProgressAsync($"Aguardando resposta do console Cisco IOS... ({sec}s)", ct);
                // Envia Enter para renovar a linha do console se logs do IOS interferirem
                await session.WriteLineAsync(string.Empty, ct);
            }
        }

        throw new DeviceSessionException("Falha ao aguardar prompt do IOS após tempo limite.");
    }

    private async Task SendConfirmAsync(DeviceSession session, string command, Regex confirmRegex, bool waitForPrompt, CancellationToken ct)
    {
        await ProgressAsync($"Executando comando: '{command}'...", ct);
        await session.WriteLineAsync(command, ct);
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(15) && !ct.IsCancellationRequested)
        {
            try
            {
                var result = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("confirm", confirmRegex),
                        new StopCondition.Prompt()
                    },
                    TimeSpan.FromSeconds(3),
                    ct);

                if (result.Matched is StopCondition.LineRegex)
                {
                    await ProgressAsync("Confirmação solicitada pelo equipamento [confirm] — enviando ENTER...", ct);
                    await session.WriteLineAsync(string.Empty, ct);
                    if (waitForPrompt)
                        await WaitForPromptAsync(session, ct);
                    return;
                }
                else if (result.Matched is StopCondition.Prompt)
                {
                    return;
                }
            }
            catch (SessionTimeoutException)
            {
                await ProgressAsync($"Processando '{command}'... ({sw.Elapsed.Seconds}s)", ct);
                await session.WriteLineAsync(string.Empty, ct);
            }
        }
    }

    private async Task SendRommonCommandAsync(
        DeviceSession session,
        string command,
        CancellationToken ct,
        bool requirePromptReturn = true,
        int maxAttempts = 5)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            await ProgressAsync($"Enviando '{command}' (tentativa {attempt}/{maxAttempts})...", ct);
            await session.WriteLineAsync(command, ct);

            if (!requirePromptReturn)
            {
                await Task.Delay(2000, ct);
                return;
            }

            try
            {
                await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("rommon-router", RommonRouterPrompt),
                        new StopCondition.LineRegex("rommon-switch", RommonSwitchPrompt)
                    },
                    TimeSpan.FromSeconds(4),
                    ct);
                return;
            }
            catch (SessionTimeoutException)
            {
                await ProgressAsync($"Comando '{command}' não confirmado — repetindo...", ct);
                await Task.Delay(500, ct);
            }
        }

        throw new DeviceSessionException($"Comando ROMMON '{command}' não foi confirmado após {maxAttempts} tentativas.");
    }

    private static string Truncate(string text, int max = 240)
    {
        var flat = text.Replace("\r", "").Replace("\n", " ").Trim();
        return flat.Length <= max ? flat : "..." + flat[^max..];
    }

    private Task ProgressAsync(string message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _progress?.Invoke(message) ?? Task.CompletedTask;
    }

    public enum RommonKind
    {
        Router,
        Switch
    }
}