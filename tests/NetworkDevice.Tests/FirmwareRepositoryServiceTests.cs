using System;
using System.IO;
using System.Threading.Tasks;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;
using NetworkDevice.Core.Validation;
using Xunit;

namespace NetworkDevice.Tests;

public class FirmwareRepositoryServiceTests : IDisposable
{
    private readonly string _tempDir;

    public FirmwareRepositoryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "sparc_fw_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void ModelMap_ContainsAllKeySeries()
    {
        var fgt = FirmwareModelMap.GetDefinition(DeviceSeries.FortiGate40F);
        Assert.NotNull(fgt);
        Assert.Equal("fortigate-40f", fgt.FolderName);
        Assert.Equal(".out", fgt.DefaultExtension);

        var c921 = FirmwareModelMap.GetDefinition(DeviceSeries.Isr921);
        Assert.NotNull(c921);
        Assert.Equal("cisco-c921", c921.FolderName);

        var c841 = FirmwareModelMap.GetDefinition(DeviceSeries.Isr841);
        Assert.NotNull(c841);
        Assert.Equal("cisco-c841", c841.FolderName);

        var msr954 = FirmwareModelMap.GetDefinition(DeviceSeries.Msr954);
        Assert.NotNull(msr954);
        Assert.Equal("hpe-msr954", msr954.FolderName);
    }

    [Fact]
    public void EnsureLocalRepositoryStructure_CreatesAllFolders()
    {
        var svc = new FirmwareRepositoryService(_tempDir);
        svc.EnsureLocalRepositoryStructure();

        foreach (var def in FirmwareModelMap.AllDefinitions)
        {
            var folder = Path.Combine(_tempDir, def.FolderName);
            Assert.True(Directory.Exists(folder), $"Pasta {def.FolderName} deve existir no repositório local.");
        }
    }

    [Fact]
    public void GetLocalFirmware_ReturnsNullWhenEmpty_AndReturnsFileWhenPresent()
    {
        var svc = new FirmwareRepositoryService(_tempDir);
        var initial = svc.GetLocalFirmware(DeviceSeries.Isr921);
        Assert.Null(initial);

        var c921Folder = svc.GetLocalFolderForSeries(DeviceSeries.Isr921);
        var testBin = Path.Combine(c921Folder, "c900-universalk9-mz.SPA.159-3.M4.bin");
        File.WriteAllText(testBin, "dummy-fw-content");

        var found = svc.GetLocalFirmware(DeviceSeries.Isr921);
        Assert.NotNull(found);
        Assert.Equal("c900-universalk9-mz.SPA.159-3.M4.bin", found.FileName);
        Assert.Equal(DeviceSeries.Isr921, found.Series);
        Assert.Equal("159-3.M4", found.DetectedVersion);
    }

    [Fact]
    public void FirmwareCompatibilityValidator_AcceptsShortAndStandardNames()
    {
        // Cisco 921
        var val921Standard = FirmwareCompatibilityValidator.Validate(DeviceSeries.Isr921, "c900-universalk9-mz.SPA.159-3.M4.bin");
        Assert.True(val921Standard.IsCompatible);

        var val921Short = FirmwareCompatibilityValidator.Validate(DeviceSeries.Isr921, "c921.bin");
        Assert.True(val921Short.IsCompatible);

        // Cisco 841
        var val841Standard = FirmwareCompatibilityValidator.Validate(DeviceSeries.Isr841, "c841-universalk9-mz.SPA.157-3.M9.bin");
        Assert.True(val841Standard.IsCompatible);

        var val841Short = FirmwareCompatibilityValidator.Validate(DeviceSeries.Isr841, "c841.bin");
        Assert.True(val841Short.IsCompatible);

        // FortiGate 40F
        var valFgtStandard = FirmwareCompatibilityValidator.Validate(DeviceSeries.FortiGate40F, "FGT_40F-v7.2.6.F-build1575-FORTINET.out");
        Assert.True(valFgtStandard.IsCompatible);

        var valFgtShort = FirmwareCompatibilityValidator.Validate(DeviceSeries.FortiGate40F, "fgt40f.out");
        Assert.True(valFgtShort.IsCompatible);

        // Incompatibilidade cruzada
        var valCross = FirmwareCompatibilityValidator.Validate(DeviceSeries.Isr921, "c1900-universalk9-mz.bin");
        Assert.False(valCross.IsCompatible);
    }

    [Theory]
    [InlineData("c900-universalk9-mz.SPA.159-3.M12.bin", DeviceSeries.Isr921)]
    [InlineData("c800m-universalk9-mz.SPA.159-3.M12.bin", DeviceSeries.Isr841)]
    [InlineData("c1900-universalk9-mz.SPA.157-3.M9.bin", DeviceSeries.Series1900)]
    [InlineData("FGT_40F-v7.2.11.M-build1740-FORTINET.out", DeviceSeries.FortiGate40F)]
    [InlineData("MSR93X-CMW520-R2512P04.BIN", DeviceSeries.Msr930)]
    [InlineData("MSR954-CMW710-R6749P43.ipe", DeviceSeries.Msr954)]
    [InlineData("MSR1002-CMW710-R6749P43.ipe", DeviceSeries.Msr1002)]
    [InlineData("MSR100X-CMW710-R6749P43.ipe", DeviceSeries.Msr1002)]
    public void MatchSeriesFromFileName_MapsAllRealAssetsCorrectly(string fileName, DeviceSeries expected)
    {
        var matched = FirmwareModelMap.MatchSeriesFromFileName(fileName);
        Assert.Equal(expected, matched);
    }

    [Fact]
    public void GitHubReleaseProperties_AreConfiguredProperly()
    {
        var svc = new FirmwareRepositoryService(_tempDir);
        Assert.Equal("Calcmann", svc.RemoteRepoOwner);
        Assert.Equal("repo", svc.RemoteRepoName);
        Assert.Equal("homologados", svc.ReleaseTag);
        Assert.Equal("https://github.com/Calcmann/repo/releases", svc.ReleasesWebUrl);
        Assert.Equal("https://github.com/Calcmann/repo/releases/new", svc.NewReleaseWebUrl);
    }
}
