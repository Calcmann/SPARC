using System.Text;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Hpe;

namespace NetworkDevice.Tests;

/// <summary>
/// Upgrade Comware via FTP do celular (cliente ftp interativo + boot-loader + reboot).
/// Transporte encenado: leituras em ordem exata (o loop pós-reboot só lê, não escreve).
/// </summary>
public sealed class HpeFtpUpgraderTests
{
    private const string FileName = "msr930-cmw710-r9999.ipe";

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
    public async Task UpgradeAsync_FtpGet_BootLoader_Reboot_Completes()
    {
        var transport = new StagedTransport();
        transport.Stage(
            "<HPE>",                                    // 0  Connect
            "<HPE>",                                    // 3  screen-length
            "Comware V7.1.070\n<HPE>",                  // 4  display version
            "Directory of flash:/\n -rw- 100 startup.cfg\n<HPE>", // 5  dir (sem arquivo)
            "Main startup: flash:/old.ipe\n<HPE>",      // 6  display boot-loader
            "Trying 10.0.0.2 ...\nftp>",                // 7  ftp -> subshell
            "ftp>",                                     // 8  binary
            "226 Transfer complete.\nftp>",             // 9  get
            "<HPE>",                                    // 10 quit
            "Directory of flash:/\n" +                  // 11 dir (com arquivo)
            $" -rw- 40000000 {FileName}\n<HPE>",
            "Continue? [Y/N]:",                         // 12 boot-loader -> confirma
            "[HPE]",                                    //    y -> prompt
            "will be written to the device. Continue? [Y/N]:", // 13 save -> confirma
            "<HPE>",                                    //    y -> prompt
            "System will reboot! Continue? [Y/N]:",     // 14 reboot -> confirma
            "Rebooting...\n<HPE>",                      //    y -> prompt (retorno rápido)
            "<HPE>",                                    // 15 boot concluído
            "Comware V7.1.071\n<HPE>");                 // 16 display version pós-boot

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();

        var upgrader = new HpeFtpUpgrader(_ => Task.CompletedTask);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ok = await upgrader.UpgradeAsync(session, FileName, "10.0.0.2", 2121, "sparc", "sparc", 40_000_000, cts.Token);

        Assert.True(ok);
        var writes = transport.Writes;
        var ftpIdx = writes.FindIndex(c => c.StartsWith("ftp 10.0.0.2 2121"));
        var getIdx = writes.FindIndex(c => c == $"get {FileName}");
        var bootIdx = writes.FindIndex(c => c.StartsWith("boot-loader file"));
        var rebootIdx = writes.FindIndex(c => c == "reboot");
        Assert.True(0 <= ftpIdx && ftpIdx < getIdx && getIdx < bootIdx && bootIdx < rebootIdx);
    }

    [Fact]
    public async Task UpgradeAsync_AlreadyCurrent_SkipsTransfer()
    {
        var transport = new StagedTransport();
        transport.Stage(
            "<HPE>",
            "<HPE>",
            "Comware V7.1.070\n<HPE>",
            $"Directory of flash:/\n -rw- 40000000 {FileName}\n<HPE>",
            $"Main startup: flash:/{FileName}\n<HPE>");

        await using var session = new DeviceSession(transport, new SessionOptions());
        await session.ConnectAsync();

        var upgrader = new HpeFtpUpgrader(_ => Task.CompletedTask);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ok = await upgrader.UpgradeAsync(session, FileName, "10.0.0.2", 2121, "sparc", "sparc", 40_000_000, cts.Token);

        Assert.True(ok);
        Assert.DoesNotContain(transport.Writes, c => c.StartsWith("ftp "));
    }
}
