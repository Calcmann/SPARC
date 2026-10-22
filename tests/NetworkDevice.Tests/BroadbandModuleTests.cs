using System;
using System.Collections.Generic;
using NetworkDevice.Core.Diagnostics.Broadband;
using Xunit;

namespace NetworkDevice.Tests;

public class BroadbandModuleTests
{
    [Fact]
    public void CalculateDistanceKm_ShouldReturnCorrectDistance_BetweenFlorianopolisAndSaoPaulo()
    {
        // Florianópolis: -27.59667, -48.54917
        // São Paulo (GRU): -23.4356, -46.4731
        var dist = BroadbandTestEngine.CalculateDistanceKm(-27.59667, -48.54917, -23.4356, -46.4731);

        // Distância real em linha reta é aproximadamente 500 km
        Assert.InRange(dist, 480.0, 520.0);
    }

    [Fact]
    public void GenerateHtml_ShouldContainExecutiveMetricsAndSvgCharts()
    {
        var geo = new BroadbandGeoInfo(
            PublicIp: "187.65.232.180",
            Asn: "28573",
            Provider: "Claro Fibra",
            ClientCity: "Florianopolis",
            ClientCountry: "BR",
            ClientLatitude: -27.59667,
            ClientLongitude: -48.54917,
            ServerColoIata: "GRU",
            ServerCity: "São Paulo (Guarulhos)",
            ServerCountry: "Brasil",
            ServerLatitude: -23.4356,
            ServerLongitude: -46.4731,
            DistanceKm: 505.2,
            Timestamp: DateTime.Now);

        var server = new BroadbandServerInfo(
            Id: "cf-gru",
            Name: "Cloudflare Edge (GRU)",
            Sponsor: "Cloudflare Speed Test",
            Location: "São Paulo (Guarulhos)",
            Country: "Brasil",
            Host: "speed.cloudflare.com",
            Port: 443,
            Latitude: -23.4356,
            Longitude: -46.4731,
            DistanceKm: 505.2,
            LatencyMs: 18.5,
            ServerType: "Cloudflare");

        var bufferbloat = new BroadbandBufferbloat(
            LatencyIdleMs: 18.5,
            LatencyUnderDownloadMs: 24.2,
            LatencyUnderUploadMs: 28.0,
            JitterIdleMs: 1.8,
            JitterUnderLoadMs: 3.2,
            DeltaDownloadMs: 5.7,
            DeltaUploadMs: 9.5,
            Grade: "A",
            Description: "Ótima responsividade sob carga.");

        var timeSeries = new List<BroadbandPoint>
        {
            new(1.0, 0, 18.5, 1.8, "Ping"),
            new(3.0, 485.5, 24.0, 2.1, "Download"),
            new(5.0, 498.0, 25.0, 2.0, "Download"),
            new(7.0, 245.0, 28.0, 2.5, "Upload")
        };

        var result = new BroadbandTestResult(
            IsSuccess: true,
            DownloadAvgMbps: 492.5,
            DownloadPeakMbps: 512.0,
            UploadAvgMbps: 248.0,
            UploadPeakMbps: 260.0,
            LatencyIdleMs: 18.5,
            LatencyMinMs: 17.0,
            LatencyMaxMs: 28.0,
            JitterMs: 1.8,
            PacketLossPct: 0.0,
            Bufferbloat: bufferbloat,
            GeoInfo: geo,
            PrimaryServer: server,
            FallbackHistory: Array.Empty<BroadbandFallbackEvent>(),
            TimeSeries: timeSeries,
            StatusMessage: "Sucesso",
            ValidationUrl: "https://speed.cloudflare.com",
            Duration: TimeSpan.FromSeconds(12),
            LocalAdapterName: "Ethernet 1",
            SourceIpAddress: "192.168.1.100");

        var html = BroadbandPdfReportService.GenerateHtml(result);

        Assert.NotNull(html);
        Assert.Contains("SPARC — Relatório de Homologação de Banda Larga", html);
        Assert.Contains("492.50", html); // Download
        Assert.Contains("248.00", html); // Upload
        Assert.Contains("18.5", html);   // Latência
        Assert.Contains("187.65.232.180", html); // IP público
        Assert.Contains("Florianopolis", html);   // Cidade
        Assert.Contains("<svg", html);           // Gráficos vetoriais SVG
        Assert.Contains("Throughput", html);
        Assert.Contains("Bufferbloat", html);
    }
}
