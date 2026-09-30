using System.Text;
using NetworkDevice.Core.Session;
using NetworkDevice.Fortinet;

namespace NetworkDevice.Tests;

/// <summary>
/// Restore FortiOS via FTP do celular (execute restore image ftp + y + reboot).
/// </summary>
public sealed class FortiFirmwareRestoreTests
{
    private sealed class StagedTransport : ITransport
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
    public async Task UpgradeAsync_ConfirmY_Reboot_LoginBack_ReturnsTrue()
    {
        var transport = new StagedTransport();
        transport.Stage(
            "This operation will replace the current firmware version! Do you want to continue? (y/n)",
            "Writing image...\nSystem is rebooting...\nFGT40F login: ");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectRawAsync();

        var upgrader = new FortiGateFirmwareUpgrader(_ => Task.CompletedTask);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ok = await upgrader.UpgradeAsync(session, "FGT40F-v7.out", "10.0.0.2", 2121, "sparc", "sparc", cts.Token);

        Assert.True(ok);
        Assert.Contains(transport.Writes, w => w.StartsWith("execute restore image ftp FGT40F-v7.out 10.0.0.2:2121 sparc sparc"));
        Assert.Contains(transport.Writes, w => w == "y");
    }

    [Fact]
    public async Task UpgradeAsync_CommandRejected_Throws()
    {
        var transport = new StagedTransport();
        transport.Stage("Command fail. Invalid firmware image.");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectRawAsync();

        var upgrader = new FortiGateFirmwareUpgrader(_ => Task.CompletedTask);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await Assert.ThrowsAsync<DeviceSessionException>(() =>
            upgrader.UpgradeAsync(session, "bad.out", "10.0.0.2", 2121, null, null, cts.Token));
    }
}
