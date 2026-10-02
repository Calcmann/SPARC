using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetworkDevice.Core.Detection;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Firmware;

public sealed record WanDiagnosticsResult(
    string InterfaceName,
    bool IsPhysicalUp,
    bool HasInternet,
    string Details);

public sealed record RouterFirmwareStatus(
    bool IsWanReachable,
    string CurrentVersion,
    string? OfficialFirmwareName,
    long OfficialSizeBytes,
    bool IsCompliant,
    string Message);

/// <summary>
/// Executa a auditoria e o download de firmware diretamente pelo roteador via link WAN/Internet,
/// sem exigir que o notebook ou celular subam servidores locais TFTP/HTTP/FTP.
/// </summary>
public sealed class RouterDirectFirmwareUpdater
{
    public const string DefaultProxyBaseUrl = "http://sparc-firmware-proxy.calcmann.workers.dev/download";
    public const string DefaultProxyKey = "CR@PS";

    public static string BuildDirectDownloadUrl(DeviceSeries series, string? baseUrl = null, string? secretKey = null)
    {
        var modelSlug = series switch
        {
            DeviceSeries.Isr841 => "c841",
            DeviceSeries.Isr921 => "c921",
            DeviceSeries.Series1900 => "c1900",
            DeviceSeries.FortiGate40F => "fgt40f",
            DeviceSeries.Msr954 => "msr954",
            DeviceSeries.Msr930 => "msr930",
            DeviceSeries.Msr1002 => "msr1002",
            _ => "c841"
        };
        var baseUri = (baseUrl ?? DefaultProxyBaseUrl).TrimEnd('/');
        var key = secretKey ?? DefaultProxyKey;
        return $"{baseUri}/{modelSlug}?key={Uri.EscapeDataString(key)}";
    }

    private readonly Func<string, Task> _logger;

