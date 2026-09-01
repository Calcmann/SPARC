using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Engines;

public interface IDeviceDetector
{
    Task<DeviceDetectionResult> DetectAsync(ITransport transport, CancellationToken ct = default);
    DeviceDetectionResult ClassifyPrompt(string rawPrompt, DeviceSeries userSelectedSeries = DeviceSeries.Unknown);
}

public interface IProvisioningEngine
{
    Task<bool> ProvisionAsync(
        DeviceSession session,
        SaipCircuitData saip,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default);
}

public interface IPasswordRecoveryEngine
{
    Task<bool> RecoverPasswordAsync(
        DeviceSession session,
        bool hasPassword,
        string? knownPassword = null,
        string? knownUsername = null,
        Func<string, CancellationToken, Task>? instructOperator = null,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default);
}

public interface IFirmwareRecoveryEngine
{
    Task<bool> RecoverFirmwareAsync(
        DeviceSession session,
        string firmwarePath,
        string hostIp,
        string routerIp,
        string subnetMask,
        Func<string, CancellationToken, Task>? instructOperator = null,
        Func<int, string, string, Task>? progress = null,
        CancellationToken ct = default);
}

public interface IDeviceDriver
{
    DeviceManufacturer Manufacturer { get; }
    DeviceSeries Series { get; }
    IProvisioningEngine Provisioning { get; }
    IPasswordRecoveryEngine PasswordRecovery { get; }
    IFirmwareRecoveryEngine FirmwareRecovery { get; }
    HpeProvisioningValidator? Validator { get; }
}
