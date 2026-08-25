using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Device;

public interface IDeviceAdapter
{
    string Vendor { get; }

    Task EnterPrivilegedExecAsync(DeviceSession session, CancellationToken cancellationToken = default);

    Task<DeviceInfo> IdentifyAsync(DeviceSession session, CancellationToken cancellationToken = default);

    Task<string> GetRunningConfigAsync(DeviceSession session, CancellationToken cancellationToken = default);

    Task<string> GetStartupConfigAsync(DeviceSession session, CancellationToken cancellationToken = default);

    Task SaveConfigAsync(DeviceSession session, CancellationToken cancellationToken = default);
}
