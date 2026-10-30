using System;
using System.Linq;
using NetworkDevice.Core.Provisioning;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class ClaroCpeCatalogTests
{
    [Fact]
    public void GetCatalog_ContainsClaroHomologatedModels()
    {
        var catalog = ClaroCpeCatalogService.GetCatalog();
        Assert.NotEmpty(catalog);

        // Modelos principais exigidos pela Claro
        Assert.Contains(catalog, m => m.PartNumber.Contains("FG-40F") && m.BldMaxMbps == 1000);
        Assert.Contains(catalog, m => m.PartNumber.Contains("C841M") && m.BldMaxMbps == 250);
        Assert.Contains(catalog, m => m.PartNumber.Contains("C921-4P") && m.BldMaxMbps == 700);
        Assert.Contains(catalog, m => m.PartNumber.Contains("JH296A") && m.BldMaxMbps == 150); // MSR954
        Assert.Contains(catalog, m => m.PartNumber.Contains("C8200L") && m.BldMaxMbps == 1000);
        Assert.Contains(catalog, m => m.PartNumber.Contains("C8500L") && m.BldMaxMbps == 10000);
    }

    [Theory]
    [InlineData("cisco.c841.break", 100, true)]    // 100M em C841M (limite 250M) -> OK
    [InlineData("cisco.c841.break", 250, true)]    // 250M em C841M (limite 250M) -> OK
    [InlineData("cisco.c841.break", 300, false)]   // 300M em C841M (limite 250M) -> INCOMPATÍVEL
    [InlineData("hpe.msr954.ctrl-b", 100, true)]   // 100M em MSR954 (limite 150M) -> OK
    [InlineData("hpe.msr954.ctrl-b", 200, false)]  // 200M em MSR954 (limite 150M) -> INCOMPATÍVEL
    [InlineData("cisco.c900.ctrl-c", 500, true)]   // 500M em C921 (limite 700M) -> OK
    [InlineData("cisco.c900.ctrl-c", 800, false)]  // 800M em C921 (limite 700M) -> INCOMPATÍVEL
    [InlineData("fortinet.fgt40f.fortios", 1000, true)] // 1G em FG-40F -> OK
    [InlineData("fortinet.fgt40f.fortios", 2000, false)] // 2G em FG-40F -> INCOMPATÍVEL
    public void ValidarCompatibilidadeBanda_BldValidation(string modelTag, double bandaMbps, bool expectedCompativel)
    {
        var circuit = new SaipCircuitData
        {
            TipoServico = SaipServiceType.BusinessLinkDirect,
            BandaMbpsNominal = bandaMbps
        };

        var result = ClaroCpeCatalogService.ValidarCompatibilidadeBanda(modelTag, circuit);

        Assert.Equal(expectedCompativel, result.IsCompativel);
        if (!expectedCompativel)
        {
            Assert.NotEmpty(result.ModelosRecomendados);
            Assert.Contains("INCOMPATIBILIDADE", result.Mensagem);
        }
    }

    [Fact]
    public void ValidarCompatibilidadeBanda_MplsHasLowerLimitThanBld()
    {
        // C841M: BLD é 250M, mas MPLS é 150M
        var circuitMpls = new SaipCircuitData
        {
            TipoServico = SaipServiceType.IpVpn,
            BandaMbpsNominal = 200 // 200M excede o limite MPLS de 150M
        };

        var result = ClaroCpeCatalogService.ValidarCompatibilidadeBanda("cisco.c841.break", circuitMpls);

        Assert.False(result.IsCompativel);
        Assert.Equal(150, result.LimiteMaximoMbps);
        Assert.Contains("MPLS", result.TipoServico);
    }

    [Fact]
    public void ValidarCompatibilidadeBanda_WhenCircuitNullOrNoBandwidth_DoesNotFail()
    {
        var res1 = ClaroCpeCatalogService.ValidarCompatibilidadeBanda("cisco.c841.break", null);
        Assert.True(res1.IsCompativel);

        var circuitSemBanda = new SaipCircuitData { BandaMbpsNominal = null };
        var res2 = ClaroCpeCatalogService.ValidarCompatibilidadeBanda("cisco.c841.break", circuitSemBanda);
        Assert.True(res2.IsCompativel);
    }

    [Fact]
    public void ValidarCompatibilidadeBanda_RecommendsOnlyModelsThatActuallySupportBandwidth()
    {
        var circuit = new SaipCircuitData
        {
            TipoServico = SaipServiceType.BusinessLinkDirect,
            BandaMbpsNominal = 500
        };

        var result = ClaroCpeCatalogService.ValidarCompatibilidadeBanda("hpe.msr954.ctrl-b", circuit);

        Assert.False(result.IsCompativel);
        Assert.NotEmpty(result.ModelosRecomendados);

        foreach (var rec in result.ModelosRecomendados)
        {
            Assert.True(rec.BldMaxMbps >= 500, $"Modelo recomendado {rec.ModeloNome} tem limite {rec.BldMaxMbps} < 500 Mbps!");
        }
    }

    [Fact]
    public void GetCatalog_ContainsCisco1111Kit3()
    {
        var catalog = ClaroCpeCatalogService.GetCatalog();
        var c1111 = catalog.FirstOrDefault(m => m.Id == "cisco.c1111.kit3");
        Assert.NotNull(c1111);
        Assert.Equal("C1111-4P-BR", c1111.PartNumber);
        Assert.Equal(850, c1111.BldMaxMbps);
        Assert.Equal(650, c1111.MplsMaxMbps);
        Assert.Contains("cisco.c1111.break", c1111.TagsSpacCompatíveis);
    }

    [Fact]
    public void FindByTagOrName_MatchesC1111And1100()
    {
        var m1 = ClaroCpeCatalogService.FindByTagOrName("cisco.c1111.break");
        Assert.NotNull(m1);
        Assert.Equal("cisco.c1111.kit3", m1.Id);

        var m2 = ClaroCpeCatalogService.FindByTagOrName("Cisco Série 1100 / C1111-4P");
        Assert.NotNull(m2);
        Assert.Equal("cisco.c1111.kit3", m2.Id);
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(850, true)]
    [InlineData(900, false)]
    public void ValidarCompatibilidadeBanda_Cisco1111_BldLimits(double bandaMbps, bool expected)
    {
        var circuit = new SaipCircuitData
        {
            TipoServico = SaipServiceType.BusinessLinkDirect,
            BandaMbpsNominal = bandaMbps
        };
        var result = ClaroCpeCatalogService.ValidarCompatibilidadeBanda("cisco.c1111.break", circuit);
        Assert.Equal(expected, result.IsCompativel);
    }
}
