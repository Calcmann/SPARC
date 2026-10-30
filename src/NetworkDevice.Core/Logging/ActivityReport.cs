using System;
using System.Collections.Generic;

namespace NetworkDevice.Core.Logging;

public enum ActivityStageStatus
{
    Pendente,
    EmExecucao,
    Sucesso,
    Falha,
    Pulado
}

public sealed class ActivityStageResult
{
    public int Numero { get; set; }
    public string Nome { get; set; } = string.Empty;
    public ActivityStageStatus Status { get; set; } = ActivityStageStatus.Pendente;
    public DateTime? IniciadoEm { get; set; }
    public DateTime? ConcluidoEm { get; set; }
    public TimeSpan Duracao => (ConcluidoEm.HasValue && IniciadoEm.HasValue) ? (ConcluidoEm.Value - IniciadoEm.Value) : TimeSpan.Zero;
    public string? Detalhes { get; set; }
}

public sealed class ActivityReport
{
    public string ActivityId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime IniciadoEm { get; set; } = DateTime.Now;
    public DateTime? ConcluidoEm { get; set; }
    public TimeSpan DuracaoTotal => (ConcluidoEm.HasValue ? ConcluidoEm.Value : DateTime.Now) - IniciadoEm;

    public string StatusFinal { get; set; } = "Em Andamento"; // Sucesso, Falha, Cancelado

    // Identificação do Circuito & Serviço
    public string? DesignacaoIp { get; set; }
    public string? ClienteRazaoSocial { get; set; }
    public string? NumeroOts { get; set; }
    public string? TipoServico { get; set; }
    public double? BandaMbpsNominal { get; set; }
    public string? PeRouter { get; set; }
    public string? WanIp { get; set; }
    public int? WanCidr { get; set; }
    public string? WanGateway { get; set; }
    public string? WanSubnetMask { get; set; }
    public string? LanIp { get; set; }
    public int? LanCidr { get; set; }
    public string? LanSubnetMask { get; set; }

    // Técnico Responsável
    public TechnicianProfile Tecnico { get; set; } = new();

    // Equipamento & Parâmetros Físicos
    public string ModeloEquipamento { get; set; } = string.Empty;
    public string? Fabricante { get; set; }
    public string PortaSerial { get; set; } = string.Empty;
    public int BaudRate { get; set; } = 9600;
    public string? FirmwareInstalado { get; set; }
    public string? FirmwareAlvo { get; set; }
    public string? SerialNumber { get; set; }

    // Fases e Resultados
    public List<ActivityStageResult> Etapas { get; set; } = new();
    public List<string> AlertasDiagnostico { get; set; } = new();
    public string? ErroGeral { get; set; }

    // Arquivos Gerados
    public string? SessionLogPath { get; set; }
    public string? PdfReportPath { get; set; }
}

