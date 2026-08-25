using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Tftp;

namespace NetworkDevice.Cisco;

public sealed class CiscoIOSUpgrader
{
    private static readonly Regex PromptConfirmRegex = new(
        @"(?i)(?:Address or name of remote host|Source filename|Destination filename|erase flash|over-write|continue\?|\[confirm\]|\?)\s*$",
        RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;
    private readonly Action<int, string, string>? _onProgress;

    public CiscoIOSUpgrader(
        Func<string, Task>? progress = null,
        Action<int, string, string>? onProgress = null)
    {
        _progress = progress;
        _onProgress = onProgress;
    }

    /// <summary>
    /// Executa o upgrade completo do Cisco IOS configurando IP temporário na LAN, copiando a imagem via TFTP, configurando o boot system e executando reload automático.
    /// </summary>
    public async Task<bool> UpgradeAsync(
        DeviceSession session,
        string imageFilePath,
        string hostIpAddress,
        string? routerIpAddress = null,
        string? subnetMask = null,
        string? lanInterface = null,
        string? expectedMd5 = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imageFilePath))
            throw new FileNotFoundException($"Arquivo de imagem IOS não encontrado: {imageFilePath}");

        var binFileName = Path.GetFileName(imageFilePath);
        var imageDir = Path.GetDirectoryName(imageFilePath) ?? AppContext.BaseDirectory;
        var fileSize = new FileInfo(imageFilePath).Length;
        var sizeMb = (fileSize / (1024.0 * 1024.0)).ToString("N1");

        await ProgressAsync($"[*] INICIANDO UPGRADE DE IOS ({binFileName} — {sizeMb} MB)...");

        // 1. Garante modo privilegiado (#) e desativa paginação
        try
        {
            var adapter = new CiscoIOSAdapter();
            await adapter.EnterPrivilegedExecAsync(session, cancellationToken);
        }
        catch { }

