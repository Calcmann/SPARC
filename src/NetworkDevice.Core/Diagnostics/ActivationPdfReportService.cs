using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetworkDevice.Core.Provisioning;

namespace NetworkDevice.Core.Diagnostics;

public sealed record TripleIcmpData(
    ConnectivityTestResult? LanResult,
    ConnectivityTestResult? WanResult,
    ConnectivityTestResult? WebResult)
{
    public bool IsLanOk => LanResult?.IsSuccess == true;
    public bool IsWanOk => WanResult?.IsSuccess == true;
    public bool IsWebOk => WebResult?.IsSuccess == true;
}

public sealed record ActivationReportData(
    DateTime DataHora,
    string ModeloEquipamento,
    string PortaSerial,
    int BaudRate,
    string? ClienteRazaoSocial,
    string? DesignacaoIp,
    string? NumeroOts,
    string? PeRouter,
    string? WanIp,
    int WanCidr,
    string? WanGateway,
    string? WanSubnetMask,
    string? WanInterface,
    string? LanIp,
    int LanCidr,
    string? LanBlockNetwork,
    string? LanSubnetMask,
    string? HostLanIp,
    string? LanInterface,
    bool Step1ZerarOk,
    bool Step2FirmwareOk,
    string? FirmwareNome,
    bool Step3SaipOk,
    bool Step4IpLocalOk,
    string? AdaptadorRedeLocal,
    TripleIcmpData? IcmpResult,
    ConnectivityService.TelnetTestResult? TelnetResult,
    BandwidthTestResult? BandResult,
    IReadOnlyList<string>? DiagnosticAlerts,
    string? FalhaGeral,
    string? AppliedConfigScript = null);

