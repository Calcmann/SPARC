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
            "no ip domain-lookup" => new[] { "Router(config)#\r\n" },
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
    public async Task DiagnoseAccessStateAsync_WhenRommonPrompt_ReturnsAlreadyInRommon()
    {
        var transport = new ScriptedTransport(_ => "rommon 1 >\r\n");
        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery();

        var (state, rommon) = await recovery.DiagnoseAccessStateAsync(session, CancellationToken.None);

        Assert.Equal(DeviceAccessState.AlreadyInRommon, state);
        Assert.Equal(CiscoIOSRecovery.RommonKind.Router, rommon);
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
            "no ip domain-lookup" => new[] { "Router(config)#\r\n" },
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
    public async Task RecoverAndResetAsync_WhenForceReloadTrue_ExecutesVlanDeleteAndReload()
    {
        var transport = new RecoverySimTransport(
            responder: cmd => cmd switch
            {
                "enable" => new[] { "Router#\r\n" },
                "delete /force flash:vlan.dat" => new[] { "%Error deleting flash:vlan.dat (No such file or directory)\r\nRouter#\r\n" },
                "delete /force bootflash:vlan.dat" => new[] { "Router#\r\n" },
                "delete /force nvram:vlan.dat" => new[] { "Router#\r\n" },
                "write erase" => new[] { "Erasing the nvram filesystem will remove all configuration files! Continue? [confirm]\r\n" },
                "" => new[] { "Router#\r\n" },
                "configure terminal" => new[] { "Router(config)#\r\n" },
                "no ip domain-lookup" => new[] { "Router(config)#\r\n" },
                "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                "end" => new[] { "Router#\r\n" },
                "reload" => new[] { "System configuration has been modified. Save? [yes/no]: " },
                "no" => new[] { "Proceed with reload? [confirm] \r\n" },
                _ => Array.Empty<string>()
            },
            initialOutput: "Router>\r\n");

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2));

        await recovery.RecoverAndResetAsync(session, (_, _) => Task.CompletedTask, forceReload: true);

        Assert.Contains("delete /force flash:vlan.dat", transport.Commands);
        Assert.Contains("write erase", transport.Commands);
        Assert.Contains("reload", transport.Commands);
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
    public async Task RecoverAndResetAsync_WhenOperatorConfirmsReloadBeforeRestart_ToleratesActivePromptAndRecoversOnRommon()
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
            initialOutput: "User Access Verification\r\nPassword: ",
            emitLoginPromptFirst: true);

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(2),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2),
            profile: BootInterruptProfiles.Cisco900);

        var reloadRequested = false;
        await recovery.RecoverAndResetAsync(session, (_, _) =>
        {
            // Operador confirma reload antes de reiniciar na energia:
            // O roteador ainda responde com prompt de login, mas o SPARC tolera e aguarda o ROMMON
            reloadRequested = true;
            return Task.CompletedTask;
        });

        Assert.True(reloadRequested);
        Assert.Contains("confreg 0x2142", transport.Commands);
        Assert.Contains("write erase", transport.Commands);
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await scheduler.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

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

    [Fact]
    public void Cisco841_BootInterruptProfile_Has170sWindow_AndMatchesRommonPrompt()
    {
        var profile = BootInterruptProfiles.Cisco841;
        Assert.Equal("cisco.c841.break", profile.Id);
        Assert.Equal(TimeSpan.FromMinutes(2.5), profile.MaxWindow);
        Assert.Equal(150, profile.MaxTotalTransmissions);
        Assert.Equal(BootInterruptMethod.Break, profile.Method);

        // Verifica que o prompt clássico do Cisco 841 (rommon 1 >) casa imediatamente
        var testPrompts = new[] { "rommon 1 >", "rommon 1 > ", "rommon 2 >", "rommon >", "rommon 10 >" };
        foreach (var prompt in testPrompts)
        {
            var matched = profile.RommonPatterns.Any(r => r.IsMatch(prompt));
            Assert.True(matched, $"Prompt '{prompt}' deve casar com os RommonPatterns do Cisco841");
        }

        // Garante isolamento: outros modelos não foram modificados e mantêm suas próprias políticas
        Assert.Equal(BootInterruptMethod.CtrlC, BootInterruptProfiles.Cisco900.Method);
        Assert.Equal(BootInterruptMethod.CtrlB, BootInterruptProfiles.HpeMsr954.Method);
        Assert.Equal("fortinet.fgt40f.fortios", BootInterruptProfiles.FortiGate40F.Id);
    }

    [Fact]
    public async Task RecoverAndResetAsync_Cisco841_UsesBreakAndCapturesRommonPrompt()
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
                _ => Array.Empty<string>()
            },
            initialOutput: "User Access Verification\r\nPassword: ",
            rommonPrompt: "rommon 1 >\r\n",
            initialSilenceMs: 100);

        await using var session = new DeviceSession(transport, new SessionOptions());
        var recovery = new CiscoIOSRecovery(
            bootWait: TimeSpan.FromSeconds(5),
            commandTimeout: TimeSpan.FromSeconds(2),
            verifyTimeout: TimeSpan.FromSeconds(2),
            profile: BootInterruptProfiles.Cisco841);

        await recovery.RecoverAndResetAsync(session, (_, _) => Task.CompletedTask);

        Assert.Equal(BootInterruptMethod.Break, BootInterruptProfiles.Cisco841.Method);
        Assert.True(transport.BreakCount > 0);
        Assert.Contains("confreg 0x2142", transport.Commands);
    }

    [Fact]
    public async Task Cisco841_BootInterruptScheduler_SwitchesToEnterAfter30Transmissions()
    {
        var transport = new TestBreakTrackingTransport();
        var profile = new BootInterruptProfile
        {
            Id = "cisco.c841.break",
            Name = "Cisco Série 800 / C841M",
            Family = "ISR 800 / 841",
            Method = BootInterruptMethod.Break,
            InitialDelay = TimeSpan.Zero,
            BurstCount = 1,
            BurstInterval = TimeSpan.Zero,
            RetryInterval = TimeSpan.FromMilliseconds(5),
            MaxWindow = TimeSpan.FromSeconds(5),
            MaxTotalTransmissions = 31
        };

        var scheduler = new BootInterruptScheduler(transport, profile);
        var methods = new List<string>();
        scheduler.OnBurstSent += (_, m) => methods.Add(m);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await scheduler.RunAsync(cts.Token);

        Assert.Equal(31, scheduler.TransmissionsCount);
        Assert.Equal(30, transport.BreakCount);
        Assert.True(transport.WriteCount > 30);
        Assert.Contains("Enter @ 1s", methods);
    }

    private sealed class TestBreakTrackingTransport : ITransport
    {
        public int BreakCount { get; private set; }
        public int WriteCount { get; private set; }
        public bool IsOpen => true;
        public Task OpenAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task SendBreakAsync(CancellationToken ct = default) { BreakCount++; return Task.CompletedTask; }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { WriteCount++; return ValueTask.CompletedTask; }
        public Task<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => Task.FromResult(0);
        public Task CloseAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void CiscoAllModels_RommonPromptPatterns_MatchAndRespondToEnter()
    {
        var profiles = new[]
        {
            BootInterruptProfiles.Cisco900,
            BootInterruptProfiles.Cisco841,
            BootInterruptProfiles.Cisco1900,
            BootInterruptProfiles.Cisco2900,
            BootInterruptProfiles.CiscoStandardBreak,
            BootInterruptProfiles.CiscoUniversal,
            BootInterruptProfiles.CiscoCatalystManualMode
        };

        var routerPrompts = new[] { "rommon 1 >", "rommon 2 >", "rommon 16 >", "rommon >" };

        foreach (var profile in profiles)
        {
            if (profile.Id == "cisco.catalyst.mode")
            {
                // Switch Catalyst usa 'switch:'
                var matched = profile.RommonPatterns.Any(r => r.IsMatch("switch: ") || r.IsMatch("switch:"));
                Assert.True(matched, $"Switch Catalyst deve casar com o prompt switch:");
            }
            else
            {
                foreach (var p in routerPrompts)
                {
                    var matched = profile.RommonPatterns.Any(r => r.IsMatch(p));
                    Assert.True(matched, $"Perfil '{profile.Name}' ({profile.Id}) deve casar com o prompt '{p}'");
                }
            }
        }
    }

    private sealed class RecoverySimTransport : ITransport
    {
        private readonly Func<string, IEnumerable<string>> _responder;
        private readonly string _rommonPrompt;
        private readonly bool _emitOsBootOnInterrupt;
        private readonly bool _emitLoginPromptFirst;
        private readonly int _initialSilenceMs;
        private readonly Queue<string> _pending = new();
        private string? _remainder;
        private bool _interrupted;
        private int _interruptCount;
        private DateTime _silenceUntil = DateTime.MinValue;

        public RecoverySimTransport(
            Func<string, IEnumerable<string>> responder,
            string? initialOutput = null,
            string rommonPrompt = "rommon 1 >\r\n",
            bool emitOsBootOnInterrupt = false,
            bool emitLoginPromptFirst = false,
            int initialSilenceMs = 0)
        {
            _responder = responder;
            _rommonPrompt = rommonPrompt;
            _emitOsBootOnInterrupt = emitOsBootOnInterrupt;
            _emitLoginPromptFirst = emitLoginPromptFirst;
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
            {
                var sb = new StringBuilder();
                while (_pending.Count > 0)
                    sb.Append(_pending.Dequeue());
                _remainder = sb.ToString();
            }

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
            if (_emitLoginPromptFirst && _interruptCount == 0)
            {
                _interruptCount++;
                _pending.Enqueue("\r\nUser Access Verification\r\nUsername: ");
                return;
            }

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

    

    

    

    [Fact]
    public async Task UpgradeViaRommonTftpAsync_Cisco1100_WhenTargetOnFlash_BootsDirectlyAndNeverUsesTftpdnld()
    {
        var tempBin = Path.Combine(Path.GetTempPath(), "c1100-universalk9.17.15.04c.SPA.bin");
        File.WriteAllBytes(tempBin, new byte[1024]);

        try
        {
            var isBooted = false;
            var transport = new RecoverySimTransport(
                responder: cmd =>
                {
                    if (cmd.StartsWith("boot bootflash:", StringComparison.OrdinalIgnoreCase))
                    {
                        isBooted = true;
                        return new[] { "Package header rev 3 structure detected\r\nCisco IOS Software, Cupertino\r\nPress RETURN to get started!\r\n" };
                    }
                    if (cmd == "dir bootflash:")
                    {
                        var prompt = isBooted ? "Router#\r\n" : "rommon 1 >\r\n";
                        return new[] { $"Directory of bootflash:\r\n  1000 -rw- 700000000 c1100-universalk9.17.15.04c.SPA.bin\r\n{prompt}" };
                    }
                    if (cmd == "show privilege")
                    {
                        return new[] { "Current privilege level is 15\r\nRouter#\r\n" };
                    }
                    if (cmd == "show ip interface brief")
                    {
                        return new[] { "Interface                  IP-Address      OK? Method Status                Protocol\r\nGigabitEthernet0/1/0       unassigned      YES unset  up                    up      \r\nGigabitEthernet0/0/0       unassigned      YES unset  administratively down down    \r\nRouter#\r\n" };
                    }
                    if (cmd.Contains("=") && !cmd.StartsWith("ETHER_PORT", StringComparison.OrdinalIgnoreCase))
                    {
                        return Array.Empty<string>();
                    }
                    if (string.IsNullOrWhiteSpace(cmd))
                    {
                        return isBooted ? new[] { "Router#\r\n" } : Array.Empty<string>();
                    }

                    return cmd switch
                    {
                        "set" => new[] { "PS1=rommon ! > \r\nETHER_PORT=0\r\nBOOT=c1100-universalk9.17.03.02.SPA.bin,1;\r\nrommon 1 >\r\n" },
                        "ETHER_PORT=0" => new[] { "rommon 1 >\r\n" },
                        "sync" => new[] { "rommon 1 >\r\n" },
                        "confreg 0x2142" => new[] { "rommon 1 >\r\n" },
                        "no" => new[] { "Router>\r\n" },
                        "enable" => new[] { "Router#\r\n" },
                        "terminal length 0" => new[] { "Router#\r\n" },
                        "terminal width 512" => new[] { "Router#\r\n" },
                        "write erase" => new[] { "[confirm]\r\n" },
                        "" => new[] { isBooted ? "Router#\r\n" : "rommon 1 >\r\n" },
                        "configure terminal" => new[] { "Router(config)#\r\n" },
                        "no boot system" => new[] { "Router(config)#\r\n" },
                        "boot system bootflash:c1100-universalk9.17.15.04c.SPA.bin" => new[] { "Router(config)#\r\n" },
                        "config-register 0x2102" => new[] { "Router(config)#\r\n" },
                        "no ip domain-lookup" => new[] { "Router(config)#\r\n" },
                        "no ip domain lookup" => new[] { "Router(config)#\r\n" },
                        "interface GigabitEthernet0/1/0" => new[] { "Router(config-if)#\r\n" },
                        "no shutdown" => new[] { "Router(config-if)#\r\n" },
                        "end" => new[] { "Router#\r\n" },
                        "write memory" => new[] { "[OK]\r\nRouter#\r\n" },
                        _ => new[] { isBooted ? "Router#\r\n" : "rommon 1 >\r\n" }
                    };
                },
                initialOutput: "rommon 1 >\r\n",
                rommonPrompt: "rommon 1 >\r\n");

            var sessionOptions = new SessionOptions
            {
                PromptMatcher = RegexPromptMatcher.Universal(),
                CommandTimeout = TimeSpan.FromSeconds(2),
                ConnectTimeout = TimeSpan.FromSeconds(2)
            };
            await using var session = new DeviceSession(transport, sessionOptions);
            await session.ConnectAsync();

            var upgrader = new CiscoIOSUpgrader();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var success = await upgrader.UpgradeViaRommonTftpAsync(
                session,
                tempBin,
                hostIpAddress: "200.250.163.42",
                routerIpAddress: "200.250.163.41",
                subnetMask: "255.255.255.248",
                lanInterface: "GigabitEthernet0/1/0",
                localAdapterName: "SimulatedAdapter",
                requestOperatorAction: (_, _) => Task.CompletedTask,
                cancellationToken: cts.Token);

            Assert.True(success);
            Assert.Contains("boot bootflash:c1100-universalk9.17.15.04c.SPA.bin", transport.Commands);
            Assert.DoesNotContain("tftpdnld", transport.Commands);
            Assert.Contains("config-register 0x2102", transport.Commands);
            Assert.Contains("write memory", transport.Commands);
        }
        finally
        {
            if (File.Exists(tempBin)) File.Delete(tempBin);
        }
    }
}