        try
        {
            await session.SendCommandAsync("enable", TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch { }

        try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), cancellationToken); } catch { }
        try { await session.SendCommandAsync("terminal width 512", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

        // 2. Consulta estado atual do equipamento (Versão em execução, Arquivos na Flash e Boot System)
        var showVer = "";
        try { showVer = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(15), cancellationToken); } catch { }

        var dirFlash = "";
        try { dirFlash = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken); } catch { }

        var showBoot = "";
        try { showBoot = await session.SendCommandAsync("show running-config | include boot", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
        if (string.IsNullOrWhiteSpace(showBoot))
        {
            try { showBoot = await session.SendCommandAsync("show boot", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
        }

        var isRunningTarget = IsCiscoRunningImage(showVer, binFileName);
        var isFileOnFlash = dirFlash.Contains(binFileName, StringComparison.OrdinalIgnoreCase) ||
                           dirFlash.Contains(Path.GetFileNameWithoutExtension(binFileName), StringComparison.OrdinalIgnoreCase);
        var isBootConfigured = IsBootSystemConfigured(showBoot, binFileName);

        // CASO A: O roteador JÁ está executando a imagem alvo
        if (isRunningTarget)
        {
            await ProgressAsync($"\n=================================================================");
            await ProgressAsync($"   IMAGEM {binFileName} JÁ ATIVA NO CISCO IOS                    ");
            await ProgressAsync("=================================================================");
            await ProgressAsync($"  O roteador Cisco já está executando a imagem alvo.");
            await ProgressAsync($"  Imagem em execução : {binFileName}");

            // Se o boot system não estiver explicitamente salvo, garante sem reiniciar
            if (!isBootConfigured && isFileOnFlash)
            {
                await ProgressAsync($"[*] Gravando boot system persistente para 'flash:{binFileName}'...");
                await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync($"boot system flash:{binFileName}", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync("config-register 0x2102", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(30), cancellationToken);
            }

            await ProgressAsync($"  -> Pulando cópia TFTP e reinicialização (100% economia de tempo).");
            await ProgressAsync("=================================================================\n");
            _onProgress?.Invoke(100, "Fase B: Firmware OK", $"Equipamento já executa {binFileName}.");
            return true;
        }

        // CASO B: A imagem já existe na Flash e o boot system já aponta para ela
        if (isFileOnFlash && isBootConfigured)
        {
            await ProgressAsync($"\n[*] [INFO] A imagem {binFileName} já existe na Flash e o boot system já está configurado!");
            await ProgressAsync($"[*] [RELOAD AUTOMÁTICO] Reiniciando roteador Cisco para carregar {binFileName}...");
            _onProgress?.Invoke(90, "Fase B: Reiniciando Equipamento...", $"Boot system configurado com {binFileName}. Reiniciando...");
            await ExecutarReloadCiscoAsync(session, cancellationToken);
            _onProgress?.Invoke(100, "Fase B Concluída!", $"Roteador reiniciado com {binFileName}.");
            return true;
        }

        // CASO C: A imagem já existe na Flash, mas o boot system precisa ser configurado
        if (isFileOnFlash)
        {
            await ProgressAsync($"\n[*] [INFO] O arquivo {binFileName} ({sizeMb} MB) já existe na memória Flash do roteador!");
            await ProgressAsync($"    -> Pulando cópia TFTP ({sizeMb} MB) e avançando diretamente para configuração de boot system e reload.");
        }
        else
        {
            // CASO D: Arquivo NÃO existe na Flash -> Realiza transferência via Servidor TFTP Integrado
                await using var tftpServer = new EmbeddedTftpServer(imageDir);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var lastUiUpdate = DateTime.MinValue;
                var lastLoggedPct = -1;

                tftpServer.TransferProgress += (file, sent, total, pct) =>
                {
                    var now = DateTime.UtcNow;
                    var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                    var sentMb = sent / (1024.0 * 1024.0);
                    var totalMb = total / (1024.0 * 1024.0);
                    var speedMbSec = elapsedSec > 0.5 ? sentMb / elapsedSec : 0;
                    var remainingSec = speedMbSec > 0 ? (totalMb - sentMb) / speedMbSec : 0;
                    var etaStr = remainingSec > 0 ? $" | Restam ~{TimeSpan.FromSeconds(remainingSec):mm\\:ss}" : "";

                    var uiPct = (int)Math.Clamp(22 + (pct * 0.2), 22, 42);

                    if ((now - lastUiUpdate).TotalMilliseconds >= 250 || pct >= 100)
                    {
                        lastUiUpdate = now;
                        _onProgress?.Invoke(
                            uiPct,
                            $"Transferindo IOS TFTP ({pct:N1}%)...",
                            $"{sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) — {speedMbSec:N1} MB/s{etaStr}");
                    }

                    var step = (int)(pct / 5) * 5;
                    if (step > lastLoggedPct)
                    {
                        lastLoggedPct = step;
                        var barLength = 20;
                        var filled = (int)Math.Round((pct / 100.0) * barLength);
                        var bar = new string('█', Math.Clamp(filled, 0, barLength)) + new string('░', Math.Max(0, barLength - filled));
                        _progress?.Invoke($"    -> [TFTP] [{bar}] {sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) | {speedMbSec:N1} MB/s{etaStr}");
                    }
                };
                tftpServer.LogMessage += msg =>
                {
                    _progress?.Invoke(msg);
                };
                tftpServer.Start();

                try
                {
                    // Configura temporariamente a interface LAN no roteador Cisco para ter IP e rota para o PC
                    var lanIf = lanInterface ?? "GigabitEthernet 0/1";
                    var rIp = routerIpAddress ?? "200.182.245.17";
                    var mask = subnetMask ?? "255.255.255.240";

                    await ProgressAsync($"[*] Configurando temporariamente {lanIf} ({rIp} {mask}) no Cisco para viabilizar transferência TFTP...");
                    await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
                    await session.SendCommandAsync($"interface {lanIf}", TimeSpan.FromSeconds(10), cancellationToken);
                    await session.SendCommandAsync($"ip address {rIp} {mask}", TimeSpan.FromSeconds(10), cancellationToken);
                    await session.SendCommandAsync("no shutdown", TimeSpan.FromSeconds(10), cancellationToken);
                    await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
                    await Task.Delay(2000, cancellationToken);

                    // Testa conectividade IP com o PC (ping)
                    await ProgressAsync($"[*] Testando conectividade de rede com o PC ({hostIpAddress})...");
                    var pingRes = await session.SendCommandAsync($"ping {hostIpAddress} repeat 4", TimeSpan.FromSeconds(15), cancellationToken);
                    if (pingRes.Contains("!"))
                    {
                        await ProgressAsync($"[OK] Conectividade IP com o PC ({hostIpAddress}) confirmada.");
                    }
                    else
                    {
                        await ProgressAsync($"[AVISO] Ping para o PC ({hostIpAddress}) ainda sem resposta. Prosseguindo com TFTP...");
                    }

                    // Envia o comando de cópia TFTP para a flash
                    await ProgressAsync($"[*] Solicitando cópia TFTP: copy tftp://{hostIpAddress}/{binFileName} flash:{binFileName}...");
                    var copyCmd = $"copy tftp://{hostIpAddress}/{binFileName} flash:{binFileName}";

                    await session.WriteLineAsync(copyCmd, cancellationToken);

                    // Responde as perguntas de confirmação do Cisco IOS e monitora transferência
                    var copyTimeout = DateTime.UtcNow.AddMinutes(15);
                    var isCopying = true;
                    var fullOutput = new System.Text.StringBuilder();

                    while (isCopying && DateTime.UtcNow < copyTimeout && !cancellationToken.IsCancellationRequested)
                    {
                        var conds = new StopCondition[]
                        {
                            new StopCondition.LineRegex("confirm", PromptConfirmRegex),
                            new StopCondition.LineRegex("prompt", new Regex(@"^[A-Za-z0-9_\-\.]+\s*[>#]"))
                        };

                        var exp = await session.SendExpectAsync(string.Empty, conds, TimeSpan.FromMinutes(2), cancellationToken);
                        fullOutput.Append(exp.Output);

                        if (exp.Matched is StopCondition.LineRegex lr)
                        {
                            if (lr.Name == "confirm")
                            {
                                await session.WriteLineAsync(string.Empty, cancellationToken);
                            }
                            else if (lr.Name == "prompt")
                            {
                                isCopying = false;
                            }
                        }
                    }

                    var copyResultText = fullOutput.ToString();
                    if (copyResultText.Contains("% Error") || copyResultText.Contains("Timed out") || copyResultText.Contains("No route to host"))
                    {
                        throw new DeviceSessionException($"Falha na cópia TFTP da imagem {binFileName}. Resposta: {copyResultText.Trim()}");
                    }
                }
                finally
                {
                    await tftpServer.StopAsync();
                }
            }

            // 6. Confirma se o arquivo está na memória Flash
            dirFlash = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken);
            if (!dirFlash.Contains(binFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new DeviceSessionException($"Arquivo {binFileName} não foi localizado na flash: após a transferência.");
            }

            await ProgressAsync($"[OK] Cópia TFTP de {binFileName} ({sizeMb} MB) concluída e validada na flash: com sucesso!");

            // 7. Validação MD5 (se fornecido)
            if (!string.IsNullOrWhiteSpace(expectedMd5))
            {
                await ProgressAsync($"[*] Verificando integridade MD5 da imagem na flash (pode levar 1-2 minutos)...");
                var md5Output = await session.SendCommandAsync($"verify /md5 flash:{binFileName}", TimeSpan.FromMinutes(3), cancellationToken);
                if (md5Output.Contains(expectedMd5.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"[OK] Checksum MD5 verificado com sucesso ({expectedMd5}).");
                }
                else
                {
                    await ProgressAsync($"[AVISO] Checksum MD5 calculado:\n{md5Output}");
                }
            }

            // 8. Configura o boot system com a nova imagem e registra 0x2102
            await ProgressAsync($"[*] Configurando boot system para 'flash:{binFileName}'...");
            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(500, cancellationToken);

            await session.SendCommandAsync("no boot system", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(500, cancellationToken);

            await session.SendCommandAsync($"boot system flash:{binFileName}", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(500, cancellationToken);

            await session.SendCommandAsync("config-register 0x2102", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(500, cancellationToken);

            await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(500, cancellationToken);

            await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(30), cancellationToken);
            await ProgressAsync($"[*] Boot system configurado e salvo com sucesso.");

            // 9. Reload automático
            await ProgressAsync($"\n[*] [RELOAD AUTOMÁTICO] Reiniciando roteador Cisco para inicializar com {binFileName}...");
            await ExecutarReloadCiscoAsync(session, cancellationToken);
            await ProgressAsync($"[OK] Comando de reinicialização enviado ao Cisco IOS!");

            return true;
    }

    private async Task ExecutarReloadCiscoAsync(DeviceSession session, CancellationToken ct)
    {
        try
        {
            await session.SendExpectAsync(
                "reload",
                new StopCondition[]
                {
                    new StopCondition.Contains("Proceed with reload? [confirm]", "Proceed with reload? [confirm]"),
                    new StopCondition.Contains("[confirm]", "[confirm]"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(10),
                ct);

            await session.WriteLineAsync(string.Empty, ct);
        }
        catch { }

        // Monitora o boot completo e envia Enter / responde diálogos iniciais
        var bootTimeout = DateTime.UtcNow.AddSeconds(160);
        var lastStatusLog = DateTime.MinValue;

        while (DateTime.UtcNow < bootTimeout && !ct.IsCancellationRequested)
        {
            var remainingSec = (int)Math.Max(0, (bootTimeout - DateTime.UtcNow).TotalSeconds);
            if ((DateTime.UtcNow - lastStatusLog).TotalSeconds >= 10)
            {
                lastStatusLog = DateTime.UtcNow;
                await ProgressAsync($"[*] Aguardando boot da nova versão Cisco IOS (~{remainingSec}s max)...");
            }

            // Envia Enter periódico para acordar console e forçar redesenho de prompt
            await session.WriteLineAsync(string.Empty, ct);

            try
            {
                var result = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("dialog", new Regex(@"(?i)initial\s+configuration\s+dialog|\?\s*\[yes/no\]|\[yes\]")),
                        new StopCondition.LineRegex("autoinstall", new Regex(@"(?i)terminate\s+autoinstall")),
                        new StopCondition.LineRegex("press-return", new Regex(@"(?i)press\s+return\s+to\s+get\s+started|press\s+enter")),
                        new StopCondition.LineRegex("cisco-prompt", new Regex(@"(?i)^[A-Za-z0-9_.+()/-]+[>#]")),
                        new StopCondition.Prompt()
                    },
                    TimeSpan.FromSeconds(4),
                    ct);

                if (result.Matched is StopCondition.LineRegex lr)
                {
                    if (lr.Name == "dialog")
                    {
                        await ProgressAsync("[*] Diálogo de configuração inicial detectado — enviando 'no'...");
                        await session.WriteLineAsync("no", ct);
                        await Task.Delay(1000, ct);
                    }
                    else if (lr.Name == "autoinstall")
                    {
                        await ProgressAsync("[*] Diálogo autoinstall detectado — enviando 'yes'...");
                        await session.WriteLineAsync("yes", ct);
                        await Task.Delay(1000, ct);
                    }
                    else if (lr.Name == "press-return")
                    {
                        await ProgressAsync("[*] 'Press RETURN to get started' detectado — enviando ENTER...");
                        await session.WriteLineAsync(string.Empty, ct);
                        await Task.Delay(1000, ct);
                    }
                    else if (lr.Name == "cisco-prompt" || result.Matched is StopCondition.Prompt)
                    {
                        await ProgressAsync("[OK] Cisco IOS reinicializado e pronto para provisionamento!");
                        await Task.Delay(2000, ct);
                        return;
                    }
                }
                else if (result.Matched is StopCondition.Prompt)
                {
                    await ProgressAsync("[OK] Prompt do Cisco IOS confirmado.");
                    await Task.Delay(2000, ct);
                    return;
                }
            }
            catch (SessionTimeoutException)
            {
                // Continua no loop de espera
            }
        }
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }

    private static bool IsCiscoRunningImage(string showVerOutput, string binFileName)
    {
        if (string.IsNullOrWhiteSpace(showVerOutput) || string.IsNullOrWhiteSpace(binFileName))
            return false;

        var cleanBin = Path.GetFileName(binFileName).Trim();
        var cleanBase = Path.GetFileNameWithoutExtension(cleanBin).Trim();

        // 1. Verifica nome exato ou sem extensão
        if (showVerOutput.Contains(cleanBin, StringComparison.OrdinalIgnoreCase) ||
            showVerOutput.Contains(cleanBase, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. Extrai tag de release do nome do arquivo (ex: "c1900-universalk9-mz.SPA.157-3.M9.bin" -> "157-3.M9", "15.7(3)M9", "15.7(3) M9")
        var match = Regex.Match(cleanBin, @"(?i)(\d{2,3})-(\d+)\.([A-Za-z0-9]+)");
        if (match.Success)
        {
            var major = match.Groups[1].Value;
            var minor = match.Groups[2].Value;
            var train = match.Groups[3].Value;

            if (major.Length == 3)
            {
                var vStr1 = $"{major[0]}{major[1]}.{major[2]}({minor}){train}"; // 15.7(3)M9
                var vStr2 = $"{major[0]}{major[1]}.{major[2]}({minor}) {train}"; // 15.7(3) M9
                if (showVerOutput.Contains(vStr1, StringComparison.OrdinalIgnoreCase) ||
                    showVerOutput.Contains(vStr2, StringComparison.OrdinalIgnoreCase))
                    return true;

                var vShort = $"{major[0]}{major[1]}.{major[2]}"; // 15.7
                if (showVerOutput.Contains(vShort, StringComparison.OrdinalIgnoreCase) &&
                    (showVerOutput.Contains(train, StringComparison.OrdinalIgnoreCase) || showVerOutput.Contains($"({minor})", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }

        // 3. Fallback genérico por versão (ex.: "15.7" presente no nome e no show version)
        var generalVerMatch = Regex.Match(cleanBin, @"(?i)(\d+\.\d+)");
        if (generalVerMatch.Success && showVerOutput.Contains(generalVerMatch.Value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsBootSystemConfigured(string bootOutput, string binFileName)
    {
        if (string.IsNullOrWhiteSpace(bootOutput) || string.IsNullOrWhiteSpace(binFileName))
            return false;

        var cleanBin = Path.GetFileName(binFileName).Trim();
        var cleanBase = Path.GetFileNameWithoutExtension(cleanBin).Trim();

        return bootOutput.Contains(cleanBin, StringComparison.OrdinalIgnoreCase) ||
               bootOutput.Contains(cleanBase, StringComparison.OrdinalIgnoreCase);
    }
}
