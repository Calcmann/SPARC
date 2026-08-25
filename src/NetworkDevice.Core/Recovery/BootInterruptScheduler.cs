using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Recovery;

public sealed class BootInterruptScheduler
{
    private static readonly ReadOnlyMemory<byte> CtrlCBytes = new(new byte[] { 0x03 });
    private static readonly ReadOnlyMemory<byte> CtrlBBytes = new(new byte[] { 0x02 });
    private static readonly ReadOnlyMemory<byte> CtrlDBytes = new(new byte[] { 0x04 });
    private static readonly ReadOnlyMemory<byte> EscBytes = new(new byte[] { 0x1B });

    private readonly ITransport _transport;
    private readonly BootInterruptProfile _profile;

    public BootInterruptScheduler(ITransport transport, BootInterruptProfile profile)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public int TransmissionsCount { get; private set; }

    public event Action<int, string>? OnBurstSent;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_profile.Method == BootInterruptMethod.None)
            return;

        // Aguarda delay inicial configurado
        if (_profile.InitialDelay > TimeSpan.Zero)
            await Task.Delay(_profile.InitialDelay, cancellationToken);

        var deadline = DateTime.UtcNow.Add(_profile.MaxWindow);

        while (!cancellationToken.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            if (TransmissionsCount >= _profile.MaxTotalTransmissions)
                break;

            // Executa rajada controlada
            var burst = Math.Min(_profile.BurstCount, _profile.MaxTotalTransmissions - TransmissionsCount);
            for (var i = 0; i < burst; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await SendSingleSignalAsync(cancellationToken);
                TransmissionsCount++;

                if (i < burst - 1 && _profile.BurstInterval > TimeSpan.Zero)
                    await Task.Delay(_profile.BurstInterval, cancellationToken);
            }

            OnBurstSent?.Invoke(TransmissionsCount, _profile.Method.ToString());

            // Intervalo entre rajadas (sem flood na console)
            if (_profile.RetryInterval > TimeSpan.Zero)
                await Task.Delay(_profile.RetryInterval, cancellationToken);
        }
    }

    private async Task SendSingleSignalAsync(CancellationToken ct)
    {
        switch (_profile.Method)
        {
            case BootInterruptMethod.CtrlC:
                await _transport.WriteAsync(CtrlCBytes, ct);
                break;

            case BootInterruptMethod.CtrlB:
                await _transport.WriteAsync(CtrlBBytes, ct);
                break;

            case BootInterruptMethod.CtrlD:
                await _transport.WriteAsync(CtrlDBytes, ct);
                break;

            case BootInterruptMethod.Break:
            case BootInterruptMethod.CtrlBreak:
                await _transport.SendBreakAsync(ct);
                break;

            case BootInterruptMethod.Esc:
                await _transport.WriteAsync(EscBytes, ct);
                break;

            case BootInterruptMethod.Dual:
                await _transport.SendBreakAsync(ct);
                await _transport.WriteAsync(CtrlCBytes, ct);
                break;

            case BootInterruptMethod.None:
            default:
                break;
        }
    }
}
