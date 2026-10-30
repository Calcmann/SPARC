using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Engines;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Cisco.Drivers;

public sealed class Cisco1100ProvisioningEngine : IProvisioningEngine
{
    private readonly CiscoSaipConfigurator _configurator;

    public Cisco1100ProvisioningEngine(CiscoSaipConfigurator configurator)
    {
        _configurator = configurator;
    }

    public async Task<bool> ProvisionAsync(
        DeviceSession session,
        SaipCircuitData saip,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (progress != null) await progress(10, "Provisionamento Cisco 1100", "Configurando interfaces GigabitEthernet0/0/0 (WAN) e Vlan1 (LAN)...");
        await _configurator.ApplyConfigAsync(session, saip, "GigabitEthernet0/0/0", "Vlan1", ct);
        if (progress != null) await progress(100, "Provisionamento Concluído", "Configuração aplicada no Cisco 1100 / C1111-4P.");
        return true;
    }
}

public sealed class Cisco1100PasswordRecoveryEngine : IPasswordRecoveryEngine
{
    private readonly CiscoIOSRecovery _recovery;

    public Cisco1100PasswordRecoveryEngine(CiscoIOSRecovery recovery)
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
            if (progress != null) await progress(10, "Login Console Cisco 1100", "Efetuando login...");
            if (!string.IsNullOrWhiteSpace(knownUsername))
            {
                await session.WriteLineAsync(knownUsername.Trim(), ct);
                await Task.Delay(300, ct);
            }
            await session.WriteLineAsync(knownPassword, ct);
            await Task.Delay(500, ct);
            var candidates = new List<string> { knownPassword };
            if (!candidates.Contains("PRO1AN")) candidates.Add("PRO1AN");
            var adapter = new CiscoIOSAdapter(enableSecret: knownPassword, candidatePasswords: candidates);
            try
            {
                await adapter.EnterPrivilegedExecAsync(session, candidates, ct);
            }
            catch { }
            return true;
        }

        await _recovery.RecoverAndResetAsync(session, instructOperator, ct);
        if (progress != null) await progress(100, "Password Recovery Concluído", "Senha resetada no Cisco 1100 / C1111-4P.");
        return true;
    }
}

public sealed class Cisco1100FirmwareRecoveryEngine : IFirmwareRecoveryEngine
{
    private readonly CiscoIOSUpgrader _upgrader;

    public Cisco1100FirmwareRecoveryEngine(CiscoIOSUpgrader upgrader)
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
            "GigabitEthernet0/1/0",
            null,
            null,
            instructOperator,
            ct,
            candidatePasswords: new[] { "PRO1AN" });
    }
}

public sealed class Cisco1100Driver : IDeviceDriver
{
    public DeviceManufacturer Manufacturer => DeviceManufacturer.Cisco;
    public DeviceSeries Series => DeviceSeries.Isr1100;

    public IProvisioningEngine Provisioning { get; }
    public IPasswordRecoveryEngine PasswordRecovery { get; }
    public IFirmwareRecoveryEngine FirmwareRecovery { get; }
    public HpeProvisioningValidator? Validator => null;

    public Cisco1100Driver(
        Func<string, Task>? logAsync = null,
        Action<int, string, string>? progressUpdated = null,
        BootInterruptProfile? profile = null)
    {
        var cisco1100Profile = profile ?? BootInterruptProfiles.Cisco1100;
        var configurator = new CiscoSaipConfigurator(logAsync);
        var recovery = new CiscoIOSRecovery(
            msg => logAsync?.Invoke(msg) ?? Task.CompletedTask,
            profile: cisco1100Profile);
        var upgrader = new CiscoIOSUpgrader(
            logAsync,
            progressUpdated != null ? (pct, tit, desc) => progressUpdated(pct, tit, desc) : null);

        Provisioning = new Cisco1100ProvisioningEngine(configurator);
        PasswordRecovery = new Cisco1100PasswordRecoveryEngine(recovery);
        FirmwareRecovery = new Cisco1100FirmwareRecoveryEngine(upgrader);
    }
}
