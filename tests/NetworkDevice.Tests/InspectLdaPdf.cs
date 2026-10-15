using System;
using System.IO;
using System.Threading.Tasks;
using NetworkDevice.Core.Provisioning;
using Xunit;
using Xunit.Abstractions;

namespace NetworkDevice.Tests;

public class InspectLdaPdf
{
    private readonly ITestOutputHelper _output;

    public InspectLdaPdf(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ParsePdf()
    {
        var pdfPath = @"C:\Users\User\Downloads\LDA-IP-02580.pdf";
        Assert.True(File.Exists(pdfPath), "PDF file does not exist at " + pdfPath);

        var data = await SaipParser.ParseFileAsync(pdfPath);
        _output.WriteLine("=== DADOS EXTRAÍDOS DO PDF LDA ===");
        _output.WriteLine($"Designacao: {data.DesignacaoIp}");
        _output.WriteLine($"ClienteRazaoSocial: {data.ClienteRazaoSocial}");
        _output.WriteLine($"Produto: {data.Produto}");
        _output.WriteLine($"TipoServico: {data.TipoServico}");
        _output.WriteLine($"NumeroOts: {data.NumeroOts}");
        _output.WriteLine($"WanIp: {data.WanIp}/{data.WanCidr} (Mask: {data.WanSubnetMask})");
        _output.WriteLine($"WanGateway: {data.WanGateway}");
        _output.WriteLine($"WanIpv6: {data.WanIpv6}/{data.WanIpv6Prefix}");
        _output.WriteLine($"LanBlockNetwork: {data.LanBlockNetwork}/{data.LanCidr} (Mask: {data.LanSubnetMask})");
        _output.WriteLine($"LanIp: {data.LanIp}");
        _output.WriteLine($"HostLanIp: {data.HostLanIp}");
        _output.WriteLine($"LanIpv6Block: {data.LanIpv6Block}/{data.LanIpv6Prefix}");
        _output.WriteLine($"BandaKbps: {data.BandaKbps}");
        _output.WriteLine($"BandaMbpsNominal: {data.BandaMbpsNominal}");
        _output.WriteLine($"PeRouter: {data.PeRouter}");
        _output.WriteLine($"PeLoopbackIp: {data.PeLoopbackIp}");
        _output.WriteLine($"VlanCliente: {data.VlanCliente}");
        _output.WriteLine("==================================");

        Assert.NotNull(data.DesignacaoIp);
    }
}
