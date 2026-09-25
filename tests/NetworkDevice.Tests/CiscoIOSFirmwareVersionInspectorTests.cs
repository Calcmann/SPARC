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
}
