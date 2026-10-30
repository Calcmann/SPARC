using System;
using System.IO;
using System.Threading.Tasks;
using NetworkDevice.Core.Firmware;
using NetworkDevice.Core.Licensing;
using Xunit;

namespace NetworkDevice.Tests;

public class SparcAppUpdateServiceTests
{
    [Theory]
    [InlineData("0.8.34", "0.8.33", true)]
    [InlineData("0.8.34", "0.8.34", false)]
    [InlineData("0.8.33", "0.8.34", false)]
    [InlineData("0.9.0", "0.8.34", true)]
    [InlineData("1.0.0", "0.8.34", true)]
    [InlineData("v0.8.40", "0.8.39", true)]
    [InlineData("0.8.40 Beta", "0.8.40", false)]
    public void IsNewerVersion_ComparesCorrectly(string remote, string current, bool expected)
    {
        var result = SparcAppUpdateService.IsNewerVersion(remote, current);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0.8.33", "0.8.34", true)]
    [InlineData("0.8.175", "0.8.176", true)]
    [InlineData("0.8.34", "0.8.34", false)]
    [InlineData("0.8.35", "0.8.34", false)]
    [InlineData("v0.8.30", "0.8.31", true)]
    [InlineData("0.8.30", "v0.8.31", true)]
    [InlineData("0.7.99", "0.8.0", true)]
    public void IsOlderVersion_DetectsDowngradeCorrectly(string remote, string current, bool expected)
    {
        var result = SparcAppUpdateService.IsOlderVersion(remote, current);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("0.8.176", "0.8.175", 1)]
    [InlineData("0.8.175", "0.8.176", -1)]
    [InlineData("0.8.176", "0.8.176", 0)]
    [InlineData("v0.8.176", "0.8.176", 0)]
    [InlineData("0.9.0", "0.8.176", 1)]
    [InlineData("0.8.0", "0.9.0", -1)]
    public void CompareVersions_ComparesNumerically(string vA, string vB, int expectedSign)
    {
        var comp = SparcAppUpdateService.CompareVersions(vA, vB);
        var sign = Math.Sign(comp);
        Assert.Equal(expectedSign, sign);
    }

    [Fact]
    public void SparcPlatformRelease_SupportsIsDowngradeFlag()
    {
        var rel = new SparcPlatformRelease
        {
            Version = "0.8.175",
            DownloadUrl = "https://example.com/sparc.exe",
            IsDowngrade = true
        };
        Assert.True(rel.IsDowngrade);
        Assert.Equal("0.8.175", rel.Version);
    }

    [Fact]
    public void SparcTextSanitizer_FormatsNamesAndCompaniesCorrectly()
    {
        Assert.Equal("Carlos Alberto da Silva", SparcTextSanitizer.FormatPersonOrCompanyName("CARLOS ALBERTO DA SILVA"));
        Assert.Equal("Telefônica Brasil S.A.", SparcTextSanitizer.FormatPersonOrCompanyName("telefônica brasil s.a."));
        Assert.Equal("Empresa de TI LTDA", SparcTextSanitizer.FormatPersonOrCompanyName("empresa de ti ltda"));
    }

    [Fact]
    public void SparcTextSanitizer_FormatsEmployeeIdAndPhoneAndUf()
    {
        Assert.Equal("MAT-12345", SparcTextSanitizer.FormatEmployeeId("  mat-12345  "));
        Assert.Equal("(11) 98765-4321", SparcTextSanitizer.FormatPhone("11987654321"));
        Assert.Equal("(11) 3333-4444", SparcTextSanitizer.FormatPhone("1133334444"));
        Assert.Equal("SP", SparcTextSanitizer.FormatUf("sp"));
        Assert.Equal("contato@empresa.com", SparcTextSanitizer.FormatEmail("  CONTATO@EMPRESA.COM  "));
    }
}

