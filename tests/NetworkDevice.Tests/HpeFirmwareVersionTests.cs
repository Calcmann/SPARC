using NetworkDevice.Protocols.Hpe;
using System.Reflection;
using Xunit;

namespace NetworkDevice.Tests;

public class HpeFirmwareVersionTests
{
    private const string DisplayVersionHpe_6749P43 = @"
HPE Comware Software, Version 7.1.064, Release 6749P43
Copyright (c) 2010-2021 Hewlett Packard Enterprise Development LP
HPE MSR954-W 1GbE 4SFP(WW) Router uptime is 0 weeks, 0 days, 1 hour, 12 minutes
Last reboot reason : User reboot
Boot image: flash:/msr954-cmw710-boot-r6749p43.bin
System image: flash:/msr954-cmw710-system-r6749p43.bin
";

    private const string DisplayVersionHpe_6749P20 = @"
HPE Comware Software, Version 7.1.064, Release 6749P20
Copyright (c) 2010-2019 Hewlett Packard Enterprise Development LP
HPE MSR954-W 1GbE 4SFP(WW) Router uptime is 2 weeks, 1 days, 4 hours, 5 minutes
";

    private const string DisplayBootLoaderHpe_6749P43 = @"
The current boot app is: flash:/msr954-cmw710-system-r6749p43.bin
The main boot app is: flash:/msr954-cmw710-system-r6749p43.bin
The backup boot app is: flash:/msr954-cmw710-system-r6749p20.bin
";

    private static string InvokeExtrairVersaoDeNomeArquivo(string fileName)
    {
        var method = typeof(HpeComwareUpgrader).GetMethod("ExtrairVersaoDeNomeArquivo", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method.Invoke(null, new object[] { fileName })!;
    }

    private static string InvokeExtrairVersaoDeTexto(string bootLoader, string version)
    {
        var method = typeof(HpeComwareUpgrader).GetMethod("ExtrairVersaoDeTexto", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method.Invoke(null, new object[] { bootLoader, version })!;
    }

    [Theory]
    [InlineData("MSR954-CMW710-R6749P43.ipe", "R6749P43")]
    [InlineData("MSR954_CMW710_R6749P43.ipe", "R6749P43")]
    [InlineData("msr954-cmw710-system-r6749p43.bin", "R6749P43")]
    [InlineData("MSR930-CMW710-R0605P20.ipe", "R0605P20")]
    [InlineData("MSR1000-CMW710-6749P45.ipe", "R6749P45")]
    public void ExtrairVersaoDeNomeArquivo_FormatosValidos_ExtraiCorretamente(string fileName, string expected)
    {
        var result = InvokeExtrairVersaoDeNomeArquivo(fileName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtrairVersaoDeNomeArquivo_NomeSemVersao_NaoRetornaDefaultHardcoded()
    {
        var result = InvokeExtrairVersaoDeNomeArquivo("firmware_msr.ipe");
        Assert.Empty(result);
    }

    [Fact]
    public void ExtrairVersaoDeTexto_DisplayVersion_ExtraiRelease()
    {
        var result = InvokeExtrairVersaoDeTexto(string.Empty, DisplayVersionHpe_6749P43);
        Assert.Equal("R6749P43", result);
    }

    [Fact]
    public void ExtrairVersaoDeTexto_DisplayBootLoader_ExtraiVersaoDoBoot()
    {
        var result = InvokeExtrairVersaoDeTexto(DisplayBootLoaderHpe_6749P43, string.Empty);
        Assert.Equal("R6749P43", result);
    }

    [Fact]
    public void ValidacaoDeVersao_MesmoMajorComPatchDiferente_NaoPulaUpgrade()
    {
        // Roteador com R6749P20 e imagem proposta R6749P43
        var runningVer = InvokeExtrairVersaoDeTexto(string.Empty, DisplayVersionHpe_6749P20);
        var targetVer = InvokeExtrairVersaoDeNomeArquivo("MSR954-CMW710-R6749P43.ipe");

        Assert.Equal("R6749P20", runningVer);
        Assert.Equal("R6749P43", targetVer);
        Assert.NotEqual(runningVer, targetVer);
    }
}
