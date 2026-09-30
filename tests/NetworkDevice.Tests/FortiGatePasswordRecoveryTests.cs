using System.Text;
using NetworkDevice.Core.Session;
using NetworkDevice.Fortinet;

namespace NetworkDevice.Tests;

/// <summary>
/// Regressão da quebra de senha do FortiGate 40F (reset físico + BIOS),
/// porte da lógica da janela WPF para <see cref="FortiGatePasswordRecovery"/>.
/// </summary>
public sealed class FortiGatePasswordRecoveryTests
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
    public async Task RecoverAndReset_PhysicalResetFlow_ValidatesFactoryLogin()
    {
        var transport = new StagedTransport();
        transport.Stage(
            "U-Boot 2019.07\nLoading kernel...\nVerifying image... OK\nSystem is starting...\nSerial number: FGT40F1234567890\n",
            "FortiGate-40F login: ",
            "Password: ",
            "Welcome!\nFGT40F # ");

        var logs = new List<string>();
        var instructions = new List<string>();
        var recovery = new FortiGatePasswordRecovery(msg => { logs.Add(msg); return Task.CompletedTask; });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ok = await recovery.RecoverAndResetAsync(
            transport,
            (msg, _) => { instructions.Add(msg); return Task.CompletedTask; },
            cts.Token);

        Assert.True(ok);
        Assert.Contains("admin", transport.Writes);
        Assert.Equal("FGT40F1234567890", recovery.DetectedSerial);
        Assert.NotEmpty(instructions); // orientou o power-cycle/RESET físico
    }

    [Fact]
    public async Task RecoverAndReset_BiosFormatFlow_SendsFYesThenQInOrder()
    {
        var transport = new StagedTransport();
        transport.Stage(
            "FortiBootLoader v1.0\nEnter Selection:\n[B]: Boot\n[F]: Format\n[Q]: Quit\n",
            "It will erase data in boot device. Continue? [yes/no]:",
            "formatting... done\nEnter Selection:\n[B]: Boot\n[F]: Format\n[Q]: Quit\n",
            "U-Boot 2019.07\nSystem is starting...\n",
            "FortiGate-40F login: ",
            "Password: ",
            "FGT40F # ");

        var recovery = new FortiGatePasswordRecovery(_ => Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ok = await recovery.RecoverAndResetAsync(
            transport,
            (msg, _) => Task.CompletedTask,
            cts.Token);

        Assert.True(ok);
        var writes = transport.Writes;
        var f = writes.FindIndex(w => w == "F");
        var yes = writes.FindIndex(w => w == "yes");
        var q = writes.FindIndex(w => w == "Q");
        var admin = writes.FindIndex(w => w == "admin");
        Assert.True(f >= 0 && yes > f && q > yes && admin > q);
    }

    [Fact]
    public async Task RecoverAndReset_LoginIncorrect_RearmsAndSucceedsOnRetry()
    {
        var transport = new StagedTransport();
        transport.Stage(
            "U-Boot 2019.07\nSystem is starting...\n",
            "FortiGate-40F login: ",
            "Password: ",
            "Login incorrect\nFortiGate-40F login: ",
            // operador refez o reset físico: novo boot + login em branco aceito
            "U-Boot 2019.07\nSystem is starting...\n",
            "FortiGate-40F login: ",
            "Password: ",
            "Welcome!\nFGT40F # ");

        var recovery = new FortiGatePasswordRecovery(_ => Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ok = await recovery.RecoverAndResetAsync(
            transport,
            (msg, _) => Task.CompletedTask,
            cts.Token);

        Assert.True(ok);
        Assert.True(transport.Writes.Count(w => w == "admin") >= 2);
    }
}
