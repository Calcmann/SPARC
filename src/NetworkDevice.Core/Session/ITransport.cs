namespace NetworkDevice.Core.Session;

public interface ITransport : IAsyncDisposable
{
    bool IsOpen { get; }

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    Task SendBreakAsync(CancellationToken cancellationToken = default);

    Task CloseAsync();
}
