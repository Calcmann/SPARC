using NetworkDevice.Cisco;
using Xunit;

namespace NetworkDevice.Tests;

public class CiscoIOSFirmwareVersionInspectorTests
{
    private const string ShowVersionCisco921_OldVersion = @"
Cisco IOS Software, C900 Software (C900-UNIVERSALK9-M), Version 15.6(3)M2, RELEASE SOFTWARE (fc2)
Technical Support: http://www.cisco.com/techsupport
Copyright (c) 1986-2017 by Cisco Systems, Inc.
Compiled Wed 29-Mar-17 14:04 by prod_rel_team

ROM: System Bootstrap, Version 15.6(1r)T, RELEASE SOFTWARE (fc1)

cisco C921-4P (revision 1.0) with 1048576K bytes of memory.
Processor board ID FGL223311AA
4 Gigabit Ethernet interfaces
DRAM configuration is 32 bits wide with parity disabled.
256K bytes of non-volatile configuration memory.
2097152K bytes of eUSB flash (Read/Write)

Configuration register is 0x2102
";

    private const string ShowVersionCisco921_159_M2 = @"
Cisco IOS Software, C900 Software (C900-UNIVERSALK9-M), Version 15.9(3)M2, RELEASE SOFTWARE (fc2)
Technical Support: http://www.cisco.com/techsupport
Copyright (c) 1986-2019 by Cisco Systems, Inc.

ROM: System Bootstrap, Version 15.6(1r)T, RELEASE SOFTWARE (fc1)

System image file is ""flash:c900-universalk9-mz.SPA.159-3.M2.bin""
Last reload type: Normal Reload

cisco C921-4P (revision 1.0) with 1048576K bytes of memory.
";

    private const string ShowVersionCisco921_159_M4 = @"
Cisco IOS Software, C900 Software (C900-UNIVERSALK9-M), Version 15.9(3)M4, RELEASE SOFTWARE (fc1)
Technical Support: http://www.cisco.com/techsupport
Copyright (c) 1986-2021 by Cisco Systems, Inc.

ROM: System Bootstrap, Version 15.6(1r)T, RELEASE SOFTWARE (fc1)

System image file is ""flash:c900-universalk9-mz.SPA.159-3.M4.bin""
Last reload type: Normal Reload

cisco C921-4P (revision 1.0) with 1048576K bytes of memory.
";

    [Fact]
    public void ExtractFromFileName_FormatoComPonto_ExtraiVersaoCorreta()
    {
        var info = CiscoIOSFirmwareVersionInspector.ExtractFromFileName("c900-universalk9-mz.SPA.15.9-3.M4.bin");
        Assert.Equal("15.9(3)M4", info.CanonicalVersion);
        Assert.Equal("c900-universalk9-mz.SPA.15.9-3.M4.bin", info.RunningImageFileName);
    }

    [Fact]
    public void ExtractFromFileName_FormatoSemPonto_ExtraiVersaoCorreta()
    {
        var info = CiscoIOSFirmwareVersionInspector.ExtractFromFileName("c900-universalk9-mz.SPA.159-3.M4.bin");
        Assert.Equal("15.9(3)M4", info.CanonicalVersion);
        Assert.Equal("c900-universalk9-mz.SPA.159-3.M4.bin", info.RunningImageFileName);
    }

    [Fact]
    public void ExtractFromFileName_Série841_ExtraiVersaoCorreta()
    {
        var info = CiscoIOSFirmwareVersionInspector.ExtractFromFileName("c841-universalk9-mz.SPA.157-3.M9.bin");
        Assert.Equal("15.7(3)M9", info.CanonicalVersion);
    }

    [Fact]
    public void ExtractFromShowVersion_ExtraiImagemEVersaoCanonica()
    {
        var info = CiscoIOSFirmwareVersionInspector.ExtractFromShowVersion(ShowVersionCisco921_159_M2);
        Assert.Equal("15.9(3)M2", info.CanonicalVersion);
        Assert.Equal("c900-universalk9-mz.SPA.159-3.M2.bin", info.RunningImageFileName);
    }

