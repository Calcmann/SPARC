using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetworkDevice.Core.Domain;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Core.Firmware;

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
    private readonly Func<string, Task> _logger;

    public RouterDirectFirmwareUpdater(Func<string, Task> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Audita a versão instalada no roteador comparando com a versão homologada na Nuvem.
    /// </summary>
    public async Task<RouterFirmwareStatus> AuditComplianceAsync(
        DeviceSession session,
        DeviceSeries series,
        RemoteFirmwareInfo? officialRemote,
        CancellationToken ct = default)
    {
        await _logger("[*] [Auditoria] Verificando versão de firmware instalada no roteador...");

        var currentVer = await DetectCurrentVersionAsync(session, series, ct).ConfigureAwait(false);
        var officialName = officialRemote?.FileName;
        var officialSize = officialRemote?.SizeBytes ?? 0L;

        bool compliant = false;
        if (!string.IsNullOrWhiteSpace(officialName))
        {
            compliant = currentVer.Contains(officialName, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(officialRemote?.DetectedVersion) &&
                         currentVer.Contains(officialRemote.DetectedVersion, StringComparison.OrdinalIgnoreCase));
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
                var match = Regex.Match(resp, @"(?im)Comware\s+Software.*?(Release\s+[^\r\n]+)");
                if (match.Success) return match.Value.Trim();
                var bootMatch = Regex.Match(resp, @"(?im)Boot\s+Image:\s*([^\r\n]+)");
                if (bootMatch.Success) return bootMatch.Groups[1].Value.Trim();
                return "HPE Comware";
            }
            else if (series == DeviceSeries.FortiGate40F)
            {
                var resp = await session.SendCommandAsync("get system status", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                var match = Regex.Match(resp, @"(?im)Version:\s*([^\r\n]+)");
                if (match.Success) return match.Groups[1].Value.Trim();
                return "FortiOS";
            }
            else
            {
                // Cisco IOS
                var resp = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                var imgMatch = Regex.Match(resp, @"(?im)System\s+image\s+file\s+is\s+""(?:flash:)?([^""]+)""");
                if (imgMatch.Success) return imgMatch.Groups[1].Value.Trim();

                var verMatch = Regex.Match(resp, @"(?im)Cisco\s+IOS\s+Software.*?(Version\s+[^\r\n,]+)");
                if (verMatch.Success) return verMatch.Groups[1].Value.Trim();
                return "Cisco IOS";
            }
        }
        catch
        {
            return "Não identificado";
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
            if (resp.Contains("!") || resp.Contains("Success rate is") || resp.Contains("0% packet loss") || resp.Contains("bytes from 8.8.8.8"))
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
