namespace NetworkDevice.Core.Device;

public sealed record DeviceInfo(
    string Vendor,
    string? Model,
    string? OsName,
    string? OsVersion,
    string? SerialNumber,
    string? Hostname)
{
    public string DisplayName => Hostname ?? Model ?? Vendor;

    public override string ToString() =>
        $"{Hostname ?? "?"} · {Model ?? "?"} · {OsName ?? "OS?"} {OsVersion}";
}
