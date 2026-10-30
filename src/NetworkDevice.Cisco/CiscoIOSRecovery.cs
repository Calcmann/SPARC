using System.Text;
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
        @"(?i)(?:rommon\s*\S*\s*>|common\s*\S*\s*>|cannot determine first executable|cannot load image)",
        RegexOptions.Compiled);

    private static readonly Regex RommonSwitchPrompt = new(
        @"(?i)^switch\s*[:>]",
        RegexOptions.Compiled);

    private static readonly Regex BootDialogPrompt = new(
        @"(?i)(?:system\s+configuration\s+dialog|initial\s+configuration\s+dialog|basic\s+management\s+setup)",
        RegexOptions.Compiled);

    private static readonly Regex PressReturnPrompt = new(
        @"(?i)press\s+return\s+to\s+get\s+started",
        RegexOptions.Compiled);

    private static readonly Regex EraseConfirm = new(
        @"(?i)(?:continue\?|\[confirm\])",
        RegexOptions.Compiled);

    private static readonly Regex StrictCiscoPromptRegex = new(
        @"^[A-Za-z0-9_\-\.]+(?:\(config[^\)]*\))?\s*[>#]\s*$",
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
        CancellationToken cancellationToken = default,
        bool forceReload = false)
    {
        var stateMachine = new RecoveryStateMachine(session.Transport, _profile);
        stateMachine.StateChanged += (state, msg) =>
        {
            StateChanged?.Invoke(state, msg);
            if (state == RecoveryState.Interrupting)
            {
                _ = ProgressAsync($"[*] {msg}", cancellationToken);
            }
            else if (state == RecoveryState.RommonDetected)
            {
                _ = ProgressAsync($"\n[OK] {msg}", cancellationToken);
            }
            else if (state == RecoveryState.WaitingReload)
            {
                _ = ProgressAsync($"[INSTRUÇÃO] {msg}", cancellationToken);
            }
            else
            {
                _ = ProgressAsync($"[{state}] {msg}", cancellationToken);
            }
        };

        stateMachine.OutputReceived += text =>
        {
            session.EmitRawOutput(text);
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && trimmed.Length > 2)
                {
                    _ = ProgressAsync($"  │ {trimmed}", cancellationToken);
                }
            }
        };

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
            await ProgressAsync("[*] Configurando registrador 0x2142 no ROMMON (ignora startup-config e senhas)...", cancellationToken);
            try { await SendRommonCommandAsync(session, "confreg 0x2142", cancellationToken, requirePromptReturn: true); await Task.Delay(300, cancellationToken); } catch { }
            stateMachine.TransitionTo(RecoveryState.Completed, "Equipamento pronto no ROMMON para carga de firmware TFTP.");
            await ProgressAsync("\n=================================================================", cancellationToken);
            await ProgressAsync("  [OK] ROTEADOR NO MODO ROMMON (SEM FIRMWARE / FLASH VAZIA)", cancellationToken);
            await ProgressAsync("  • Quebra de senha (confreg 0x2142) configurada no ROMMON.", cancellationToken);
            await ProgressAsync("  • Equipamento aguardando gravação de imagem de firmware via TFTP.", cancellationToken);
            await ProgressAsync("  • Fase 1 concluída: Avançando para Atualização de Firmware via TFTP (Fase 2)...", cancellationToken);
            await ProgressAsync("=================================================================\n", cancellationToken);
            return;
        }

        if (accessState == DeviceAccessState.UnlockedPrompt)
        {
            stateMachine.TransitionTo(RecoveryState.ExecutingRecovery, "Equipamento acessível sem senha. Executando zeramento direto via CLI...");
            await ProgressAsync("Equipamento acessível sem senha (prompt aberto). Zeramento direto via CLI...", cancellationToken);
            await ExecuteDirectCliResetAsync(session, forceReload, cancellationToken);
            stateMachine.TransitionTo(RecoveryState.Completed, "Procedimento de zeramento concluído diretamente via CLI.");
            await ProgressAsync("Equipamento zerado e registrador 0x2102 garantido. Pronto para provisionamento!", cancellationToken);
            return;
        }

        // 2) Equipamento bloqueado por senha (PasswordLocked): Solicita reload, limpa buffers e inicia o agendador de interrupção
        await ProgressAsync("Equipamento bloqueado por senha ou não inicializado. Preparando processo de quebra via ROMMON...", cancellationToken);

        stateMachine.TransitionTo(RecoveryState.WaitingReload, "Solicitando reload do equipamento...");
        if (requestReload is not null)
        {
            var reloadInstruction = _profile.RequiresManualIntervention && !string.IsNullOrEmpty(_profile.ManualInterventionPrompt)
                ? _profile.ManualInterventionPrompt
                : _profile.Method == BootInterruptMethod.CtrlC
                    ? "Desligue e religue (reload / power-cycle) o equipamento agora na chave de energia ou cabo de força (aguarde 5 segundos desligado). O software enviará pulsos contínuos de Ctrl+C para interceptar o ROMMON. Clique em OK assim que o equipamento for religado."
                    : "Desligue e religue (reload / power-cycle) o equipamento agora na chave de energia ou cabo de força (aguarde 5 segundos desligado). O software enviará pulsos contínuos de Break para interceptar o ROMMON. Clique em OK assim que o equipamento for religado.";

            await ProgressAsync($"Solicitando reload do equipamento ({_profile.Name})...", cancellationToken);
            await requestReload(reloadInstruction, cancellationToken);
            await ProgressAsync("Confirmação recebida do operador. Monitorando captura do ROMMON...", cancellationToken);
        }
        else
        {
            await ProgressAsync($"Solicitado reload do equipamento. Monitorando boot...", cancellationToken);
        }

        // Drena buffers residuais da console antes de ligar o monitor de boot
        try
        {
            var drainBuf = new byte[2048];
            while (await session.Transport.ReadAsync(drainBuf, cancellationToken) > 0) { }
        }
        catch { }

        await ProgressAsync($"Iniciando pulsos de interrupção ({_profile.Name}) e aguardando ROMMON...", cancellationToken);
        using var interruptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var rommonPrompt = await stateMachine.RunInterruptPhaseAsync(interruptCts.Token);
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
            if (rawOutput.Contains("initial configuration dialog", StringComparison.OrdinalIgnoreCase) ||
                rawOutput.Contains("System Configuration Dialog", StringComparison.OrdinalIgnoreCase) ||
                rawOutput.Contains("[yes/no]", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync("Detectado diálogo de configuração inicial. Respondendo 'no'...", ct);
                await session.WriteLineAsync("no", ct);
                await Task.Delay(800, ct);

                try
                {
                    var after = await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.LineRegex("autoinstall", new Regex(@"(?i)terminate\s+autoinstall.*\[yes(?:/no)?\]|\[yes\]:")),
                            new StopCondition.LineRegex("press-return", PressReturnPrompt),
                            new StopCondition.LineRegex("prompt-priv", new Regex(@"#\s*$")),
                            new StopCondition.LineRegex("prompt-user", new Regex(@">\s*$")),
                            new StopCondition.LineRegex("any", new Regex(@"\S"))
                        }, TimeSpan.FromSeconds(5), ct);

                    rawOutput += "\n" + after.Output;
                    if (after.Output.Contains("terminate autoinstall", StringComparison.OrdinalIgnoreCase) || after.Output.Contains("[yes]:", StringComparison.OrdinalIgnoreCase))
                    {
                        await session.WriteLineAsync("yes", ct);
                        await Task.Delay(600, ct);
                    }

                    await session.WriteLineAsync(string.Empty, ct);
                    await Task.Delay(400, ct);

                    var promptCheck = await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.LineRegex("prompt-priv", new Regex(@"#\s*$")),
                            new StopCondition.LineRegex("prompt-user", new Regex(@">\s*$")),
                            new StopCondition.LineRegex("press-return", PressReturnPrompt),
                            new StopCondition.LineRegex("password", new Regex(@"(?i)(?:user|username|login|password|user access verification|secret)\s*[:?]"))
                        }, TimeSpan.FromSeconds(4), ct);

                    rawOutput += "\n" + promptCheck.Output;
                    tail = rawOutput.Replace("\r", "").Replace("\n", " ").Trim();
                    await ProgressAsync($"Após resposta ao diálogo inicial: {Truncate(tail)}", ct);
                }
                catch { }
            }
            else if (rawOutput.Contains("Press RETURN to get started", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync(string.Empty, ct);
                await Task.Delay(500, ct);
                try
                {
                    var promptCheck = await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.LineRegex("prompt-priv", new Regex(@"#\s*$")),
                            new StopCondition.LineRegex("prompt-user", new Regex(@">\s*$"))
                        }, TimeSpan.FromSeconds(3), ct);
                    rawOutput += "\n" + promptCheck.Output;
                    tail = rawOutput.Replace("\r", "").Replace("\n", " ").Trim();
                }
                catch { }
            }

            // 3. Prompt aberto com # (Privilegiado)
            if (tail.EndsWith("#") || Regex.IsMatch(tail, @"[^\r\n]+#\s*$"))
            {
                return (DeviceAccessState.UnlockedPrompt, null);
            }

            // 4. Prompt de usuário (>) -> Testa se enable é livre
            if (tail.EndsWith(">") || Regex.IsMatch(tail, @"[^\r\n]+>\s*$"))
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
                    if (enableTail.EndsWith("#") || Regex.IsMatch(enableTail, @"[^\r\n]+#\s*$"))
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
        for (int i = 0; i < 5; i++)
        {
            // Envia ENTER limpo para sondar o prompt atual
            await session.WriteLineAsync(string.Empty, ct);
            await Task.Delay(250, ct);

            var res = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("press-return", PressReturnPrompt),
                    new StopCondition.LineRegex("config-prompt", new Regex(@"\([^\)\r\n]+\)#\s*$", RegexOptions.Compiled)),
                    new StopCondition.LineRegex("priv-prompt", new Regex(@"[^\(\r\n]+#\s*$", RegexOptions.Compiled)),
                    new StopCondition.LineRegex("user-prompt", new Regex(@"[^\r\n]+>\s*$", RegexOptions.Compiled)),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(3),
                ct);

            var outText = res.Output.Trim();

            if (outText.Contains("Press RETURN", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync(string.Empty, ct);
                await Task.Delay(300, ct);
                continue;
            }

            // Se estiver em modo de configuração (ex: Router(config)# ou Router(config-if)#)
            if (Regex.IsMatch(outText, @"\([^\)\r\n]+\)#\s*$"))
            {
                await session.WriteLineAsync("end", ct);
                await Task.Delay(400, ct);
                continue;
            }

            // Se estiver em prompt não-config terminado em # (ex: Router#)
            if (Regex.IsMatch(outText, @"[^\(\r\n]+#\s*$"))
            {
                if (progress != null && i > 0)
                    await progress("[*] Retornado com sucesso ao menu privilegiado do Cisco (Privileged EXEC #).");
                return true;
            }

            // Se estiver em modo de usuário (ex: Router>)
            if (Regex.IsMatch(outText, @"[^\r\n]+>\s*$"))
            {
                var adapter = new CiscoIOSAdapter(candidatePasswords: new[] { "PRO1AN" });
                try
                {
                    await adapter.EnterPrivilegedExecAsync(session, cancellationToken: ct);
                    if (progress != null)
                        await progress("[*] Retornado com sucesso ao menu privilegiado do Cisco (Privileged EXEC #).");
                    return true;
                }
                catch
                {
                    await session.WriteLineAsync("enable", ct);
                    await Task.Delay(400, ct);
                    continue;
                }
            }
        }
        return false;
    }

    private async Task ExecuteDirectCliResetAsync(DeviceSession session, bool forceReload, CancellationToken ct)
    {
        // Garante que o equipamento está no modo privilegiado raiz (#)
        await EnsurePrivilegedExecViewAsync(session, _progress, ct);

        // Desativa lookup DNS para evitar travamentos com "Translating... domain server" e garante config-register 0x2102
        await session.WriteLineAsync("configure terminal", ct);
        await WaitForPromptAsync(session, ct);
        await session.WriteLineAsync("no ip domain-lookup", ct);
        await WaitForPromptAsync(session, ct);
        await session.WriteLineAsync("config-register 0x2102", ct);
        await WaitForPromptAsync(session, ct);
        await session.WriteLineAsync("end", ct);
        await WaitForPromptAsync(session, ct);

        // Apaga base de dados de VLANs residuais (vlan.dat)
        await ProgressAsync("Removendo base de dados de VLANs residuais (vlan.dat)...", ct);
        try { await session.SendCommandAsync("delete /force flash:vlan.dat", TimeSpan.FromSeconds(5), ct); } catch { }
        try { await session.SendCommandAsync("delete /force bootflash:vlan.dat", TimeSpan.FromSeconds(5), ct); } catch { }
        try { await session.SendCommandAsync("delete /force nvram:vlan.dat", TimeSpan.FromSeconds(5), ct); } catch { }

        await ProgressAsync("Apagando configuração e removendo senha antiga (write erase)...", ct);
        await SendConfirmAsync(session, "write erase", EraseConfirm, waitForPrompt: true, ct);

        if (forceReload)
        {
            await ProgressAsync("Reiniciando equipamento para aplicar o padrão de fábrica limpo (reload)...", ct);
            session.ResetPrompt();
            var reloadConditions = new StopCondition[]
            {
                new StopCondition.Contains("SaveConfirm", "[yes/no]"),
                new StopCondition.Contains("ProceedConfirm", "[confirm]"),
                new StopCondition.LineRegex("prompt", StrictCiscoPromptRegex)
            };
            var reloadRes = await session.SendExpectAsync("reload", reloadConditions, TimeSpan.FromSeconds(15), ct);
            if (reloadRes.Output.Contains("[yes/no]", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendRawAsync("no\r", ct);
                await Task.Delay(500, ct);
            }
            await session.SendRawAsync("\r", ct);

            await ProgressAsync("Aguardando reinicialização do Cisco IOS após reload...", ct);
            await SkipInitialDialogAsync(session, ct);

            try
            {
                var recoveryAdapter = new CiscoIOSAdapter(candidatePasswords: new[] { "PRO1AN", "" });
                await recoveryAdapter.EnterPrivilegedExecAsync(session, cancellationToken: ct);
            }
            catch
            {
                await session.WriteLineAsync("enable", ct);
                await Task.Delay(300, ct);
                await WaitForPromptAsync(session, ct);
            }
        }

        await ProgressAsync("Configuração limpa e registrador 0x2102 garantido.", ct);
    }

    private async Task<RommonKind> RunRecoveryStepsAsync(
        DeviceSession session,
        RommonKind rommon,
        RecoveryStateMachine stateMachine,
        CancellationToken ct)
    {
        stateMachine.TransitionTo(RecoveryState.ExecutingRecovery, "Executando procedimentos de limpeza de senha...");

        // Sincroniza e desobstrui a linha do terminal ROMMON com ENTER limpo antes de executar comandos
        try
        {
            await session.WriteLineAsync(string.Empty, ct);
            await Task.Delay(300, ct);
        }
        catch { }

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
            await ProgressAsync("ROMMON detectado (roteador Cisco 1900 / ISR / 841 / 921). Definindo config-register 0x2142 (ignora startup-config)...", ct);
            await SendRommonCommandAsync(session, "confreg 0x2142", ct);
            await Task.Delay(500, ct);
            await ProgressAsync("Reiniciando equipamento a partir do ROMMON (reset)...", ct);
            await SendRommonCommandAsync(session, "reset", ct, requirePromptReturn: false);
        }

        // 1. Reseta prompt residual da sessão para nunca reutilizar 'rommon 1 >'
        session.ResetPrompt();

        // 2. Aguarda a reinicialização e descompressão do Cisco IOS com bypass de configuração (0x2142)
        await ProgressAsync("Aguardando boot da IOS sem startup-config (isso pode levar ~60-120s)...", ct);
        await SkipInitialDialogAsync(session, ct);

        // 3. Aguarda estabilização do console pós-boot
        await Task.Delay(1000, ct);

        // 4. Entra em modo privilegiado (enable) - sem senha pois startup-config foi ignorado (0x2142)
        await ProgressAsync("Entrando em modo privilegiado (enable)...", ct);
        var recoveryAdapter = new CiscoIOSAdapter(candidatePasswords: new[] { "PRO1AN", "" });
        try
        {
            await recoveryAdapter.EnterPrivilegedExecAsync(session, cancellationToken: ct);
        }
        catch
        {
            await session.WriteLineAsync("enable", ct);
            await Task.Delay(300, ct);
            await WaitForPromptAsync(session, ct);
        }

        // 5. Apaga permanentemente configurações e senhas antigas da NVRAM
        await ProgressAsync("Apagando configuração e removendo senha antiga (write erase)...", ct);
        await SendConfirmAsync(session, "write erase", EraseConfirm, waitForPrompt: true, ct);

        // 6. Restaura config-register 0x2102 para que próximos boots carreguem startup-config normal (roteadores)
        if (rommon == RommonKind.Router)
        {
            await ProgressAsync("Restaurando config-register 0x2102 (boot normal)...", ct);
            await session.WriteLineAsync("configure terminal", ct);
            await WaitForPromptAsync(session, ct);
            await session.WriteLineAsync("config-register 0x2102", ct);
            await WaitForPromptAsync(session, ct);
            await session.WriteLineAsync("end", ct);
            await WaitForPromptAsync(session, ct);
        }

        // 7. Salva a configuração limpa
        await ProgressAsync("Salvando configuração limpa (write memory)...", ct);
        await session.WriteLineAsync("write memory", ct);
        await WaitForPromptAsync(session, ct);

        stateMachine.TransitionTo(RecoveryState.Completed, "Procedimento de quebra de senha e restauração de registrador concluído com sucesso.");
        await ProgressAsync("Procedimento concluído com sucesso! Equipamento desbloqueado, registrador 0x2102 restaurado e pronto para provisionamento.", ct);
        return rommon;
    }

    private async Task SkipInitialDialogAsync(DeviceSession session, CancellationToken ct)
    {
        var bootTimeout = _bootWait; // Timeout amplo para descompressão e boot do IOS (geralmente 3 a 5 min)
        var deadline = DateTime.UtcNow.Add(bootTimeout);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool decompressionCompleted = false;
        bool decompressionReported = false;
        bool bootCommandSent = false;
        var lastKeepAlive = DateTime.UtcNow;

        await ProgressAsync("Aguardando inicialização do Cisco IOS e liberação do console (1 a 3 min)...", ct);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var elapsedSec = (int)sw.Elapsed.TotalSeconds;

            // Só envia ENTER após 30 segundos ou se descompressão concluída, com intervalo mínimo de 4s
            // (evita floodar o console serial durante bootstrap e auto-descompressão da DRAM)
            if ((elapsedSec >= 30 || decompressionCompleted || _bootWait <= TimeSpan.FromSeconds(30)) && (DateTime.UtcNow - lastKeepAlive).TotalSeconds >= 4)
            {
                lastKeepAlive = DateTime.UtcNow;
                await session.SendRawAsync("\r\n", ct);
            }

            try
            {
                var result = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("rommon-prompt", RommonRouterPrompt),
                        new StopCondition.Contains("decompressing", "Self decompressing"),
                        new StopCondition.Contains("ios_software", "Cisco IOS Software"),
                        new StopCondition.LineRegex("terminate-autoinstall", new Regex(@"(?i)terminate\s+autoinstall|cancel\s+autoinstall")),
                        new StopCondition.LineRegex("initial-dialog", BootDialogPrompt),
                        new StopCondition.LineRegex("yes-no-choice", new Regex(@"\?\s*\[yes/no\]")),
                        new StopCondition.LineRegex("press-return", PressReturnPrompt),
                        new StopCondition.LineRegex("cisco-prompt", StrictCiscoPromptRegex)
                    },
                    TimeSpan.FromSeconds(3),
                    ct);

                if (result.Matched is StopCondition.Contains c)
                {
                    if (c.Text.Contains("Self decompressing") && !decompressionReported)
                    {
                        decompressionReported = true;
                        await ProgressAsync("  │ [BOOT] Descompactando imagem Cisco IOS na memória DRAM (Self decompressing)...", ct);
                    }
                    else if (c.Text.Contains("Cisco IOS Software"))
                    {
                        decompressionCompleted = true;
                        await ProgressAsync("  │ [BOOT] Kernel Cisco IOS em execução. Aguardando serviços...", ct);
                    }
                    continue;
                }

                if (result.Output.Contains("[OK]") && decompressionReported && !decompressionCompleted)
                {
                    decompressionCompleted = true;
                    await ProgressAsync("  │ [BOOT] Descompressão da imagem IOS concluída com sucesso! [OK]", ct);
                }

                if (result.Matched is StopCondition.LineRegex lr)
                {
                    if (lr.Name == "rommon-prompt")
                    {
                        if (!bootCommandSent)
                        {
                            bootCommandSent = true;
                            await ProgressAsync("[*] Prompt ROMMON pós-reset detectado — iniciando boot ('boot')...", ct);
                            await session.SendRawAsync("boot\r", ct);
                            await Task.Delay(300, ct);
                        }
                        continue;
                    }
                    else if (lr.Name == "terminate-autoinstall")
                    {
                        await ProgressAsync("[*] Diálogo autoinstall detectado — enviando 'yes' para cancelar...", ct);
                        await session.WriteLineAsync("yes", ct);
                        await Task.Delay(500, ct);
                    }
                    else if (lr.Name == "initial-dialog")
                    {
                        await ProgressAsync("[*] Diálogo de configuração inicial detectado — enviando 'no'...", ct);
                        await session.WriteLineAsync("no", ct);
                        await Task.Delay(500, ct);
                    }
                    else if (lr.Name == "yes-no-choice")
                    {
                        var answer = result.Output.Contains("autoinstall", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
                        await ProgressAsync($"[*] Diálogo [yes/no] detectado — enviando '{answer}'...", ct);
                        await session.WriteLineAsync(answer, ct);
                        await Task.Delay(500, ct);
                    }
                    else if (lr.Name == "press-return")
                    {
                        await ProgressAsync("[*] 'Press RETURN to get started' detectado — enviando ENTER...", ct);
                        await session.WriteLineAsync(string.Empty, ct);
                        await Task.Delay(500, ct);
                    }
                    else if (lr.Name == "cisco-prompt")
                    {
                        var line = result.Output.Trim();
                        // Rejeita qualquer falso positivo de rommon ou switch:
                        if (line.StartsWith("rommon", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("switch:", StringComparison.OrdinalIgnoreCase) ||
                            line.StartsWith("common", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // BLINDAGEM ANTI-ECO PRÉ-BOOT:
                        // O roteador Cisco não inicializa o IOS completo em menos de 30 segundos (em ambiente real).
                        if (_bootWait > TimeSpan.FromSeconds(30) && elapsedSec < 30 && !decompressionCompleted)
                        {
                            continue;
                        }

                        await ProgressAsync("[OK] Prompt do Cisco IOS detectado. Console pronto.", ct);
                        return;
                    }
                }
            }
            catch (SessionTimeoutException)
            {
                var phase = decompressionReported ? "descompactando imagem" : (elapsedSec < 30 ? "reiniciando hardware" : "carregando sistema");
                await ProgressAsync($"Aguardando boot e prontidão do console Cisco IOS ({phase}... {elapsedSec}s)", ct);
            }

            // Checagem segura de CurrentPrompt (somente se não for rommon/switch e já tiver passado tempo de boot)
            if (session.CurrentPrompt != null &&
                !session.CurrentPrompt.StartsWith("rommon", StringComparison.OrdinalIgnoreCase) &&
                !session.CurrentPrompt.StartsWith("switch:", StringComparison.OrdinalIgnoreCase) &&
                StrictCiscoPromptRegex.IsMatch(session.CurrentPrompt) &&
                (_bootWait <= TimeSpan.FromSeconds(30) || elapsedSec >= 30 || decompressionCompleted))
            {
                await ProgressAsync($"[OK] Prompt do IOS detectado ({session.CurrentPrompt}). Console pronto.", ct);
                return;
            }
        }

        throw new DeviceSessionException("Tempo esgotado aguardando inicialização do Cisco IOS após o reload.");
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
                        new StopCondition.LineRegex("terminate-autoinstall", new Regex(@"(?i)terminate\s+autoinstall|cancel\s+autoinstall")),
                        new StopCondition.LineRegex("initial-dialog", BootDialogPrompt),
                        new StopCondition.LineRegex("yes-no-choice", new Regex(@"\?\s*\[yes/no\]")),
                        new StopCondition.LineRegex("press-return", PressReturnPrompt),
                        new StopCondition.LineRegex("cisco-prompt", StrictCiscoPromptRegex)
                    },
                    TimeSpan.FromSeconds(3),
                    ct);

                if (result.Matched is StopCondition.LineRegex lr)
                {
                    if (lr.Name == "terminate-autoinstall")
                    {
                        await session.WriteLineAsync("yes", ct);
                        await Task.Delay(500, ct);
                        continue;
                    }
                    if (lr.Name == "initial-dialog")
                    {
                        await session.WriteLineAsync("no", ct);
                        await Task.Delay(500, ct);
                        continue;
                    }
                    if (lr.Name == "yes-no-choice")
                    {
                        var answer = result.Output.Contains("autoinstall", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
                        await session.WriteLineAsync(answer, ct);
                        await Task.Delay(500, ct);
                        continue;
                    }
                    if (lr.Name == "press-return")
                    {
                        await session.WriteLineAsync(string.Empty, ct);
                        await Task.Delay(500, ct);
                        continue;
                    }
                    if (lr.Name == "cisco-prompt")
                    {
                        var last = session.CurrentPrompt ?? result.Output.Trim();
                        if (!last.StartsWith("rommon", StringComparison.OrdinalIgnoreCase) && !last.StartsWith("switch:", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                }
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
                        new StopCondition.LineRegex("cisco-prompt", StrictCiscoPromptRegex)
                    },
                    TimeSpan.FromSeconds(3),
                    ct);

                if (result.Matched is StopCondition.LineRegex lr)
                {
                    if (lr.Name == "confirm")
                    {
                        await ProgressAsync("Confirmação solicitada pelo equipamento [confirm] — enviando ENTER...", ct);
                        await session.WriteLineAsync(string.Empty, ct);
                        if (waitForPrompt)
                            await WaitForPromptAsync(session, ct);
                        return;
                    }
                    else if (lr.Name == "cisco-prompt")
                    {
                        var last = session.CurrentPrompt ?? result.Output.Trim();
                        if (!last.StartsWith("rommon", StringComparison.OrdinalIgnoreCase) && !last.StartsWith("switch:", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
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
                await ProgressAsync($"Comando '{command}' não confirmado — renovando linha com ENTER e repetindo...", ct);
                try { await session.WriteLineAsync(string.Empty, ct); } catch { }
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