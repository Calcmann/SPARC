using NetworkDevice.Core.UI;
using Xunit;

namespace NetworkDevice.Tests;

public class MobileDeviceHelperTests
{
    [Theory]
    [InlineData("Samsung", "SM-A546E", "Samsung Galaxy A54 5G")]
    [InlineData("Samsung", "SM-A145M", "Samsung Galaxy A14")]
    [InlineData("Samsung", "SM-A346M", "Samsung Galaxy A34 5G")]
    [InlineData("Samsung", "SM-A035M", "Samsung Galaxy A03")]
    [InlineData("Samsung", "SM-S911B", "Samsung Galaxy S23 Series")]
    [InlineData("Samsung", "SM-X200", "Samsung Galaxy Tab (SM-X200)")]
    [InlineData("Motorola", "moto g54 5g", "Moto G54 5G")]
    [InlineData("Motorola", "XT2343-1", "Motorola (XT2343-1)")]
    [InlineData("Xiaomi", "Redmi Note 12", "Redmi Note 12")]
    [InlineData("Xiaomi", "23021RAAEG", "Xiaomi / Redmi (23021RAAEG)")]
    [InlineData("Xiaomi", "POCO X6 Pro", "POCO X6 Pro")]
    [InlineData("Realme", "RMX3710", "Realme (RMX3710)")]
    [InlineData("Oppo", "CPH2579", "Oppo (CPH2579)")]
    [InlineData("Google", "Pixel 7a", "Google Pixel 7a")]
    public void ResolveFriendlyModelName_RecognizesDiverseSmartphones(string mfr, string rawModel, string expected)
    {
        var result = MobileDeviceHelper.ResolveFriendlyModelName(mfr, rawModel);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(320, MobileScreenCategory.Compact)]
    [InlineData(359, MobileScreenCategory.Compact)]
    [InlineData(360, MobileScreenCategory.Standard)]
    [InlineData(393, MobileScreenCategory.Standard)]
    [InlineData(412, MobileScreenCategory.Standard)]
    [InlineData(440, MobileScreenCategory.Large)]
    [InlineData(580, MobileScreenCategory.Large)]
    [InlineData(600, MobileScreenCategory.Tablet)]
    [InlineData(800, MobileScreenCategory.Tablet)]
    public void ClassifyScreenCategory_AdaptsToAllScreenSizes(double widthDp, MobileScreenCategory expected)
    {
        var result = MobileDeviceHelper.ClassifyScreenCategory(widthDp);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ClassifyHardwareTier_EntryTier_LowRamOrQuadCore()
    {
        var tier = MobileDeviceHelper.ClassifyHardwareTier(2.0, 4, false);
        Assert.Equal(MobileHardwareTier.Entry, tier);

        var buffer = MobileDeviceHelper.DetermineOptimalUdpBufferSizeBytes(tier);
        Assert.Equal(1024 * 1024, buffer); // 1 MB buffer

        var interval = MobileDeviceHelper.DetermineTelemetryRefreshIntervalMs(tier);
        Assert.Equal(1000, interval); // 1000 ms interval
    }

    [Fact]
    public void ClassifyHardwareTier_MidTier_StandardPhone()
    {
        var tier = MobileDeviceHelper.ClassifyHardwareTier(4.0, 8, false);
        Assert.Equal(MobileHardwareTier.Mid, tier);

        var buffer = MobileDeviceHelper.DetermineOptimalUdpBufferSizeBytes(tier);
        Assert.Equal(2 * 1024 * 1024, buffer); // 2 MB buffer

        var interval = MobileDeviceHelper.DetermineTelemetryRefreshIntervalMs(tier);
        Assert.Equal(500, interval); // 500 ms interval
    }

    [Fact]
    public void ClassifyHardwareTier_HighTier_FlagshipPhone()
    {
        var tier = MobileDeviceHelper.ClassifyHardwareTier(8.0, 8, false);
        Assert.Equal(MobileHardwareTier.High, tier);

        var buffer = MobileDeviceHelper.DetermineOptimalUdpBufferSizeBytes(tier);
        Assert.Equal(4 * 1024 * 1024, buffer); // 4 MB buffer
    }

    [Theory]
    [InlineData("Xiaomi", true)]
    [InlineData("Redmi", true)]
    [InlineData("POCO", true)]
    [InlineData("Realme", true)]
    [InlineData("Oppo", true)]
    [InlineData("Vivo", true)]
    [InlineData("Samsung", false)]
    [InlineData("Motorola", false)]
    public void DetermineVendorOtgRequirement_DetectsBrandsRequiringManualOTG(string mfr, bool expectedRequiresManual)
    {
        var requiresManual = MobileDeviceHelper.DetermineVendorOtgRequirement(mfr, out var guide);
        Assert.Equal(expectedRequiresManual, requiresManual);
        Assert.False(string.IsNullOrWhiteSpace(guide));
    }
}