    [Fact]
    public void IsSameVersion_VersaoDiferenteMesmoMajor_RetornaFalseENaoPulaUpgrade()
    {
        // Roteador rodando 15.9(3)M2 e proposta 15.9(3)M4 (formato com ponto)
        var isSameWithDot = CiscoIOSFirmwareVersionInspector.IsSameVersion(
            ShowVersionCisco921_159_M2,
            "c900-universalk9-mz.SPA.15.9-3.M4.bin",
            out var curVer,
            out var tgtVer);

        Assert.False(isSameWithDot);
        Assert.Equal("15.9(3)M2", curVer.CanonicalVersion);
        Assert.Equal("15.9(3)M4", tgtVer.CanonicalVersion);

        // Roteador rodando 15.9(3)M2 e proposta 15.9(3)M4 (formato sem ponto)
        var isSameWithoutDot = CiscoIOSFirmwareVersionInspector.IsSameVersion(
            ShowVersionCisco921_159_M2,
            "c900-universalk9-mz.SPA.159-3.M4.bin",
            out _,
            out _);

        Assert.False(isSameWithoutDot);
    }

    [Fact]
    public void IsSameVersion_Versao156AntigaVs156Nova_RetornaFalse()
    {
        // Roteador rodando 15.6(3)M2 e proposta 15.6(3)M8
        var isSame = CiscoIOSFirmwareVersionInspector.IsSameVersion(
            ShowVersionCisco921_OldVersion,
            "c900-universalk9-mz.SPA.15.6-3.M8.bin",
            out var curVer,
            out var tgtVer);

        Assert.False(isSame);
        Assert.Equal("15.6(3)M2", curVer.CanonicalVersion);
        Assert.Equal("15.6(3)M8", tgtVer.CanonicalVersion);
    }

    [Fact]
    public void IsSameVersion_ImagemExatamenteIgual_RetornaTrue()
    {
        // Roteador rodando 15.9(3)M4 e proposta 15.9(3)M4
        var isSame = CiscoIOSFirmwareVersionInspector.IsSameVersion(
            ShowVersionCisco921_159_M4,
            "c900-universalk9-mz.SPA.159-3.M4.bin",
            out var curVer,
            out var tgtVer);

        Assert.True(isSame);
        Assert.Equal("15.9(3)M4", curVer.CanonicalVersion);
        Assert.Equal("15.9(3)M4", tgtVer.CanonicalVersion);
    }

    [Fact]
    public void IsSameVersion_ImagemComPontoEquivalenteCanonica_RetornaTrue()
    {
        // Roteador rodando com imagem binária 159-3.M4 e arquivo selecionado 15.9-3.M4 (ambos canônicos 15.9(3)M4)
        var isSame = CiscoIOSFirmwareVersionInspector.IsSameVersion(
            ShowVersionCisco921_159_M4,
            "c900-universalk9-mz.SPA.15.9-3.M4.bin",
            out var curVer,
            out var tgtVer);

        Assert.True(isSame);
        Assert.Equal(curVer.CanonicalVersion, tgtVer.CanonicalVersion);
    }

