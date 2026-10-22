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

    private static ActivationReportData RelatorioBanda(double medidoMbps, double? nominalMbps)
    {
        return new ActivationReportData(
            DataHora: new DateTime(2026, 9, 10, 12, 0, 0),
            ModeloEquipamento: "Cisco Série 1900",
            PortaSerial: "COM1",
            BaudRate: 9600,
            ClienteRazaoSocial: "LAB",
            DesignacaoIp: "LAB4G-SP01",
            NumeroOts: "LAB000001",
            PeRouter: "PE-LAB",
            WanIp: "192.168.10.2",
            WanCidr: 30,
            WanGateway: "192.168.10.1",
            WanSubnetMask: "255.255.255.252",
            WanInterface: "GigabitEthernet 0/0",
            LanIp: "10.10.10.1",
            LanCidr: 29,
            LanBlockNetwork: "10.10.10.0",
            LanSubnetMask: "255.255.255.248",
            HostLanIp: "10.10.10.2",
            LanInterface: "GigabitEthernet 0/1",
            Step1ZerarOk: true,
            Step2FirmwareOk: true,
            FirmwareNome: "c1900-universalk9-mz.bin",
            Step3SaipOk: true,
            Step4IpLocalOk: true,
            AdaptadorRedeLocal: "Ethernet",
            IcmpResult: null,
            TelnetResult: null,
            BandResult: new BandwidthTestResult(medidoMbps, 0, 10.0, 1.0, "HTTP CDN", "Nativo HTTP", true, "Download OK"),
            DiagnosticAlerts: null,
            FalhaGeral: null,
            BandaMbpsNominal: nominalMbps
        );
    }

    [Fact]
    public void GenerateHtml_BandaAprovada_AcimaDe92()
    {
        var html = ActivationPdfReportService.GenerateHtml(RelatorioBanda(48.5, 50.0));
        Assert.Contains("APROVADO (97", html);
        Assert.Contains("Nominal:", html);
    }

    [Fact]
    public void GenerateHtml_BandaReprovada_AbaixoDe92()
    {
        var html = ActivationPdfReportService.GenerateHtml(RelatorioBanda(40.0, 50.0));
        Assert.Contains("REPROVADO (80", html);
    }

    [Fact]
    public void GenerateHtml_SemNominal_SemCritica()
    {
        var html = ActivationPdfReportService.GenerateHtml(RelatorioBanda(40.0, null));
        Assert.DoesNotContain("REPROVADO (", html);
        Assert.Contains("VAZÃO OK", html);
    }

    [Fact]
    public void GenerateDirectAccessHtml_ProducesValidReport_WhenAllOk()
    {
        var pePing = new ConnectivityTestResult("189.40.100.1", 4, 4, 0, 8, 12, 9.5, 0.4, true, Array.Empty<PingPacketInfo>());
        var googlePing = new ConnectivityTestResult("8.8.8.8", 4, 4, 0, 15, 20, 16.2, 0.5, true, Array.Empty<PingPacketInfo>());
        var cfPing = new ConnectivityTestResult("1.1.1.1", 4, 4, 0, 12, 18, 14.1, 0.3, true, Array.Empty<PingPacketInfo>());
        var speedtest = new BandwidthTestResult(95.4, 0, 10.0, 1.2, "Speedtest CDN", "HTTP Multi-thread", true, "Download OK");

        var data = new DirectAccessReportData(
            DataHora: new DateTime(2026, 10, 20, 15, 0, 0),
            ClienteRazaoSocial: "EMPRESA TESTE ACESSO DIRETO S.A.",
            DesignacaoIp: "SPO/ACC/12345",
            NumeroOts: "OTS-2026-9999",
            PeRouter: "PE-SPO-EDD-01",
            WanIp: "189.40.100.2",
            WanCidr: 30,
            WanGateway: "189.40.100.1",
            WanSubnetMask: "255.255.255.252",
            BandaMbpsNominal: 100.0,
            AdaptadorRedeLocal: "Realtek PCIe GbE Family Controller",
            PeResult: pePing,
            GoogleResult: googlePing,
            CloudflareResult: cfPing,
            BandResult: speedtest,
            DiagnosticAlerts: new List<string> { "Link testado diretamente na porta EDD/GPON do cliente." },
            Observacoes: null
        );

        var html = ActivationPdfReportService.GenerateDirectAccessHtml(data);

        Assert.Contains("EMPRESA TESTE ACESSO DIRETO", html);
        Assert.Contains("SPO/ACC/12345", html);
        Assert.Contains("189.40.100.2/30", html);
        Assert.Contains("189.40.100.1", html);
        Assert.Contains("Realtek PCIe GbE", html);
        Assert.Contains("100 Mbps", html);
        Assert.Contains("APROVADO", html);
        Assert.Contains("95", html);
        Assert.Contains("Mbps", html);
        Assert.Contains("ICMP Gateway PE", html);
        Assert.Contains("ICMP Google DNS", html);
        Assert.Contains("ICMP Cloudflare DNS", html);
    }

    [Fact]
    public void GenerateDirectAccessHtml_ReprovesWhenLossOrLowBandwidth()
    {
        var pePing = new ConnectivityTestResult("189.40.100.1", 4, 0, 100, 0, 0, 0, 0, false, Array.Empty<PingPacketInfo>());
        var speedtest = new BandwidthTestResult(50.0, 0, 30.0, 5.0, "Speedtest CDN", "HTTP", true, "Download OK");

        var data = new DirectAccessReportData(
            DataHora: DateTime.Now,
            ClienteRazaoSocial: null,
            DesignacaoIp: null,
            NumeroOts: null,
            PeRouter: null,
            WanIp: "10.0.0.2",
            WanCidr: 30,
            WanGateway: "10.0.0.1",
            WanSubnetMask: null,
            BandaMbpsNominal: 100.0,
            AdaptadorRedeLocal: "Ethernet",
            PeResult: pePing,
            GoogleResult: null,
            CloudflareResult: null,
            BandResult: speedtest,
            DiagnosticAlerts: null,
            Observacoes: null
        );

        var html = ActivationPdfReportService.GenerateDirectAccessHtml(data);

        Assert.Contains("NÃO CONFORME", html);
        Assert.Contains("REPROVADO", html);
        Assert.Contains("SEM RESPOSTA", html);
    }

    [Fact]
    public void GenerateHtml_WithTacacsTakeover_DisplaysTacacsBadgeAndNotice()
    {
        var icmpLan = new ConnectivityTestResult("200.182.245.17", 4, 4, 0, 1, 2, 1.2, 0.2, true, Array.Empty<PingPacketInfo>());
        var icmpWan = new ConnectivityTestResult("201.90.204.21", 4, 4, 0, 10, 15, 12.5, 0.5, true, Array.Empty<PingPacketInfo>());
        var icmpWeb = new ConnectivityTestResult("1.1.1.1", 4, 4, 0, 5, 8, 6.0, 0.3, true, Array.Empty<PingPacketInfo>());
        var icmpData = new TripleIcmpData(icmpLan, icmpWan, icmpWeb);

        // Simula o caso em que o Telnet padrão foi desabilitado e o TACACS/SSH assumiu
        var tacacsTelnetRes = new ConnectivityService.TelnetTestResult(
            "200.182.245.17",
            22,
            true,
            12,
            "SSH/TACACS+ Ativo (Time CPE)",
            "[TACACS] O Telnet padrão foi desabilitado e o time CPE terá acesso pelo TACACS.",
            null);

        var bandRes = new BandwidthTestResult(50.0, 0, 10.0, 1.0, "iPerf Server", "HTTP Download", true, "Download OK");

        var reportData = new ActivationReportData(
            DataHora: new DateTime(2026, 10, 22, 11, 0, 0),
            ModeloEquipamento: "Cisco 921",
            PortaSerial: "COM3",
            BaudRate: 9600,
            ClienteRazaoSocial: "CLIENTE BLD TESTE",
            DesignacaoIp: "SPO/IP/99999",
            NumeroOts: "OT-BLD-001",
            PeRouter: "PE.SPO",
            WanIp: "201.90.204.22",
            WanCidr: 30,
            WanGateway: "201.90.204.21",
            WanSubnetMask: "255.255.255.252",
            WanInterface: "GigabitEthernet 4",
            LanIp: "200.182.245.17",
            LanCidr: 28,
            LanBlockNetwork: "200.182.245.16",
            LanSubnetMask: "255.255.255.240",
            HostLanIp: "200.182.245.18",
            LanInterface: "GigabitEthernet 5",
            Step1ZerarOk: true,
            Step2FirmwareOk: true,
            FirmwareNome: "c900-universalk9-mz.SPA.159-3.M3.bin",
            Step3SaipOk: true,
            Step4IpLocalOk: true,
            AdaptadorRedeLocal: "Ethernet",
            IcmpResult: icmpData,
            TelnetResult: tacacsTelnetRes,
            BandResult: bandRes,
            DiagnosticAlerts: new List<string>(),
            FalhaGeral: null
        );

        var html = ActivationPdfReportService.GenerateHtml(reportData);

        // Deve identificar claramente SSH/TACACS+ e aprovar
        Assert.Contains("TACACS+ (SSH OK)", html);
        Assert.Contains("Time CPE via TACACS", html);
        Assert.Contains("100% HOMOLOGADO E APROVADO", html);
    }
}
