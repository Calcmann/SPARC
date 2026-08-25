using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetworkDevice.Core.Device;

namespace NetworkDevice.Core.Backup;

public sealed record BackupResult(
    string FilePath,
    string ReportPath,
    string Sha256,
    string Md5,
    DateTime Timestamp,
    long Bytes);

public sealed record BackupReport(
    string FileName,
    string Hostname,
    string Vendor,
    string? Model,
    string? OsName,
    string? OsVersion,
    string? SerialNumber,
    string Sha256,
    string Md5,
    DateTime Timestamp,
    long Bytes,
    string? Operator);

public sealed class ConfigBackupService
{
    private readonly string _outputDirectory;

    public ConfigBackupService(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
    }

    public async Task<BackupResult> SaveAsync(DeviceInfo device, string config, string? operatorName = null, CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.Now;
        var safeHost = Sanitize(device.Hostname ?? device.Model ?? "device");
        var fileName = $"{safeHost}-{timestamp:yyyyMMdd-HHmmss}.cfg";
        var filePath = Path.Combine(_outputDirectory, fileName);
        var content = config.TrimEnd() + Environment.NewLine;

        Directory.CreateDirectory(_outputDirectory);
        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);

        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var md5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var bytes = new FileInfo(filePath).Length;

        var report = new BackupReport(
            fileName,
            device.Hostname ?? string.Empty,
            device.Vendor,
            device.Model,
            device.OsName,
            device.OsVersion,
            device.SerialNumber,
            sha256,
            md5,
            timestamp,
            bytes,
            operatorName);

        var reportPath = Path.ChangeExtension(filePath, ".json");
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            cancellationToken);

        return new BackupResult(filePath, reportPath, sha256, md5, timestamp, bytes);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
            builder.Append(invalid.Contains(c) ? '_' : c);
        return builder.Length == 0 ? "device" : builder.ToString();
    }
}
