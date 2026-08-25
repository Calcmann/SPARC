using System.Text;
using NetworkDevice.Cisco;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;
using NetworkDevice.Tests.TestDoubles;

namespace NetworkDevice.Tests;

public class CiscoIOSRecoveryTests
{
    [Fact]
    public async Task RecoverAndResetAsync_WithoutTerminalResponse_Aborts()
    {
        var transport = new ScriptedTransport(_ => "");
        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(verifyTimeout: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<DeviceSessionException>(
            () => recovery.RecoverAndResetAsync(session));
    }

    [Fact]
    public async Task RecoverAndResetAsync_RequestsReload_AndResetsConfig()
    {
        var transport = new RecoverySimTransport(
            responder: cmd => cmd switch
            {
                "confreg 0x2142" => new[] { "rommon 1 >\r\n" },
                "reset" => new[] { "System Bootstrap...\r\nWould you like to enter the initial configuration dialog? [yes/no]: " },
                "no" => new[] { "Press RETURN to get started\r\n", "Router>\r\n" },
                "enable" => new[] { "Router#\r\n" },
                "write erase" => new[] { "Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]\r\n", "Router#\r\n" },
                "configure terminal" => new[] { "Router(config)#\r\n" },
                "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                "end" => new[] { "Router#\r\n" },
                "write memory" => new[] { "Building configuration...\r\n[OK]\r\nRouter#\r\n" },
                "reload" => new[] { "System configuration has been modified. Save? [yes/no]:\r\nProceed with reload? [confirm]\r\n" },
                _ => Array.Empty<string>()
            },
            initialOutput: "User Access Verification\r\nPassword: ");

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2));

        string? reloadRequested = null;
        await recovery.RecoverAndResetAsync(session, (message, ct) =>
        {
            reloadRequested = message;
            return Task.CompletedTask;
        });

        Assert.NotNull(reloadRequested);
        Assert.Contains("reload", reloadRequested, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confreg 0x2142", transport.Commands);
        Assert.Contains("write erase", transport.Commands);
        Assert.Contains("config-register 0x2102", transport.Commands);
        Assert.Contains("write memory", transport.Commands);
    }

    [Fact]
    public async Task DiagnoseAccessStateAsync_WhenPasswordPrompt_ReturnsPasswordLocked()
    {
        var transport = new ScriptedTransport(_ => "User Access Verification\r\nPassword: ");
        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery();

        var (state, rommon) = await recovery.DiagnoseAccessStateAsync(session, CancellationToken.None);

        Assert.Equal(DeviceAccessState.PasswordLocked, state);
        Assert.Null(rommon);
    }

    [Fact]
    public async Task DiagnoseAccessStateAsync_WhenRouterPrompt_ReturnsUnlockedPrompt()
    {
        var transport = new ScriptedTransport(cmd => cmd == "enable" ? "Router#\r\n" : "Router>\r\n");
        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery();

        var (state, rommon) = await recovery.DiagnoseAccessStateAsync(session, CancellationToken.None);

        Assert.Equal(DeviceAccessState.UnlockedPrompt, state);
        Assert.Null(rommon);
    }

    [Fact]
    public async Task DiagnoseAccessStateAsync_WhenPrivilegedPrompt_ReturnsUnlockedPrompt()
    {
        var transport = new ScriptedTransport(_ => "Router#\r\n");
        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery();

        var (state, rommon) = await recovery.DiagnoseAccessStateAsync(session, CancellationToken.None);

        Assert.Equal(DeviceAccessState.UnlockedPrompt, state);
        Assert.Null(rommon);
    }

    [Fact]
    public async Task DiagnoseAccessStateAsync_WhenUserPromptRequiresEnablePassword_ReturnsPasswordLocked()
    {
        var transport = new ScriptedTransport(cmd => cmd == "enable" ? "Password: " : "Router>\r\n");
        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery();

        var (state, rommon) = await recovery.DiagnoseAccessStateAsync(session, CancellationToken.None);

        Assert.Equal(DeviceAccessState.PasswordLocked, state);
        Assert.Null(rommon);
    }