    [Fact]
    public void Cisco1905_SemAspas_DetectaVersaoEImpedeFalsoPositivoDeUpgrade()
    {
        const string showVersion1905_SemAspas = @"
Cisco IOS Software, C1900 Software (C1900-UNIVERSALK9-M), Version 15.7(3)M9, RELEASE SOFTWARE (fc2)
Technical Support: http://www.cisco.com/techsupport
Copyright (c) 1986-2020 by Cisco Systems, Inc.
Compiled Wed 29-Apr-20 01:23 by prod_rel_team

ROM: System Bootstrap, Version 15.0(1r)M16, RELEASE SOFTWARE (fc1)

Router uptime is 15 minutes
System returned to ROM by reload
System image file is flash0:c1900-universalk9-mz.SPA.157-3.M9.bin
Last reload type: Normal Reload

cisco 1905 (revision 1.0) with 499712K/24576K bytes of memory.
Processor board ID FHK12345678
";

        var info = CiscoIOSFirmwareVersionInspector.ExtractFromShowVersion(showVersion1905_SemAspas);
        Assert.Equal("15.7(3)M9", info.CanonicalVersion);
        Assert.Equal("c1900-universalk9-mz.SPA.157-3.M9.bin", info.RunningImageFileName);

        var isSame = CiscoIOSFirmwareVersionInspector.IsSameVersion(
            showVersion1905_SemAspas,
            "c1900-universalk9-mz.SPA.157-3.M9.bin",
            out var curVer,
            out var tgtVer);

        Assert.True(isSame);
        Assert.Equal("15.7(3)M9", curVer.CanonicalVersion);
        Assert.Equal("15.7(3)M9", tgtVer.CanonicalVersion);
    }

    [Fact]
    public void Cisco1905_ComAspas_DetectaVersaoCorretamente()
    {
        const string showVersion1905_ComAspas = @"
Cisco IOS Software, C1900 Software (C1900-UNIVERSALK9-M), Version 15.7(3)M9, RELEASE SOFTWARE (fc2)
ROM: System Bootstrap, Version 15.0(1r)M16, RELEASE SOFTWARE (fc1)
System image file is ""flash0:c1900-universalk9-mz.SPA.157-3.M9.bin""
";

        var info = CiscoIOSFirmwareVersionInspector.ExtractFromShowVersion(showVersion1905_ComAspas);
        Assert.Equal("15.7(3)M9", info.CanonicalVersion);
        Assert.Equal("c1900-universalk9-mz.SPA.157-3.M9.bin", info.RunningImageFileName);
    }

    [Fact]
    public void Cisco1905_NuncaCapturaRomBootstrapComoVersaoIos()
    {
        const string outputSemLinhaIos = @"
ROM: System Bootstrap, Version 15.0(1r)M16, RELEASE SOFTWARE (fc1)
System image file is flash0:c1900-universalk9-mz.SPA.157-3.M9.bin
";
        var info = CiscoIOSFirmwareVersionInspector.ExtractFromShowVersion(outputSemLinhaIos);
        Assert.NotEqual("15.0(1R)M16", info.CanonicalVersion);
    }

    [Fact]
    public void Cisco1905_ExtractDisplayVersion_ExtraiCanonicalEFileNameCorretamente()
    {
        const string rawOutput = @"
System image file is flash0:c1900-universalk9-mz.SPA.157-3.M9.bin
";
        var displayVer = NetworkDevice.Core.Firmware.RouterDirectFirmwareUpdater.ExtractDisplayVersion(
            NetworkDevice.Core.Domain.DeviceSeries.Series1900,
            rawOutput);

        Assert.Contains("15.7(3)M9", displayVer);
        Assert.Contains("c1900-universalk9-mz.SPA.157-3.M9.bin", displayVer);
    }

    [Fact]
    public void Cisco1905_EvaluateComplianceStrict_HomologadoRetornaTrue()
    {
        const string rawOutput = @"
Cisco IOS Software, C1900 Software (C1900-UNIVERSALK9-M), Version 15.7(3)M9, RELEASE SOFTWARE (fc2)
System image file is flash0:c1900-universalk9-mz.SPA.157-3.M9.bin
";
        var isCompliant = NetworkDevice.Core.Firmware.RouterDirectFirmwareUpdater.EvaluateComplianceStrict(
            NetworkDevice.Core.Domain.DeviceSeries.Series1900,
            rawOutput,
            "15.7(3)M9 (c1900-universalk9-mz.SPA.157-3.M9.bin)",
            "c1900-universalk9-mz.SPA.157-3.M9.bin");

        Assert.True(isCompliant);
    }
}
