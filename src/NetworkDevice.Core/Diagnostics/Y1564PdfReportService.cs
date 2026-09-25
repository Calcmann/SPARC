using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Core.Diagnostics;

public static class Y1564PdfReportService
{
    public static async Task<string> GenerateReportPdfAsync(
        Y1564Result result,
        string? targetPdfPath = null,
        CancellationToken cancellationToken = default)
    {
        var htmlContent = GenerateHtml(result);
        var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"sparc_y1564_{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempHtmlPath, htmlContent, Encoding.UTF8, cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPdfPath))
        {
            var reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "Certidoes_Y1564");
            if (!Directory.Exists(reportsDir))
            {
                Directory.CreateDirectory(reportsDir);
            }

            var cleanDesig = string.IsNullOrWhiteSpace(result.Designation)
                ? "Circuito"
                : Path.GetInvalidFileNameChars().Aggregate(result.Designation, (curr, c) => curr.Replace(c, '_'));

            var fileName = $"Certidao_Y1564_{cleanDesig}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            targetPdfPath = Path.Combine(reportsDir, fileName);
        }

        var browserExe = FindHeadlessBrowser();
        if (browserExe != null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = browserExe,
                    Arguments = $"--headless --disable-gpu --run-all-compositor-stages-before-draw --no-pdf-header-footer --print-to-pdf=\"{targetPdfPath}\" \"{tempHtmlPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync(cancellationToken);
                }

                if (File.Exists(targetPdfPath) && new FileInfo(targetPdfPath).Length > 0)
                {
                    return targetPdfPath;
                }
            }
            catch
            {
                // Fallback para arquivo HTML em caso de exceção na chamada headless
            }
        }

        var fallbackHtmlPath = Path.ChangeExtension(targetPdfPath, ".html");
        File.Copy(tempHtmlPath, fallbackHtmlPath, true);
        return fallbackHtmlPath;
    }

    private static string? FindHeadlessBrowser()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files (x86)\Microsoft\EdgeCore\151.0.4129.101\msedge.exe",
            @"C:\Program Files (x86)\Microsoft\EdgeCore\151.0.4129.107\msedge.exe",
            @"C:\Program Files (x86)\Microsoft\EdgeCore\151.0.4129.93\msedge.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    public static string FormatDuration(TimeSpan duration)
    {
        var totalMinutes = (int)duration.TotalMinutes;
        var seconds = duration.Seconds;
        if (totalMinutes > 0)
        {
            return $"{totalMinutes} min e {seconds} seg";
        }
        return $"{seconds} seg";
    }

    public static string GenerateHtml(Y1564Result r)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var clientName = string.IsNullOrWhiteSpace(r.ClientName) ? "NORTEL ELETR" : r.ClientName.Trim();
        var designation = string.IsNullOrWhiteSpace(r.Designation) ? "LFS/IP/02924" : r.Designation.Trim();
        var startTimeStr = r.StartTime.ToString("dd/MM/yyyy HH:mm:ss");
        var endTimeStr = r.EndTime.ToString("dd/MM/yyyy HH:mm:ss");
        var headerTimeStr = r.EndTime.ToString("dd/MM/yyyy HH:mm:ss");
        var durationStr = FormatDuration(r.Duration);
        var statusColor = r.IsPass ? "#16A34A" : "#DC2626";
        var summaryText = r.SummaryText;

        var rxMbpsStr = r.RxThroughputMbps.ToString("F2", inv);
        var lossStr = r.LossPercentage.ToString("F2", inv);
        var delayAvgStr = r.DelayAvgMs.ToString("F2", inv);
        var delayMinStr = r.DelayMinMs.ToString("F2", inv);
        var delayMaxStr = r.DelayMaxMs.ToString("F2", inv);
        var jitterAvgStr = r.JitterAvgMs.ToString("F2", inv);
        var jitterMaxStr = r.JitterMaxMs.ToString("F2", inv);
        var slaJitterStr = r.SlaJitterMs.ToString("F0", inv);
        var lostPctStr = r.LostPercentage.ToString("F2", inv);
        var ulrMbpsStr = r.NetworkUlrMbps.ToString("F0", inv);

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
<meta charset=""utf-8"">
<title>Resultados - Y.1564 — {designation}</title>
<style>
  @page {{
    size: A4 portrait;
    margin: 18mm 20mm;
  }}
  * {{
    box-sizing: border-box;
  }}
  body {{
    font-family: Arial, Helvetica, sans-serif;
    color: #1e293b;
    margin: 0;
    padding: 0;
    background: #ffffff;
    font-size: 13px;
    line-height: 1.4;
  }}
  .header-bar {{
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 25px;
  }}
  .logo-container {{
    display: flex;
    align-items: center;
    gap: 12px;
  }}
  .header-title {{
    font-size: 20px;
    font-weight: bold;
    color: #0c2340;
    letter-spacing: -0.2px;
  }}
  .header-date {{
    font-size: 12px;
    color: #334155;
    font-weight: 500;
  }}
  .card {{
    border: 1px solid #d1d5db;
    border-radius: 3px;
    padding: 16px 20px;
    margin-bottom: 22px;
    background: #ffffff;
  }}
  .card-header {{
    font-size: 15px;
    font-weight: bold;
    color: #1d4ed8;
    margin-bottom: 14px;
  }}
  .info-grid {{
    display: grid;
    grid-template-columns: 180px 1fr;
    row-gap: 6px;
    font-size: 12.5px;
  }}
  .info-label {{
    color: #0c2340;
    font-weight: 600;
  }}
  .info-value {{
    color: #1e293b;
  }}
  .badge-pass {{
    color: {statusColor};
    font-weight: bold;
  }}
  .perf-container {{
    display: flex;
    gap: 20px;
  }}
  .perf-left {{
    width: 150px;
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    gap: 10px;
    font-size: 12.5px;
  }}
  .perf-item-title {{
    font-weight: 600;
    color: #0c2340;
    margin-bottom: 1px;
  }}
  .perf-item-val {{
    color: #1e293b;
  }}
  .perf-table-container {{
    flex-grow: 1;
  }}
  table.perf-table {{
    width: 100%;
    border-collapse: collapse;
    font-size: 12px;
    border: 1px solid #64748b;
  }}
  table.perf-table th {{
    border: 1px solid #64748b;
    padding: 7px 8px;
    font-weight: bold;
    color: #0c2340;
    text-align: center;
    background: #f8fafc;
  }}
  table.perf-table th small {{
    display: block;
    font-size: 10.5px;
    font-weight: normal;
    color: #64748b;
  }}
  table.perf-table td {{
    border: 1px solid #64748b;
    padding: 8px 10px;
    vertical-align: top;
    color: #1e293b;
    line-height: 1.45;
  }}
