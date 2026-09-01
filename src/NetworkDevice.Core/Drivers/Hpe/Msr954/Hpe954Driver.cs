using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Engines;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Recovery;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Drivers.Hpe.Msr954;

public sealed class Hpe954ProvisioningEngine : IProvisioningEngine
{
    private readonly HpeSaipConfigurator _configurator;

    public Hpe954ProvisioningEngine(HpeSaipConfigurator configurator)
    {
        _configurator = configurator;
    }

    public async Task<bool> ProvisionAsync(
        DeviceSession session,
        SaipCircuitData saip,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default)
    {
        if (progress != null) await progress(10, "Provisionamento HPE MSR954", "Aplicando configuração Comware 7...");
        var report = await _configurator.ApplyConfigAsync(session, saip, "GigabitEthernet0/0", "GigabitEthernet0/1", ct);
        if (progress != null) await progress(100, "Provisionamento Concluído", $"Configuração aplicada ({report.PassedCount} itens validados).");
        return report.PassedCount > 0;
    }
}

public sealed class Hpe954PasswordRecoveryEngine : IPasswordRecoveryEngine
{
    private readonly HpeComwareRecovery _recovery;

    public Hpe954PasswordRecoveryEngine(HpeComwareRecovery recovery)
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
            // Login direto com credencial informada pelo operador
            if (progress != null) await progress(10, "Login Console", "Efetuando login no HPE 954...");
            if (!string.IsNullOrWhiteSpace(knownUsername))
            {
                await session.WriteLineAsync(knownUsername.Trim(), ct);
                await Task.Delay(300, ct);
            }
            await session.WriteLineAsync(knownPassword, ct);
            await Task.Delay(500, ct);
            var isUserView = await HpeComwareRecovery.EnsureHpeUserViewAsync(session, null, ct);
            if (isUserView)
            {
                if (progress != null) await progress(100, "Login Concluído", "Acesso obtido com sucesso.");
                return true;
            }
        }

        // Caso sem senha ou login com senha falhe: aciona quebra de senha via BootWare (opções 6 + 8 + 1)
        if (progress != null)
        {
            _recovery.ProgressUpdated += (pct, tit, desc) => _ = progress(pct, tit, desc);
        }

        return await _recovery.RecoverAndResetAsync(
            session,
            instructOperator,
            firmwareFilePath: null,
            hostIpAddress: null,
            routerIpAddress: null,
            subnetMask: null,
            tftpDownloader: null,
            requestFirmwareFile: null,
            forceFirmwareRecovery: false,
            ct: ct);
    }
}

public sealed class Hpe954FirmwareRecoveryEngine : IFirmwareRecoveryEngine
{
    private readonly HpeComwareRecovery _recovery;

    public Hpe954FirmwareRecoveryEngine(HpeComwareRecovery recovery)
    {
        _recovery = recovery;
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
        if (progress != null)
        {
            _recovery.ProgressUpdated += (pct, tit, desc) => _ = progress(pct, tit, desc);
        }

        return await _recovery.RecoverAndResetAsync(
            session,
            instructOperator,
            firmwareFilePath: firmwarePath,
            hostIpAddress: hostIp,
            routerIpAddress: routerIp,
            subnetMask: subnetMask,
            tftpDownloader: null,
            requestFirmwareFile: null,
            forceFirmwareRecovery: true,
            ct: ct);
    }
}

public sealed class Hpe954Driver : IDeviceDriver
{
    public DeviceManufacturer Manufacturer => DeviceManufacturer.Hpe;
    public DeviceSeries Series => DeviceSeries.Msr954;

    public IProvisioningEngine Provisioning { get; }
    public IPasswordRecoveryEngine PasswordRecovery { get; }
    public IFirmwareRecoveryEngine FirmwareRecovery { get; }
    public HpeProvisioningValidator? Validator { get; }

    public Hpe954Driver(
        Func<string, Task>? logAsync = null,
        BootInterruptProfile? profile = null)
    {
        var hpeProfile = profile ?? BootInterruptProfiles.HpeMsr;
        var configurator = new HpeSaipConfigurator(logAsync);
        var recovery = new HpeComwareRecovery(logAsync, hpeProfile);

        Provisioning = new Hpe954ProvisioningEngine(configurator);
        PasswordRecovery = new Hpe954PasswordRecoveryEngine(recovery);
        FirmwareRecovery = new Hpe954FirmwareRecoveryEngine(recovery);
        Validator = new HpeProvisioningValidator(logAsync);
    }
}
