using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Firmware;
using Xunit;

namespace NetworkDevice.Tests;

public class FirmwareHomologationCheckerTests
{
    [Theory]
    [InlineData(DeviceSeries.Series1900, "c1900-universalk9-mz.SPA.157-3.M9.bin", true)]
    [InlineData(DeviceSeries.Series1900, "c1900-universalk9-mz.SPA.157-3.M7.bin", false)]
    [InlineData(DeviceSeries.Isr921, "c900-universalk9-mz.SPA.159-3.M12.bin", true)]
    [InlineData(DeviceSeries.Isr921, "c900-universalk9-mz.SPA.158-3.M4.bin", false)]
    [InlineData(DeviceSeries.Isr841, "c800m-universalk9-mz.SPA.159-3.M12.bin", true)]
    [InlineData(DeviceSeries.Isr841, "c841-universalk9-mz.SPA.158-3.M4.bin", false)]
    [InlineData(DeviceSeries.Series2900, "c2900-universalk9-mz.SPA.157-3.M8.bin", true)]
    [InlineData(DeviceSeries.Series2900, "c2900-universalk9-mz.SPA.157-3.M4.bin", false)]
    [InlineData(DeviceSeries.FortiGate40F, "FGT_40F-v7.2.11.M-build1740-FORTINET.out", true)]
    [InlineData(DeviceSeries.FortiGate40F, "FGT_40F-v7.2.6.F-build1575-FORTINET.out", false)]
    [InlineData(DeviceSeries.Msr954, "MSR954-CMW710-R6749P43.ipe", true)]
    [InlineData(DeviceSeries.Msr954, "msr954-cmw710-r0413p03.ipe", false)]
    [InlineData(DeviceSeries.Msr930, "MSR93X-CMW520-R2512P04.BIN", true)]
    [InlineData(DeviceSeries.Msr1002, "MSR100X-CMW710-R6749P43.ipe", true)]
    public void IsVersaoHomologada_IdentificaCorretamenteVersoesOficiaisEAlternativas(
        DeviceSeries series,
        string fileName,
        bool expectedHomologada)
    {
        var ok = FirmwareHomologationChecker.IsVersaoHomologada(series, fileName, null, out var homologadoEsperado);

        Assert.Equal(expectedHomologada, ok);
        Assert.NotNull(homologadoEsperado);
        if (!expectedHomologada)
        {
            Assert.NotEqual(fileName, homologadoEsperado, System.StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void IsVersaoHomologada_RetornaTrueParaArquivoVazioOuModeloDesconhecido()
    {
        var ok1 = FirmwareHomologationChecker.IsVersaoHomologada(DeviceSeries.Series1900, "", null, out _);
        Assert.True(ok1);

        var ok2 = FirmwareHomologationChecker.IsVersaoHomologada(DeviceSeries.Unknown, "c1900.bin", null, out _);
        Assert.True(ok2);
    }

    [Theory]
    [InlineData("c1900-universalk9-mz.SPA.157-3.M7.bin", DeviceSeries.Series1900)]
    [InlineData("c1905-universalk9-mz.SPA.157-3.M7.bin", DeviceSeries.Series1900)]
    [InlineData("c800m-universalk9-mz.SPA.159-3.M10.bin", DeviceSeries.Isr841)]
    [InlineData("c900-universalk9-mz.SPA.159-3.M9.bin", DeviceSeries.Isr921)]
    [InlineData("c2900-universalk9-mz.SPA.157-3.M7.bin", DeviceSeries.Series2900)]
    [InlineData("FGT_40F-v7.0.14.M-build0582-FORTINET.out", DeviceSeries.FortiGate40F)]
    [InlineData("MSR954-CMW710-R0809P33.ipe", DeviceSeries.Msr954)]
    [InlineData("MSR93X-CMW520-R2512P01.BIN", DeviceSeries.Msr930)]
    [InlineData("MSR100X-CMW710-R0809P33.ipe", DeviceSeries.Msr1002)]
    public void MatchSeriesFromFileName_ReconheceTodosOsModelosSuportados(string fileName, DeviceSeries expectedSeries)
    {
        var series = FirmwareModelMap.MatchSeriesFromFileName(fileName);
        Assert.Equal(expectedSeries, series);
    }
}
