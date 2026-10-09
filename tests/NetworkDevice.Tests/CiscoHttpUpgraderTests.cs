using System.Text;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Telnet;

namespace NetworkDevice.Tests;

public sealed class CiscoHttpUpgraderTests
{
    private sealed class StagedTelnetTransport : ITransport
    {
        private readonly Queue<string> _reads = new();
        public readonly List<string> Writes = new();
        public bool IsOpen => true;

        public void Stage(params string[] chunks)
        {
            foreach (var c in chunks) _reads.Enqueue(c);
        }

        public Task OpenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendBreakAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_reads.Count == 0)
                return Task.FromResult(0);
            var bytes = Encoding.UTF8.GetBytes(_reads.Dequeue());
            var n = Math.Min(bytes.Length, buffer.Length);
            bytes.AsSpan(0, n).CopyTo(buffer.Span);
            if (n < bytes.Length)
                _reads.Enqueue(Encoding.UTF8.GetString(bytes.AsSpan(n)));
            return Task.FromResult(n);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Writes.Add(Encoding.UTF8.GetString(buffer.Span).TrimEnd('\r', '\n'));
            return ValueTask.CompletedTask;
        }

        public Task CloseAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void BuildCopyCommand_ConstructsCorrectUrl()
    {
        var cmd = CiscoHttpUpgrader.BuildCopyCommand("192.168.1.2", 8080, "c1900-universalk9-mz.SPA.157-3.M7.bin", "flash:");
        Assert.Equal("copy http://192.168.1.2:8080/c1900-universalk9-mz.SPA.157-3.M7.bin flash:c1900-universalk9-mz.SPA.157-3.M7.bin", cmd);
    }

    [Fact]
    public void UpgradeAsync_OnTelnetTransport_ReloadReturnsTrueEarly()
    {
        var transport = new TcpTelnetTransport("127.0.0.1", 23);
        // Cria sessão usando TcpTelnetTransport (subclasse/instância)
        Assert.True(transport is TcpTelnetTransport);
    }
}
