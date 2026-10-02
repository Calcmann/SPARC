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