    [Fact]
    public async Task RecoverAndResetAsync_WhenUnlockedPrompt_SkipsRecoveryAndReload()
    {
        var transport = new RecoverySimTransport(
            responder: cmd => cmd switch
            {
                "enable" => new[] { "Router#\r\n" },
                "write erase" => new[] { "Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]\r\n" },
                "" => new[] { "Router#\r\n" },
                "configure terminal" => new[] { "Router(config)#\r\n" },
                "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                "end" => new[] { "Router#\r\n" },
                "write memory" => new[] { "Building configuration...\r\n[OK]\r\nRouter#\r\n" },
                _ => Array.Empty<string>()
            },
            initialOutput: "Router>\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2));

        var reloadCalled = false;
        await recovery.RecoverAndResetAsync(session, (_, _) =>
        {
            reloadCalled = true;
            return Task.CompletedTask;
        });

        Assert.False(reloadCalled);
        Assert.DoesNotContain("confreg 0x2142", transport.Commands);
        Assert.Contains("write erase", transport.Commands);
    }

    [Fact]
    public async Task RecoverAndResetAsync_WhenAlreadyInRommon_SkipsReload()
    {
        var transport = new RecoverySimTransport(
            responder: cmd => cmd switch
            {
                "confreg 0x2142" => new[] { "rommon 1 >\r\n" },
                "reset" => new[] { "System Bootstrap...\r\nWould you like to enter the initial configuration dialog? [yes/no]: " },
                "no" => new[] { "Press RETURN to get started\r\n", "Router>\r\n" },
                "enable" => new[] { "Router#\r\n" },
                "write erase" => new[] { "Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]\r\n", "Router#\r\n" },
                "configure terminal" => new[] { "Router(config)#\r\n" },
                "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                "end" => new[] { "Router#\r\n" },
                "write memory" => new[] { "Building configuration...\r\n[OK]\r\nRouter#\r\n" },
                "reload" => new[] { "Proceed with reload? [confirm]\r\n" },
                _ => Array.Empty<string>()
            },
            initialOutput: "rommon 1 >\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2));

        var reloadCalled = false;
        await recovery.RecoverAndResetAsync(session, (_, _) =>
        {
            reloadCalled = true;
            return Task.CompletedTask;
        });

        Assert.False(reloadCalled);
        Assert.Contains("confreg 0x2142", transport.Commands);
    }

    [Fact]
    public async Task RecoverAndResetAsync_RommonPromptWithBang_IsDetected()
    {
        var transport = new RecoverySimTransport(
            responder: cmd => cmd switch
            {
                "confreg 0x2142" => new[] { "rommon 1 >\r\n" },
                "reset" => new[] { "System Bootstrap...\r\nWould you like to enter the initial configuration dialog? [yes/no]: " },
                "no" => new[] { "Press RETURN to get started\r\n", "Router>\r\n" },
                "enable" => new[] { "Router#\r\n" },
                "write erase" => new[] { "Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]\r\n", "Router#\r\n" },
                "configure terminal" => new[] { "Router(config)#\r\n" },
                "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                "end" => new[] { "Router#\r\n" },
                "write memory" => new[] { "Building configuration...\r\n[OK]\r\nRouter#\r\n" },
                "reload" => new[] { "Proceed with reload? [confirm]\r\n" },
                _ => Array.Empty<string>()
            },
            initialOutput: "rommon ! >\r\n",
            rommonPrompt: "rommon ! >\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2));

        await recovery.RecoverAndResetAsync(session, (_, _) => Task.CompletedTask);

        Assert.Contains("confreg 0x2142", transport.Commands);
    }

    [Fact]
    public async Task RecoverAndResetAsync_WithCtrlCInterrupt_UsesCtrlC()
    {
        var transport = new RecoverySimTransport(
            responder: cmd => cmd switch
            {
                "confreg 0x2142" => new[] { "rommon ! >\r\n" },
                "reset" => new[] { "System Bootstrap...\r\nWould you like to enter the initial configuration dialog? [yes/no]: " },
                "no" => new[] { "Press RETURN to get started\r\n", "Router>\r\n" },
                "enable" => new[] { "Router#\r\n" },
                "write erase" => new[] { "Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]\r\n", "Router#\r\n" },
                "configure terminal" => new[] { "Router(config)#\r\n" },
                "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                "end" => new[] { "Router#\r\n" },
                "write memory" => new[] { "Building configuration...\r\n[OK]\r\nRouter#\r\n" },
                "reload" => new[] { "Proceed with reload? [confirm]\r\n" },
                _ => Array.Empty<string>()
            },
            initialOutput: "ROMON\r\n",
            rommonPrompt: "rommon 1 >\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2),
            profile: BootInterruptProfiles.Cisco900);

        var reloadRequested = false;
        await recovery.RecoverAndResetAsync(session, (_, _) =>
        {
            reloadRequested = true;
            return Task.CompletedTask;
        });

        Assert.True(reloadRequested);
        Assert.True(transport.CtrlCCount > 0);
        Assert.Equal(0, transport.BreakCount);
        Assert.Contains("confreg 0x2142", transport.Commands);
    }

    [Fact]
    public async Task RecoverAndResetAsync_WithBreakInterrupt_UsesBreak()
    {
        var transport = new RecoverySimTransport(
            responder: cmd => cmd switch
            {
                "confreg 0x2142" => new[] { "rommon ! >\r\n" },
                "reset" => new[] { "System Bootstrap...\r\nWould you like to enter the initial configuration dialog? [yes/no]: " },
                "no" => new[] { "Press RETURN to get started\r\n", "Router>\r\n" },
                "enable" => new[] { "Router#\r\n" },
                "write erase" => new[] { "Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]\r\n", "Router#\r\n" },
                "configure terminal" => new[] { "Router(config)#\r\n" },
                "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                "end" => new[] { "Router#\r\n" },
                "write memory" => new[] { "Building configuration...\r\n[OK]\r\nRouter#\r\n" },
                "reload" => new[] { "Proceed with reload? [confirm]\r\n" },
                _ => Array.Empty<string>()
            },
            initialOutput: "ROMON\r\n",
            rommonPrompt: "rommon 1 >\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2),
            profile: BootInterruptProfiles.CiscoStandardBreak);

        await recovery.RecoverAndResetAsync(session, (_, _) => Task.CompletedTask);

        Assert.True(transport.BreakCount > 0);
        Assert.Equal(0, transport.CtrlCCount);
        Assert.Contains("confreg 0x2142", transport.Commands);
    }

    [Fact]
    public async Task RecoverAndResetAsync_WhenOsBootStarts_FailsFastWithBootInterruptionFailed()
    {
        var transport = new RecoverySimTransport(
            responder: _ => Array.Empty<string>(),
            initialOutput: "User Access Verification\r\nPassword: ",
            emitOsBootOnInterrupt: true);

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2),
            profile: BootInterruptProfiles.Cisco900);

        var ex = await Assert.ThrowsAsync<BootInterruptionFailedException>(
            () => recovery.RecoverAndResetAsync(session, (_, _) => Task.CompletedTask));

        Assert.Contains("BOOT_INTERRUPTION_FAILED", ex.Message);
        Assert.Equal("OS_BOOT_DETECTED", ex.Reason);
    }

    [Fact]
    public async Task RecoverAndResetAsync_WhenSilentSerialStream_SchedulerContinuesWithoutDeadlock()
    {
        // Simula silêncio inicial em RX antes do ROMMON responder
        var transport = new RecoverySimTransport(
            responder: cmd => cmd switch
            {
                "confreg 0x2142" => new[] { "rommon 1 >\r\n" },
                "reset" => new[] { "System Bootstrap...\r\nWould you like to enter the initial configuration dialog? [yes/no]: " },
                "no" => new[] { "Press RETURN to get started\r\n", "Router>\r\n" },
                "enable" => new[] { "Router#\r\n" },
                "write erase" => new[] { "Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]\r\n", "Router#\r\n" },
                "configure terminal" => new[] { "Router(config)#\r\n" },
                "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                "end" => new[] { "Router#\r\n" },
                "write memory" => new[] { "Building configuration...\r\n[OK]\r\nRouter#\r\n" },
                "reload" => new[] { "Proceed with reload? [confirm]\r\n" },
                _ => Array.Empty<string>()
            },
            initialOutput: "User Access Verification\r\nPassword: ",
            initialSilenceMs: 200);

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(5),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2),
            profile: BootInterruptProfiles.Cisco900);

