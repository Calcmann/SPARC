using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Validation;
using Xunit;

namespace NetworkDevice.Tests;

public class FirmwareCompatibilityTests
{
    [Theory]
    [InlineData(DeviceSeries.Isr921, "c900-universalk9-mz.SPA.158-3.M4.bin", true)]
    [InlineData(DeviceSeries.Isr921, "c900-universalk9_npe-mz.SPA.159-3.M2.bin", true)]
    [InlineData(DeviceSeries.Isr921, "c921-universalk9-mz.bin", true)]
    [InlineData(DeviceSeries.Isr921, "c1900-universalk9-mz.SPA.158-3.M7.bin", false)]
    [InlineData(DeviceSeries.Isr921, "c841-universalk9-mz.bin", false)]
    [InlineData(DeviceSeries.Isr921, "c1905-universalk9-mz.bin", false)]
    [InlineData(DeviceSeries.Isr921, "msr954-cmw710-r6749p43.ipe", false)]
    [InlineData(DeviceSeries.Isr921, "firmware.tar", false)]
    public void Validate_Cisco921_ValidatesOnlyCompatibleBinImages(DeviceSeries series, string fileName, bool expectedCompatible)
    {
        var result = FirmwareCompatibilityValidator.Validate(series, fileName);
        Assert.Equal(expectedCompatible, result.IsCompatible);
        if (!expectedCompatible)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
    }

    [Theory]
    [InlineData(DeviceSeries.Isr841, "c841-universalk9-mz.SPA.158-3.M4.bin", true)]
    [InlineData(DeviceSeries.Isr841, "c800-universalk9-mz.SPA.159-3.M2.bin", true)]
    [InlineData(DeviceSeries.Isr841, "c800m-universalk9-mz.bin", true)]
    [InlineData(DeviceSeries.Isr841, "c900-universalk9-mz.SPA.158-3.M4.bin", false)]
    [InlineData(DeviceSeries.Isr841, "c1900-universalk9-mz.SPA.158-3.M7.bin", false)]
    [InlineData(DeviceSeries.Isr841, "msr954-cmw710-r6749p43.ipe", false)]
    [InlineData(DeviceSeries.Isr841, "firmware.tar", false)]
    public void Validate_Cisco841_ValidatesOnlyCompatibleBinImages(DeviceSeries series, string fileName, bool expectedCompatible)
    {
        var result = FirmwareCompatibilityValidator.Validate(series, fileName);
        Assert.Equal(expectedCompatible, result.IsCompatible);
        if (!expectedCompatible)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
    }

    [Theory]
    [InlineData(DeviceSeries.Series1900, "c1900-universalk9-mz.SPA.158-3.M7.bin", true)]
    [InlineData(DeviceSeries.Series1900, "c1905-universalk9-mz.SPA.152-4.M5.bin", true)]
    [InlineData(DeviceSeries.Series1900, "c1921-universalk9-mz.bin", true)]
    [InlineData(DeviceSeries.Series1900, "c1941-universalk9-mz.bin", true)]
    [InlineData(DeviceSeries.Series1900, "c900-universalk9-mz.SPA.158-3.M4.bin", false)]
    [InlineData(DeviceSeries.Series1900, "msr954-cmw710-r6749p43.ipe", false)]
    [InlineData(DeviceSeries.Series1900, "c1900-firmware.zip", false)]
    public void Validate_Cisco1900_ValidatesOnlyCompatibleBinImages(DeviceSeries series, string fileName, bool expectedCompatible)
    {
        var result = FirmwareCompatibilityValidator.Validate(series, fileName);
        Assert.Equal(expectedCompatible, result.IsCompatible);
        if (!expectedCompatible)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
    }

    [Theory]
    [InlineData(DeviceSeries.Msr954, "msr954-cmw710-r6749p43.ipe", true)]
    [InlineData(DeviceSeries.Msr954, "msr954-cmw710-boot.bin", true)]
    [InlineData(DeviceSeries.Msr954, "msr930-cmw710-r6749p43.ipe", false)]
    [InlineData(DeviceSeries.Msr954, "c900-universalk9-mz.bin", false)]
    [InlineData(DeviceSeries.Msr954, "c1900-universalk9-mz.bin", false)]
    public void Validate_HpeMsr954_ValidatesOnlyCompatibleIpeImages(DeviceSeries series, string fileName, bool expectedCompatible)
    {
        var result = FirmwareCompatibilityValidator.Validate(series, fileName);
        Assert.Equal(expectedCompatible, result.IsCompatible);
        if (!expectedCompatible)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
    }

    [Theory]
    [InlineData(DeviceSeries.Msr930, "msr930-cmw710-r6749p43.ipe", true)]
    [InlineData(DeviceSeries.Msr930, "msr930-cmw710-boot.bin", true)]
    [InlineData(DeviceSeries.Msr930, "msr954-cmw710-r6749p43.ipe", false)]
    [InlineData(DeviceSeries.Msr930, "msr1000-cmw710-r6749p43.ipe", false)]
    [InlineData(DeviceSeries.Msr930, "c900-universalk9-mz.bin", false)]
    [InlineData(DeviceSeries.Msr930, "c1900-universalk9-mz.bin", false)]
    public void Validate_HpeMsr930_ValidatesOnlyCompatibleIpeImages(DeviceSeries series, string fileName, bool expectedCompatible)
    {
        var result = FirmwareCompatibilityValidator.Validate(series, fileName);
        Assert.Equal(expectedCompatible, result.IsCompatible);
        if (!expectedCompatible)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
    }

    [Theory]
    [InlineData(DeviceSeries.Msr1002, "msr1000-cmw710-r6749p43.ipe", true)]
    [InlineData(DeviceSeries.Msr1002, "msr1002-cmw710-boot.bin", true)]
    [InlineData(DeviceSeries.Msr1002, "msr954-cmw710-r6749p43.ipe", false)]
    [InlineData(DeviceSeries.Msr1002, "msr930-cmw710-r6749p43.ipe", false)]
    [InlineData(DeviceSeries.Msr1002, "c900-universalk9-mz.bin", false)]
    [InlineData(DeviceSeries.Msr1002, "c1900-universalk9-mz.bin", false)]
    public void Validate_HpeMsr1002_ValidatesOnlyCompatibleIpeImages(DeviceSeries series, string fileName, bool expectedCompatible)
    {
        var result = FirmwareCompatibilityValidator.Validate(series, fileName);
        Assert.Equal(expectedCompatible, result.IsCompatible);
        if (!expectedCompatible)
        {
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        }
    }
}
