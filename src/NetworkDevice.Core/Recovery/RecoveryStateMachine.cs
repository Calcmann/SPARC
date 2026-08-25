using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Recovery;

public sealed class RecoveryStateMachine
{
    private readonly ITransport _transport;
    private readonly BootInterruptProfile _profile;

    public RecoveryStateMachine(ITransport transport, BootInterruptProfile profile)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public RecoveryState State { get; private set; } = RecoveryState.Disconnected;

    public event Action<RecoveryState, string>? StateChanged;
    public event Action<string>? OutputReceived;

    public void TransitionTo(RecoveryState newState, string message)
    {
        State = newState;
        StateChanged?.Invoke(newState, message);
    }

    public async Task<string> RunInterruptPhaseAsync(CancellationToken cancellationToken)
    {
        TransitionTo(RecoveryState.Interrupting, $"Iniciando monitoramento de boot e interrupções ({_profile.Name})...");

        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var schedulerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var monitor = new BootMonitor(_transport, _profile);
        var scheduler = new BootInterruptScheduler(_transport, _profile);

        var rommonPromptTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var osBootTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        monitor.EventReceived += evt =>
        {
            if (evt.Type == BootEventType.Output)
            {
                OutputReceived?.Invoke(evt.Text);
            }
            else if (evt.Type == BootEventType.RommonDetected)
            {
                rommonPromptTcs.TrySetResult(evt.Line ?? evt.Text);
            }
            else if (evt.Type == BootEventType.OsBootDetected && _profile.OsBootPolicy == OsBootPolicy.TerminalFail)
            {
                osBootTcs.TrySetResult(evt.Line ?? evt.Text);
            }
        };

        scheduler.OnBurstSent += (count, method) =>
        {
            StateChanged?.Invoke(RecoveryState.Interrupting, $"Enviadas {count} interrupções ({method}) — aguardando ROMMON...");
        };

        // Inicia RX contínuo
        var rxTask = Task.Run(() => monitor.RunAsync(monitorCts.Token), CancellationToken.None);

        // Inicia TX escalonado (se aplicável)
        var txTask = Task.Run(() => scheduler.RunAsync(schedulerCts.Token), CancellationToken.None);

        var timeoutTask = Task.Delay(_profile.MaxWindow, cancellationToken);

        try
        {
            var completed = await Task.WhenAny(rommonPromptTcs.Task, osBootTcs.Task, timeoutTask);

            // Cancela TX imediatamente (best-effort)
            await schedulerCts.CancelAsync();

            if (completed == rommonPromptTcs.Task)
            {
                var prompt = await rommonPromptTcs.Task;
                TransitionTo(RecoveryState.RommonDetected, $"ROMMON capturado com sucesso: '{prompt}'.");
                await monitorCts.CancelAsync();
                return prompt;
            }

            if (completed == osBootTcs.Task)
            {
                var line = await osBootTcs.Task;
                TransitionTo(RecoveryState.Failed, $"BOOT_INTERRUPTION_FAILED: O equipamento ignorou a interrupção e iniciou o carregamento normal do SO ({line}).");
                await monitorCts.CancelAsync();
                throw new BootInterruptionFailedException(
                    "BOOT_INTERRUPTION_FAILED: O equipamento não respondeu ao sinal de interrupção e continuou o boot do sistema operacional.",
                    reason: "OS_BOOT_DETECTED",
                    matchedBootPattern: line,
                    capturedOutput: monitor.CapturedOutput);
            }

            // Timeout da janela
            TransitionTo(RecoveryState.Failed, "BOOT_INTERRUPTION_TIMEOUT: Não foi possível capturar o ROMMON dentro do tempo limite da janela de boot.");
            await monitorCts.CancelAsync();
            throw new BootInterruptionFailedException(
                $"BOOT_INTERRUPTION_TIMEOUT: Tempo esgotado ({_profile.MaxWindow.TotalSeconds}s) aguardando ROMMON.",
                reason: "TIMEOUT",
                capturedOutput: monitor.CapturedOutput);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await schedulerCts.CancelAsync();
            await monitorCts.CancelAsync();
            TransitionTo(RecoveryState.Failed, "Operação cancelada pelo operador.");
            throw;
        }
        finally
        {
            await Task.WhenAll(
                txTask.ContinueWith(_ => { }),
                rxTask.ContinueWith(_ => { }));
        }
    }
}
