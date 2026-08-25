using System.Text;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Tests.TestDoubles;

internal sealed class ScriptedTransport : ITransport
{
    private readonly Func<string, string> _responder;
    private readonly Queue<string> _pending = new();
    private string? _remainder;

    public ScriptedTransport(Func<string, string> responder, string? initialOutput = null)
    {
        _responder = responder;
        if (!string.IsNullOrEmpty(initialOutput))
            _pending.Enqueue(initialOutput);
    }

    public List<string> Commands { get; } = new();

    public bool IsOpen => true;

    public Task OpenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendBreakAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_remainder) && _pending.Count == 0)
            return Task.FromResult(0);

        if (string.IsNullOrEmpty(_remainder))
            _remainder = _pending.Dequeue();

        var bytes = Encoding.UTF8.GetBytes(_remainder);
        if (bytes.Length <= buffer.Length)
        {
            var chunk = _remainder;
            _remainder = null;
            Encoding.UTF8.GetBytes(chunk).CopyTo(buffer);
            return Task.FromResult(chunk.Length);
        }

        var head = bytes.AsSpan(0, buffer.Length).ToArray();
        head.CopyTo(buffer);
        _remainder = Encoding.UTF8.GetString(bytes.AsSpan(buffer.Length));
        return Task.FromResult(head.Length);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var text = Encoding.UTF8.GetString(buffer.Span);
        var command = text.TrimEnd('\r', '\n');
        Commands.Add(command);
        var response = _responder(command);
        if (!string.IsNullOrEmpty(response))
            _pending.Enqueue(response);
        return ValueTask.CompletedTask;
    }

    public Task CloseAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}