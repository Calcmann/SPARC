using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Recovery;

public sealed class BootInterruptScheduler
{
    private static readonly ReadOnlyMemory<byte> CtrlCBytes = new(new byte[] { 0x03 });
    private static readonly ReadOnlyMemory<byte> CtrlBBytes = new(new byte[] { 0x02 });
    private static readonly ReadOnlyMemory<byte> CtrlDBytes = new(new byte[] { 0x04 });
    private static readonly ReadOnlyMemory<byte> EscBytes = new(new byte[] { 0x1B });
    private static readonly ReadOnlyMemory<byte> CrBytes = new(new byte[] { 0x0D });
    private static readonly ReadOnlyMemory<byte> CrLfBytes = new(new byte[] { 0x0D, 0x0A });

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

        // Envia um Enter inicial limpo caso o equipamento já esteja aguardando no prompt do bootloader
        try
        {
            await _transport.WriteAsync(CrLfBytes, cancellationToken);
        }
        catch { }

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
                try
                {
                    await SendSingleSignalAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Falha transitória de driver USB não deve abortar o escalonador de interrupções
                }
                TransmissionsCount++;

                if (i < burst - 1 && _profile.BurstInterval > TimeSpan.Zero)
                    await Task.Delay(_profile.BurstInterval, cancellationToken);
            }

            var methodName = (IsCisco841 && TransmissionsCount >= 30)
                ? "Enter @ 1s"
                : _profile.Method.ToString();

            OnBurstSent?.Invoke(TransmissionsCount, methodName);

            if (TransmissionsCount >= _profile.MaxTotalTransmissions)
                break;

            // Intervalo entre rajadas (adota 1s a partir do 30° envio no Cisco 841)
            var retryInterval = (IsCisco841 && TransmissionsCount >= 30)
                ? TimeSpan.FromSeconds(1)
                : _profile.RetryInterval;

            if (retryInterval > TimeSpan.Zero)
                await Task.Delay(retryInterval, cancellationToken);
        }
    }

    private bool IsCisco841 => _profile.Id.Contains("841", StringComparison.OrdinalIgnoreCase) ||
                               _profile.Family.Contains("841", StringComparison.OrdinalIgnoreCase) ||
                               _profile.Family.Contains("800", StringComparison.OrdinalIgnoreCase);

    private async Task SendSingleSignalAsync(CancellationToken ct)
    {
        // A partir do 30° envio no Cisco 841, adota enviar <ENTER> em intervalos de 1s para capturar o terminal ROMMON
        if (IsCisco841 && TransmissionsCount >= 30)
        {
            await _transport.WriteAsync(CrBytes, ct);
            await Task.Delay(50, ct);
            await _transport.WriteAsync(CrLfBytes, ct);
            return;
        }

        switch (_profile.Method)
        {
            case BootInterruptMethod.CtrlC:
                await _transport.WriteAsync(CtrlCBytes, ct);
                await Task.Delay(50, ct);
                await _transport.WriteAsync(CrLfBytes, ct);
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
                await Task.Delay(80, ct);
                await _transport.WriteAsync(CrBytes, ct);
                break;

            case BootInterruptMethod.Esc:
                await _transport.WriteAsync(EscBytes, ct);
                break;

            case BootInterruptMethod.Dual:
                await _transport.SendBreakAsync(ct);
                await Task.Delay(100, ct);
                await _transport.WriteAsync(CtrlCBytes, ct);
                await Task.Delay(100, ct);
                await _transport.WriteAsync(CrLfBytes, ct);
                break;

            case BootInterruptMethod.None:
            default:
                break;
        }
    }
}