    public RouterDirectFirmwareUpdater(Func<string, Task> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Avaliador externo opcional plugado por camadas com dependências específicas (ex.: CiscoIOSFirmwareVersionInspector, FortiOsFirmwareVersionInspector).
    /// Assinatura: (series, rawDeviceOutput, targetFileName) => bool isSameVersion
    /// </summary>
    public static Func<DeviceSeries, string, string, bool>? ExternalComplianceEvaluator { get; set; }

    /// <summary>
    /// Audita a versão instalada no roteador comparando de forma estrita com a versão homologada na Nuvem.
    /// Previne falsos positivos (ex: M11 vs M12, 7.2.6 vs 7.2.11).
    /// </summary>
    public async Task<RouterFirmwareStatus> AuditComplianceAsync(
        DeviceSession session,
        DeviceSeries series,
        RemoteFirmwareInfo? officialRemote,
        CancellationToken ct = default)
    {
        await _logger("[*] [Auditoria] Verificando versão de firmware instalada no roteador...");

        string rawOutput = string.Empty;
        try
        {
            if (series is DeviceSeries.Msr930 or DeviceSeries.Msr954 or DeviceSeries.Msr1002)
            {
                rawOutput = await session.SendCommandAsync("display version", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            }
            else if (series == DeviceSeries.FortiGate40F)
            {
                rawOutput = await session.SendCommandAsync("get system status", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            }
            else
            {
                rawOutput = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await _logger($"[AVISO] Falha ao coletar dados de versão: {ex.Message}");
        }

        var currentVer = ExtractDisplayVersion(series, rawOutput);
        var officialName = officialRemote?.FileName;
        var officialSize = officialRemote?.SizeBytes ?? 0L;

        bool compliant = false;
        if (!string.IsNullOrWhiteSpace(officialName))
        {
            compliant = EvaluateComplianceStrict(series, rawOutput, currentVer, officialName, officialRemote?.DetectedVersion);
        }

        var reachable = await TestWanReachabilityAsync(session, series, ct).ConfigureAwait(false);

        var msg = compliant
            ? $"Equipamento em conformidade com a versão homologada ({officialName})."
            : $"Atualização recomendada. Versão atual: {currentVer} • Homologada: {officialName ?? "N/D"}";

        return new RouterFirmwareStatus(
            reachable,
            currentVer,
            officialName,
            officialSize,
            compliant,
            msg);
    }

    /// <summary>
    /// Avalia a conformidade de firmware de forma estrita, sem ambiguidades de substring.
    /// </summary>
    public static bool EvaluateComplianceStrict(
        DeviceSeries series,
        string rawDeviceOutput,
        string currentVer,
        string targetFileName,
        string? targetDetectedVer = null)
    {
        if (string.IsNullOrWhiteSpace(targetFileName))
            return false;

        var cleanTarget = Path.GetFileName(targetFileName).Trim();

        // 1. Avaliador externo plugado (ex.: CiscoIOSFirmwareVersionInspector / FortiOsFirmwareVersionInspector)
        if (ExternalComplianceEvaluator != null)
        {
            try
            {
                return ExternalComplianceEvaluator(series, rawDeviceOutput, cleanTarget);
            }
            catch
            {
                // Fallback para avaliação estrita interna
            }
        }

        // 2. Cisco IOS (ISR 841, ISR 921, 1900, etc.)
        if (series is DeviceSeries.Isr841 or DeviceSeries.Isr921 or DeviceSeries.Series1900)
        {
            return EvaluateCiscoStrict(rawDeviceOutput, currentVer, cleanTarget);
        }

        // 3. Fortinet FortiGate 40F
        if (series == DeviceSeries.FortiGate40F)
        {
            return EvaluateFortinetStrict(rawDeviceOutput, currentVer, cleanTarget);
        }

        // 4. HPE Comware
        if (series is DeviceSeries.Msr930 or DeviceSeries.Msr954 or DeviceSeries.Msr1002)
        {
            return EvaluateHpeCompliance(rawDeviceOutput, cleanTarget, currentVer);
        }

        // Genérico: nome exato do arquivo presente
        return cleanTarget.Equals(currentVer, StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(rawDeviceOutput) && rawDeviceOutput.Contains(cleanTarget, StringComparison.OrdinalIgnoreCase));
    }

    private static bool EvaluateCiscoStrict(string rawOutput, string currentVer, string targetFileName)
    {
        // Se a saída contém o System image file idêntico
        if (!string.IsNullOrWhiteSpace(rawOutput))
        {
            var imgMatch = Regex.Match(rawOutput, @"(?im)System\s+image\s+file\s+is\s+""(?:[^:\""]+:)?(?:\/)?(?<fileName>[^\""\r\n]+)""");
            if (imgMatch.Success)
            {
                var runningFile = Path.GetFileName(imgMatch.Groups["fileName"].Value.Trim());
                if (runningFile.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileNameWithoutExtension(runningFile).Equals(Path.GetFileNameWithoutExtension(targetFileName), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // Extrai versão canônica do alvo (ex: 159-3.M12 -> 15.9(3)M12)
        var targetCanonical = ExtractCiscoCanonical(targetFileName);
        var runningCanonical = ExtractCiscoCanonical(currentVer) ?? (!string.IsNullOrWhiteSpace(rawOutput) ? ExtractCiscoCanonical(rawOutput) : null);

        if (!string.IsNullOrWhiteSpace(targetCanonical) && !string.IsNullOrWhiteSpace(runningCanonical))
        {
            return string.Equals(targetCanonical, runningCanonical, StringComparison.OrdinalIgnoreCase);
        }

        // Se o nome do arquivo exato bate
        if (currentVer.Equals(targetFileName, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string? ExtractCiscoCanonical(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Formato 15.9(3)M12 ou 15.7(3)M9
        var m1 = Regex.Match(text, @"(?im)\b(?<ver>\d+\.\d+\(\d+\)[A-Za-z0-9]+)");
        if (m1.Success)
            return Regex.Replace(m1.Groups["ver"].Value, @"\s+", "").ToUpperInvariant();

        // Formato no binário: 159-3.M12 -> 15.9(3)M12
        var m2 = Regex.Match(text, @"(?i)(?<maj>\d{2,3})-(?<min>\d+)\.(?<trn>[A-Za-z0-9]+)");
        if (m2.Success)
        {
            var maj = m2.Groups["maj"].Value;
            var min = m2.Groups["min"].Value;
            var trn = m2.Groups["trn"].Value;
            if (maj.Length == 3)
            {
                return $"{maj[0]}{maj[1]}.{maj[2]}({min}){trn}".ToUpperInvariant();
            }
        }

        // Formato com ponto: 15.9-3.M12 -> 15.9(3)M12
        var m3 = Regex.Match(text, @"(?i)(?<maj>\d+)\.(?<submaj>\d+)-(?<min>\d+)\.(?<trn>[A-Za-z0-9]+)");
        if (m3.Success)
        {
            return $"{m3.Groups["maj"].Value}.{m3.Groups["submaj"].Value}({m3.Groups["min"].Value}){m3.Groups["trn"].Value}".ToUpperInvariant();
        }

        return null;
    }

    private static bool EvaluateFortinetStrict(string rawOutput, string currentVer, string targetFileName)
    {
        // Alvo: FGT_40F-v7.2.11.M-build1740-FORTINET.out
        var tgtVerMatch = Regex.Match(targetFileName, @"(?i)v?(?<ver>\d+\.\d+\.\d+)");
        var tgtBldMatch = Regex.Match(targetFileName, @"(?i)build(?<build>\d+)");

        var text = $"{currentVer} {rawOutput}";
        var curVerMatch = Regex.Match(text, @"(?i)v?(?<ver>\d+\.\d+\.\d+)");
        var curBldMatch = Regex.Match(text, @"(?i)build(?<build>\d+)");

        if (tgtVerMatch.Success && curVerMatch.Success)
        {
            if (!string.Equals(tgtVerMatch.Groups["ver"].Value, curVerMatch.Groups["ver"].Value, StringComparison.OrdinalIgnoreCase))
                return false;

            if (tgtBldMatch.Success && curBldMatch.Success)
            {
                if (int.TryParse(tgtBldMatch.Groups["build"].Value, out var tb) &&
                    int.TryParse(curBldMatch.Groups["build"].Value, out var cb))
                {
                    return tb == cb;
                }
            }
            return true;
        }

        return false;
    }

    public static bool EvaluateHpeCompliance(string rawOutput, string targetFileName, string? currentVer = null)
    {
        // Alvo: MSR954-CMW710-R6749P43.ipe
        var tgtRelMatch = Regex.Match(targetFileName, @"(?i)[-_]R(?<rel>\d{4}[A-Za-z0-9]+)");
        var text = $"{currentVer} {rawOutput}";
        var curRelMatch = Regex.Match(text, @"(?i)Release\s+(?<rel>\d{4}[A-Za-z0-9]+)|[-_]R(?<rel>\d{4}[A-Za-z0-9]+)");

        if (tgtRelMatch.Success && curRelMatch.Success)
        {
            var tgtRel = tgtRelMatch.Groups["rel"].Value.TrimStart('0');
            var curRel = curRelMatch.Groups["rel"].Value.TrimStart('0');
            return string.Equals(tgtRel, curRel, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(targetFileName) && !string.IsNullOrWhiteSpace(rawOutput))
        {
            return rawOutput.Contains(targetFileName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// Detecta a versão atual do sistema operacional no roteador via console serial.
    /// </summary>
    public static async Task<string> DetectCurrentVersionAsync(
        DeviceSession session,
        DeviceSeries series,
        CancellationToken ct = default)
    {
        try
        {
            if (series is DeviceSeries.Msr930 or DeviceSeries.Msr954 or DeviceSeries.Msr1002)
            {
                var resp = await session.SendCommandAsync("display version", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                return ExtractDisplayVersion(series, resp);
            }
            else if (series == DeviceSeries.FortiGate40F)
            {
                var resp = await session.SendCommandAsync("get system status", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                return ExtractDisplayVersion(series, resp);
            }
            else
            {
                // Cisco IOS
                var resp = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                return ExtractDisplayVersion(series, resp);
            }
        }
        catch
        {
            return "Não identificado";
        }
    }

    public static string ExtractDisplayVersion(DeviceSeries series, string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput)) return "Não identificado";

        if (series is DeviceSeries.Msr930 or DeviceSeries.Msr954 or DeviceSeries.Msr1002)
        {
            var match = Regex.Match(rawOutput, @"(?im)Comware\s+Software.*?(Release\s+[^\r\n]+)");
            if (match.Success) return match.Value.Trim();
            var bootMatch = Regex.Match(rawOutput, @"(?im)Boot\s+Image:\s*([^\r\n]+)");
            if (bootMatch.Success) return bootMatch.Groups[1].Value.Trim();
            return "HPE Comware";
        }
        else if (series == DeviceSeries.FortiGate40F)
        {
            var match = Regex.Match(rawOutput, @"(?im)Version:\s*([^\r\n]+)");
            if (match.Success) return match.Groups[1].Value.Trim();
            return "FortiOS";
        }
        else
        {
            // Cisco IOS
            string? fileName = null;
            var imgMatch = Regex.Match(rawOutput, @"(?im)System\s+image\s+file\s+is\s+""(?:[^:\""]+:)?(?:\/)?(?<fileName>[^\""\r\n]+)""");
            if (imgMatch.Success)
            {
                fileName = Path.GetFileName(imgMatch.Groups["fileName"].Value.Trim());
            }

            string? ver = null;
            var verMatch = Regex.Match(rawOutput, @"(?im)Cisco\s+IOS\s+Software.*?(?:Version\s+|,\s*Version\s*)(?<ver>[0-9]+\.[0-9]+\([0-9]+\)[A-Za-z0-9]+)");
            if (verMatch.Success)
            {
                ver = verMatch.Groups["ver"].Value.Trim();
            }
            else
            {
                var genMatch = Regex.Match(rawOutput, @"(?im)\bVersion\s+(?<ver>[0-9]+\.[0-9]+\([0-9]+\)[A-Za-z0-9]+)");
                if (genMatch.Success) ver = genMatch.Groups["ver"].Value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(ver) && !string.IsNullOrWhiteSpace(fileName))
                return $"{ver} ({fileName})";
            if (!string.IsNullOrWhiteSpace(ver))
                return ver;
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;

            return "Cisco IOS";
        }
    }

    /// <summary>
    /// Testa se o roteador tem alcance à WAN (pingando o gateway default ou DNS).
    /// </summary>
    public async Task<bool> TestWanReachabilityAsync(
        DeviceSession session,
        DeviceSeries series,
        CancellationToken ct = default)
    {
        try
        {
            await _logger("[*] Testando conectividade WAN do roteador...");
            string pingCmd = series == DeviceSeries.FortiGate40F
                ? "execute ping-options count 3\nexecute ping 8.8.8.8"
                : "ping 8.8.8.8 repeat 3";

            var resp = await session.SendCommandAsync(pingCmd, TimeSpan.FromSeconds(12), ct).ConfigureAwait(false);
            bool pingSuccess = resp.Contains("!") ||
                               (resp.Contains("Success rate is") && !resp.Contains("Success rate is 0 percent")) ||
                               (resp.Contains("packet loss") && !resp.Contains("100% packet loss") && !resp.Contains("100.0% packet loss") && (resp.Contains("0% packet loss") || resp.Contains("0.0% packet loss"))) ||
                               resp.Contains("bytes from 8.8.8.8");

            if (pingSuccess)
            {
                await _logger("[✓] Conectividade WAN confirmada no roteador!");
                return true;
            }

            await _logger("[AVISO] Ping externo não respondeu (pode ser restrição de ICMP ou link WAN ainda sincronizando).");
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Diagnostica o status físico da porta WAN e a conectividade com a internet a partir do roteador.
    /// </summary>
    public async Task<WanDiagnosticsResult> CheckWanAndInternetAsync(
        DeviceSession session,
        DeviceSeries series,
        CancellationToken ct = default)
    {
        var wanInfo = WanPortInspector.ObterPorSerie(series)
                      ?? WanPortInspector.PorTagModelo(series.ToString())
                      ?? new WanPortaInfo("Genérico", "WAN", new[] { "wan", "gigabitethernet0/4", "gigabitethernet4", "ge0/0" }, false);

        await _logger($"[*] Verificando status físico da porta WAN ({wanInfo.InterfaceExibicao})...");

        bool physicalUp = false;
        try
        {
            if (wanInfo.IsForti || series == DeviceSeries.FortiGate40F)
            {
                var resp = await session.SendCommandAsync("get system interface physical", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                var (found, down) = WanPortInspector.FortiWanStatus(resp, wanInfo.NomesBusca);
                physicalUp = found && !down;
            }
            else if (wanInfo.IsHpe || series is DeviceSeries.Msr930 or DeviceSeries.Msr954 or DeviceSeries.Msr1002)
            {
                var resp = await session.SendCommandAsync("display interface brief", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                var (found, down) = WanPortInspector.HpeWanStatus(resp, wanInfo.NomesBusca);
                physicalUp = found && !down;
            }
            else
            {
                // Cisco IOS
                var resp = await session.SendCommandAsync("show ip interface brief", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                var (found, down) = WanPortInspector.CiscoWanStatus(resp, wanInfo.NomesBusca);
                physicalUp = found && !down;
            }
        }
        catch (Exception ex)
        {
            await _logger($"[AVISO] Falha ao consultar status da interface WAN: {ex.Message}");
        }

        if (!physicalUp)
        {
            await _logger($"[!] Porta WAN {wanInfo.InterfaceExibicao} está DOWN (sem link físico conectado).");
            return new WanDiagnosticsResult(wanInfo.InterfaceExibicao, false, false, $"Porta WAN {wanInfo.InterfaceExibicao} está DOWN (sem link físico).");
        }

        await _logger($"[✓] Porta WAN {wanInfo.InterfaceExibicao} está UP (Link físico ativo).");
        var hasInternet = await TestWanReachabilityAsync(session, series, ct).ConfigureAwait(false);

        var details = hasInternet
            ? $"Porta WAN {wanInfo.InterfaceExibicao} UP e Conectividade Internet OK."
            : $"Porta WAN {wanInfo.InterfaceExibicao} UP, mas sem conectividade com a Internet.";

        return new WanDiagnosticsResult(wanInfo.InterfaceExibicao, true, hasInternet, details);
    }

    /// <summary>
    /// Dispara o download do arquivo de firmware diretamente pelo roteador via link WAN/Internet.
    /// Suporta Cisco IOS, HPE Comware e Fortinet.
    /// </summary>
    public async Task<bool> TriggerDownloadOnRouterAsync(
        DeviceSession session,
        DeviceSeries series,
        string firmwareUrl,
        string firmwareFileName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(firmwareUrl) || string.IsNullOrWhiteSpace(firmwareFileName))
        {
            throw new ArgumentException("URL de download e nome do firmware são obrigatórios.");
        }

        await _logger($"[*] ================================================================");
        await _logger($"[*] INICIANDO DOWNLOAD DO FIRMWARE DIRETAMENTE PELO ROTEADOR (WAN) ");
        await _logger($"[*] Arquivo: {firmwareFileName}");
        await _logger($"[*] Origem: {firmwareUrl}");
        await _logger($"[*] ================================================================");

        if (series == DeviceSeries.FortiGate40F)
        {
            return await DownloadFortinetAsync(session, firmwareUrl, firmwareFileName, ct).ConfigureAwait(false);
        }
        else if (series is DeviceSeries.Msr930 or DeviceSeries.Msr954 or DeviceSeries.Msr1002)
        {
            return await DownloadHpeAsync(session, firmwareUrl, firmwareFileName, ct).ConfigureAwait(false);
        }
        else
        {
            return await DownloadCiscoAsync(session, firmwareUrl, firmwareFileName, ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> DownloadCiscoAsync(
        DeviceSession session,
        string firmwareUrl,
        string firmwareFileName,
        CancellationToken ct)
    {
        // 1. Verifica espaço livre na flash
        await _logger("[*] Verificando espaço disponível na flash do Cisco...");
        try
        {
            var dirResp = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            await _logger($"    {dirResp.Trim()}");
        }
        catch { }

        // 2. Dispara a cópia via comando copy
        var copyCmd = $"copy {firmwareUrl} flash:{firmwareFileName}";
        await _logger($"[*] Enviando comando de cópia: {copyCmd}");

        // No Cisco IOS, copy solicita confirmação de 'Destination filename'
        var resp = await session.SendCommandAsync(copyCmd, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        if (resp.Contains("Destination filename") || resp.Contains("filename"))
        {
            await _logger("[*] Confirmando nome de destino na flash...");
            resp = await session.SendCommandAsync("", TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
        }

        if (resp.Contains("[OK") || resp.Contains("bytes copied") || resp.Contains("!"))
        {
            await _logger($"[✓] Download concluído com sucesso na flash:{firmwareFileName}!");

            // 3. Validação do MD5
            await _logger("[*] Validando integridade na flash (verify /md5)...");
            try
            {
                var verifyResp = await session.SendCommandAsync($"verify /md5 flash:{firmwareFileName}", TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
                await _logger($"    Resultado: {verifyResp.Trim()}");
            }
            catch { }

            // 4. Configurar boot system e salvar
            await _logger("[*] Atualizando comando de boot na configuração de inicialização...");
            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            await session.SendCommandAsync($"boot system flash:{firmwareFileName}", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

            await _logger("[✓] Firmware gravado e definido como padrão para o próximo boot!");
            return true;
        }

        await _logger($"[!] Resposta do roteador: {resp}");
        return false;
    }

    private async Task<bool> DownloadHpeAsync(
        DeviceSession session,
        string firmwareUrl,
        string firmwareFileName,
        CancellationToken ct)
    {
        await _logger("[*] Verificando arquivos na flash do HPE...");
        try
        {
            var dirResp = await session.SendCommandAsync("dir", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            await _logger($"    {dirResp.Trim()}");
        }
        catch { }

        await _logger($"[*] Disparando download Comware para flash:/{firmwareFileName}...");
        // Em redes WAN com HTTP/FTP, Comware suporta cliente FTP/TFTP nativo
        var resp = await session.SendCommandAsync($"tftp {firmwareUrl} get {firmwareFileName} flash:/{firmwareFileName}", TimeSpan.FromMinutes(8), ct).ConfigureAwait(false);

        if (resp.Contains("100%") || resp.Contains("File copied successfully") || !resp.Contains("Failed"))
        {
            await _logger("[✓] Download concluído! Configurando boot-loader...");
            await session.SendCommandAsync($"boot-loader file flash:/{firmwareFileName} main", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            await session.SendCommandAsync("save force", TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            await _logger("[✓] HPE Comware configurado com a nova imagem para o próximo boot!");
            return true;
        }

        await _logger($"[!] Falha na transferência: {resp}");
        return false;
    }

    private async Task<bool> DownloadFortinetAsync(
        DeviceSession session,
        string firmwareUrl,
        string firmwareFileName,
        CancellationToken ct)
    {
        await _logger($"[*] Solicitando restauração de imagem oficial no FortiGate...");
        var cmd = $"execute restore image tftp {firmwareFileName} {firmwareUrl}";
        var resp = await session.SendCommandAsync(cmd, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);

        if (resp.Contains("Do you want to continue? (y/n)"))
        {
            resp = await session.SendCommandAsync("y", TimeSpan.FromMinutes(8), ct).ConfigureAwait(false);
        }

        await _logger($"[*] Resposta do FortiGate: {resp.Trim()}");
        return true;
    }
}
