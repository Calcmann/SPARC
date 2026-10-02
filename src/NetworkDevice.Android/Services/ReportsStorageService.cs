using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetworkDevice.Android.Services;

public sealed record SavedReportItem(
    string FileName,
    string FilePath,
    DateTime CreatedAt,
    long SizeBytes,
    string ReportType,
    string Title);

public sealed class ReportsStorageService
{
    private static readonly Lazy<ReportsStorageService> _instance = new(() => new ReportsStorageService());
    public static ReportsStorageService Instance => _instance.Value;

    private readonly string _reportsDirectory;

    private ReportsStorageService()
    {
        _reportsDirectory = Path.Combine(FileSystem.AppDataDirectory, "Reports");
        if (!Directory.Exists(_reportsDirectory))
        {
            Directory.CreateDirectory(_reportsDirectory);
        }
    }

    public string ReportsDirectory => _reportsDirectory;

    public async Task<string> SaveReportAsync(string baseName, string content, string extension = ".html", string reportType = "Ativação")
    {
        var sanitized = string.Join("_", baseName.Split(Path.GetInvalidFileNameChars()));
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{sanitized}_{timestamp}{extension}";
        var fullPath = Path.Combine(_reportsDirectory, fileName);

        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
        return fullPath;
    }

    public IReadOnlyList<SavedReportItem> ListReports()
    {
        if (!Directory.Exists(_reportsDirectory))
            return Array.Empty<SavedReportItem>();

        var dir = new DirectoryInfo(_reportsDirectory);
        return dir.GetFiles("*.*")
            .Where(f => f.Extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                        f.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
                        f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.CreationTime)
            .Select(f =>
            {
                var type = f.Name.Contains("Y1564", StringComparison.OrdinalIgnoreCase) ? "Y.1564 SLA" :
                           f.Name.Contains("Relatorio", StringComparison.OrdinalIgnoreCase) ? "Ativação SAIP" : "Diagnóstico";
                return new SavedReportItem(
                    FileName: f.Name,
                    FilePath: f.FullName,
                    CreatedAt: f.CreationTime,
                    SizeBytes: f.Length,
                    ReportType: type,
                    Title: Path.GetFileNameWithoutExtension(f.Name)
                );
            })
            .ToList();
    }

    public bool DeleteReport(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch { }
        return false;
    }
}
