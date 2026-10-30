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
    string? AppliedConfigScript = null,
    double? BandaMbpsNominal = null,
    string? TipoServicoDescricao = null,
    string? MediaGatewayIp = null,
    string? PeLoopbackIp = null,
    string? TecnicoNome = null,
    string? TecnicoEmpresa = null,
    string? TecnicoMatricula = null,
    string? TecnicoCluster = null);

public sealed record DirectAccessReportData(
    DateTime DataHora,
    string? ClienteRazaoSocial,
    string? DesignacaoIp,
    string? NumeroOts,
    string? PeRouter,
    string? WanIp,
    int WanCidr,
    string? WanGateway,
    string? WanSubnetMask,
    double? BandaMbpsNominal,
    string? AdaptadorRedeLocal,
    ConnectivityTestResult? PeResult,
    ConnectivityTestResult? GoogleResult,
    ConnectivityTestResult? CloudflareResult,
    BandwidthTestResult? BandResult,
    IReadOnlyList<string>? DiagnosticAlerts = null,
    string? Observacoes = null,
    string? TecnicoNome = null,
    string? TecnicoEmpresa = null,
    string? TecnicoMatricula = null,
    string? TecnicoCluster = null);

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
        var tecInfo = !string.IsNullOrWhiteSpace(d.TecnicoNome)
            ? $"{d.TecnicoNome}" + (!string.IsNullOrWhiteSpace(d.TecnicoEmpresa) ? $" ({d.TecnicoEmpresa})" : "") + (!string.IsNullOrWhiteSpace(d.TecnicoMatricula) ? $" - Mat. {d.TecnicoMatricula}" : "")
            : "Técnico em Campo Claro";

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
        var isTacacs = d.TelnetResult?.IsSuccess == true &&
            (d.TelnetResult?.Banner?.Contains("TACACS", StringComparison.OrdinalIgnoreCase) == true ||
             d.TelnetResult?.Transcript?.Contains("TACACS", StringComparison.OrdinalIgnoreCase) == true ||
             d.TelnetResult?.Port == 22);
        var telnetBadge = isTelnet
            ? (isTacacs ? "<span class=\"tag-ok\">✅ TACACS+ (SSH OK)</span>" : "<span class=\"tag-ok\">✅ CONEXÃO OK</span>")
            : "<span class=\"tag-fail\">❌ FALHA</span>";
        var avBanda = BandwidthTestService.AvaliarBanda(d.BandaMbpsNominal, d.BandResult?.DownloadMbps ?? 0);
        var bandBadge = d.BandResult == null
            ? "<span class=\"tag-warn\">⚠️ NÃO MEDIDO</span>"
            : avBanda == null
                ? (isBand ? "<span class=\"tag-ok\">✅ VAZÃO OK</span>" : "<span class=\"tag-warn\">⚠️ NÃO MEDIDO</span>")
                : avBanda.Aprovado
                    ? $"<span class=\"tag-ok\">✅ APROVADO ({avBanda.Percentual:F1}%)</span>"
                    : $"<span class=\"tag-fail\">❌ REPROVADO ({avBanda.Percentual:F1}%)</span>";

        var rtt5a = d.IcmpResult?.LanResult?.AvgRttMs > 0 ? $"{d.IcmpResult.LanResult.AvgRttMs:F1} ms" : "< 1 ms";
        var rtt5b = d.IcmpResult?.WanResult?.AvgRttMs > 0 ? $"{d.IcmpResult.WanResult.AvgRttMs:F1} ms" : "—";
        var rtt5c = d.IcmpResult?.WebResult?.AvgRttMs > 0 ? $"{d.IcmpResult.WebResult.AvgRttMs:F1} ms" : "—";
        var loss5a = d.IcmpResult?.LanResult?.PacketLossPercentage ?? 0;
        var loss5b = d.IcmpResult?.WanResult?.PacketLossPercentage ?? (is5b ? 0 : 100);
        var loss5c = d.IcmpResult?.WebResult?.PacketLossPercentage ?? (is5c ? 0 : 100);
        var isVoz = d.TipoServicoDescricao?.Contains("VOZ", StringComparison.OrdinalIgnoreCase) == true;
        var isMpls = d.TipoServicoDescricao?.Contains("MPLS", StringComparison.OrdinalIgnoreCase) == true || d.TipoServicoDescricao?.Contains("VPN", StringComparison.OrdinalIgnoreCase) == true;
        var label5c = isVoz ? "5c. ICMP VOZ" : (isMpls ? "5c. ICMP MPLS" : "5c. ICMP WEB");
        var target5c = isVoz
            ? (!string.IsNullOrWhiteSpace(d.MediaGatewayIp) ? $"{d.MediaGatewayIp} (Media Gateway Claro)" : (!string.IsNullOrWhiteSpace(d.IcmpResult?.WebResult?.Target) ? $"{d.IcmpResult.WebResult.Target} (Media Gateway Claro)" : "Media Gateway / Softswitch"))
            : (isMpls
                ? (!string.IsNullOrWhiteSpace(d.PeLoopbackIp) ? $"{d.PeLoopbackIp} (Loopback PE Claro)" : (!string.IsNullOrWhiteSpace(d.IcmpResult?.WebResult?.Target) ? d.IcmpResult.WebResult.Target : $"{wanGw} (Core / PE Claro)"))
                : "1.1.1.1 / 8.8.8.8 (Internet)");

        var bandSpeed = d.BandResult != null
            ? $"{d.BandResult.DownloadMbps:F1} Mbps" +
              (d.BandaMbpsNominal is > 0
                  ? $" | Nominal: {d.BandaMbpsNominal:F1} Mbps ({(avBanda != null ? $"{avBanda.Percentual:F1}% — mín 92% ({avBanda.MinimoMbps:F1} Mbps)" : "—")})"
                  : "")
            : "—";

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
    <div class=""field-row""><span class=""field-label"">Técnico Resp.:</span><span class=""field-val"">{tecInfo}</span></div>
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
      <td><b>{label5c}</b></td>
      <td>{target5c}</td>
      <td>RTT Médio: {rtt5c} | Perda: {loss5c:F0}%</td>
      <td>{icmp5cBadge}</td>
    </tr>
    <tr>
      <td><b>{(isTacacs ? "6. Acesso Remoto (SSH / TACACS+)" : "6. Acesso Remoto (Telnet)")}</b></td>
      <td>Porta TCP {(d.TelnetResult?.Port ?? 23)} @ {d.LanIp}</td>
      <td>{(isTacacs ? "Telnet padrão desabilitado — Time CPE via TACACS" : "Usuário EBT (Privilege 15)")}</td>
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

        sb.Append($@"
<div class=""footer-sign"">
  <div>
    <div style=""text-align: center; font-size: 11px; color: #1E293B; font-weight: 700; margin-bottom: 2px;"">{tecInfo}</div>
    <div class=""sign-line"">TÉCNICO RESPONSÁVEL / HOMOLOGAÇÃO</div>
  </div>
  <div>
    <div style=""text-align: center; font-size: 11px; color: #1E293B; font-weight: 700; margin-bottom: 2px;"">Operadora Claro S.A.</div>
    <div class=""sign-line"">CONTROLE DE QUALIDADE / ATIVAÇÃO CLARO</div>
  </div>
</div>

</body>
</html>");

        return sb.ToString();
    }

    public static async Task<string> GenerateDirectAccessPdfAsync(
        DirectAccessReportData data,
        string? targetPdfPath = null,
        CancellationToken cancellationToken = default)
    {
        var htmlContent = GenerateDirectAccessHtml(data);
        var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"sparc_direct_report_{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(tempHtmlPath, htmlContent, Encoding.UTF8, cancellationToken);

        if (string.IsNullOrWhiteSpace(targetPdfPath))
        {
            var reportsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "Relatorios");
            if (!Directory.Exists(reportsDir))
            {
                Directory.CreateDirectory(reportsDir);
            }

            var cleanDesig = string.IsNullOrWhiteSpace(data.DesignacaoIp)
                ? "AcessoDireto"
                : Path.GetInvalidFileNameChars().Aggregate(data.DesignacaoIp, (curr, c) => curr.Replace(c, '_'));

            var fileName = $"Laudo_AcessoDireto_{cleanDesig}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
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
                // Fallback para HTML
            }
        }

        var fallbackHtmlPath = Path.ChangeExtension(targetPdfPath, ".html");
        File.Copy(tempHtmlPath, fallbackHtmlPath, true);
        return fallbackHtmlPath;
    }

    public static string GenerateDirectAccessHtml(DirectAccessReportData d)
    {
        var cliente = SaipParser.CleanRazaoSocial(d.ClienteRazaoSocial) ?? "Não informado";
        var designacao = d.DesignacaoIp ?? d.NumeroOts ?? "Não informada";
        var ots = d.NumeroOts ?? "—";
        var pe = d.PeRouter ?? "—";
        var wanGw = d.WanGateway ?? "—";
        var wanMask = d.WanSubnetMask ?? "—";
        var adapterName = d.AdaptadorRedeLocal ?? "Placa de Rede Ethernet";
        var tecInfo = !string.IsNullOrWhiteSpace(d.TecnicoNome)
            ? $"{d.TecnicoNome}" + (!string.IsNullOrWhiteSpace(d.TecnicoEmpresa) ? $" ({d.TecnicoEmpresa})" : "") + (!string.IsNullOrWhiteSpace(d.TecnicoMatricula) ? $" - Mat. {d.TecnicoMatricula}" : "")
            : "Técnico em Campo Claro";

        var isPeOk = d.PeResult?.IsSuccess == true;
        var isGoogleOk = d.GoogleResult?.IsSuccess == true;
        var isCloudflareOk = d.CloudflareResult?.IsSuccess == true;
        var isBandOk = d.BandResult?.IsSuccess == true;
        var avBanda = BandwidthTestService.AvaliarBanda(d.BandaMbpsNominal, d.BandResult?.DownloadMbps ?? 0);

        var is100PercentApproved = isPeOk && (isGoogleOk || isCloudflareOk) && (avBanda?.Aprovado ?? isBandOk);
        var statusBadge = is100PercentApproved ? "🟢 100% HOMOLOGADO E APROVADO" : "🔴 NÃO CONFORME / REPROVADO";
        var statusColor = is100PercentApproved ? "#16A34A" : "#DC2626";

        var peBadge = isPeOk ? "<span class=\"tag-ok\">✅ RESPOSTA OK</span>" : "<span class=\"tag-fail\">❌ SEM RESPOSTA</span>";
        var googleBadge = isGoogleOk ? "<span class=\"tag-ok\">✅ RESPOSTA OK</span>" : "<span class=\"tag-fail\">❌ SEM RESPOSTA</span>";
        var cfBadge = isCloudflareOk ? "<span class=\"tag-ok\">✅ RESPOSTA OK</span>" : "<span class=\"tag-fail\">❌ SEM RESPOSTA</span>";

        var bandBadge = d.BandResult == null
            ? "<span class=\"tag-warn\">⚠️ NÃO MEDIDO</span>"
            : avBanda == null
                ? (isBandOk ? "<span class=\"tag-ok\">✅ VAZÃO OK</span>" : "<span class=\"tag-warn\">⚠️ NÃO MEDIDO</span>")
                : avBanda.Aprovado
                    ? $"<span class=\"tag-ok\">✅ APROVADO ({avBanda.Percentual:F1}%)</span>"
                    : $"<span class=\"tag-fail\">❌ REPROVADO ({avBanda.Percentual:F1}%)</span>";

        var rttPe = d.PeResult?.AvgRttMs > 0 ? $"{d.PeResult.AvgRttMs:F1} ms" : "—";
        var rttGoogle = d.GoogleResult?.AvgRttMs > 0 ? $"{d.GoogleResult.AvgRttMs:F1} ms" : "—";
        var rttCf = d.CloudflareResult?.AvgRttMs > 0 ? $"{d.CloudflareResult.AvgRttMs:F1} ms" : "—";
        var lossPe = d.PeResult?.PacketLossPercentage ?? (isPeOk ? 0 : 100);
        var lossGoogle = d.GoogleResult?.PacketLossPercentage ?? (isGoogleOk ? 0 : 100);
        var lossCf = d.CloudflareResult?.PacketLossPercentage ?? (isCloudflareOk ? 0 : 100);

        var bandSpeed = d.BandResult != null
            ? $"{d.BandResult.DownloadMbps:F1} Mbps (Down) / {d.BandResult.UploadMbps:F1} Mbps (Up)" +
              (d.BandaMbpsNominal is > 0
                  ? $" | Nominal: {d.BandaMbpsNominal:F1} Mbps ({(avBanda != null ? $"{avBanda.Percentual:F1}% — mín 92% ({avBanda.MinimoMbps:F1} Mbps)" : "—")})"
                  : "")
            : "—";

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
<meta charset=""utf-8"">
<title>Laudo de Homologação de Link — {designacao}</title>
<style>
  @page {{ size: A4 portrait; margin: 12mm 15mm; }}
  body {{ font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif; color: #1E293B; margin: 0; padding: 0; background: #FFF; font-size: 13px; line-height: 1.45; }}
  .header {{ display: flex; justify-content: space-between; align-items: center; border-bottom: 2px solid #E2E8F0; padding-bottom: 12px; margin-bottom: 16px; }}
  .logo {{ font-size: 22px; font-weight: 800; color: #E11D48; letter-spacing: -0.5px; }}
  .logo span {{ color: #0F172A; }}
  .badge-status {{ display: inline-block; padding: 6px 14px; border-radius: 9999px; font-weight: 700; font-size: 12px; color: #FFF; background: {statusColor}; }}
  .section-title {{ font-size: 14px; font-weight: 700; color: #0F172A; text-transform: uppercase; letter-spacing: 0.5px; margin-top: 18px; margin-bottom: 8px; border-left: 4px solid #059669; padding-left: 8px; }}
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
    <div class=""logo"">SPARC <span>| Laudo Técnico de Link (Acesso Direto)</span></div>
    <div style=""font-size: 11px; color: #64748B; margin-top: 2px;"">Homologação de Entrega em Modo Access (EDD/GPON) — Claro / Embratel</div>
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
    <div class=""card-title"">Parâmetros de Teste & Conexão Física</div>
    <div class=""field-row""><span class=""field-label"">Modo de Entrega:</span><span class=""field-val"">Acesso Direto (Sem Roteador)</span></div>
    <div class=""field-row""><span class=""field-label"">Interface do Notebook:</span><span class=""field-val"">{adapterName}</span></div>
    <div class=""field-row""><span class=""field-label"">Data / Hora:</span><span class=""field-val"">{d.DataHora:dd/MM/yyyy HH:mm:ss}</span></div>
    <div class=""field-row""><span class=""field-label"">CIR Nominal (Banda):</span><span class=""field-val"">{(d.BandaMbpsNominal.HasValue ? $"{d.BandaMbpsNominal.Value:F0} Mbps" : "Não informado")}</span></div>
  </div>
</div>

<div class=""section-title"">1. Endereçamento WAN Direto (Placa do Notebook)</div>
<div class=""grid-2"">
  <div class=""card"">
    <div class=""card-title"">Endereço IP Atribuído ao Notebook</div>
    <div class=""field-row""><span class=""field-label"">IP WAN:</span><span class=""field-val"">{d.WanIp}/{d.WanCidr}</span></div>
    <div class=""field-row""><span class=""field-label"">Máscara:</span><span class=""field-val"">{wanMask}</span></div>
  </div>

  <div class=""card"">
    <div class=""card-title"">Gateway da Operadora (PE) & DNS</div>
    <div class=""field-row""><span class=""field-label"">Gateway PE:</span><span class=""field-val"">{wanGw}</span></div>
    <div class=""field-row""><span class=""field-label"">DNS Primário / Secundário:</span><span class=""field-val"">1.1.1.1 / 8.8.8.8</span></div>
  </div>
</div>

<div class=""section-title"">2. Resultados das Aferições de Conectividade e Banda</div>
<table>
  <thead>
    <tr>
      <th>Teste / Destino</th>
      <th>Endereço Alvo</th>
      <th>Métricas Obtidas (RTT / Perda / Jitter)</th>
      <th>Resultado</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td><b>ICMP Gateway PE (Operadora)</b></td>
      <td>{wanGw}</td>
      <td>RTT Médio: {rttPe} | Perda: {lossPe:F0}%</td>
      <td>{peBadge}</td>
    </tr>
    <tr>
      <td><b>ICMP Google DNS</b></td>
      <td>8.8.8.8</td>
      <td>RTT Médio: {rttGoogle} | Perda: {lossGoogle:F0}%</td>
      <td>{googleBadge}</td>
    </tr>
    <tr>
      <td><b>ICMP Cloudflare DNS</b></td>
      <td>1.1.1.1</td>
      <td>RTT Médio: {rttCf} | Perda: {lossCf:F0}%</td>
      <td>{cfBadge}</td>
    </tr>
    <tr>
      <td><b>Teste de Largura de Banda (Speedtest)</b></td>
      <td>Internet / CDN Pública</td>
      <td>Velocidade Medida: {bandSpeed}</td>
      <td>{bandBadge}</td>
    </tr>
  </tbody>
</table>");

        if (d.DiagnosticAlerts != null && d.DiagnosticAlerts.Count > 0)
        {
            sb.Append(@"<div class=""diag-box""><div class=""diag-title"">⚠️ Observações Técnicas & Alertas</div>");
            foreach (var diag in d.DiagnosticAlerts)
            {
                sb.Append($@"<div class=""diag-item"">• {diag.Replace("\n", "<br>• ")}</div>");
            }
            sb.Append("</div>");
        }

        sb.Append($@"
<div class=""footer-sign"">
  <div>
    <div style=""text-align: center; font-size: 11px; color: #1E293B; font-weight: 700; margin-bottom: 2px;"">{tecInfo}</div>
    <div class=""sign-line"">TÉCNICO RESPONSÁVEL / HOMOLOGAÇÃO</div>
  </div>
  <div>
    <div style=""text-align: center; font-size: 11px; color: #1E293B; font-weight: 700; margin-bottom: 2px;"">Operadora Claro S.A.</div>
    <div class=""sign-line"">RECEBIDO / CLIENTE OU FISCAL CLARO</div>
  </div>
</div>

</body>
</html>");

        return sb.ToString();
    }
}
