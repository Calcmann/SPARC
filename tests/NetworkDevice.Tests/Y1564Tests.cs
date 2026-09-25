using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NetworkDevice.Core.Diagnostics;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class Y1564Tests
{
    [Fact]
    public void GenerateHtml_ProducesExactClaroOfficialReportFormat()
    {
        var result = new Y1564Result(
            StartTime: new DateTime(2026, 9, 18, 11, 57, 2),
            EndTime: new DateTime(2026, 9, 18, 12, 12, 4),
            Duration: TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(2),
            ClientName: "NORTEL ELETR",
            Designation: "LFS/IP/02924",
            RemoteIp: "200.182.245.1",
            RemotePort: 5001,
            FrameSize: 1500,
            FrameSizeDescription: "1500",
            NetworkUlrMbps: 200,
            RxThroughputMbps: 197.56,
            LossPercentage: 0.00,
            DelayAvgMs: 37.85,
            DelayMinMs: 37.54,
            DelayMaxMs: 37.96,
            JitterAvgMs: 0.14,
            JitterMaxMs: 0.11,
            SlaJitterMs: 160,
            SlaDelayMs: 250,
            SlaLossPercent: 0.02,
            DistanceTier: "≤ 1200 Km",
            TxPackets: 14616001,
            RxPackets: 14616001,
            LostPercentage: 0.00,
            OosPackets: 0,
            IsPass: true,
            SummaryText: "PASS",
            DetailsMessage: "Certificação concluída com sucesso.");

        var html = Y1564PdfReportService.GenerateHtml(result);

        // Verifica cabeçalho e títulos oficiais
        Assert.Contains("Resultados - Y.1564", html);
        Assert.Contains("Informações do Teste", html);
        Assert.Contains("Resultado de Testes de Performance", html);

        // Verifica dados do teste
        Assert.Contains("18/09/2026 11:57:02", html);
        Assert.Contains("18/09/2026 12:12:04", html);
        Assert.Contains("15 min e 2 seg", html);
        Assert.Contains("NORTEL ELETR", html);
        Assert.Contains("LFS/IP/02924", html);
        Assert.Contains("PASS", html);

        // Verifica métricas de performance
        Assert.Contains("1500", html);
        Assert.Contains("200 Mbps", html);
        Assert.Contains("Rx: 197.56 Mbps", html);
        Assert.Contains("Avg: 0.00 %", html);
        Assert.Contains("Avg: 37.85 ms", html);
        Assert.Contains("Min: 37.54 ms", html);
        Assert.Contains("Max: 37.96 ms", html);
        Assert.Contains("Avg: 0.14 ms", html);
        Assert.Contains("Max: 0.11 ms", html);
        Assert.Contains("SLA: 160 ms", html);
        Assert.Contains("Tx: 14616001", html);
        Assert.Contains("Rx: 14616001", html);
    }

    [Fact]
    public void GenerateHtml_WithJumboAndVariableFrames_RendersCorrectly()
    {
        var resultJumbo = new Y1564Result(
            StartTime: DateTime.Now.AddMinutes(-5),
            EndTime: DateTime.Now,
            Duration: TimeSpan.FromMinutes(5),
            ClientName: "CLARO BACKBONE",
            Designation: "BB/IP/99999",
            RemoteIp: "10.0.0.1",
            RemotePort: 5001,
            FrameSize: 9000,
            FrameSizeDescription: "9000 (Jumbo)",
            NetworkUlrMbps: 1000,
            RxThroughputMbps: 994.20,
            LossPercentage: 0.01,
            DelayAvgMs: 12.3,
            DelayMinMs: 11.8,
            DelayMaxMs: 14.1,
            JitterAvgMs: 0.05,
            JitterMaxMs: 0.08,
            SlaJitterMs: 28,
            SlaDelayMs: 74,
            SlaLossPercent: 0.02,
            DistanceTier: "≤ 250Km",
            TxPackets: 500000,
            RxPackets: 499950,
            LostPercentage: 0.01,
            OosPackets: 0,
            IsPass: true,
            SummaryText: "PASS",
            DetailsMessage: "OK");

        var htmlJumbo = Y1564PdfReportService.GenerateHtml(resultJumbo);
        Assert.Contains("9000 (Jumbo)", htmlJumbo);
        Assert.Contains("PASS", htmlJumbo);

        var resultImix = resultJumbo with { FrameSizeDescription = "Variável / IMIX (64, 512, 1500 B)" };
        var htmlImix = Y1564PdfReportService.GenerateHtml(resultImix);
        Assert.Contains("Variável / IMIX (64, 512, 1500 B)", htmlImix);
    }

    [Theory]
    [InlineData(15, 2, "15 min e 2 seg")]
    [InlineData(5, 0, "5 min e 0 seg")]
    [InlineData(0, 45, "45 seg")]
    [InlineData(1, 10, "1 min e 10 seg")]
    public void FormatDuration_FormatsExpectedStrings(int minutes, int seconds, string expected)
    {
        var ts = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
        var actual = Y1564PdfReportService.FormatDuration(ts);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task RunTestAsync_ThrowsOnEmptyRemoteIp()
    {
        var service = new Y1564TestService();
        var config = new Y1564TestConfig(RemoteIp: "   ");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RunTestAsync(config, cancellationToken: CancellationToken.None));
    }

    [Fact]
    public async Task RunTestAsync_LoopbackShortExecution_ReturnsValidResult()
    {
        var service = new Y1564TestService();
        var config = new Y1564TestConfig(
            RemoteIp: "127.0.0.1",
            RemotePort: 5001,
            TargetBandwidthMbps: 10,
            FrameSizeBytes: 1500,
            Duration: TimeSpan.FromMilliseconds(500),
            ClientName: "TESTE LAB",
            Designation: "LAB/IP/001",
            SlaLossPercent: 0.02);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await service.RunTestAsync(config, null, null, cts.Token);

        Assert.NotNull(result);
        Assert.Equal("TESTE LAB", result.ClientName);
        Assert.Equal("LAB/IP/001", result.Designation);
        Assert.Equal(0.02, result.SlaLossPercent);
        Assert.True(result.TxPackets > 0);
        Assert.NotNull(result.SummaryText);
    }

    [Fact]
    public async Task RunTestAsync_VariableFramesAndJumbo_RunsWithoutErrors()
    {
        var service = new Y1564TestService();
        var config = new Y1564TestConfig(
            RemoteIp: "127.0.0.1",
            RemotePort: 5001,
            TargetBandwidthMbps: 10,
            IsVariableFrame: true,
            VariableFrameSizes: new[] { 64, 512, 1500, 9000 },
            FrameSizeLabel: "Variável c/ Jumbo (64 a 9000 B)",
            Duration: TimeSpan.FromMilliseconds(500),
            ClientName: "TESTE LAB JUMBO",
            Designation: "LAB/JUMBO/001",
            SlaLossPercent: 0.02);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await service.RunTestAsync(config, null, null, cts.Token);

        Assert.NotNull(result);
        Assert.Equal("Variável c/ Jumbo (64 a 9000 B)", result.FrameSizeDescription);
        Assert.True(result.TxPackets > 0);
    }

    [Fact]
    public async Task RunTestAsync_LoopbackWithoutReflector_FailsWith100PercentLoss()
    {
        var service = new Y1564TestService();
        var config = new Y1564TestConfig(
            RemoteIp: "127.0.0.1",
            RemotePort: 54321, // Porta onde não há nenhum refletor escutando
            TargetBandwidthMbps: 10,
            FrameSizeBytes: 1500,
            Duration: TimeSpan.FromMilliseconds(400),
            ClientName: "TESTE FALHA REFLETOR",
            Designation: "FAIL/001",
            Mode: Y1564TestMode.LoopbackBidirectional);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await service.RunTestAsync(config, null, null, cts.Token);

        Assert.NotNull(result);
        Assert.False(result.IsPass, "O teste Y.1564 em modo Loopback Bidirecional DEVE reprovar se Rx for 0!");
        Assert.Equal(0, result.RxPackets);
        Assert.Equal(100.0, result.LossPercentage);
        Assert.Equal(0.0, result.RxThroughputMbps);
        Assert.Contains("Nenhum pacote refletido foi recebido", result.DetailsMessage);
    }

    [Fact]
    public async Task RunTestAsync_LoopbackWithReflector_SucceedsWithLowLoss()
    {
        using var reflector = new DigitalLoopbackService();
        const int testPort = 54322;
        reflector.Start(testPort, System.Net.IPAddress.Loopback);

        try
        {
            var service = new Y1564TestService();
            var config = new Y1564TestConfig(
                RemoteIp: "127.0.0.1",
                RemotePort: testPort,
                TargetBandwidthMbps: 5,
                FrameSizeBytes: 1500,
                Duration: TimeSpan.FromMilliseconds(400),
                ClientName: "TESTE REFLETOR ATIVO",
                Designation: "PASS/001",
                SlaLossPercent: 2.0,
                Mode: Y1564TestMode.LoopbackBidirectional);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var result = await service.RunTestAsync(config, null, null, cts.Token);

            Assert.NotNull(result);
            Assert.True(result.RxPackets > 0, "O teste com refletor ativo deve receber pacotes de retorno.");
            Assert.True(result.LossPercentage < 5.0, $"Perda deve ser baixa em loopback local (obtido: {result.LossPercentage}%).");
        }
        finally
        {
            reflector.Stop();
        }
    }

    [Fact]
    public async Task RunTestAsync_GatewayTrafficLoad_RecordsModeAndEvaluates()
    {
        var service = new Y1564TestService();
        var config = new Y1564TestConfig(
            RemoteIp: "127.0.0.1",
            RemotePort: 5001,
            TargetBandwidthMbps: 5,
            FrameSizeBytes: 1500,
            Duration: TimeSpan.FromMilliseconds(400),
            ClientName: "TESTE GATEWAY LOAD",
            Designation: "GW/001",
            Mode: Y1564TestMode.GatewayTrafficLoad);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await service.RunTestAsync(config, null, null, cts.Token);

        Assert.NotNull(result);
        Assert.Contains("contra o Gateway", result.DetailsMessage);
    }

    [Fact]
    public async Task RunTestAsync_LoopbackBidirectional_WhenNoReflector_LossIs100AndDelayIsZero()
    {
        var service = new Y1564TestService();
        // Porta não utilizada para garantir que não há refletor
        var config = new Y1564TestConfig(
            RemoteIp: "127.0.0.1",
            RemotePort: 59981,
            TargetBandwidthMbps: 5,
            FrameSizeBytes: 1500,
            Duration: TimeSpan.FromMilliseconds(400),
            ClientName: "TESTE SEM REFLETOR",
            Designation: "NO-REFLECTOR",
            Mode: Y1564TestMode.LoopbackBidirectional);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var result = await service.RunTestAsync(config, null, null, cts.Token);

        Assert.NotNull(result);
        Assert.Equal(0, result.RxPackets);
        Assert.Equal(100.0, result.LossPercentage);
        Assert.Equal(0.0, result.DelayAvgMs); // Não contaminado pelo Ping ICMP
        Assert.False(result.IsPass);
        Assert.Contains("não possui um Refletor / Loopback UDP ativo", result.DetailsMessage);
    }

    [Fact]
    public async Task GenerateReportPdfAsync_CreatesValidReportFile()
    {
        var result = new Y1564Result(
            StartTime: new DateTime(2026, 9, 18, 11, 57, 2),
            EndTime: new DateTime(2026, 9, 18, 12, 12, 4),
            Duration: TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(2),
            ClientName: "NORTEL ELETR",
            Designation: "LFS/IP/02924",
            RemoteIp: "200.182.245.1",
            RemotePort: 5001,
            FrameSize: 1500,
            FrameSizeDescription: "1500",
            NetworkUlrMbps: 200,
            RxThroughputMbps: 197.56,
            LossPercentage: 0.00,
            DelayAvgMs: 37.85,
            DelayMinMs: 37.54,
            DelayMaxMs: 37.96,
            JitterAvgMs: 0.14,
            JitterMaxMs: 0.11,
            SlaJitterMs: 160,
            SlaDelayMs: 250,
            SlaLossPercent: 0.02,
            DistanceTier: "≤ 1200 Km",
            TxPackets: 14616001,
            RxPackets: 14616001,
            LostPercentage: 0.00,
            OosPackets: 0,
            IsPass: true,
            SummaryText: "PASS",
            DetailsMessage: "OK");

        var tempPath = Path.Combine(Path.GetTempPath(), $"Certidao_Test_{Guid.NewGuid():N}.pdf");
        var generated = await Y1564PdfReportService.GenerateReportPdfAsync(result, tempPath);

        Assert.True(File.Exists(generated));
        var info = new FileInfo(generated);
        Assert.True(info.Length > 0);

        try { File.Delete(generated); } catch { }
    }
}