        await recovery.RecoverAndResetAsync(session, (_, _) => Task.CompletedTask);

        Assert.True(transport.CtrlCCount > 0);
        Assert.Contains("confreg 0x2142", transport.Commands);
    }

    [Fact]
    public async Task BootInterruptScheduler_LimitsTotalTransmissionsAndAvoidsFlood()
    {
        var profile = new BootInterruptProfile
        {
            Id = "test.profile",
            Name = "Test",
            Method = BootInterruptMethod.CtrlC,
            InitialDelay = TimeSpan.Zero,
            BurstCount = 2,
            BurstInterval = TimeSpan.FromMilliseconds(5),
            RetryInterval = TimeSpan.FromMilliseconds(20),
            MaxWindow = TimeSpan.FromMilliseconds(300),
            MaxTotalTransmissions = 6
        };

        var transport = new DelayedRommonTransport(initialSilenceCycles: 100);
        var scheduler = new BootInterruptScheduler(transport, profile);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await scheduler.RunAsync(cts.Token);

        Assert.True(scheduler.TransmissionsCount <= 6, $"Transmissões: {scheduler.TransmissionsCount} (máximo esperado era 6).");
    }

    [Fact]
    public void BootInterruptProfiles_CiscoCatalyst_RequiresManualIntervention()
    {
        var profile = BootInterruptProfiles.CiscoCatalystManualMode;
        Assert.True(profile.RequiresManualIntervention);
        Assert.Equal(BootInterruptMethod.None, profile.Method);
        Assert.Contains("MODE", profile.ManualInterventionPrompt);
    }

    private sealed class DelayedRommonTransport : ITransport
    {
        private readonly int _initialSilenceCycles;
        private readonly string _rommonPrompt;
        private int _readCycles;
        private bool _emitted;

        public DelayedRommonTransport(int initialSilenceCycles, string rommonPrompt = "rommon 1 >\r\n")
        {
            _initialSilenceCycles = initialSilenceCycles;
            _rommonPrompt = rommonPrompt;
        }

        public int TransmissionsCount { get; private set; }
        public bool IsOpen => true;

        public Task OpenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CloseAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task SendBreakAsync(CancellationToken cancellationToken = default)
        {
            TransmissionsCount++;
            return Task.CompletedTask;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            TransmissionsCount++;
            return ValueTask.CompletedTask;
        }

        public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _readCycles++;
            if (_readCycles < _initialSilenceCycles)
            {
                await Task.Delay(20, cancellationToken);
                return 0; // silêncio
            }

            if (!_emitted)
            {
                _emitted = true;
                var bytes = Encoding.UTF8.GetBytes(_rommonPrompt);
                bytes.CopyTo(buffer);
                return bytes.Length;
            }

            await Task.Delay(50, cancellationToken);
            return 0;
        }
    }

    private sealed class RecoverySimTransport : ITransport
    {
        private readonly Func<string, IEnumerable<string>> _responder;
        private readonly string _rommonPrompt;
        private readonly bool _emitOsBootOnInterrupt;
        private readonly int _initialSilenceMs;
        private readonly Queue<string> _pending = new();
        private string? _remainder;
        private bool _interrupted;
        private DateTime _silenceUntil = DateTime.MinValue;

        public RecoverySimTransport(
            Func<string, IEnumerable<string>> responder,
            string? initialOutput = null,
            string rommonPrompt = "rommon 1 >\r\n",
            bool emitOsBootOnInterrupt = false,
            int initialSilenceMs = 0)
        {
            _responder = responder;
            _rommonPrompt = rommonPrompt;
            _emitOsBootOnInterrupt = emitOsBootOnInterrupt;
            _initialSilenceMs = initialSilenceMs;
            if (!string.IsNullOrEmpty(initialOutput))
                _pending.Enqueue(initialOutput);
        }

        public List<string> Commands { get; } = new();

        public bool IsOpen => true;

        public Task OpenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public int BreakCount { get; private set; }
        public int CtrlCCount { get; private set; }

        public Task SendBreakAsync(CancellationToken cancellationToken = default)
        {
            BreakCount++;
            TriggerInterruptResponse();
            return Task.CompletedTask;
        }

        public async Task<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (DateTime.UtcNow < _silenceUntil)
            {
                await Task.Delay(20, cancellationToken);
                return 0;
            }

            if (string.IsNullOrEmpty(_remainder) && _pending.Count == 0)
                return 0;

            if (string.IsNullOrEmpty(_remainder))
                _remainder = _pending.Dequeue();

            var bytes = Encoding.UTF8.GetBytes(_remainder);
            if (bytes.Length <= buffer.Length)
            {
                var chunk = _remainder;
                _remainder = null;
                Encoding.UTF8.GetBytes(chunk).CopyTo(buffer);
                return chunk.Length;
            }

            var head = bytes.AsSpan(0, buffer.Length).ToArray();
            head.CopyTo(buffer);
            _remainder = Encoding.UTF8.GetString(bytes.AsSpan(buffer.Length));
            return head.Length;
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var span = buffer.Span;
            if (span.Length >= 1 && span[0] == 0x03)
            {
                CtrlCCount++;
                TriggerInterruptResponse();
                return ValueTask.CompletedTask;
            }

            var text = Encoding.UTF8.GetString(span);
            var command = text.TrimEnd('\r', '\n');
            Commands.Add(command);
            foreach (var response in _responder(command))
                if (!string.IsNullOrEmpty(response))
                    _pending.Enqueue(response);
            return ValueTask.CompletedTask;
        }

        private void TriggerInterruptResponse()
        {
            if (_interrupted)
                return;
            _interrupted = true;

            if (_initialSilenceMs > 0)
                _silenceUntil = DateTime.UtcNow.AddMilliseconds(_initialSilenceMs);

            if (_emitOsBootOnInterrupt)
            {
                _pending.Enqueue("Self-decompressing the image : ##################\r\n");
            }
            else
            {
                _pending.Enqueue(_rommonPrompt);
            }
        }

        public Task CloseAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}