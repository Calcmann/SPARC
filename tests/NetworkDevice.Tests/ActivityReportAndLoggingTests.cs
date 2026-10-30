using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using NetworkDevice.Core.Logging;
using NetworkDevice.Core.Provisioning;
using Xunit;

namespace NetworkDevice.Tests;

public sealed class ActivityReportAndLoggingTests : IDisposable
{
    private readonly string _testDir;

    public ActivityReportAndLoggingTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"sparc_test_logs_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public void TechnicianProfile_FormatsSummaryCorrectly()
    {
        var profile = new TechnicianProfile
        {
            FirstName = "George Luís",
            LastName = "Calcmann",
            Company = "Claro S.A.",
            EmployeeId = "123456",
            Cluster = "Florianópolis",
            Uf = "SC",
            MachineGuid = "ABC-123",
            MachineDisplayId = "ABC-123"
        };

        Assert.Equal("George Luís Calcmann", profile.FullName);
        var summary = profile.Summary;
        Assert.Contains("George Luís Calcmann", summary);
        Assert.Contains("Claro S.A.", summary);
        Assert.Contains("123456", summary);
        Assert.Contains("Florianópolis/SC", summary);
    }

    [Fact]
    public void SessionLogService_SanitizesSensitivePasswordsAndSecrets()
    {
        var raw1 = "enable secret 5 $1$mERr$hx5rVt7rPNoS4wqbXKX7m0";
        var raw2 = "username admin privilege 15 password 7 0822455D0A16";
        var raw3 = "snmp-server community claro_telecom_ro RO";
        var raw4 = "tacacs-server key 7 070C285F4D0648";
        var raw5 = "set password SuperSecretPassword123!";

        Assert.Equal("enable secret ***", SessionLogService.Sanitize(raw1));
        Assert.Equal("username admin privilege 15 password ***", SessionLogService.Sanitize(raw2));
        Assert.Equal("snmp-server community *** RO", SessionLogService.Sanitize(raw3));
        Assert.Equal("tacacs-server key ***", SessionLogService.Sanitize(raw4));
        Assert.Equal("set password ***", SessionLogService.Sanitize(raw5));
    }

    [Fact]
    public void SessionLogService_CreatesExecutionAndDailyLogFiles()
    {
        var logService = new SessionLogService();
        var logsSubdir = Path.Combine(_testDir, "logs");
        logService.SetCustomLogsDirectory(logsSubdir);

        var tech = new TechnicianProfile
        {
            FirstName = "Carlos",
            LastName = "Silva",
            Company = "Telemont",
            EmployeeId = "TEL999"
        };

        var execLogPath = logService.StartExecution("LDA-IP-02580", "Fortinet FortiGate 40F", tech);
        Assert.True(File.Exists(execLogPath));

        logService.Log("Testando envio de comando serial", "SERIAL");
        logService.LogRaw("FortiGate-40F # config system interface\r\n");

        var completedPath = logService.EndExecution(true, "Atividade concluída com 100% de sucesso.");
        Assert.Equal(execLogPath, completedPath);

        var logContent = File.ReadAllText(execLogPath);
        Assert.Contains("LDA-IP-02580", logContent);
        Assert.Contains("Fortinet FortiGate 40F", logContent);
        Assert.Contains("Carlos Silva", logContent);
        Assert.Contains("STATUS FINAL: SUCESSO", logContent);
        Assert.Contains("config system interface", logContent);
    }

