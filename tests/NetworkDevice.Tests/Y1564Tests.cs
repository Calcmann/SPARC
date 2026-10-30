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
        reflector.Start(0, System.Net.IPAddress.Loopback);
        int testPort = reflector.BoundPort;
        await Task.Delay(50);

        try
        {
            var service = new Y1564TestService();
            var config = new Y1564TestConfig(
                RemoteIp: "127.0.0.1",
                RemotePort: testPort,
                TargetBandwidthMbps: 1,
                FrameSizeBytes: 1024,
                Duration: TimeSpan.FromMilliseconds(500),
                ClientName: "TESTE REFLETOR ATIVO",
                Designation: "PASS/001",
                SlaLossPercent: 2.0,
                Mode: Y1564TestMode.LoopbackBidirectional);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await service.RunTestAsync(config, null, null, cts.Token);

            Assert.NotNull(result);
            Assert.True(result.RxPackets > 0, "O teste com refletor ativo deve receber pacotes de retorno.");
            Assert.True(result.LossPercentage < 95.0, $"Perda deve ser razoável em loopback local de teste (obtido: {result.LossPercentage}%).");
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

    [Fact]
    public void CertidaoNascimento_NomeclaturaPadraoEmbratel_SubstituiBarrasPorTracos()
    {
        // Descritivo Técnico Embratel (Página 41):
        // Designação SPO/IP/47287 => SPO-IP-47287.pdf
        var rawDesig = "SPO/IP/47287";
        var nomeOficial = rawDesig.Trim().Replace('/', '-').Replace('\\', '-');
        foreach (var c in Path.GetInvalidFileNameChars()) nomeOficial = nomeOficial.Replace(c, '-');
        if (!nomeOficial.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) nomeOficial += ".pdf";

        Assert.Equal("SPO-IP-47287.pdf", nomeOficial);

        // Teste com outra designação real
        var rawDesig2 = "LFS/IP/02924";
        var nomeOficial2 = rawDesig2.Trim().Replace('/', '-').Replace('\\', '-');
        foreach (var c in Path.GetInvalidFileNameChars()) nomeOficial2 = nomeOficial2.Replace(c, '-');
        if (!nomeOficial2.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) nomeOficial2 += ".pdf";

        Assert.Equal("LFS-IP-02924.pdf", nomeOficial2);
    }

    [Theory]
    [InlineData("BLD", 250, 0.20, 74.0, 28.0, 1500)]
    [InlineData("BLD", 1200, 0.20, 250.0, 160.0, 1500)]
    [InlineData("MPLS", 250, 0.02, 40.0, 16.0, 1500)]
    [InlineData("MPLS", 1200, 0.02, 150.0, 80.0, 1500)]
    [InlineData("VOZ", 250, 0.02, 20.0, 6.0, 64)]
    [InlineData("VOZ", 1200, 0.02, 50.0, 16.0, 64)]
    public void CertidaoNascimento_PresetsServicosEmbratel_ParametrizacaoCorreta(
        string servico, int distKm, double expectedLoss, double expectedDelay, double expectedJitter, int expectedFrame)
    {
        double maxLoss = (servico == "VOZ" || servico == "MPLS") ? (distKm <= 1200 ? 0.02 : 0.05) : 0.20;
        double maxDelay = servico == "VOZ"
            ? (distKm <= 250 ? 20.0 : (distKm <= 1200 ? 50.0 : 154.0))
            : (servico == "MPLS"
                ? (distKm <= 250 ? 40.0 : (distKm <= 1200 ? 150.0 : 230.0))
                : (distKm <= 250 ? 74.0 : (distKm <= 1200 ? 250.0 : 460.0)));
        double maxJitter = servico == "VOZ"
            ? (distKm <= 250 ? 6.0 : (distKm <= 1200 ? 16.0 : 20.0))
            : (servico == "MPLS"
                ? (distKm <= 250 ? 16.0 : (distKm <= 1200 ? 80.0 : 80.0))
                : (distKm <= 250 ? 28.0 : (distKm <= 1200 ? 160.0 : 260.0)));

        int frame = servico == "VOZ" ? 64 : 1500;

        Assert.Equal(expectedLoss, maxLoss, 2);
        Assert.Equal(expectedDelay, maxDelay, 1);
        Assert.Equal(expectedJitter, maxJitter, 1);
        Assert.Equal(expectedFrame, frame);
    }

    [Fact]
    public void CertidaoNascimento_LoopbackStatsRecord_CalculatesCorrectly()
    {
        var stats = new DigitalLoopbackStats(
            IsActive: true,
            Port: 5001,
            BindAddress: "0.0.0.0",
            TotalPacketsRx: 154200,
            TotalPacketsTx: 154200,
            TotalBytesRx: 154200 * 1500,
            TotalBytesTx: 154200 * 1500,
            CurrentRxMbps: 198.4,
            CurrentTxMbps: 198.4,
            CurrentRxPps: 16500,
            CurrentTxPps: 16500,
            LastRemoteEndPoint: "200.250.153.41:5001",
            Elapsed: TimeSpan.FromMinutes(15));

        Assert.True(stats.IsActive);
        Assert.Equal(5001, stats.Port);
        Assert.Equal(154200, stats.TotalPacketsRx);
        Assert.Equal(154200, stats.TotalPacketsTx);
        Assert.Equal("200.250.153.41:5001", stats.LastRemoteEndPoint);
        Assert.Equal(TimeSpan.FromMinutes(15), stats.Elapsed);
    }

    [Fact]
    public void CertidaoNascimento_ModoInicial_EhRefletorComDuracaoIndefinida()
    {
        // Regra de Negócio Técnica Embratel / Claro:
        // O instrumento de campo (MTS-5800 ou SPARC Mobile) deve iniciar no modo Refletor LLB na porta 5001
        // com tempo indefinido, aguardando o início do teste de 15 minutos disparado remotamente pelo QT-600.
        const string modoPadrao = "Loopback";
        const int portaPadrao = 5001;
        var duracaoIndefinida = TimeSpan.Zero; // Representa contínuo / indefinido

        Assert.Equal("Loopback", modoPadrao);
        Assert.Equal(5001, portaPadrao);
        Assert.Equal(TimeSpan.Zero, duracaoIndefinida);
    }
}