public static class ActivationPdfReportService
{
    public static async Task<string> GenerateReportPdfAsync(
        ActivationReportData data,
        string? targetPdfPath = null,
        CancellationToken cancellationToken = default)
    {
        var htmlContent = GenerateHtml(data);
        var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"sparc_report_{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempHtmlPath, htmlContent, Encoding.UTF8, cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPdfPath))
        {
            var reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "Relatorios");
            if (!Directory.Exists(reportsDir))
            {
                Directory.CreateDirectory(reportsDir);
            }

            var cleanDesig = string.IsNullOrWhiteSpace(data.DesignacaoIp)
                ? "Circuito"
                : Path.GetInvalidFileNameChars().Aggregate(data.DesignacaoIp, (curr, c) => curr.Replace(c, '_'));
            
            var fileName = $"Relatorio_SPARC_{cleanDesig}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
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
                // Fallback para arquivo HTML caso ocorra erro no processo
            }
        }

        // Se nenhum browser para PDF estiver disponível, salva o arquivo HTML direto como relatório
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

    public static string GenerateHtml(ActivationReportData d)
    {
        var cliente = SaipParser.CleanRazaoSocial(d.ClienteRazaoSocial) ?? "Não informado";
        var designacao = d.DesignacaoIp ?? d.NumeroOts ?? "Não informada";
        var ots = d.NumeroOts ?? "—";
        var pe = d.PeRouter ?? "—";
        var fw = d.FirmwareNome ?? "Padrão Flash";
        var wanIf = d.WanInterface ?? "GigabitEthernet 0/0";
        var wanGw = d.WanGateway ?? "—";
        var wanMask = d.WanSubnetMask ?? "—";
        var lanIf = d.LanInterface ?? "GigabitEthernet 0/1";
        var hostLan = d.HostLanIp ?? "—";
        var lanBlock = d.LanBlockNetwork ?? "—";
        var lanMask = d.LanSubnetMask ?? "—";
        var adapterName = d.AdaptadorRedeLocal ?? "Placa de Rede Ethernet";

        var is5a = d.IcmpResult?.IsLanOk == true;
        var is5b = d.IcmpResult?.IsWanOk == true;
        var is5c = d.IcmpResult?.IsWebOk == true;
        var isTelnet = d.TelnetResult?.IsSuccess == true;
        var isBand = d.BandResult?.IsSuccess == true;

        var is100PercentApproved = d.Step1ZerarOk && d.Step2FirmwareOk && d.Step3SaipOk && is5a && is5b && is5c && isTelnet && isBand && string.IsNullOrEmpty(d.FalhaGeral);
        var statusBadge = is100PercentApproved ? "🟢 100% HOMOLOGADO E APROVADO" : "🔴 NÃO HOMOLOGADO / REPROVADO";
        var statusColor = is100PercentApproved ? "#16A34A" : "#DC2626";

        var step1Badge = d.Step1ZerarOk ? "<span class=\"tag-ok\">✅ APROVADO</span>" : "<span class=\"tag-fail\">❌ FALHA</span>";
        var step2Badge = d.Step2FirmwareOk ? "<span class=\"tag-ok\">✅ APROVADO</span>" : "<span class=\"tag-fail\">❌ FALHA</span>";
        var step3Badge = d.Step3SaipOk ? "<span class=\"tag-ok\">✅ APROVADO</span>" : "<span class=\"tag-fail\">❌ FALHA</span>";
        var step4Badge = d.Step4IpLocalOk ? "<span class=\"tag-ok\">✅ APROVADO</span>" : "<span class=\"tag-warn\">⏭ PULADO</span>";
        var icmp5aBadge = is5a ? "<span class=\"tag-ok\">✅ RESPOSTA OK</span>" : "<span class=\"tag-fail\">❌ SEM RESPOSTA</span>";
        var icmp5bBadge = is5b ? "<span class=\"tag-ok\">✅ RESPOSTA OK</span>" : "<span class=\"tag-fail\">❌ SEM RESPOSTA</span>";
        var icmp5cBadge = is5c ? "<span class=\"tag-ok\">✅ RESPOSTA OK</span>" : "<span class=\"tag-fail\">❌ SEM RESPOSTA</span>";
        var telnetBadge = isTelnet ? "<span class=\"tag-ok\">✅ CONEXÃO OK</span>" : "<span class=\"tag-fail\">❌ FALHA</span>";
        var bandBadge = isBand ? "<span class=\"tag-ok\">✅ VAZÃO OK</span>" : "<span class=\"tag-warn\">⚠️ NÃO MEDIDO</span>";

        var rtt5a = d.IcmpResult?.LanResult?.AvgRttMs > 0 ? $"{d.IcmpResult.LanResult.AvgRttMs:F1} ms" : "< 1 ms";
        var rtt5b = d.IcmpResult?.WanResult?.AvgRttMs > 0 ? $"{d.IcmpResult.WanResult.AvgRttMs:F1} ms" : "—";
        var rtt5c = d.IcmpResult?.WebResult?.AvgRttMs > 0 ? $"{d.IcmpResult.WebResult.AvgRttMs:F1} ms" : "—";
        var loss5a = d.IcmpResult?.LanResult?.PacketLossPercentage ?? 0;
        var loss5b = d.IcmpResult?.WanResult?.PacketLossPercentage ?? (is5b ? 0 : 100);
        var loss5c = d.IcmpResult?.WebResult?.PacketLossPercentage ?? (is5c ? 0 : 100);
        var bandSpeed = d.BandResult != null ? $"{d.BandResult.DownloadMbps:F1} Mbps" : "—";

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
<meta charset=""utf-8"">
<title>Relatório de Homologação — {designacao}</title>
<style>
  @page {{ size: A4 portrait; margin: 12mm 15mm; }}
  body {{ font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif; color: #1E293B; margin: 0; padding: 0; background: #FFF; font-size: 13px; line-height: 1.45; }}
  .header {{ display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid #E2E8F0; padding-bottom: 12px; margin-bottom: 16px; }}
  .logo {{ font-size: 24px; font-weight: 800; color: #E11D48; letter-spacing: -0.5px; }}
  .logo span {{ color: #0F172A; }}
  .badge-status {{ display: inline-block; padding: 6px 14px; border-radius: 9999px; font-weight: 700; font-size: 12px; color: #FFF; background: {statusColor}; }}
  .section-title {{ font-size: 14px; font-weight: 700; color: #0F172A; text-transform: uppercase; letter-spacing: 0.5px; margin-top: 18px; margin-bottom: 8px; border-left: 4px solid #E11D48; padding-left: 8px; }}
  .grid-2 {{ display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }}
  .card {{ background: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 12px; }}
  .card-title {{ font-size: 11px; font-weight: 700; color: #64748B; text-transform: uppercase; margin-bottom: 6px; }}
  .field-row {{ display: flex; justify-content: space-between; margin-bottom: 4px; border-bottom: 1px dashed #E2E8F0; padding-bottom: 2px; }}
  .field-label {{ color: #64748B; font-weight: 500; }}
  .field-val {{ color: #0F172A; font-weight: 600; font-family: 'Consolas', monospace; }}
  table {{ width: 100%; border-collapse: collapse; margin-top: 6px; }}
  th {{ background: #0F172A; color: #FFF; font-weight: 600; font-size: 11px; text-align: left; padding: 6px 10px; }}
  td {{ padding: 6px 10px; border-bottom: 1px solid #E2E8F0; font-size: 12px; }}
  tr:nth-child(even) {{ background: #F8FAFC; }}
  .tag-ok {{ background: #DCFCE7; color: #166534; font-weight: 700; padding: 2px 8px; border-radius: 4px; display: inline-block; font-size: 11px; }}
  .tag-fail {{ background: #FEE2E2; color: #991B1B; font-weight: 700; padding: 2px 8px; border-radius: 4px; display: inline-block; font-size: 11px; }}
  .tag-warn {{ background: #FEF3C7; color: #92400E; font-weight: 700; padding: 2px 8px; border-radius: 4px; display: inline-block; font-size: 11px; }}
  .diag-box {{ background: #FFFBEB; border: 1px solid #FCD34D; border-radius: 8px; padding: 12px; margin-top: 14px; }}
  .diag-title {{ font-weight: 700; color: #B45309; margin-bottom: 6px; display: flex; align-items: center; gap: 6px; }}
  .diag-item {{ margin-bottom: 6px; font-size: 12px; color: #78350F; }}
  .footer-sign {{ display: grid; grid-template-columns: 1fr 1fr; gap: 30px; margin-top: 30px; padding-top: 15px; border-top: 1px solid #CBD5E1; }}
  .sign-line {{ border-top: 1px solid #94A3B8; margin-top: 35px; text-align: center; font-size: 11px; color: #475569; font-weight: 600; padding-top: 4px; }}
</style>
</head>
<body>

<div class=""header"">
  <div>
    <div class=""logo"">SPARC <span>| Relatório Técnico de Homologação</span></div>
    <div style=""font-size: 11px; color: #64748B; margin-top: 2px;"">Plataforma Automatizada de Ativação de Roteadores (Cisco & HPE)</div>
  </div>
  <div>
    <div class=""badge-status"">{statusBadge}</div>
  </div>
</div>

<div class=""grid-2"">
  <div class=""card"">
    <div class=""card-title"">Identificação do Circuito & Cliente</div>
    <div class=""field-row""><span class=""field-label"">Cliente:</span><span class=""field-val"">{cliente}</span></div>
    <div class=""field-row""><span class=""field-label"">Designação IP:</span><span class=""field-val"">{designacao}</span></div>
    <div class=""field-row""><span class=""field-label"">Número OTS:</span><span class=""field-val"">{ots}</span></div>
    <div class=""field-row""><span class=""field-label"">PE Router:</span><span class=""field-val"">{pe}</span></div>
  </div>

  <div class=""card"">
    <div class=""card-title"">Equipamento & Parâmetros Físicos</div>
    <div class=""field-row""><span class=""field-label"">Modelo:</span><span class=""field-val"">{d.ModeloEquipamento}</span></div>
    <div class=""field-row""><span class=""field-label"">Porta Serial:</span><span class=""field-val"">{d.PortaSerial} @ {d.BaudRate} bps</span></div>
    <div class=""field-row""><span class=""field-label"">Data / Hora:</span><span class=""field-val"">{d.DataHora:dd/MM/yyyy HH:mm:ss}</span></div>
    <div class=""field-row""><span class=""field-label"">Firmware / SO:</span><span class=""field-val"">{fw}</span></div>
  </div>
</div>

<div class=""section-title"">1. Endereçamento e Interfaces de Rede</div>
<div class=""grid-2"">
  <div class=""card"">
    <div class=""card-title"">Rede WAN (Operadora Claro)</div>
    <div class=""field-row""><span class=""field-label"">Interface WAN:</span><span class=""field-val"">{wanIf}</span></div>
    <div class=""field-row""><span class=""field-label"">IP WAN (Roteador):</span><span class=""field-val"">{d.WanIp}/{d.WanCidr}</span></div>
    <div class=""field-row""><span class=""field-label"">Gateway Claro:</span><span class=""field-val"">{wanGw}</span></div>
    <div class=""field-row""><span class=""field-label"">Máscara WAN:</span><span class=""field-val"">{wanMask}</span></div>
  </div>

  <div class=""card"">
    <div class=""card-title"">Rede LAN (Rede Local do Cliente)</div>
    <div class=""field-row""><span class=""field-label"">Interface LAN:</span><span class=""field-val"">{lanIf}</span></div>
    <div class=""field-row""><span class=""field-label"">IP LAN (Gateway Cliente):</span><span class=""field-val"">{d.LanIp}/{d.LanCidr}</span></div>
    <div class=""field-row""><span class=""field-label"">Host IP de Teste (PC):</span><span class=""field-val"">{hostLan}</span></div>
    <div class=""field-row""><span class=""field-label"">Bloco Alocado:</span><span class=""field-val"">{lanBlock}/{d.LanCidr}</span></div>
  </div>
</div>

<div class=""section-title"">2. Resultados das Baterias de Testes</div>
<table>
  <thead>
    <tr>
      <th>Etapa / Teste</th>
      <th>Alvo / Parâmetro</th>
      <th>Métricas Obtidas</th>
      <th>Resultado</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td><b>1. Zerar Configuração</b></td>
      <td>ROMMON / BootWare Reset</td>
      <td>Configuração limpa, senha zerada</td>
      <td>{step1Badge}</td>
    </tr>
    <tr>
      <td><b>2. Firmware & SO</b></td>
      <td>{fw}</td>
      <td>SO ativo e boot system gravado na Flash</td>
      <td>{step2Badge}</td>
    </tr>
    <tr>
      <td><b>3. Provisionamento SAIP</b></td>
      <td>Configuração Completa</td>
      <td>Rotas, Interfaces e Telnet aplicados</td>
      <td>{step3Badge}</td>
    </tr>
    <tr>
      <td><b>4. Configuração IP Teste</b></td>
      <td>{adapterName}</td>
      <td>IP: {hostLan} (DNS: 1.1.1.1, 8.8.8.8)</td>
      <td>{step4Badge}</td>
    </tr>
    <tr>
      <td><b>5a. ICMP LAN</b></td>
      <td>{d.LanIp} (Roteador)</td>
      <td>RTT Médio: {rtt5a} | Perda: {loss5a:F0}%</td>
      <td>{icmp5aBadge}</td>
    </tr>
    <tr>
      <td><b>5b. ICMP WAN</b></td>
      <td>{wanGw} (Gateway Claro)</td>
      <td>RTT Médio: {rtt5b} | Perda: {loss5b:F0}%</td>
      <td>{icmp5bBadge}</td>
    </tr>
    <tr>
      <td><b>5c. ICMP WEB</b></td>
      <td>1.1.1.1 / 8.8.8.8 (Internet)</td>
      <td>RTT Médio: {rtt5c} | Perda: {loss5c:F0}%</td>
      <td>{icmp5cBadge}</td>
    </tr>
    <tr>
      <td><b>6. Acesso Remoto (Telnet)</b></td>
      <td>Porta TCP 23 @ {d.LanIp}</td>
      <td>Usuário EBT (Privilege 15)</td>
      <td>{telnetBadge}</td>
    </tr>
    <tr>
      <td><b>7. Teste de Largura de Banda</b></td>
      <td>Vazão TCP / HTTP</td>
      <td>Velocidade Medida: {bandSpeed}</td>
      <td>{bandBadge}</td>
    </tr>
  </tbody>
</table>");

        if (d.DiagnosticAlerts != null && d.DiagnosticAlerts.Count > 0)
        {
            sb.Append(@"<div class=""diag-box""><div class=""diag-title"">⚠️ Diagnóstico de Causas & Ações Recomendadas</div>");
            foreach (var diag in d.DiagnosticAlerts)
            {
                sb.Append($@"<div class=""diag-item"">• {diag.Replace("\n", "<br>• ")}</div>");
            }
            sb.Append("</div>");
        }

        if (!string.IsNullOrWhiteSpace(d.AppliedConfigScript))
        {
            var esc = System.Net.WebUtility.HtmlEncode(d.AppliedConfigScript);
            sb.Append($@"<div class=""section-title"">3. Script / Running-Config Aplicado e Salvo (write memory)</div>
<div style=""background:#0F172A;color:#E2E8F0;border-radius:8px;padding:12px;font-family:'Consolas',monospace;font-size:11px;white-space:pre-wrap;word-break:break-all;max-height:420px;overflow:auto;border:1px solid #334155;"">{esc}</div>
<div style=""font-size:10px;color:#64748B;margin-top:4px;"">Configuração capturada via 'show running-config' após provisionamento e gravada com 'write memory'.</div>");
        }

        sb.Append(@"
<div class=""footer-sign"">
  <div>
    <div class=""sign-line"">TÉCNICO RESPONSÁVEL / HOMOLOGAÇÃO</div>
  </div>
  <div>
    <div class=""sign-line"">CONTROLE DE QUALIDADE / ATIVAÇÃO CLARO</div>
  </div>
</div>

</body>
</html>");

        return sb.ToString();
    }
}
