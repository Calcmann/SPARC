using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using NetworkDevice.Core.Installation;
using NetworkDevice.Core.Provisioning;

namespace NetworkDevice.Core.Logging;

public sealed record GeneratedReportFiles(string JsonReportPath, string TextReportPath, string? DebugZipPath);

public sealed class ActivityReportService
{
    private static readonly Lazy<ActivityReportService> _instance = new(() => new ActivityReportService());
    public static ActivityReportService Instance => _instance.Value;

    private readonly object _syncLock = new();
    private string? _customActivitiesDir;

    public string ActivitiesDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_customActivitiesDir))
            {
                return _customActivitiesDir;
            }

            var root = WorkspaceManager.GetDefaultWorkspaceRoot();
            return Path.Combine(root, "backups", "Atividades");
        }
    }

    public ActivityReportService()
    {
        EnsureDirectory();
    }

    public void SetCustomActivitiesDirectory(string dir)
    {
        _customActivitiesDir = dir;
        EnsureDirectory();
    }

    private void EnsureDirectory()
    {
        try
        {
            if (!Directory.Exists(ActivitiesDirectory))
            {
                Directory.CreateDirectory(ActivitiesDirectory);
            }
        }
        catch { }
    }

    public ActivityReport StartActivity(
        SaipCircuitData? circuit,
        string modelo,
        string porta,
        int baud,
        TechnicianProfile? technician = null)
    {
        lock (_syncLock)
        {
            EnsureDirectory();
            technician ??= TechnicianProfileService.LoadCurrentProfile();

            var report = new ActivityReport
            {
                IniciadoEm = DateTime.Now,
                DesignacaoIp = circuit?.DesignacaoIp ?? circuit?.NumeroOts ?? "Não Informada",
                ClienteRazaoSocial = SaipParser.CleanRazaoSocial(circuit?.ClienteRazaoSocial) ?? "Não Informado / Manual",
                NumeroOts = circuit?.NumeroOts,
                TipoServico = circuit?.TipoServicoDescricao ?? circuit?.TipoServico.ToString() ?? "Internet Corporativa",
                BandaMbpsNominal = circuit?.BandaMbpsNominal,
                PeRouter = circuit?.PeRouter,
                WanIp = circuit?.WanIp,
                WanCidr = circuit?.WanCidr,
                WanGateway = circuit?.WanGateway,
                WanSubnetMask = circuit?.WanSubnetMask,
                LanIp = circuit?.LanIp,
                LanCidr = circuit?.LanCidr,
                LanSubnetMask = circuit?.LanSubnetMask,
                ModeloEquipamento = modelo,
                PortaSerial = porta,
                BaudRate = baud,
                Tecnico = technician
            };

            var nomesEtapas = new[]
            {
                "Zerar Configuração (Factory Reset + Reload)",
                "Atualizar Firmware",
                "Provisionar Equipamento (SAIP / BLD)",
                "Configurar IP de Teste",
                "Testar Conectividade (ICMP)",
                "Testar Acesso Remoto (Telnet)",
                "Testar Banda"
            };

            for (int i = 0; i < nomesEtapas.Length; i++)
            {
                report.Etapas.Add(new ActivityStageResult
                {
                    Numero = i + 1,
                    Nome = nomesEtapas[i],
                    Status = ActivityStageStatus.Pendente
                });
            }

            // Inicia gravação contínua no SessionLogService
            var logPath = SessionLogService.Instance.StartExecution(report.DesignacaoIp, modelo, technician);
            report.SessionLogPath = logPath;

            return report;
        }
    }

    public void UpdateStage(
        ActivityReport report,
        int stageNumber,
        ActivityStageStatus status,
        string? details = null)
    {
        lock (_syncLock)
        {
            var etapa = report.Etapas.FirstOrDefault(e => e.Numero == stageNumber);
            if (etapa == null) return;

            etapa.Status = status;
            if (status == ActivityStageStatus.EmExecucao)
            {
                etapa.IniciadoEm ??= DateTime.Now;
            }
            else if (status is ActivityStageStatus.Sucesso or ActivityStageStatus.Falha or ActivityStageStatus.Pulado)
            {
                etapa.IniciadoEm ??= DateTime.Now;
                etapa.ConcluidoEm = DateTime.Now;
            }

            if (!string.IsNullOrWhiteSpace(details))
            {
                etapa.Detalhes = details;
            }

            var logMsg = $"[ETAPA {stageNumber}] {etapa.Nome}: {status} " + (!string.IsNullOrWhiteSpace(details) ? $"({details})" : "");
            SessionLogService.Instance.Log(logMsg, status == ActivityStageStatus.Falha ? "WARN" : "STAGE");
        }
    }

    public GeneratedReportFiles FinishActivity(
        ActivityReport report,
        bool isSuccess,
        string? erroGeral = null,
        string? pdfPath = null)
    {
        lock (_syncLock)
        {
            EnsureDirectory();
            report.ConcluidoEm = DateTime.Now;
            report.StatusFinal = isSuccess ? "Sucesso" : "Falha";
            report.ErroGeral = erroGeral;
            report.PdfReportPath = pdfPath;

            // Finaliza o log da sessão no SessionLogService
            SessionLogService.Instance.EndExecution(isSuccess, erroGeral);

            var cleanDesig = SanitizeFileName(report.DesignacaoIp ?? "ATIVIDADE");
            var timestamp = report.IniciadoEm.ToString("yyyyMMdd_HHmmss");

            // 1. Gera relatório estruturado JSON
            var jsonPath = Path.Combine(ActivitiesDirectory, $"Atividade-{cleanDesig}-{timestamp}.json");
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var jsonText = JsonSerializer.Serialize(report, jsonOptions);
            File.WriteAllText(jsonPath, jsonText, Encoding.UTF8);

            // 2. Gera relatório legível TXT
            var textPath = Path.Combine(ActivitiesDirectory, $"Atividade-{cleanDesig}-{timestamp}.txt");
            var textContent = FormatTextReport(report);
            File.WriteAllText(textPath, textContent, Encoding.UTF8);

            string? zipPath = null;

            // 3. Em caso de falha, gera pacote .zip de diagnóstico rápido (Log + Relatório)
            if (!isSuccess)
            {
                try
                {
                    zipPath = Path.Combine(ActivitiesDirectory, $"Atividade-{cleanDesig}-{timestamp}-FALHA.zip");
                    if (File.Exists(zipPath)) File.Delete(zipPath);

                    using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                    
                    // Adiciona relatório TXT
                    zip.CreateEntryFromFile(textPath, Path.GetFileName(textPath), CompressionLevel.Optimal);
                    
                    // Adiciona relatório JSON
                    zip.CreateEntryFromFile(jsonPath, Path.GetFileName(jsonPath), CompressionLevel.Optimal);

                    // Adiciona log da sessão se existir
                    if (!string.IsNullOrWhiteSpace(report.SessionLogPath) && File.Exists(report.SessionLogPath))
                    {
                        zip.CreateEntryFromFile(report.SessionLogPath, Path.GetFileName(report.SessionLogPath), CompressionLevel.Optimal);
                    }
                }
                catch (Exception ex)
                {
                    SessionLogService.Instance.Log($"Falha ao empacotar zip de diagnóstico: {ex.Message}", "WARN");
                }
            }

            return new GeneratedReportFiles(jsonPath, textPath, zipPath);
        }
    }

    public static string FormatTextReport(ActivityReport r)
    {
        var sb = new StringBuilder();
        var sep = new string('=', 80);
        var subSep = new string('-', 80);

        sb.AppendLine(sep);
        sb.AppendLine("           SPARC - RELATÓRIO OPERACIONAL DE ATIVIDADE & HOMOLOGAÇÃO           ");
        sb.AppendLine(sep);
        sb.AppendLine($"  Atividade ID      : {r.ActivityId}");
        sb.AppendLine($"  Data / Hora Início: {r.IniciadoEm:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"  Data / Hora Fim   : {r.ConcluidoEm:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"  Duração Total     : {r.DuracaoTotal:hh\\:mm\\:ss}");
        sb.AppendLine($"  Status da Entrega : {(r.StatusFinal == "Sucesso" ? "🟢 100% HOMOLOGADO COM SUCESSO" : "🔴 NÃO HOMOLOGADO / INTERROMPIDO COM FALHA")}");
        if (!string.IsNullOrWhiteSpace(r.ErroGeral))
        {
            sb.AppendLine($"  Motivo da Falha   : {r.ErroGeral}");
        }

        sb.AppendLine(subSep);
        sb.AppendLine("1. RESPONSÁVEL TÉCNICO & IDENTIFICAÇÃO OPERACIONAL");
        sb.AppendLine(subSep);
        sb.AppendLine($"  Técnico em Campo  : {r.Tecnico.FullName}");
        sb.AppendLine($"  Empresa           : {r.Tecnico.Company}");
        sb.AppendLine($"  Matrícula / ID    : {r.Tecnico.EmployeeId}");
        if (!string.IsNullOrWhiteSpace(r.Tecnico.Phone))
            sb.AppendLine($"  Telefone / Whats  : {r.Tecnico.Phone}");
        if (!string.IsNullOrWhiteSpace(r.Tecnico.Cluster) || !string.IsNullOrWhiteSpace(r.Tecnico.Uf))
            sb.AppendLine($"  Cluster / UF      : {r.Tecnico.Cluster} - {r.Tecnico.Uf}");
        sb.AppendLine($"  Identificador PC  : {r.Tecnico.MachineGuid} ({r.Tecnico.MachineDisplayId})");

        sb.AppendLine(subSep);
        sb.AppendLine("2. IDENTIFICAÇÃO DO CIRCUITO & CLIENTE CLARO");
        sb.AppendLine(subSep);
        sb.AppendLine($"  Cliente           : {r.ClienteRazaoSocial}");
        sb.AppendLine($"  Designação IP     : {r.DesignacaoIp}");
        sb.AppendLine($"  Número OTS        : {r.NumeroOts ?? "Não informado"}");
        sb.AppendLine($"  Tipo de Serviço   : {r.TipoServico}");
        if (r.BandaMbpsNominal.HasValue)
            sb.AppendLine($"  Banda Contratada  : {r.BandaMbpsNominal.Value:F1} Mbps");
        sb.AppendLine($"  PE Router         : {r.PeRouter ?? "Não informado"}");
        sb.AppendLine($"  IP WAN / Gateway  : {r.WanIp}/{r.WanCidr} (GW: {r.WanGateway ?? "N/D"})");
        sb.AppendLine($"  IP LAN / Bloco    : {r.LanIp}/{r.LanCidr} (Masc: {r.LanSubnetMask ?? "N/D"})");

        sb.AppendLine(subSep);
        sb.AppendLine("3. EQUIPAMENTO & PARÂMETROS DE CONEXÃO");
        sb.AppendLine(subSep);
        sb.AppendLine($"  Modelo            : {r.ModeloEquipamento}");
        sb.AppendLine($"  Porta Serial      : {r.PortaSerial} @ {r.BaudRate} bps");
        if (!string.IsNullOrWhiteSpace(r.FirmwareInstalado))
            sb.AppendLine($"  Firmware Detectado: {r.FirmwareInstalado}");
        if (!string.IsNullOrWhiteSpace(r.FirmwareAlvo))
            sb.AppendLine($"  Firmware Alvo     : {r.FirmwareAlvo}");

        sb.AppendLine(subSep);
        sb.AppendLine("4. EXECUÇÃO FASE A FASE DO PROVISIONAMENTO");
        sb.AppendLine(subSep);
        sb.AppendLine(string.Format("  {0,-3} | {1,-38} | {2,-12} | {3,-10}", "Nº", "Etapa", "Status", "Duração"));
        sb.AppendLine("  ----+----------------------------------------+--------------+-----------");
        foreach (var e in r.Etapas)
        {
            var stIcon = e.Status switch
            {
                ActivityStageStatus.Sucesso => "[OK] Sucesso",
                ActivityStageStatus.Falha => "[X]  Falha  ",
                ActivityStageStatus.Pulado => "[-]  Pulado ",
                ActivityStageStatus.EmExecucao => "[...] Execução",
                _ => "[ ]  Pendente"
            };
            var durStr = e.Duracao.TotalSeconds > 0 ? $"{e.Duracao:mm\\:ss}" : "--:--";
            sb.AppendLine(string.Format("  {0,2}  | {1,-38} | {2,-12} | {3,9}", e.Numero, e.Nome.Length > 38 ? e.Nome[..38] : e.Nome, stIcon, durStr));
            if (!string.IsNullOrWhiteSpace(e.Detalhes))
            {
                sb.AppendLine($"     └─ Detalhe: {e.Detalhes}");
            }
        }

        if (r.AlertasDiagnostico.Count > 0)
        {
            sb.AppendLine(subSep);
            sb.AppendLine("5. ALERTAS E DIAGNÓSTICO DE FALHAS");
            sb.AppendLine(subSep);
            foreach (var a in r.AlertasDiagnostico)
            {
                sb.AppendLine($"  ⚠️ {a}");
            }
        }

        sb.AppendLine(subSep);
        sb.AppendLine("6. RASTREABILIDADE & REGISTROS ASSOCIADOS");
        sb.AppendLine(subSep);
        if (!string.IsNullOrWhiteSpace(r.SessionLogPath))
            sb.AppendLine($"  Log Detalhado da Sessão : {r.SessionLogPath}");
        if (!string.IsNullOrWhiteSpace(r.PdfReportPath))
            sb.AppendLine($"  Relatório PDF Homologado: {r.PdfReportPath}");
        sb.AppendLine(sep);

        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) || c == '/' || c == '\\' || c == ':' ? '_' : c);
        }
        return sb.ToString();
    }
}
