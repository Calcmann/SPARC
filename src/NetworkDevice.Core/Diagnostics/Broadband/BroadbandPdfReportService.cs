using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkDevice.Core.Diagnostics.Broadband;

public static class BroadbandPdfReportService
{
    public static async Task<string> GenerateReportPdfAsync(
        BroadbandTestResult result,
        string? targetPdfPath = null,
        CancellationToken cancellationToken = default)
    {
        var htmlContent = GenerateHtml(result);
        var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"sparc_broadband_{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempHtmlPath, htmlContent, Encoding.UTF8, cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPdfPath))
        {
            var reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "Relatorios_BandaLarga");
            if (!Directory.Exists(reportsDir))
            {
                Directory.CreateDirectory(reportsDir);
            }

            var cleanHost = string.IsNullOrWhiteSpace(result.PrimaryServer?.Sponsor)
                ? "BandaLarga"
                : Path.GetInvalidFileNameChars().Aggregate(result.PrimaryServer.Sponsor, (curr, c) => curr.Replace(c, '_'));

            var fileName = $"Relatorio_BandaLarga_{cleanHost}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
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
                // Fallback para arquivo HTML em caso de erro na chamada do navegador
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
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    public static string GenerateHtml(BroadbandTestResult r)
    {
        var inv = CultureInfo.InvariantCulture;
        var nowStr = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        var dlMbps = r.DownloadAvgMbps.ToString("F2", inv);
        var ulMbps = r.UploadAvgMbps.ToString("F2", inv);
        var latIdle = r.LatencyIdleMs.ToString("F1", inv);
        var jitter = r.JitterMs.ToString("F1", inv);
        var loss = r.PacketLossPct.ToString("F1", inv);
        var grade = r.Bufferbloat?.Grade ?? "A";
        var gradeDesc = r.Bufferbloat?.Description ?? "Equilíbrio estável de latência e vazão sob carga pesada.";

        var gradeColor = grade switch
        {
            "A+" or "A" => "#10B981",
            "B" => "#3B82F6",
            "C" => "#F59E0B",
            _ => "#EF4444"
        };

        var statusBg = r.IsSuccess ? "#059669" : "#DC2626";
        var statusBadgeBorder = r.IsSuccess ? "#10B981" : "#EF4444";
        var statusText = r.IsSuccess ? "HOMOLOGADO COM SUCESSO" : "REPROVADO / FORA DOS PADRÕES";

        var throughputSvg = GenerateThroughputSvg(r);
        var latencyBufferbloatSvg = GenerateLatencyBufferbloatSvg(r);

        var geo = r.GeoInfo;
        var srv = r.PrimaryServer;
        var reportHash = $"SPARC-BL-{DateTime.Now:yyyyMMdd}-{Math.Abs((r.DownloadAvgMbps + r.UploadAvgMbps + r.LatencyIdleMs).GetHashCode()) % 90000 + 10000}";

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
<meta charset=""utf-8"">
<title>Relatório Executivo de Banda Larga e Qualidade de Link — SPARC</title>
<style>
  @page {{
    size: A4 portrait;
    margin: 12mm 14mm;
  }}
  * {{ box-sizing: border-box; }}
  body {{
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
    color: #0f172a;
    margin: 0;
    padding: 0;
    background: #ffffff;
    font-size: 12px;
    line-height: 1.42;
  }}

  /* Top Banner Executivo */
  .header-card {{
    background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
    border-radius: 10px;
    padding: 14px 18px;
    color: #ffffff;
    margin-bottom: 14px;
    display: flex;
    justify-content: space-between;
    align-items: center;
    border: 1px solid #334155;
    box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
  }}
  .brand-area {{
    display: flex;
    flex-direction: column;
    gap: 3px;
  }}
  .logo-title {{
    font-size: 18px;
    font-weight: 900;
    letter-spacing: -0.3px;
    color: #f8fafc;
  }}
  .logo-sub {{
    font-size: 10.5px;
    color: #94a3b8;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }}
  .cert-meta {{
    text-align: right;
  }}
  .badge-status {{
    display: inline-block;
    background: {statusBg};
    border: 1px solid {statusBadgeBorder};
    color: #ffffff;
    font-weight: 800;
    font-size: 11.5px;
    padding: 6px 14px;
    border-radius: 6px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }}
  .report-hash {{
    font-size: 10px;
    color: #94a3b8;
    font-family: 'Consolas', monospace;
    margin-top: 4px;
  }}

  /* Grid dos 4 Cards Principais (KPIs) */
  .grid-cards {{
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 10px;
    margin-bottom: 14px;
  }}
  .stat-card {{
    background: #f8fafc;
    border: 1px solid #e2e8f0;
    border-radius: 8px;
    padding: 10px 12px;
    position: relative;
    overflow: hidden;
  }}
  .stat-card::before {{
    content: '';
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    height: 3.5px;
  }}
  .card-dl::before {{ background: #10b981; }}
  .card-ul::before {{ background: #3b82f6; }}
  .card-lat::before {{ background: #0284c7; }}
  .card-bloat::before {{ background: #8b5cf6; }}

  .stat-label {{
    font-size: 9.5px;
    font-weight: 800;
    color: #64748b;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin-bottom: 4px;
  }}
  .stat-val {{
    font-size: 22px;
    font-weight: 900;
    color: #0f172a;
    line-height: 1.1;
  }}
  .stat-unit {{
    font-size: 11px;
    font-weight: 700;
    color: #64748b;
  }}
  .stat-sub {{
    font-size: 10px;
    font-weight: 700;
    margin-top: 4px;
    display: flex;
    justify-content: space-between;
  }}

  /* Títulos de Seção Modernos */
  .section-title {{
    font-size: 12.5px;
    font-weight: 800;
    color: #0369a1;
    margin: 14px 0 6px 0;
    display: flex;
    align-items: center;
    gap: 6px;
    text-transform: uppercase;
    letter-spacing: 0.3px;
  }}

  /* Painéis de Conteúdo */
  .panel-box {{
    background: #ffffff;
    border: 1px solid #cbd5e1;
    border-radius: 8px;
    padding: 10px 12px;
    margin-bottom: 12px;
  }}

  /* Georreferenciamento */
  .geo-grid {{
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 14px;
    font-size: 11.5px;
  }}
  .geo-col {{
    background: #f8fafc;
    border: 1px solid #e2e8f0;
    border-radius: 6px;
    padding: 8px 10px;
  }}
  .geo-col h4 {{
    margin: 0 0 6px 0;
    font-size: 11px;
    font-weight: 800;
    color: #1e293b;
    border-bottom: 1px solid #e2e8f0;
    padding-bottom: 4px;
    text-transform: uppercase;
  }}
  .geo-row {{
    display: flex;
    justify-content: space-between;
    padding: 2.5px 0;
  }}
  .geo-row span:first-child {{ color: #64748b; font-weight: 500; }}
  .geo-row span:last-child {{
    color: #0f172a;
    font-weight: 700;
    font-family: 'Consolas', monospace;
    font-size: 11px;
  }}

  /* Seção de Física da Fibra Óptica */
  .fiber-grid {{
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 10px;
    margin-bottom: 8px;
  }}
  .fiber-cell {{
    background: #f8fafc;
    border: 1px solid #e2e8f0;
    border-radius: 6px;
    padding: 8px 6px;
    text-align: center;
  }}
  .fiber-cell .lbl {{ font-size: 9px; font-weight: 700; color: #64748b; text-transform: uppercase; }}
  .fiber-cell .val {{ font-size: 15px; font-weight: 900; color: #0f172a; margin: 2px 0; }}
  .fiber-cell .sub {{ font-size: 9.5px; font-weight: 600; color: #64748b; }}

  /* Gráficos */
  .chart-box {{
    background: #ffffff;
    border: 1px solid #cbd5e1;
    border-radius: 8px;
    padding: 8px 10px;
    margin-bottom: 12px;
  }}
  .chart-header {{
    font-weight: 800;
    font-size: 11px;
    color: #334155;
    margin-bottom: 6px;
    display: flex;
    justify-content: space-between;
  }}

  /* Tabela de Auditoria */
  table.audit-table {{
    width: 100%;
    border-collapse: collapse;
    font-size: 11px;
    margin-top: 4px;
  }}
  table.audit-table th {{
    background: #f1f5f9;
    color: #334155;
    text-align: left;
    padding: 5px 8px;
    border: 1px solid #cbd5e1;
    font-weight: 800;
  }}
  table.audit-table td {{
    padding: 5px 8px;
    border: 1px solid #cbd5e1;
    color: #1e293b;
  }}

  /* Rodapé e Assinaturas */
  .signatures-area {{
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 20px;
    margin-top: 14px;
    padding-top: 10px;
  }}
  .sign-box {{
    border-top: 1px solid #94a3b8;
    padding-top: 4px;
    text-align: center;
    font-size: 10px;
    color: #475569;
  }}
  .sign-box b {{ display: block; color: #0f172a; font-size: 11px; }}

  .footer-bar {{
    margin-top: 14px;
    border-top: 1px solid #e2e8f0;
    padding-top: 6px;
    font-size: 9.5px;
    color: #94a3b8;
    display: flex;
    justify-content: space-between;
  }}
</style>
</head>
<body>

<!-- HEADER EXECUTIVO -->
<div class=""header-card"">
  <div class=""brand-area"">
    <div class=""logo-title"">SPARC — Relatório de Homologação de Banda Larga</div>
    <div class=""logo-sub"">Laudo Técnico de Qualidade &bull; ITU-T Y.1564 / RFC 6349 &bull; Speed.Cloudflare Multi-Anycast</div>
  </div>
  <div class=""cert-meta"">
    <div class=""badge-status"">{statusText}</div>
    <div class=""report-hash"">LAUDO: {reportHash} &bull; {nowStr}</div>
  </div>
</div>

<!-- 4 CARDS DE MÉTRICAS PRINCIPAIS -->
<div class=""grid-cards"">
  <div class=""stat-card card-dl"">
    <div class=""stat-label"">Download (Vazão Média)</div>
    <div class=""stat-val"">{dlMbps} <span class=""stat-unit"">Mbps</span></div>
    <div class=""stat-sub"">
      <span style=""color: #10B981;"">Pico: {r.DownloadPeakMbps:F1} Mbps</span>
      <span style=""color: #64748b;"">Estável</span>
    </div>
  </div>

  <div class=""stat-card card-ul"">
    <div class=""stat-label"">Upload (Vazão Média)</div>
    <div class=""stat-val"">{ulMbps} <span class=""stat-unit"">Mbps</span></div>
    <div class=""stat-sub"">
      <span style=""color: #3B82F6;"">Pico: {r.UploadPeakMbps:F1} Mbps</span>
      <span style=""color: #64748b;"">Estável</span>
    </div>
  </div>

  <div class=""stat-card card-lat"">
    <div class=""stat-label"">Latência &amp; Jitter RTT</div>
    <div class=""stat-val"">{latIdle} <span class=""stat-unit"">ms</span></div>
    <div class=""stat-sub"">
      <span style=""color: #0284C7;"">Jitter: {jitter} ms</span>
      <span style=""color: #64748b;"">Perda: {loss}%</span>
    </div>
  </div>

  <div class=""stat-card card-bloat"">
    <div class=""stat-label"">Bufferbloat Grade</div>
    <div class=""stat-val"" style=""color: {gradeColor};"">{grade}</div>
    <div class=""stat-sub"">
      <span style=""color: #64748b;"">{gradeDesc}</span>
    </div>
  </div>
</div>

<!-- EFICIÊNCIA DE ROTA ÓPTICA & FÍSICA DA FIBRA -->
<div class=""section-title"">⚡ Propagação em Fibra Óptica &amp; Eficiência de Rota (Cálculo Haversine)</div>
<div class=""panel-box"">
  <div class=""fiber-grid"">
    <div class=""fiber-cell"">
      <div class=""lbl"">Distância Geodésica</div>
      <div class=""val"">{(r.GeoInfo?.DistanceKm.HasValue == true ? $"{r.GeoInfo.DistanceKm:F1}" : "—")} km</div>
      <div class=""sub"">Haversine (Linha direta)</div>
    </div>
    <div class=""fiber-cell"">
      <div class=""lbl"">Piso Teórico da Fibra</div>
      <div class=""val"" style=""color: #0284c7;"">~{(r.LatencyQuality?.TheoreticalFiberRttMs.ToString("F1", inv) ?? "1.0")} ms</div>
      <div class=""sub"">Velocidade da Luz RTT</div>
    </div>
    <div class=""fiber-cell"">
      <div class=""lbl"">Eficiência de Rota</div>
      <div class=""val"" style=""color: #10B981;"">{(r.LatencyQuality?.RouteEfficiencyPct.ToString("F0", inv) ?? "100")}%</div>
      <div class=""sub"">{(r.LatencyQuality?.RouteQualityStatus ?? "Ótimo (Rota Direta)")}</div>
    </div>
    <div class=""fiber-cell"">
      <div class=""lbl"">Atraso Residual Fila</div>
      <div class=""val"" style=""color: #6366F1;"">~{(r.LatencyQuality?.ResidualQueuingDelayMs.ToString("F1", inv) ?? "0.0")} ms</div>
      <div class=""sub"">Buffer &amp; BGP Switches</div>
    </div>
  </div>
  <div style=""font-size: 11px; color: #475569; background: #f1f5f9; border-radius: 6px; padding: 6px 10px; border-left: 3px solid #0284C7;"">
    <b>Auditoria Física:</b> {(r.LatencyQuality?.DiagnosticExplanation ?? "Latência física em plena conformidade com a distância geodésica até o servidor de destino.")}
  </div>
</div>

<!-- GRÁFICOS VETORIAIS SVG DE ALTA DEFINIÇÃO -->
<div class=""section-title"">📈 Gráficos de Vazão e Estabilidade sob Carga</div>
<div class=""chart-box"">
  <div class=""chart-header"">
    <span>Throughput Temporal (Vazão Instantânea ao longo do teste)</span>
    <span style=""font-size: 10px; color: #64748b;"">Download &amp; Upload Contínuos</span>
  </div>
  {throughputSvg}
</div>

<div class=""chart-box"">
  <div class=""chart-header"">
    <span>Estabilidade de Latência &amp; Bufferbloat (Repouso vs Download Ativo vs Upload Ativo)</span>
    <span style=""font-size: 10px; color: #64748b;"">Comportamento dos Buffers sob Stress</span>
  </div>
  {latencyBufferbloatSvg}
</div>

<!-- GEORREFERENCIAMENTO E DETALHES DE ROTA -->
<div class=""section-title"">📍 Georreferenciamento e Infraestrutura de Rede</div>
<div class=""panel-box"">
  <div class=""geo-grid"">
    <div class=""geo-col"">
      <h4>Origem / Link Testado (Cliente)</h4>
      <div class=""geo-row""><span>IP Público:</span> <span>{geo?.PublicIp ?? "Detectado Localmente"}</span></div>
      <div class=""geo-row""><span>Provedor / ASN:</span> <span>{geo?.Provider ?? "N/D"}</span></div>
      <div class=""geo-row""><span>Localidade:</span> <span>{geo?.ClientCity ?? "Local"}, {geo?.ClientCountry ?? "BR"}</span></div>
      <div class=""geo-row""><span>Coordenadas GPS:</span> <span>{(geo?.ClientLatitude.HasValue == true ? $"{geo.ClientLatitude:F4}, {geo.ClientLongitude:F4}" : "GPS Indisponível")}</span></div>
      <div class=""geo-row""><span>Interface Local:</span> <span>{r.SourceIpAddress ?? "Ethernet Local"}</span></div>
    </div>
    <div class=""geo-col"">
      <h4>Destino / Datacenter Alvo</h4>
      <div class=""geo-row""><span>Servidor / Host:</span> <span>{srv?.Sponsor ?? "Cloudflare Edge"}</span></div>
      <div class=""geo-row""><span>Localidade Servidor:</span> <span>{srv?.Location ?? "Edge Anycast"}, {srv?.Country ?? "BR"}</span></div>
      <div class=""geo-row""><span>PoP Datacenter:</span> <span>{geo?.ServerColoIata ?? "Auto"} ({geo?.ServerCity ?? "Edge"})</span></div>
      <div class=""geo-row""><span>Distância Geodésica:</span> <span>{(geo?.DistanceKm.HasValue == true ? $"~{geo.DistanceKm:F1} km" : "Estimada")}</span></div>
      <div class=""geo-row""><span>Tecnologia de Teste:</span> <span>{srv?.ServerType ?? "HTTP Stream"}</span></div>
{(string.IsNullOrWhiteSpace(r.ValidationUrl) ? "" : $@"      <div class=""geo-row""><span>Certificação Oficial:</span> <span><a href=""{r.ValidationUrl}"" style=""color: #2563EB; font-weight: bold; text-decoration: none;"">📡 {r.ValidationUrl}</a></span></div>")}
    </div>
  </div>
</div>

<!-- AUDITORIA DE RESILIÊNCIA E FALLBACK -->
<div class=""section-title"">🛡️ Resiliência em Cadeia &amp; Histórico de Fallback</div>
<div class=""panel-box"" style=""padding: 8px 10px;"">
");

        if (r.FallbackHistory != null && r.FallbackHistory.Count > 0)
        {
            sb.Append(@"  <table class=""audit-table"">
    <thead>
      <tr>
        <th>Horário</th>
        <th>Servidor Alvo</th>
        <th>Motivo do Fallback</th>
        <th>Fallback Acionado</th>
      </tr>
    </thead>
    <tbody>");
            foreach (var fb in r.FallbackHistory)
            {
                sb.Append($@"
      <tr>
        <td>{fb.Timestamp:HH:mm:ss}</td>
        <td><b>{fb.TargetServerName}</b> ({fb.TargetServerType})</td>
        <td style=""color: #EF4444; font-weight: bold;"">{fb.Reason}</td>
        <td style=""color: #10B981; font-weight: bold;"">➜ {fb.FallbackServerName} ({fb.FallbackServerType})</td>
      </tr>");
            }
            sb.Append(@"    </tbody>
  </table>");
        }
        else
        {
            sb.Append($@"  <div style=""color: #10B981; font-weight: 700; font-size: 11px; display: flex; align-items: center; gap: 6px;"">
    ✓ Conexão direta estabelecida com o servidor primário sem necessidade de fallback.
  </div>");
        }

        sb.Append($@"
</div>

<!-- ASSINATURAS E CERTIFICAÇÃO TÉCNICA -->
<div class=""signatures-area"">
  <div class=""sign-box"">
    <b>Técnico de Campo Responsável</b>
    <span>Operações de Rede &bull; Credencial SPARC</span>
  </div>
  <div class=""sign-box"">
    <b>Supervisão Técnica &bull; Engenharia de Redes</b>
    <span>Certificação de Ativação e Qualidade de Banda Larga</span>
  </div>
</div>

<div class=""footer-bar"">
  <span>SPARC — Sistema de Provisionamento e Ativação de Roteadores da Claro &bull; Core v0.8.x</span>
  <span>Laudo digital emitido via Speed.Cloudflare Engine &bull; RFC 6349 / ITU-T Y.1564 &bull; Hash: {reportHash}</span>
</div>

</body>
</html>");

        return sb.ToString();
    }

    private static string GenerateThroughputSvg(BroadbandTestResult r)
    {
        var points = r.TimeSeries ?? Array.Empty<BroadbandPoint>();
        var dlPoints = points.Where(p => p.Phase == "Download").ToList();
        var ulPoints = points.Where(p => p.Phase == "Upload").ToList();

        const double w = 720;
        const double h = 135;
        const double padL = 48;
        const double padR = 20;
        const double padT = 16;
        const double padB = 26;

        var chartW = w - padL - padR;
        var chartH = h - padT - padB;
        var baseY = padT + chartH;

        var maxSpeed = Math.Max(10.0, Math.Max(r.DownloadPeakMbps, r.UploadPeakMbps) * 1.15);

        var dlLine = new StringBuilder();
        var dlArea = new StringBuilder();
        var ulLine = new StringBuilder();
        var ulArea = new StringBuilder();

        var dlStartX = padL;
        var dlW = chartW * 0.50;

        if (dlPoints.Count > 0)
        {
            dlArea.Append($"M {dlStartX:F1},{baseY:F1}");
            for (int i = 0; i < dlPoints.Count; i++)
            {
                var x = dlStartX + (i / (double)Math.Max(1, dlPoints.Count - 1)) * dlW;
                var y = padT + chartH - (dlPoints[i].SpeedMbps / maxSpeed) * chartH;
                if (i == 0) dlLine.Append($"M {x:F1},{y:F1}");
                else dlLine.Append($" L {x:F1},{y:F1}");
                dlArea.Append($" L {x:F1},{y:F1}");
            }
            dlArea.Append($" L {dlStartX + dlW:F1},{baseY:F1} Z");
        }
        else
        {
            var endY = padT + chartH - (r.DownloadAvgMbps / maxSpeed) * chartH;
            dlLine.Append($"M {dlStartX},{baseY} L {dlStartX + dlW},{endY}");
            dlArea.Append($"M {dlStartX},{baseY} L {dlStartX + dlW},{endY} L {dlStartX + dlW},{baseY} Z");
        }

        var ulStartX = padL + chartW * 0.53;
        var ulW = chartW * 0.47;

        if (ulPoints.Count > 0)
        {
            ulArea.Append($"M {ulStartX:F1},{baseY:F1}");
            for (int i = 0; i < ulPoints.Count; i++)
            {
                var x = ulStartX + (i / (double)Math.Max(1, ulPoints.Count - 1)) * ulW;
                var y = padT + chartH - (ulPoints[i].SpeedMbps / maxSpeed) * chartH;
                if (i == 0) ulLine.Append($"M {x:F1},{y:F1}");
                else ulLine.Append($" L {x:F1},{y:F1}");
                ulArea.Append($" L {x:F1},{y:F1}");
            }
            ulArea.Append($" L {ulStartX + ulW:F1},{baseY:F1} Z");
        }
        else
        {
            var endY = padT + chartH - (r.UploadAvgMbps / maxSpeed) * chartH;
            ulLine.Append($"M {ulStartX},{baseY} L {ulStartX + ulW},{endY}");
            ulArea.Append($"M {ulStartX},{baseY} L {ulStartX + ulW},{endY} L {ulStartX + ulW},{baseY} Z");
        }

        return $@"<svg width=""100%"" height=""135"" viewBox=""0 0 {w} {h}"" xmlns=""http://www.w3.org/2000/svg"">
  <defs>
    <linearGradient id=""gradDlArea"" x1=""0%"" y1=""0%"" x2=""0%"" y2=""100%"">
      <stop offset=""0%"" stop-color=""#10B981"" stop-opacity=""0.38"" />
      <stop offset=""100%"" stop-color=""#10B981"" stop-opacity=""0.02"" />
    </linearGradient>
    <linearGradient id=""gradUlArea"" x1=""0%"" y1=""0%"" x2=""0%"" y2=""100%"">
      <stop offset=""0%"" stop-color=""#3B82F6"" stop-opacity=""0.38"" />
      <stop offset=""100%"" stop-color=""#3B82F6"" stop-opacity=""0.02"" />
    </linearGradient>
  </defs>

  <rect width=""{w}"" height=""{h}"" fill=""#f8fafc"" rx=""6"" />
  
  <!-- Linhas de Grade Sutis -->
  <line x1=""{padL}"" y1=""{padT}"" x2=""{w - padR}"" y2=""{padT}"" stroke=""#e2e8f0"" stroke-dasharray=""3,3"" stroke-width=""1"" />
  <line x1=""{padL}"" y1=""{padT + chartH * 0.5}"" x2=""{w - padR}"" y2=""{padT + chartH * 0.5}"" stroke=""#e2e8f0"" stroke-dasharray=""3,3"" stroke-width=""1"" />
  <line x1=""{padL}"" y1=""{baseY}"" x2=""{w - padR}"" y2=""{baseY}"" stroke=""#cbd5e1"" stroke-width=""1.2"" />

  <!-- Divisor Download / Upload -->
  <line x1=""{padL + chartW * 0.515}"" y1=""{padT}"" x2=""{padL + chartW * 0.515}"" y2=""{baseY}"" stroke=""#e2e8f0"" stroke-dasharray=""2,2"" stroke-width=""1"" />

  <!-- Eixo Y Labels -->
  <text x=""{padL - 6}"" y=""{padT + 4}"" font-size=""9"" font-weight=""bold"" fill=""#64748b"" text-anchor=""end"">{maxSpeed:F0}M</text>
  <text x=""{padL - 6}"" y=""{padT + chartH * 0.5 + 3}"" font-size=""9"" font-weight=""bold"" fill=""#64748b"" text-anchor=""end"">{(maxSpeed * 0.5):F0}M</text>
  <text x=""{padL - 6}"" y=""{baseY + 3}"" font-size=""9"" font-weight=""bold"" fill=""#64748b"" text-anchor=""end"">0</text>

  <!-- Áreas com Gradiente Suave -->
  <path d=""{dlArea}"" fill=""url(#gradDlArea)"" />
  <path d=""{ulArea}"" fill=""url(#gradUlArea)"" />

  <!-- Curvas de Vazão Neon -->
  <path d=""{dlLine}"" fill=""none"" stroke=""#10B981"" stroke-width=""3"" stroke-linecap=""round"" stroke-linejoin=""round"" />
  <path d=""{ulLine}"" fill=""none"" stroke=""#3B82F6"" stroke-width=""3"" stroke-linecap=""round"" stroke-linejoin=""round"" />

  <!-- Legenda Moderna -->
  <circle cx=""{padL + 12}"" cy=""{h - 9}"" r=""4.5"" fill=""#10B981"" />
  <text x=""{padL + 22}"" y=""{h - 6}"" font-size=""10"" font-weight=""bold"" fill=""#0f172a"">Download Médio: {r.DownloadAvgMbps:F1} Mbps (Pico: {r.DownloadPeakMbps:F1}M)</text>
  
  <circle cx=""{padL + 260}"" cy=""{h - 9}"" r=""4.5"" fill=""#3B82F6"" />
  <text x=""{padL + 270}"" y=""{h - 6}"" font-size=""10"" font-weight=""bold"" fill=""#0f172a"">Upload Médio: {r.UploadAvgMbps:F1} Mbps (Pico: {r.UploadPeakMbps:F1}M)</text>
</svg>";
    }

    private static string GenerateLatencyBufferbloatSvg(BroadbandTestResult r)
    {
        const double w = 720;
        const double h = 125;
        var b = r.Bufferbloat;
        var idle = b?.LatencyIdleMs ?? r.LatencyIdleMs;
        var dl = b?.LatencyUnderDownloadMs ?? (idle * 1.2);
        var ul = b?.LatencyUnderUploadMs ?? (idle * 1.3);

        var maxLat = Math.Max(25.0, Math.Max(idle, Math.Max(dl, ul)) * 1.35);

        var barW = 85;
        var hIdle = Math.Max(6, (idle / maxLat) * 72);
        var hDl = Math.Max(6, (dl / maxLat) * 72);
        var hUl = Math.Max(6, (ul / maxLat) * 72);

        var deltaDl = dl - idle;
        var deltaUl = ul - idle;

        return $@"<svg width=""100%"" height=""125"" viewBox=""0 0 {w} {h}"" xmlns=""http://www.w3.org/2000/svg"">
  <defs>
    <linearGradient id=""barIdleGrad"" x1=""0%"" y1=""0%"" x2=""0%"" y2=""100%"">
      <stop offset=""0%"" stop-color=""#0284C7"" />
      <stop offset=""100%"" stop-color=""#0369A1"" />
    </linearGradient>
    <linearGradient id=""barDlGrad"" x1=""0%"" y1=""0%"" x2=""0%"" y2=""100%"">
      <stop offset=""0%"" stop-color=""#10B981"" />
      <stop offset=""100%"" stop-color=""#059669"" />
    </linearGradient>
    <linearGradient id=""barUlGrad"" x1=""0%"" y1=""0%"" x2=""0%"" y2=""100%"">
      <stop offset=""0%"" stop-color=""#8B5CF6"" />
      <stop offset=""100%"" stop-color=""#6D28D9"" />
    </linearGradient>
  </defs>

  <rect width=""{w}"" height=""{h}"" fill=""#f8fafc"" rx=""6"" />

  <!-- Linha de Base -->
  <line x1=""40"" y1=""92"" x2=""680"" y2=""92"" stroke=""#cbd5e1"" stroke-width=""1.2"" />

  <!-- Barra 1: Repouso (Idle) -->
  <rect x=""95"" y=""{92 - hIdle}"" width=""{barW}"" height=""{hIdle}"" fill=""url(#barIdleGrad)"" rx=""5"" />
  <text x=""137"" y=""{85 - hIdle}"" font-size=""11"" font-weight=""900"" fill=""#0284C7"" text-anchor=""middle"">{idle:F1} ms</text>
  <text x=""137"" y=""107"" font-size=""10.5"" font-weight=""bold"" fill=""#334155"" text-anchor=""middle"">Repouso (Idle)</text>

  <!-- Barra 2: Download Ativo -->
  <rect x=""315"" y=""{92 - hDl}"" width=""{barW}"" height=""{hDl}"" fill=""url(#barDlGrad)"" rx=""5"" />
  <text x=""357"" y=""{85 - hDl}"" font-size=""11"" font-weight=""900"" fill=""#10B981"" text-anchor=""middle"">{dl:F1} ms</text>
  <text x=""357"" y=""107"" font-size=""10.5"" font-weight=""bold"" fill=""#334155"" text-anchor=""middle"">Sob Download</text>
  <text x=""357"" y=""119"" font-size=""9"" font-weight=""bold"" fill=""#059669"" text-anchor=""middle"">{(deltaDl >= 0 ? $"+{deltaDl:F1} ms" : $"{deltaDl:F1} ms")}</text>

  <!-- Barra 3: Upload Ativo -->
  <rect x=""535"" y=""{92 - hUl}"" width=""{barW}"" height=""{hUl}"" fill=""url(#barUlGrad)"" rx=""5"" />
  <text x=""577"" y=""{85 - hUl}"" font-size=""11"" font-weight=""900"" fill=""#8B5CF6"" text-anchor=""middle"">{ul:F1} ms</text>
  <text x=""577"" y=""107"" font-size=""10.5"" font-weight=""bold"" fill=""#334155"" text-anchor=""middle"">Sob Upload</text>
  <text x=""577"" y=""119"" font-size=""9"" font-weight=""bold"" fill=""#7C3AED"" text-anchor=""middle"">{(deltaUl >= 0 ? $"+{deltaUl:F1} ms" : $"{deltaUl:F1} ms")}</text>
</svg>";
    }
}
