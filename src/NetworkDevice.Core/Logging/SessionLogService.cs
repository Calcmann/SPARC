using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NetworkDevice.Core.Installation;

namespace NetworkDevice.Core.Logging;

public sealed class SessionLogService
{
    private static readonly Lazy<SessionLogService> _instance = new(() => new SessionLogService());
    public static SessionLogService Instance => _instance.Value;

    private readonly object _writeLock = new();
    private string? _customLogsDir;
    private string? _currentExecutionLogPath;
    private string? _currentDesignacao;
    private DateTime? _executionStartTime;

    // Regexes para sanitização de credenciais e segredos em logs
    private static readonly Regex SecretPatterns = new(
        @"(?i)\b(password|secret|pre-shared-key|tacacs-server\s+key|snmp-server\s+community|set\s+password|enable\s+secret)\s+([0-9]\s+)?(\S+)",
        RegexOptions.Compiled);

    public string LogsDirectory
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_customLogsDir))
            {
                return _customLogsDir;
            }

            var root = WorkspaceManager.GetDefaultWorkspaceRoot();
            return Path.Combine(root, "logs");
        }
    }

    public string? CurrentExecutionLogPath => _currentExecutionLogPath;

    public SessionLogService()
    {
        EnsureDirectory();
    }

    public void SetCustomLogsDirectory(string dir)
    {
        _customLogsDir = dir;
        EnsureDirectory();
    }

    private void EnsureDirectory()
    {
        try
        {
            if (!Directory.Exists(LogsDirectory))
            {
                Directory.CreateDirectory(LogsDirectory);
            }
        }
        catch { }
    }

    public string StartExecution(string? designacao, string? modelo, TechnicianProfile? technician = null)
    {
        lock (_writeLock)
        {
            EnsureDirectory();
            _currentDesignacao = string.IsNullOrWhiteSpace(designacao) ? "SEM-DESIG" : designacao.Trim();
            _executionStartTime = DateTime.Now;

            var cleanDesig = SanitizeFileName(_currentDesignacao);
            var timestamp = _executionStartTime.Value.ToString("yyyyMMdd_HHmmss");
            var fileName = $"exec-{cleanDesig}-{timestamp}.log";
            _currentExecutionLogPath = Path.Combine(LogsDirectory, fileName);

            technician ??= TechnicianProfileService.LoadCurrentProfile();

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("           SPARC - REGISTRO OPERACIONAL DE ATIVIDADE & EXECUÇÃO                ");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Data/Hora Início   : {_executionStartTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Circuito / OTS     : {_currentDesignacao}");
            sb.AppendLine($"Modelo Equipamento : {modelo ?? "Não identificado"}");
            sb.AppendLine($"Técnico Responsável: {technician.Summary}");
            sb.AppendLine($"Identificador PC   : {technician.MachineGuid} ({technician.MachineDisplayId})");
            sb.AppendLine($"Arquivo de Log     : {_currentExecutionLogPath}");
            sb.AppendLine("================================================================================");
            sb.AppendLine();

            File.WriteAllText(_currentExecutionLogPath, sb.ToString(), Encoding.UTF8);

            Log($"Início de atividade de provisionamento para circuito {_currentDesignacao} (Modelo: {modelo ?? "N/D"}).", "START");

            // Executa limpeza periódica de logs com mais de 30 dias
            PruneOldLogs(30);

            return _currentExecutionLogPath;
        }
    }

    public string? EndExecution(bool isSuccess, string? message = null)
    {
        lock (_writeLock)
        {
            if (string.IsNullOrWhiteSpace(_currentExecutionLogPath))
            {
                return null;
            }

            var endTime = DateTime.Now;
            var duration = _executionStartTime.HasValue ? endTime - _executionStartTime.Value : TimeSpan.Zero;
            var statusStr = isSuccess ? "SUCESSO (HOMOLOGADO)" : "FALHA / INTERROMPIDO";

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("================================================================================");
            sb.AppendLine($"           STATUS FINAL: {statusStr}");
            sb.AppendLine($"Data/Hora Fim      : {endTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Duração Total      : {duration:hh\\:mm\\:ss}");
            if (!string.IsNullOrWhiteSpace(message))
            {
                sb.AppendLine($"Detalhe/Motivo     : {message}");
            }
            sb.AppendLine("================================================================================");

            try
            {
                File.AppendAllText(_currentExecutionLogPath, sb.ToString(), Encoding.UTF8);
            }
            catch { }

            Log($"Término de atividade: {statusStr}. Duração: {duration:hh\\:mm\\:ss}. {message ?? ""}".Trim(), isSuccess ? "FINISH" : "ERROR");

            var completedPath = _currentExecutionLogPath;
            _currentExecutionLogPath = null;
            _currentDesignacao = null;
            _executionStartTime = null;

            return completedPath;
        }
    }

    public void Log(string message, string level = "INFO")
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        lock (_writeLock)
        {
            try
            {
                EnsureDirectory();
                var now = DateTime.Now;
                var sanitized = Sanitize(message);
                var formattedLine = $"[{now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {sanitized}{Environment.NewLine}";

                // 1. Grava no log diário rotativo (sparc-yyyy-MM-dd.log)
                var dailyLogFile = Path.Combine(LogsDirectory, $"sparc-{now:yyyy-MM-dd}.log");
                File.AppendAllText(dailyLogFile, formattedLine, Encoding.UTF8);

                // 2. Se houver execução ativa em andamento, espelha no log individual da atividade
                if (!string.IsNullOrWhiteSpace(_currentExecutionLogPath) && File.Exists(_currentExecutionLogPath))
                {
                    File.AppendAllText(_currentExecutionLogPath, formattedLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Não interrompe o fluxo principal por falha de I/O em log
            }
        }
    }

    public void LogRaw(string rawChunk)
    {
        if (string.IsNullOrEmpty(rawChunk)) return;

        lock (_writeLock)
        {
            if (string.IsNullOrWhiteSpace(_currentExecutionLogPath)) return;

            try
            {
                var sanitized = Sanitize(rawChunk);
                File.AppendAllText(_currentExecutionLogPath, sanitized, Encoding.UTF8);
            }
            catch { }
        }
    }

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return SecretPatterns.Replace(input, match =>
        {
            var keyGroup = match.Groups[1].Value;
            return $"{keyGroup} ***";
        });
    }

    public void PruneOldLogs(int daysToKeep = 30)
    {
        try
        {
            if (!Directory.Exists(LogsDirectory)) return;

            var cutoff = DateTime.Now.AddDays(-daysToKeep);
            var files = Directory.GetFiles(LogsDirectory, "*.*");
            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                if (fi.LastWriteTime < cutoff && (fi.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    try { fi.Delete(); } catch { }
                }
            }
        }
        catch { }
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
