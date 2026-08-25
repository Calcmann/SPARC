using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using NetworkDevice.Core.Diagnostics;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class ActivationPdfReportServiceTests
{
    [Fact]
    public void GenerateHtml_ProducesValidStructuredReport()
    {
        var icmpLan = new ConnectivityTestResult("200.182.245.17", 4, 4, 0, 1, 2, 1.2, 0.2, true, Array.Empty<PingPacketInfo>());
        var icmpWan = new ConnectivityTestResult("201.90.204.21", 4, 4, 0, 10, 15, 12.5, 0.5, true, Array.Empty<PingPacketInfo>());
        var icmpWeb = new ConnectivityTestResult("1.1.1.1", 4, 0, 100, 0, 0, 0, 0, false, Array.Empty<PingPacketInfo>());

        var icmpData = new TripleIcmpData(icmpLan, icmpWan, icmpWeb);
        var telnetRes = new ConnectivityService.TelnetTestResult("200.182.245.17", 23, true, 15, "Cisco IOS Banner", "Transcript OK", null);
        var bandRes = new BandwidthTestResult(48.5, 0, 12.0, 1.5, "iPerf Server", "HTTP Download", true, "Download OK");

        var reportData = new ActivationReportData(
            DataHora: new DateTime(2026, 8, 25, 14, 0, 0),
            ModeloEquipamento: "Cisco Série 1900",
            PortaSerial: "COM1",
            BaudRate: 9600,
            ClienteRazaoSocial: "SOLDI PROMOTORA DE VENDAS LTDA CONTA CORRENTE00015187188",
            DesignacaoIp: "FNS/IP/03977",
            NumeroOts: "IM-SPO-IGC--IP-50631/2026",
            PeRouter: "AGG01.SOONS",
            WanIp: "201.90.204.22",
            WanCidr: 30,
            WanGateway: "201.90.204.21",
            WanSubnetMask: "255.255.255.252",
            WanInterface: "GigabitEthernet 0/0",
            LanIp: "200.182.245.17",
            LanCidr: 28,
            LanBlockNetwork: "200.182.245.16",
            LanSubnetMask: "255.255.255.240",
            HostLanIp: "200.182.245.18",
            LanInterface: "GigabitEthernet 0/1",
            Step1ZerarOk: true,
            Step2FirmwareOk: true,
            FirmwareNome: "c1900-universalk9-mz.SPA.157-3.M9.bin",
            Step3SaipOk: true,
            Step4IpLocalOk: true,
            AdaptadorRedeLocal: "Ethernet 1",
            IcmpResult: icmpData,
            TelnetResult: telnetRes,
            BandResult: bandRes,
            DiagnosticAlerts: new List<string> { "Falha no Teste 5c (ICMP WEB): Rota padrão pendente na operadora." },
            FalhaGeral: null
        );

        var html = ActivationPdfReportService.GenerateHtml(reportData);

        // Asserções estruturais
        Assert.Contains("SOLDI PROMOTORA DE VENDAS LTDA", html);
        Assert.DoesNotContain("CONTA CORRENTE00015187188", html);
        Assert.Contains("FNS/IP/03977", html);
        Assert.Contains("201.90.204.22/30", html);
        Assert.Contains("200.182.245.17/28", html);
        Assert.Contains("Mbps", html);
        Assert.Contains("ms", html);
        Assert.Contains("Rota padrão pendente na operadora", html);
    }
}