    [Fact]
    public void ActivityReportService_GeneratesJsonAndTextReportsOnSuccess()
    {
        var actService = new ActivityReportService();
        var actSubdir = Path.Combine(_testDir, "atividades");
        actService.SetCustomActivitiesDirectory(actSubdir);

        var circuit = new SaipCircuitData
        {
            DesignacaoIp = "FLN/IP/00123",
            ClienteRazaoSocial = "EMPRESA MODELO BRASIL LTDA",
            NumeroOts = "OTS-SC-2026-999",
            WanIp = "200.179.12.94",
            WanCidr = 30,
            WanGateway = "200.179.12.93",
            LanIp = "200.250.163.41",
            LanCidr = 28,
            BandaMbpsNominal = 100.0,
            TipoServico = SaipServiceType.BusinessLinkDirect
        };

        var tech = new TechnicianProfile
        {
            FirstName = "George Luís",
            LastName = "Calcmann",
            Company = "Claro S.A.",
            EmployeeId = "123456",
            Cluster = "Florianópolis",
            Uf = "SC"
        };

        var report = actService.StartActivity(circuit, "Fortinet FortiGate 40F", "COM3", 9600, tech);
        Assert.NotNull(report);
        Assert.Equal(7, report.Etapas.Count);

        actService.UpdateStage(report, 1, ActivityStageStatus.Sucesso, "Factory reset concluído com sucesso");
        actService.UpdateStage(report, 2, ActivityStageStatus.Sucesso, "Firmware v7.2.11 mantido");
        actService.UpdateStage(report, 3, ActivityStageStatus.Sucesso, "Script SAIP aplicado sem erros");
        actService.UpdateStage(report, 4, ActivityStageStatus.Sucesso, "IP local configurado na interface Ethernet");
        actService.UpdateStage(report, 5, ActivityStageStatus.Sucesso, "ICMP LAN/WAN/WEB 100% OK");
        actService.UpdateStage(report, 6, ActivityStageStatus.Sucesso, "Telnet OK");
        actService.UpdateStage(report, 7, ActivityStageStatus.Sucesso, "Banda 102.4 Mbps OK");

        var files = actService.FinishActivity(report, isSuccess: true, erroGeral: null, pdfPath: @"C:\SPARC\backups\Relatorios\relatorio.pdf");

        Assert.True(File.Exists(files.JsonReportPath));
        Assert.True(File.Exists(files.TextReportPath));
        Assert.Null(files.DebugZipPath); // Em sucesso não gera zip de falha

        var textContent = File.ReadAllText(files.TextReportPath);
        Assert.Contains("FLN/IP/00123", textContent);
        Assert.Contains("EMPRESA MODELO BRASIL LTDA", textContent);
        Assert.Contains("George Luís Calcmann", textContent);
        Assert.Contains("Claro S.A.", textContent);
        Assert.Contains("100% HOMOLOGADO COM SUCESSO", textContent);
        Assert.Contains("Factory reset concluído com sucesso", textContent);

        var jsonContent = File.ReadAllText(files.JsonReportPath);
        using var doc = JsonDocument.Parse(jsonContent);
        Assert.Equal("Sucesso", doc.RootElement.GetProperty("StatusFinal").GetString());
        Assert.Equal("FLN/IP/00123", doc.RootElement.GetProperty("DesignacaoIp").GetString());
    }

    [Fact]
    public void ActivityReportService_GeneratesDebugZipPackageOnFailure()
    {
        var actService = new ActivityReportService();
        var actSubdir = Path.Combine(_testDir, "atividades_falha");
        actService.SetCustomActivitiesDirectory(actSubdir);

        var logService = SessionLogService.Instance;
        var logsSubdir = Path.Combine(_testDir, "logs_falha");
        logService.SetCustomLogsDirectory(logsSubdir);

        var circuit = new SaipCircuitData
        {
            DesignacaoIp = "LDA/IP/02580",
            ClienteRazaoSocial = "TESTE FALHA LTDA",
            NumeroOts = "OTS-FALHA-01",
            WanIp = "200.179.12.94",
            WanCidr = 30
        };

        var tech = new TechnicianProfile
        {
            FirstName = "Técnico",
            LastName = "Campo",
            Company = "Claro S.A.",
            EmployeeId = "998877"
        };

        var report = actService.StartActivity(circuit, "Fortinet FortiGate 40F", "COM1", 9600, tech);

        // Simula falha na Etapa 3
        actService.UpdateStage(report, 1, ActivityStageStatus.Sucesso, "Factory reset OK");
        actService.UpdateStage(report, 2, ActivityStageStatus.Sucesso, "Firmware OK");
        actService.UpdateStage(report, 3, ActivityStageStatus.Falha, "Timeout aguardando prompt no comando 'set hostname'");

        var files = actService.FinishActivity(report, isSuccess: false, erroGeral: "Tempo esgotado aguardando resposta da console serial.");

        Assert.True(File.Exists(files.JsonReportPath));
        Assert.True(File.Exists(files.TextReportPath));
        Assert.NotNull(files.DebugZipPath);
        Assert.True(File.Exists(files.DebugZipPath));

        // Inspeciona o arquivo ZIP gerado
        using var zip = ZipFile.OpenRead(files.DebugZipPath);
        Assert.Contains(zip.Entries, e => e.Name.EndsWith(".txt"));
        Assert.Contains(zip.Entries, e => e.Name.EndsWith(".json"));
        Assert.Contains(zip.Entries, e => e.Name.EndsWith(".log"));

        var textContent = File.ReadAllText(files.TextReportPath);
        Assert.Contains("NÃO HOMOLOGADO / INTERROMPIDO COM FALHA", textContent);
        Assert.Contains("Tempo esgotado aguardando resposta da console serial.", textContent);
    }
}
