using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Engines;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Cisco.Drivers;

public sealed class Cisco1900ProvisioningEngine : IProvisioningEngine
{
    private readonly CiscoSaipConfigurator _configurator;

    public Cisco1900ProvisioningEngine(CiscoSaipConfigurator configurator)
    {
        _configurator = configurator;
    }

    public async Task<bool> ProvisionAsync(
        DeviceSession session,
        SaipCircuitData saip,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (progress != null) await progress(10, "Provisionamento Cisco 1900", "Iniciando configuração SAIP...");
        var cmds = CiscoSaipConfigurator.GenerateCommands(saip, "GigabitEthernet0/0", "GigabitEthernet0/1");
        foreach (var cmd in cmds)
        {
            await session.WriteLineAsync(cmd, ct);
            await Task.Delay(100, ct);
        }
        if (progress != null) await progress(100, "Provisionamento Concluído", "Comandos Cisco 1900 aplicados.");
        return true;
    }
}

public sealed class Cisco1900PasswordRecoveryEngine : IPasswordRecoveryEngine
{
    private readonly CiscoIOSRecovery _recovery;

    public Cisco1900PasswordRecoveryEngine(CiscoIOSRecovery recovery)
    {
        _recovery = recovery;
    }

    public async Task<bool> RecoverPasswordAsync(
        DeviceSession session,
        bool hasPassword,
        string? knownPassword = null,
        string? knownUsername = null,
        Func<string, CancellationToken, Task>? instructOperator = null,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (hasPassword && !string.IsNullOrEmpty(knownPassword))
        {
            if (progress != null) await progress(10, "Login Console", "Efetuando login com senha...");
            if (!string.IsNullOrWhiteSpace(knownUsername))
            {
                await session.WriteLineAsync(knownUsername.Trim(), ct);
                await Task.Delay(300, ct);
            }
            await session.WriteLineAsync(knownPassword, ct);
            await Task.Delay(500, ct);
            await session.WriteLineAsync("enable", ct);
            await Task.Delay(500, ct);
            return true;
        }

        await _recovery.RecoverAndResetAsync(session, instructOperator, ct);
        if (progress != null) await progress(100, "Password Recovery Concluído", "Senha resetada no Cisco 1900.");
        return true;
    }
}

public sealed class Cisco1900FirmwareRecoveryEngine : IFirmwareRecoveryEngine
{
    private readonly CiscoIOSUpgrader _upgrader;

    public Cisco1900FirmwareRecoveryEngine(CiscoIOSUpgrader upgrader)
    {
        _upgrader = upgrader;
    }

    public async Task<bool> RecoverFirmwareAsync(
        DeviceSession session,
        string firmwarePath,
        string hostIp,
        string routerIp,
        string subnetMask,
        Func<string, CancellationToken, Task>? instructOperator = null,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        return await _upgrader.UpgradeAsync(
            session,
            firmwarePath,
            hostIp,
            routerIp,
            subnetMask,
            "GigabitEthernet0/1",
            null,
            null,
            instructOperator,
            ct);
    }
}

public sealed class Cisco1900Driver : IDeviceDriver
{
    public DeviceManufacturer Manufacturer => DeviceManufacturer.Cisco;
    public DeviceSeries Series => DeviceSeries.Series1900;

    public IProvisioningEngine Provisioning { get; }
    public IPasswordRecoveryEngine PasswordRecovery { get; }
    public IFirmwareRecoveryEngine FirmwareRecovery { get; }
    public HpeProvisioningValidator? Validator => null;

    public Cisco1900Driver(
        Func<string, Task>? logAsync = null,
        Action<int, string, string>? progressUpdated = null,
        BootInterruptProfile? profile = null)
    {
        var ciscoProfile = profile ?? BootInterruptProfiles.Cisco1900;
        var configurator = new CiscoSaipConfigurator(logAsync);
        var recovery = new CiscoIOSRecovery(
            msg => logAsync?.Invoke(msg) ?? Task.CompletedTask,
            profile: ciscoProfile);
        var upgrader = new CiscoIOSUpgrader(
            logAsync,
            progressUpdated != null ? (pct, tit, desc) => progressUpdated(pct, tit, desc) : null);

        Provisioning = new Cisco1900ProvisioningEngine(configurator);
        PasswordRecovery = new Cisco1900PasswordRecoveryEngine(recovery);
        FirmwareRecovery = new Cisco1900FirmwareRecoveryEngine(upgrader);
    }
}
