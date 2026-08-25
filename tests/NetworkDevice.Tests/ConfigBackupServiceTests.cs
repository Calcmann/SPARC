using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NetworkDevice.Core.Backup;
using NetworkDevice.Core.Device;

namespace NetworkDevice.Tests;

public class ConfigBackupServiceTests
{
    [Fact]
    public async Task SaveAsync_WritesConfigFileReportAndHashes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nd-tests", Guid.NewGuid().ToString("N"));
        var device = new DeviceInfo(
            "Cisco", "WS-C2960X", "Cisco IOS", "15.2(7)E6", "FOC123", "SW-DEPTO-01");
        const string config = "hostname SW-DEPTO-01\ninterface GigabitEthernet0/1\n switchport mode access\n";
        var service = new ConfigBackupService(dir);

        var result = await service.SaveAsync(device, config, "operador");

        Assert.True(File.Exists(result.FilePath));
        Assert.True(File.Exists(result.ReportPath));

        var content = config.TrimEnd() + Environment.NewLine;
        var expectedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

        Assert.Equal(expectedSha, result.Sha256);
        Assert.Equal(File.ReadAllText(result.FilePath), content);

        var report = JsonSerializer.Deserialize<BackupReport>(await File.ReadAllTextAsync(result.ReportPath));
        Assert.NotNull(report);
        Assert.Equal("SW-DEPTO-01", report!.Hostname);
        Assert.Equal("WS-C2960X", report.Model);
        Assert.Equal(result.Sha256, report.Sha256);
        Assert.Equal("operador", report.Operator);
        Assert.Contains("SW-DEPTO-01-", result.FilePath);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task SaveAsync_SanitizesInvalidCharactersInFileName()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nd-tests", Guid.NewGuid().ToString("N"));
        var device = new DeviceInfo("Cisco", "WS-C2960X", "Cisco IOS", null, null, "SW:DEPTO/01");
        var service = new ConfigBackupService(dir);

        var result = await service.SaveAsync(device, "hostname x\n");

        Assert.DoesNotContain(':', Path.GetFileName(result.FilePath));
        Assert.DoesNotContain('/', Path.GetFileName(result.FilePath));
        Assert.Contains("SW_DEPTO_01", result.FilePath);

        Directory.Delete(dir, recursive: true);
    }
}