</style>
</head>
<body>

  <!-- Top Header -->
  <div class=""header-bar"">
    <div class=""logo-container"">
      <!-- Claro Official Red Badge SVG -->
      <svg width=""76"" height=""44"" viewBox=""0 0 120 70"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
        <circle cx=""35"" cy=""35"" r=""33"" fill=""#DA291C""/>
        <path d=""M 49 14 L 54 8 M 53 19 L 60 16 M 55 25 L 63 24"" stroke=""white"" stroke-width=""3"" stroke-linecap=""round""/>
        <text x=""12"" y=""42"" fill=""white"" font-family=""'Arial Black', Impact, sans-serif"" font-size=""18"" font-weight=""900"">claro</text>
      </svg>
    </div>
    <div class=""header-title"">Resultados - Y.1564</div>
    <div class=""header-date"">{headerTimeStr}</div>
  </div>

  <!-- Box 1: Informações do Teste -->
  <div class=""card"">
    <div class=""card-header"">Informações do Teste</div>
    <div class=""info-grid"">
      <div class=""info-label"">Nome do Teste</div>
      <div class=""info-value"">Y.1564</div>

      <div class=""info-label"">Data de Início</div>
      <div class=""info-value"">{startTimeStr}</div>

      <div class=""info-label"">Data de Término</div>
      <div class=""info-value"">{endTimeStr}</div>

      <div class=""info-label"">Duração</div>
      <div class=""info-value"">{durationStr}</div>

      <div class=""info-label"">Nome do Cliente</div>
      <div class=""info-value"">{clientName}</div>

      <div class=""info-label"">Designação</div>
      <div class=""info-value"">{designation}</div>

      <div class=""info-label"">Resumo</div>
      <div class=""info-value badge-pass"">{summaryText}</div>
    </div>
  </div>

  <!-- Box 2: Resultado de Testes de Performance -->
  <div class=""card"">
    <div class=""card-header"">Resultado de Testes de Performance</div>
    <div class=""perf-container"">
      <div class=""perf-left"">
        <div>
          <div class=""perf-item-title"">Nome do Teste</div>
          <div class=""perf-item-val"">Y.1564</div>
        </div>
        <div>
          <div class=""perf-item-title"">Resumo do Teste</div>
          <div class=""perf-item-val badge-pass"">{summaryText}</div>
        </div>
        <div>
          <div class=""perf-item-title"">Frame</div>
          <div class=""perf-item-val"">{(!string.IsNullOrWhiteSpace(r.FrameSizeDescription) ? r.FrameSizeDescription : r.FrameSize.ToString())}</div>
        </div>
        <div>
          <div class=""perf-item-title"">Network ULR</div>
          <div class=""perf-item-val"">{ulrMbpsStr} Mbps</div>
        </div>
      </div>

      <div class=""perf-table-container"">
        <table class=""perf-table"">
          <thead>
            <tr>
              <th style=""width: 21%;"">L1 Mbps (ULR)</th>
              <th style=""width: 15%;"">Loss</th>
              <th style=""width: 22%;"">FD<small>(Delay)</small></th>
              <th style=""width: 20%;"">FDV<small>(Jitter)</small></th>
              <th style=""width: 22%;"">Counts</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td style=""font-weight: 500;"">
                Rx: {rxMbpsStr} Mbps
              </td>
              <td>
                Avg: {lossStr} %
              </td>
              <td>
                Avg: {delayAvgStr} ms<br>
                Min: {delayMinStr} ms<br>
                Max: {delayMaxStr} ms
              </td>
              <td>
                Avg: {jitterAvgStr} ms<br>
                Max: {jitterMaxStr} ms<br>
                SLA: {slaJitterStr} ms
              </td>
              <td style=""font-size: 11.5px;"">
                Tx: {r.TxPackets}<br>
                Rx: {r.RxPackets}<br>
                Lost: {lostPctStr} %<br>
                OOS: {r.OosPackets}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>

</body>
</html>");

        return sb.ToString();
    }
}
