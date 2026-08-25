namespace NetworkDevice.Core.Power;

public sealed class ManualPowerController : IPowerController
{
    private readonly Func<string, CancellationToken, Task> _instruct;
    private readonly TimeSpan _offWait;
    private readonly TimeSpan _onWait;

    public ManualPowerController(
        Func<string, CancellationToken, Task> instruct,
        TimeSpan? offWait = null,
        TimeSpan? onWait = null)
    {
        _instruct = instruct ?? throw new ArgumentNullException(nameof(instruct));
        _offWait = offWait ?? TimeSpan.FromSeconds(30);
        _onWait = onWait ?? TimeSpan.FromSeconds(60);
    }

    public string Description => $"Manual (operador liga/desliga; aguarda {_offWait.TotalSeconds:0}s desligado e {_onWait.TotalSeconds:0}s ligado)";

    public bool CanControlRemotely => false;

    public async Task PowerOffAsync(CancellationToken cancellationToken = default)
    {
        await _instruct(
            "DESLIGUE o equipamento agora (chave de energia ou cabo de alimentação). " +
            "Não desconecte o cabo serial. O break já está ativo — aguardando a queda de energia...",
            cancellationToken);
        await DelayCancellable(_offWait, cancellationToken);
    }

    public async Task PowerOnAsync(CancellationToken cancellationToken = default)
    {
        await _instruct(
            "LIGUE o equipamento agora. O break está ativo e capturará o ROMMON assim que o boot começar...",
            cancellationToken);
        await DelayCancellable(_onWait, cancellationToken);
    }

    public async Task PowerCycleAsync(CancellationToken cancellationToken = default)
    {
        await PowerOffAsync(cancellationToken);
        await PowerOnAsync(cancellationToken);
    }

    private static async Task DelayCancellable(TimeSpan delay, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.Add(delay);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(500, ct);
        }
    }
}