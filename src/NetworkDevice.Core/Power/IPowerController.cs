namespace NetworkDevice.Core.Power;

public interface IPowerController
{
    string Description { get; }

    bool CanControlRemotely { get; }

    Task PowerOffAsync(CancellationToken cancellationToken = default);

    Task PowerOnAsync(CancellationToken cancellationToken = default);

    Task PowerCycleAsync(CancellationToken cancellationToken = default);
}