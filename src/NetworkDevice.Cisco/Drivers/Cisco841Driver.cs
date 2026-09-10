using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Engines;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Cisco.Drivers;

public sealed class Cisco841ProvisioningEngine : IProvisioningEngine
{
    private readonly CiscoSaipConfigurator _configurator;

    public Cisco841ProvisioningEngine(CiscoSaipConfigurator configurator)
    {
        _configurator = configurator;
    }

    public async Task<bool> ProvisionAsync(
        DeviceSession session,
        SaipCircuitData saip,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (progress != null) await progress(10, "Provisionamento Cisco 841", "Configurando interfaces GigabitEthernet0/4 (WAN) e GigabitEthernet0/5 (LAN)...");
        await _configurator.ApplyConfigAsync(session, saip, "GigabitEthernet0/4", "GigabitEthernet0/5", ct);
        if (progress != null) await progress(100, "Provisionamento Concluído", "Configuração aplicada no Cisco 841.");
        return true;
    }
}

public sealed class Cisco841PasswordRecoveryEngine : IPasswordRecoveryEngine
{
    private readonly CiscoIOSRecovery _recovery;

    public Cisco841PasswordRecoveryEngine(CiscoIOSRecovery recovery)
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
            if (progress != null) await progress(10, "Login Console Cisco 841", "Efetuando login...");
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
        if (progress != null) await progress(100, "Password Recovery Concluído", "Senha resetada no Cisco 841.");
        return true;
    }
}

public sealed class Cisco841FirmwareRecoveryEngine : IFirmwareRecoveryEngine
{
    private readonly CiscoIOSUpgrader _upgrader;

    public Cisco841FirmwareRecoveryEngine(CiscoIOSUpgrader upgrader)
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
            "GigabitEthernet 5",
            null,
            null,
            instructOperator,
            ct);
    }
}

public sealed class Cisco841Driver : IDeviceDriver
{
    public DeviceManufacturer Manufacturer => DeviceManufacturer.Cisco;
    public DeviceSeries Series => DeviceSeries.Isr841;

    public IProvisioningEngine Provisioning { get; }
    public IPasswordRecoveryEngine PasswordRecovery { get; }
    public IFirmwareRecoveryEngine FirmwareRecovery { get; }
    public HpeProvisioningValidator? Validator => null;

    public Cisco841Driver(
        Func<string, Task>? logAsync = null,
        Action<int, string, string>? progressUpdated = null,
        BootInterruptProfile? profile = null)
    {
        var cisco841Profile = profile ?? BootInterruptProfiles.Cisco841;
        var configurator = new CiscoSaipConfigurator(logAsync);
        var recovery = new CiscoIOSRecovery(
            msg => logAsync?.Invoke(msg) ?? Task.CompletedTask,
            profile: cisco841Profile);
        var upgrader = new CiscoIOSUpgrader(
            logAsync,
            progressUpdated != null ? (pct, tit, desc) => progressUpdated(pct, tit, desc) : null);

        Provisioning = new Cisco841ProvisioningEngine(configurator);
        PasswordRecovery = new Cisco841PasswordRecoveryEngine(recovery);
        FirmwareRecovery = new Cisco841FirmwareRecoveryEngine(upgrader);
    }
}
