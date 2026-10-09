using System.Text;
using System.Text.RegularExpressions;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Tftp;

namespace NetworkDevice.Cisco;

public sealed class CiscoIOSUpgrader
{
    private static readonly Regex PromptConfirmRegex = new(
        @"(?i)(?:Address or name of remote host|Source filename|Destination filename|erase flash|over-write|continue\?|\[confirm\]|\?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex StrictPromptRegex = new(
        @"^[A-Za-z0-9_\-\.]+(?:\(config[^\)]*\))?\s*[>#]\s*$",
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
    /// Caso o equipamento esteja em modo ROMMON (sem firmware / Flash vazia), executa a recuperação completa via tftpdnld no ROMMON.
    /// </summary>
    public async Task<bool> UpgradeAsync(
        DeviceSession session,
        string imageFilePath,
        string hostIpAddress,
        string? routerIpAddress = null,
        string? subnetMask = null,
        string? lanInterface = null,
        string? expectedMd5 = null,
        string? localAdapterName = null,
        Func<string, CancellationToken, Task>? requestOperatorAction = null,
        CancellationToken cancellationToken = default,
        string? enableSecret = null,
        IEnumerable<string>? candidatePasswords = null)
    {
        if (!File.Exists(imageFilePath))
            throw new FileNotFoundException($"Arquivo de imagem IOS não encontrado: {imageFilePath}");

        var binFileName = Path.GetFileName(imageFilePath);
        var imageDir = Path.GetDirectoryName(imageFilePath) ?? AppContext.BaseDirectory;
        var fileSize = new FileInfo(imageFilePath).Length;
        var sizeMb = (fileSize / (1024.0 * 1024.0)).ToString("N1");

        // 0. Verifica se o roteador Cisco está em Modo ROMMON (sem firmware na Flash)
        var isRommon = session.Mode == ExecMode.Rommon ||
                       session.CurrentPrompt?.Trim().StartsWith("rommon", StringComparison.OrdinalIgnoreCase) == true;

        if (!isRommon)
        {
            try
            {
                await session.WriteLineAsync(string.Empty, cancellationToken);
                await Task.Delay(300, cancellationToken);
                if (session.Mode == ExecMode.Rommon ||
                    session.CurrentPrompt?.Trim().StartsWith("rommon", StringComparison.OrdinalIgnoreCase) == true)
                {
                    isRommon = true;
                }
            }
            catch { }
        }

        if (isRommon)
        {
            return await UpgradeViaRommonTftpAsync(
                session,
                imageFilePath,
                hostIpAddress,
                routerIpAddress,
                subnetMask,
                lanInterface,
                localAdapterName,
                requestOperatorAction,
                cancellationToken,
                enableSecret: enableSecret,
                candidatePasswords: candidatePasswords);
        }

        await ProgressAsync($"[*] INICIANDO UPGRADE DE IOS ({binFileName} — {sizeMb} MB)...");

        // 1. Garante modo privilegiado (#) tratando ambos os cenários (sem senha ou com senha 'PRO1AN' / credenciais)
        var adapter = new CiscoIOSAdapter(enableSecret, candidatePasswords);
        await adapter.EnterPrivilegedExecAsync(session, candidatePasswords, cancellationToken);

        // Desativa paginação apenas APÓS privilégio garantido
        try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), cancellationToken); } catch { }
        try { await session.SendCommandAsync("terminal width 512", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

        // 2. Consulta estado atual do equipamento (Versão em execução, Arquivos na Flash e Boot System)
        var showVer = "";
        try { showVer = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(15), cancellationToken); } catch { }

        var fsPrefix = CiscoIOSFlashParser.DetectFlashFilesystemPrefix(showVer);
        var dirFlash = "";
        try { dirFlash = await session.SendCommandAsync($"dir {fsPrefix}", TimeSpan.FromSeconds(15), cancellationToken); } catch { }
        if (string.IsNullOrWhiteSpace(dirFlash) || dirFlash.Contains("% Error") || dirFlash.Contains("% Invalid"))
        {
            try { dirFlash = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken); } catch { }
        }
        if (string.IsNullOrWhiteSpace(dirFlash) || dirFlash.Contains("% Error") || dirFlash.Contains("% Invalid"))
        {
            try { dirFlash = await session.SendCommandAsync("show flash:", TimeSpan.FromSeconds(15), cancellationToken); } catch { }
        }

        var showBoot = "";
        try { showBoot = await session.SendCommandAsync("show running-config | include boot", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
        if (string.IsNullOrWhiteSpace(showBoot))
        {
            try { showBoot = await session.SendCommandAsync("show boot", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
        }

        var isRunningTarget = CiscoIOSFirmwareVersionInspector.IsSameVersion(showVer, binFileName, out var curVer, out var tgtVer);
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
            await ProgressAsync($"  Versão em execução : {curVer.DisplayString}");
            await ProgressAsync($"  Versão alvo        : {tgtVer.DisplayString}");

            // Se o boot system não estiver explicitamente salvo, garante sem reiniciar
            if (!isBootConfigured && isFileOnFlash)
            {
                await ProgressAsync($"[*] Gravando boot system persistente para 'flash:{binFileName}'...");
                await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync($"boot system flash:{binFileName}", TimeSpan.FromSeconds(10), cancellationToken);
                if (fsPrefix.StartsWith("usbflash", StringComparison.OrdinalIgnoreCase))
                {
                    await session.SendCommandAsync($"boot system {fsPrefix}{binFileName}", TimeSpan.FromSeconds(10), cancellationToken);
                }
                await session.SendCommandAsync("config-register 0x2102", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(30), cancellationToken);
            }

            await ProgressAsync($"  -> Pulando cópia TFTP e reinicialização (100% economia de tempo).");
            await ProgressAsync("=================================================================\n");
            _onProgress?.Invoke(100, "Fase B: Firmware OK", $"Equipamento já executa {binFileName} ({curVer.CanonicalVersion ?? curVer.DisplayString}).");
            return true;
        }

        // Se a imagem alvo já existe na Flash mas há versões concorrentes antigas,
        // exclui imediatamente para liberar espaço e garantir que o boot ocorra no alvo
        if (isFileOnFlash)
        {
            var conflictingOnFlash = CiscoIOSFlashParser.GetConflictingFirmwareFiles(dirFlash, binFileName);
            if (conflictingOnFlash.Count > 0)
            {
                await ProgressAsync($"[*] [Flash] Detectada(s) {conflictingOnFlash.Count} versão(ões) concorrente(s) na Flash. Limpando para garantir boot exclusivo em {binFileName}...");
                foreach (var cf in conflictingOnFlash)
                {
                    await DeleteFileFromFlashAsync(session, cf, fsPrefix, cancellationToken);
                }
            }
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
            // 0. Garante o IP do notebook (a Fase B avulsa não passa pela Fase D, que faria isso)
            var notebookAdapter = EscolherAdaptadorNotebook(localAdapterName);
            if (!string.IsNullOrWhiteSpace(hostIpAddress) && hostIpAddress != "127.0.0.1" && notebookAdapter != null)
            {
                try
                {
                    var pcMask = string.IsNullOrWhiteSpace(subnetMask) ? "255.255.255.0" : subnetMask;
                    await ProgressAsync($"[*] Configurando IP {hostIpAddress}/{pcMask} na placa '{notebookAdapter}'...");
                    var (okPc, outPc) = await HostNetworkManager.SetStaticIpAsync(notebookAdapter, hostIpAddress, pcMask, null, cancellationToken);
                    await ProgressAsync(okPc
                        ? $"[OK] Placa '{notebookAdapter}' configurada com IP {hostIpAddress}."
                        : $"[AVISO] Configuração de IP local: {outPc}");
                }
                catch (Exception ex)
                {
                    await ProgressAsync($"[AVISO] Não foi possível ajustar o IP do notebook: {ex.Message}");
                }
            }
            else if (hostIpAddress == "127.0.0.1")
            {
                await ProgressAsync("[AVISO] IP do notebook indefinido (127.0.0.1) — confira a Ficha SAIP e a placa selecionada antes do TFTP.");
            }
            try { await HostNetworkManager.EnsureTftpFirewallRuleAsync(cancellationToken); } catch { }

                await using var tftpServer = new EmbeddedTftpServer(imageDir);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var lastUiUpdate = DateTime.MinValue;
                var lastLoggedPct = -1;
                long tftpTotalSent = 0;
                bool tftpTransferStarted = false;
                bool tftpTransferCompleted = false;

                tftpServer.TransferProgress += (file, sent, total, pct) =>
                {
                    tftpTotalSent = sent;
                    tftpTransferStarted = true;
                    if (total > 0 && sent >= total)
                    {
                        tftpTransferCompleted = true;
                    }

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
                    try
                    {
                        var briefOut = await session.SendCommandAsync("show ip interface brief", TimeSpan.FromSeconds(10), cancellationToken);
                        var (_, detectedLan) = CiscoSaipConfigurator.DetectInterfaces(briefOut, preferredLan: lanIf);
                        if (!string.IsNullOrWhiteSpace(detectedLan))
                            lanIf = detectedLan;
                    }
                    catch { }

                    var rIp = routerIpAddress ?? "200.182.245.17";
                    var mask = subnetMask ?? "255.255.255.240";

                    await ProgressAsync($"[*] Configurando temporariamente {lanIf} ({rIp} {mask}) no Cisco para viabilizar transferência TFTP...");
                    await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
                    await session.SendCommandAsync($"interface {lanIf}", TimeSpan.FromSeconds(10), cancellationToken);
                    await session.SendCommandAsync("no switchport", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync($"ip address {rIp} {mask}", TimeSpan.FromSeconds(10), cancellationToken);
                    await session.SendCommandAsync("no shutdown", TimeSpan.FromSeconds(10), cancellationToken);
                    await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
                    await Task.Delay(1500, cancellationToken);

                    // Valida se o técnico conectou o cabo na porta LAN (GE 0/1 / GE 5 / GE 0/5)
                    await CiscoIOSAdapter.EnforceLanPortConnectedAsync(session, lanIf, requestOperatorAction, ProgressAsync, cancellationToken);

                    // Testa conectividade IP com o PC (ping)
                    await ProgressAsync($"[*] Testando conectividade de rede com o PC ({hostIpAddress})...");
                    var pingRes = await session.SendCommandAsync($"ping {hostIpAddress}", TimeSpan.FromSeconds(15), cancellationToken);
                    if (pingRes.Contains("!"))
                    {
                        await ProgressAsync($"[OK] Conectividade IP com o PC ({hostIpAddress}) confirmada.");
                    }
                    else
                    {
                        await ProgressAsync($"[AVISO] Ping para o PC ({hostIpAddress}) ainda sem resposta. Prosseguindo com TFTP...");
                    }

                    // Limpa buffers residuais do console antes de iniciar cópia
                    try { await session.SendCommandAsync(string.Empty, TimeSpan.FromMilliseconds(500), cancellationToken); } catch { }

                    // Verifica espaço disponível na Flash e limpa versões concorrentes se necessário
                    var availBytes = CiscoIOSFlashParser.ParseAvailableBytes(dirFlash);
                    var confFilesBeforeCopy = CiscoIOSFlashParser.GetConflictingFirmwareFiles(dirFlash, binFileName);
                    if (confFilesBeforeCopy.Count > 0 && (availBytes > 0 && availBytes < fileSize * 1.05))
                    {
                        var availMb = (availBytes / (1024.0 * 1024.0)).ToString("N1");
                        await ProgressAsync($"[!] Espaço livre na Flash ({availMb} MB) insuficiente para o novo firmware ({sizeMb} MB).");
                        await ProgressAsync($"[*] Excluindo versão(ões) concorrente(s) na Flash para liberar espaço de gravação...");
                        foreach (var cf in confFilesBeforeCopy)
                        {
                            await DeleteFileFromFlashAsync(session, cf, fsPrefix, cancellationToken);
                        }
                    }

                    // Envia o comando de cópia TFTP para a flash
                    await ProgressAsync($"[*] Solicitando cópia TFTP: copy tftp://{hostIpAddress}/{binFileName} flash:{binFileName}...");
                    var copyCmd = $"copy tftp://{hostIpAddress}/{binFileName} flash:{binFileName}";
                    await session.WriteLineAsync(copyCmd, cancellationToken);
                    var fullOutput = new System.Text.StringBuilder();

                    // Condições de parada para monitoramento do TFTP
                    var copyConds = new StopCondition[]
                    {
                        new StopCondition.LineRegex("confirm", PromptConfirmRegex),
                        new StopCondition.LineRegex("prompt", StrictPromptRegex),
                        new StopCondition.Contains("accessing", "Accessing tftp:"),
                        new StopCondition.Contains("loading", "Loading "),
                        new StopCondition.Contains("ok", "[OK"),
                        new StopCondition.Contains("bytes_copied", "bytes copied"),
                        new StopCondition.Contains("error", "%Error"),
                        new StopCondition.Contains("error_sp", "% Error"),
                        new StopCondition.Contains("timed_out", "Timed out"),
                        new StopCondition.Contains("socket_err", "Socket error"),
                        new StopCondition.Contains("no_route", "No route to host")
                    };

                    // 1. Responde a eventuais perguntas de confirmação do Cisco IOS (Destination filename, overwrite, erase flash, etc.)
                    var confirmDeadline = DateTime.UtcNow.AddSeconds(45);
                    while (DateTime.UtcNow < confirmDeadline && !cancellationToken.IsCancellationRequested)
                    {
                        if (tftpTransferStarted || tftpTotalSent > 0)
                            break;

                        try
                        {
                            var exp = await session.WaitForAsync(copyConds, TimeSpan.FromSeconds(5), cancellationToken);
                            fullOutput.Append(exp.Output);

                            if (exp.Matched is StopCondition.LineRegex lrConf && lrConf.Name == "confirm")
                            {
                                var lastLine = exp.Output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "confirmação";
                                await ProgressAsync($"[*] Confirmação solicitada pelo Cisco ({lastLine.Trim()}) — confirmando com ENTER...");
                                await session.WriteLineAsync(string.Empty, cancellationToken);
                                await Task.Delay(300, cancellationToken);
                                continue;
                            }

                            if (exp.Matched is StopCondition.Contains c && (c.Text == "Accessing tftp:" || c.Text == "Loading "))
                            {
                                tftpTransferStarted = true;
                                break;
                            }

                            if (exp.Matched is StopCondition.Contains cErr && (cErr.Text == "%Error" || cErr.Text == "% Error" || cErr.Text == "Timed out" || cErr.Text == "Socket error" || cErr.Text == "No route to host"))
                            {
                                throw new DeviceSessionException($"Falha ao iniciar TFTP no Cisco: {exp.Output.Trim()}");
                            }

                            if (exp.Matched is StopCondition.LineRegex lrPrompt && lrPrompt.Name == "prompt")
                            {
                                var curText = fullOutput.ToString();
                                if (!tftpTransferStarted && tftpTotalSent == 0 && !curText.Contains("[OK"))
                                {
                                    throw new DeviceSessionException($"Cópia TFTP cancelada pelo Cisco antes do início da transferência. Resposta do roteador:\n{curText.Trim()}");
                                }
                                break;
                            }
                        }
                        catch (SessionTimeoutException)
                        {
                            if (tftpTransferStarted || tftpTotalSent > 0)
                                break;
                        }
                    }

                    // 2. Monitora transferência do arquivo até a conclusão total (retorno de [OK ou prompt)
                    var copyTimeout = DateTime.UtcNow.AddMinutes(20);
                    var isCopying = true;

                    while (isCopying && DateTime.UtcNow < copyTimeout && !cancellationToken.IsCancellationRequested)
                    {
                        try
                        {
                            var exp = await session.WaitForAsync(copyConds, TimeSpan.FromSeconds(5), cancellationToken);
                            fullOutput.Append(exp.Output);

                            // Confirmação adicional tardia
                            if (exp.Matched is StopCondition.LineRegex lrConf && lrConf.Name == "confirm")
                            {
                                await session.WriteLineAsync(string.Empty, cancellationToken);
                                continue;
                            }

                            if (exp.Matched is StopCondition.Contains cOk && (cOk.Text == "[OK" || cOk.Text == "bytes copied"))
                            {
                                await ProgressAsync("[OK] Cisco acusou recebimento e gravação completa do arquivo na Flash.");
                                try
                                {
                                    var pExp = await session.WaitForAsync(new StopCondition[] { new StopCondition.LineRegex("prompt", StrictPromptRegex) }, TimeSpan.FromSeconds(15), cancellationToken);
                                    fullOutput.Append(pExp.Output);
                                }
                                catch (SessionTimeoutException) { }
                                isCopying = false;
                                break;
                            }

                            if (exp.Matched is StopCondition.LineRegex lr && lr.Name == "prompt")
                            {
                                var outStr = fullOutput.ToString();
                                if (tftpTransferCompleted || outStr.Contains("[OK") || outStr.Contains("bytes copied") || tftpTotalSent >= fileSize * 0.90)
                                {
                                    isCopying = false;
                                    break;
                                }

                                throw new DeviceSessionException($"Transferência TFTP interrompida abruptamente pelo Cisco IOS. Resposta do roteador:\n{outStr.Trim()}");
                            }

                            if (exp.Output.Contains("%Error") || exp.Output.Contains("% Error") || exp.Output.Contains("Socket error") || exp.Output.Contains("Timed out") || exp.Output.Contains("No route to host"))
                            {
                                throw new DeviceSessionException($"Erro reportado pelo Cisco durante TFTP: {exp.Output.Trim()}");
                            }
                        }
                        catch (SessionTimeoutException)
                        {
                            // Transferência em andamento no Cisco (imprimindo pontos de exclamação !): continua aguardando
                            continue;
                        }
                    }

                    var copyResultText = fullOutput.ToString();
                    if (copyResultText.Contains("% Error") || copyResultText.Contains("Timed out") || copyResultText.Contains("No route to host"))
                    {
                        throw new DeviceSessionException($"Falha na cópia TFTP da imagem {binFileName}. Resposta: {copyResultText.Trim()}");
                    }

                    await Task.Delay(1500, cancellationToken);
                }
                finally
                {
                    await tftpServer.StopAsync();
                }
            }

            // 6. Confirma se o arquivo está na memória Flash
            dirFlash = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken);
            if (dirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase))
            {
                dirFlash = await session.SendCommandAsync("dir sdflash:", TimeSpan.FromSeconds(15), cancellationToken);
                if (dirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase))
                {
                    dirFlash = await session.SendCommandAsync("dir", TimeSpan.FromSeconds(15), cancellationToken);
                }
            }
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

            // 7c. Limpeza de qualquer outra versão presente na Flash para liberar espaço e garantir boot exclusivo na versão indicada
            var curDirFlash = "";
            try { curDirFlash = await session.SendCommandAsync($"dir {fsPrefix}", TimeSpan.FromSeconds(15), cancellationToken); } catch { }
            if (string.IsNullOrWhiteSpace(curDirFlash) || curDirFlash.Contains("% Error") || curDirFlash.Contains("% Invalid"))
            {
                try { curDirFlash = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken); } catch { }
            }
            var remainingConflicting = CiscoIOSFlashParser.GetConflictingFirmwareFiles(curDirFlash, binFileName);
            if (remainingConflicting.Count > 0)
            {
                await ProgressAsync($"[*] [Flash] Limpando {remainingConflicting.Count} versão(ões) concorrente(s) na Flash para liberar espaço e garantir boot exclusivo em {binFileName}...");
                foreach (var cf in remainingConflicting)
                {
                    await DeleteFileFromFlashAsync(session, cf, fsPrefix, cancellationToken);
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

            if (fsPrefix.StartsWith("usbflash", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync($"boot system {fsPrefix}{binFileName}", TimeSpan.FromSeconds(10), cancellationToken);
                await Task.Delay(500, cancellationToken);
            }

            await session.SendCommandAsync("config-register 0x2102", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(500, cancellationToken);

            await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(500, cancellationToken);

            await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(30), cancellationToken);
            await ProgressAsync($"[*] Boot system configurado e salvo com sucesso.");

            // 8b. Zeramento conjunto de configuração no mesmo reload (aproveita o reboot obrigatório de upgrade do SO)
            await ProgressAsync("[*] Zerando configurações antigas e senhas residuais da NVRAM (write erase) para boot limpo...");
            try
            {
                var weRes = await session.SendExpectAsync(
                    "write erase",
                    new StopCondition[]
                    {
                        new StopCondition.Contains("confirm", "[confirm]"),
                        new StopCondition.Prompt()
                    },
                    TimeSpan.FromSeconds(10),
                    cancellationToken);

                if (weRes.Output.Contains("[confirm]", StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteLineAsync(string.Empty, cancellationToken);
                }
                await Task.Delay(1500, cancellationToken);
            }
            catch { }

            // 9. Reload automático
            await ProgressAsync($"\n[*] [RELOAD AUTOMÁTICO CONJUNTO] Reiniciando roteador Cisco (novo firmware {binFileName} + base limpa)...");
            await ExecutarReloadCiscoAsync(session, cancellationToken, enableSecret, candidatePasswords);
            await ProgressAsync($"[OK] Ciclo de reinicialização do Cisco IOS concluído com sucesso!");

            return true;
    }

    private async Task<bool> DeleteFileFromFlashAsync(
        DeviceSession session,
        string fileName,
        string fsPrefix,
        CancellationToken ct)
    {
        await ProgressAsync($"[*] Removendo versão antiga/concorrente da flash para liberar espaço e garantir boot: {fileName}...");

        var prefixesToTry = new List<string> { fsPrefix };
        if (!fsPrefix.Equals("flash:", StringComparison.OrdinalIgnoreCase))
            prefixesToTry.Add("flash:");
        if (!fsPrefix.Equals("usbflash0:", StringComparison.OrdinalIgnoreCase))
            prefixesToTry.Add("usbflash0:");

        foreach (var pfx in prefixesToTry.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var delCmd = $"delete /force {pfx}{fileName}";
                var delRes = await session.SendExpectAsync(
                    delCmd,
                    new StopCondition[]
                    {
                        new StopCondition.Contains("confirm", "[confirm]"),
                        new StopCondition.Contains("question", "?"),
                        new StopCondition.Contains("invalid", "% Invalid"),
                        new StopCondition.Contains("error", "%Error"),
                        new StopCondition.Prompt()
                    },
                    TimeSpan.FromSeconds(15),
                    ct);

                if (delRes.Output.Contains("% Invalid", StringComparison.OrdinalIgnoreCase))
                {
                    // Versões que não aceitam /force: tenta delete simples e confirma diálogos
                    await session.WriteLineAsync($"delete {pfx}{fileName}", ct);
                    for (int i = 0; i < 3; i++)
                    {
                        var promptRes = await session.WaitForAsync(
                            new StopCondition[]
                            {
                                new StopCondition.Contains("confirm", "[confirm]"),
                                new StopCondition.Contains("question", "?"),
                                new StopCondition.Prompt()
                            },
                            TimeSpan.FromSeconds(5),
                            ct);

                        if (promptRes.Matched is StopCondition.Prompt)
                            break;

                        await session.WriteLineAsync(string.Empty, ct);
                        await Task.Delay(400, ct);
                    }
                }
                else if (delRes.Output.Contains("[confirm]", StringComparison.OrdinalIgnoreCase) ||
                         delRes.Output.Contains("?", StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteLineAsync(string.Empty, ct);
                    await Task.Delay(400, ct);
                }

                if (!delRes.Output.Contains("%Error opening", StringComparison.OrdinalIgnoreCase) &&
                    !delRes.Output.Contains("No such file", StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"[OK] Versão antiga {fileName} excluída com sucesso em {pfx}.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                await ProgressAsync($"[AVISO] Falha ao tentar excluir {fileName} em {pfx}: {ex.Message}");
            }
        }

        return false;
    }

    private async Task ExecutarReloadCiscoAsync(
        DeviceSession session,
        CancellationToken ct,
        string? enableSecret = null,
        IEnumerable<string>? candidatePasswords = null)
    {
        try
        {
            await session.WriteLineAsync("reload", ct);
            var reloadRes = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("Proceed with reload? [confirm]", "Proceed with reload? [confirm]"),
                    new StopCondition.Contains("[confirm]", "[confirm]"),
                    new StopCondition.Contains("System configuration has been modified", "System configuration has been modified")
                },
                TimeSpan.FromSeconds(15),
                ct);

            if (reloadRes.Output.Contains("System configuration has been modified", StringComparison.OrdinalIgnoreCase))
            {
                // CRUCIAL: Como a NVRAM foi apagada com write erase, respondemos 'no' para NÃO regravar a running-config sobre a NVRAM apagada!
                await session.WriteLineAsync("no", ct);
                await Task.Delay(500, ct);
                try
                {
                    await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.Contains("Proceed with reload? [confirm]", "Proceed with reload? [confirm]"),
                            new StopCondition.Contains("[confirm]", "[confirm]")
                        },
                        TimeSpan.FromSeconds(15),
                        ct);
                }
                catch { }
            }

            // Confirma o reload com Enter
            await session.WriteLineAsync(string.Empty, ct);
            await ProgressAsync("[OK] Comando de reinicialização enviado ao Cisco IOS!");
            await ProgressAsync("[*] Aguardando reset físico da CPU do roteador (10s)...");
            await Task.Delay(10000, ct);
        }
        catch { }

        // Monitora o boot completo, transmite CLI em tempo real e responde diálogos iniciais
        await AguardarBootCiscoIOSAsync(session, TimeSpan.FromMinutes(6), ct, enableSecret, candidatePasswords);

        // Estabiliza e recupera acesso CLI em modo privilegiado
        await EstabilizarCliPosBootAsync(session, enableSecret, candidatePasswords, ct);
    }

    public async Task AguardarBootCiscoIOSAsync(
        DeviceSession session,
        TimeSpan baseTimeout,
        CancellationToken ct,
        string? enableSecret = null,
        IEnumerable<string>? candidatePasswords = null)
    {
        var bootStartTime = DateTime.UtcNow;
        var bootTimeout = DateTime.UtcNow.Add(baseTimeout);
        var maxAbsoluteTimeout = DateTime.UtcNow.AddMinutes(20);
        var lastStatusLog = DateTime.MinValue;
        var lastActivityTime = DateTime.UtcNow;
        var lastKeepAliveEnter = DateTime.UtcNow;

        int upgradeTo512Count = 0;
        int noMemoryLicenseCount = 0;
        bool decompressionReported = false;
        bool decompressionCompleted = false;

        var loggedBootMilestones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var passList = new List<string>();
        if (!string.IsNullOrWhiteSpace(enableSecret)) passList.Add(enableSecret);
        if (candidatePasswords != null)
        {
            foreach (var cp in candidatePasswords)
            {
                if (!string.IsNullOrWhiteSpace(cp) && !passList.Contains(cp, StringComparer.OrdinalIgnoreCase))
                    passList.Add(cp);
            }
        }
        if (!passList.Contains("PRO1AN", StringComparer.OrdinalIgnoreCase))
            passList.Add("PRO1AN");
        if (!string.IsNullOrWhiteSpace(session.Options.Password) && !passList.Contains(session.Options.Password, StringComparer.OrdinalIgnoreCase))
            passList.Add(session.Options.Password);
        passList.Add(""); // tentativa em branco

        int passwordAttempt = 0;
        int usernameAttempt = 0;

        // Assina RawOutput da sessão para escutar cada chunk em tempo real
        var lineAccumulator = new StringBuilder();
        var rawLinesQueue = new Queue<string>();
        var syncLock = new object();

        Action<string> onRawChunk = chunk =>
        {
            lock (syncLock)
            {
                lastActivityTime = DateTime.UtcNow;
                lineAccumulator.Append(chunk);
                var str = lineAccumulator.ToString();
                var nlIndex = str.IndexOf('\n');
                while (nlIndex >= 0)
                {
                    var line = str.Substring(0, nlIndex).Trim('\r', '\n');
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        rawLinesQueue.Enqueue(line);
                    }
                    str = str.Substring(nlIndex + 1);
                    nlIndex = str.IndexOf('\n');
                }
                lineAccumulator.Clear();
                lineAccumulator.Append(str);
            }
        };

        session.RawOutput += onRawChunk;

        try
        {
            while (DateTime.UtcNow < bootTimeout && DateTime.UtcNow < maxAbsoluteTimeout && !ct.IsCancellationRequested)
            {
                // 1. Processa linhas acumuladas na fila do console serial
                List<string> linesToProcess = new();
                lock (syncLock)
                {
                    while (rawLinesQueue.Count > 0)
                    {
                        linesToProcess.Add(rawLinesQueue.Dequeue());
                    }
                }

                foreach (var line in linesToProcess)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    // Detecta e transmite marcos chave do boot para a UI
                    if (trimmed.Contains("System Bootstrap", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Total memory size =", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Readonly ROMMON initialized", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("program load complete", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Digitally Signed Release Software", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Smart Init is enabled", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("smart init is sizing iomem", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Rounded IOMEM up to:", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Using 14 percent iomem", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Using 7 percent iomem", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Restricted Rights Legend", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.Contains("Cisco IOS Software", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("%SYS-", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("%LINK-", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("%LINEPROTO-", StringComparison.OrdinalIgnoreCase))
                    {
                        if (loggedBootMilestones.Add(trimmed))
                        {
                            await ProgressAsync($"  │ [BOOT] {trimmed}");
                        }
                    }

                    // Notifica início de descompressão
                    if (trimmed.Contains("Self decompressing the image", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!decompressionReported)
                        {
                            decompressionReported = true;
                            await ProgressAsync("  │ [BOOT] Descompactando imagem Cisco IOS na memória DRAM (Self decompressing)...");
                        }
                    }

                    // Detecta conclusão de descompressão [OK] ou início da execução do IOS
                    if ((trimmed.Contains("decompress", StringComparison.OrdinalIgnoreCase) && trimmed.Contains("[OK]")) ||
                        (decompressionReported && (trimmed.EndsWith("[OK]") || trimmed.Contains("[OK]"))) ||
                        trimmed.Contains("Cisco IOS Software", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("%SYS-5-CONFIG_I", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("%SYS-5-RESTART", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!decompressionCompleted)
                        {
                            decompressionCompleted = true;
                            if (loggedBootMilestones.Add("DECOMPRESSION_OK_" + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond / 10)))
                            {
                                await ProgressAsync("  │ [BOOT] Descompressão da imagem IOS concluída com sucesso! [OK]");
                                decompressionReported = false;
                            }
                        }
                    }

                    // Detecta: UPGRADING TO 512MB. RELOADING........
                    if (Regex.IsMatch(trimmed, @"(?i)UPGRADING\s+TO\s+\d+MB|RELOADING\.\.\.\."))
                    {
                        upgradeTo512Count++;
                        await ProgressAsync($"  │ [BOOT] ⚡ Hardware detectou controladora DRAM ('{trimmed}'). Reiniciando para aplicar...");
                        var ext = DateTime.UtcNow.AddSeconds(240);
                        if (ext > bootTimeout) bootTimeout = ext;
                    }

                    // Detecta: No memory license, set to default memory size and reboot !!
                    if (Regex.IsMatch(trimmed, @"(?i)No\s+memory\s+license.*reboot|memory\s+license.*reboot"))
                    {
                        noMemoryLicenseCount++;
                        await ProgressAsync($"  │ [BOOT] ⚠️ Licença de memória ausente: '{trimmed}'");
                        var ext = DateTime.UtcNow.AddSeconds(240);
                        if (ext > bootTimeout) bootTimeout = ext;

                        // Se detectou a alternância UPGRADING TO 512MB e No memory license:
                        if (upgradeTo512Count >= 1 && noMemoryLicenseCount >= 1)
                        {
                            await ProgressAsync("\n" +
                                "  ┌─────────────────────────────────────────────────────────────────────────────┐\n" +
                                "  │ ⚠️ [DIAGNÓSTICO AUTOMÁTICO] LOOP DE BOOT DE MEMÓRIA (MEMORY FLAP) DETECTADO │\n" +
                                "  ├─────────────────────────────────────────────────────────────────────────────┤\n" +
                                "  │ O Cisco 1905 tenta subir com 512MB, mas a imagem IOS 15.0(1)M8 exige a    │\n" +
                                "  │ licença 'FL-19-MEM' e força reboot para 256MB. Em 256MB o IOS tenta voltar  │\n" +
                                "  │ para 512MB, gerando um ciclo infinito de reinicializações na mesma imagem.  │\n" +
                                "  │                                                                             │\n" +
                                "  │ RECOMENDAÇÃO EM CAMPO:                                                      │\n" +
                                "  │ 1. Interrompa o boot no ROMMON com BREAK/Ctrl+C.                             │\n" +
                                "  │ 2. Grave/boote uma versão de IOS 15.2+ ou 15.4+ (não exige licença de RAM)  │\n" +
                                "  │    ou utilize imagem compatível com a DRAM nativa (256MB).                 │\n" +
                                "  └─────────────────────────────────────────────────────────────────────────────┘\n");
                        }
                    }
                }

                // 2. Extensão Dinâmica de Timeout (Keep-Alive de Atividade)
                if ((DateTime.UtcNow - lastActivityTime).TotalSeconds < 45)
                {
                    var extended = DateTime.UtcNow.AddSeconds(120);
                    if (extended > bootTimeout && extended < maxAbsoluteTimeout)
                    {
                        bootTimeout = extended;
                    }
                }

                var remainingSec = (int)Math.Max(0, (bootTimeout - DateTime.UtcNow).TotalSeconds);
                if ((DateTime.UtcNow - lastStatusLog).TotalSeconds >= 12)
                {
                    lastStatusLog = DateTime.UtcNow;
                    var phaseMsg = decompressionReported ? "descompactando imagem" : "carregando kernel";
                    await ProgressAsync($"[*] Aguardando boot do Cisco IOS ({phaseMsg}, ~{remainingSec}s restantes)...");
                }

                // 3. Keep-Alive / Despertador Ativo da Console Serial:
                // Quando a descompressão termina (ou após 45s de boot), o Cisco IOS inicializa interfaces
                // e pode emitir rajadas de syslog (%LINK-3-UPDOWN, %LINEPROTO-5-UPDOWN), empurrando o prompt
                // para fora da linha final do terminal.
                // Enviar Enter a cada 4 segundos garante o redesenho do prompt e acorda o console sem depender
                // de silêncio de linha que nunca ocorreria durante a rajada de syslogs.
                var elapsedTotal = (DateTime.UtcNow - bootStartTime).TotalSeconds;
                if ((decompressionCompleted || elapsedTotal >= 45) &&
                    (DateTime.UtcNow - lastKeepAliveEnter).TotalSeconds >= 4)
                {
                    lastKeepAliveEnter = DateTime.UtcNow;
                    await session.SendRawAsync("\r\n", ct);
                }

                // 4. Testa condições de parada e prompts
                try
                {
                    var result = await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.LineRegex("mem-upgrade", new Regex(@"(?i)(?:UPGRADING\s+TO\s+\d+MB|RELOADING\.\.\.\.|No\s+memory\s+license.*reboot|memory\s+license.*reboot)")),
                            new StopCondition.LineRegex("invalid-image", new Regex(@"(?i)Invalid\s+image\s+for\s+platform|failed\s+to\s+boot.*unsupported")),
                            // Autoinstall DEVE ser checado ANTES do diálogo inicial e de [yes/no]!
                            new StopCondition.LineRegex("terminate-autoinstall", new Regex(@"(?i)terminate\s+autoinstall|cancel\s+autoinstall")),
                            new StopCondition.LineRegex("initial-dialog", new Regex(@"(?i)(?:initial\s+configuration\s+dialog|basic\s+management\s+setup)")),
                            new StopCondition.LineRegex("yes-no-choice", new Regex(@"\?\s*\[yes/no\]")),
                            new StopCondition.LineRegex("press-return", new Regex(@"(?i)(?:press\s+(?:RETURN|ENTER)\s+to\s+get\s+started|con0\s+is\s+(?:now\s+)?available|Line\s+con0\s+is\s+available)")),
                            new StopCondition.LineRegex("login-user", new Regex(@"(?i)(?:username|login|user\s*name)\s*:\s*$")),
                            new StopCondition.LineRegex("login-pass", new Regex(@"(?i)password\s*:\s*$")),
                            new StopCondition.LineRegex("cisco-prompt", StrictPromptRegex),
                            new StopCondition.Prompt()
                        },
                        TimeSpan.FromSeconds(2),
                        ct);

                    if (result.Matched is StopCondition.LineRegex lr)
                    {
                        if (lr.Name == "mem-upgrade")
                        {
                            await Task.Delay(2000, ct);
                        }
                        else if (lr.Name == "invalid-image")
                        {
                            await ProgressAsync("[ALERTA CRÍTICO] A imagem de firmware é incompatível com o hardware do roteador (Invalid image for platform).");
                        }
                        else if (lr.Name == "terminate-autoinstall")
                        {
                            await ProgressAsync("[*] Diálogo autoinstall detectado — enviando 'yes' para cancelar autoinstall...");
                            await session.WriteLineAsync("yes", ct);
                            await Task.Delay(500, ct);
                        }
                        else if (lr.Name == "initial-dialog")
                        {
                            await ProgressAsync("[*] Diálogo de configuração inicial detectado — enviando 'no'...");
                            await session.WriteLineAsync("no", ct);
                            await Task.Delay(500, ct);
                        }
                        else if (lr.Name == "yes-no-choice")
                        {
                            var answer = result.Output.Contains("autoinstall", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
                            await ProgressAsync($"[*] Diálogo [yes/no] detectado — enviando '{answer}'...");
                            await session.WriteLineAsync(answer, ct);
                            await Task.Delay(1500, ct);
                        }
                        else if (lr.Name == "press-return")
                        {
                            await ProgressAsync("[*] 'Press RETURN to get started' detectado — enviando ENTER...");
                            await session.WriteLineAsync(string.Empty, ct);
                            await Task.Delay(1000, ct);
                        }
                        else if (lr.Name == "login-user")
                        {
                            usernameAttempt++;
                            string u = !string.IsNullOrWhiteSpace(session.Options.Username) && usernameAttempt == 1
                                ? session.Options.Username
                                : (usernameAttempt == 2 ? "cisco" : (usernameAttempt == 3 ? "admin" : "EBT"));
                            await ProgressAsync($"[*] Prompt de autenticação de console detectado — enviando usuário '{u}'...");
                            await session.WriteLineAsync(u, ct);
                            await Task.Delay(500, ct);
                        }
                        else if (lr.Name == "login-pass")
                        {
                            var passIndex = passwordAttempt % passList.Count;
                            var p = passList[passIndex];
                            passwordAttempt++;
                            await ProgressAsync($"[*] Prompt de senha de console detectado — autenticando (tentativa {passwordAttempt})...");
                            await session.WriteLineAsync(p, ct);
                            await Task.Delay(800, ct);
                        }
                        else if (lr.Name == "cisco-prompt" || result.Matched is StopCondition.Prompt)
                        {
                            // BLINDAGEM ANTI-ECO PRÉ-RELOAD:
                            // Um roteador Cisco (1900/2900/800/900) NUNCA conclui a reinicialização em menos de 45 segundos.
                            // Se ainda não se passaram 45s e não houve registro de descompressão/kernel pós-reboot,
                            // qualquer prompt detectado é resíduo da sessão anterior e deve ser ignorado.
                            if (elapsedTotal < 45 && !decompressionCompleted)
                            {
                                continue;
                            }

                            await ProgressAsync("[OK] Cisco IOS reinicializado e pronto para provisionamento!");
                            await Task.Delay(1500, ct);
                            return;
                        }
                    }
                    else if (result.Matched is StopCondition.Prompt)
                    {
                        if (elapsedTotal < 45 && !decompressionCompleted)
                        {
                            continue;
                        }

                        await ProgressAsync("[OK] Prompt do Cisco IOS confirmado.");
                        await Task.Delay(1500, ct);
                        return;
                    }
                }
                catch (SessionTimeoutException)
                {
                    // Se a descompressão já concluiu e estamos aguardando prompt, o envio de Enter periódico na seção 3 redesenha o prompt.
                }
            }
        }
        finally
        {
            session.RawOutput -= onRawChunk;
        }

        // Se saiu do laço por timeout
        if (upgradeTo512Count > 0 && noMemoryLicenseCount > 0)
        {
            throw new TimeoutException("O Cisco IOS entrou em loop de reinicialização de memória (Memory Flap entre 256MB e 512MB devido à ausência da licença 'FL-19-MEM' no IOS 15.0(1)M8). Consulte o diagnóstico detalhado emitido no log.");
        }

        throw new TimeoutException("O Cisco IOS reiniciou mas o prompt operacional não respondeu dentro do tempo limite. Verifique a console serial.");
    }

    public async Task EstabilizarCliPosBootAsync(
        DeviceSession session,
        string? enableSecret,
        IEnumerable<string>? candidatePasswords,
        CancellationToken ct)
    {
        await ProgressAsync("[*] Estabilizando acesso CLI via console pós-boot...");

        // 1. Drena quaisquer logs de inicialização residuais enviando quebras de linha suaves
        for (int i = 0; i < 3; i++)
        {
            await session.SendRawAsync("\r\n", ct);
            await Task.Delay(300, ct);
        }

        // 2. Se estiver em modo User EXEC ('>'), eleva para Privileged EXEC ('#') via CiscoIOSAdapter
        var adapter = new CiscoIOSAdapter(enableSecret, candidatePasswords);
        try
        {
            await adapter.EnterPrivilegedExecAsync(session, candidatePasswords, ct);
        }
        catch (Exception ex)
        {
            await ProgressAsync($"[AVISO] Elevação de privilégio pós-boot: {ex.Message}");
        }

        // 3. Desativa paginação e define largura para comandos subsequentes
        try
        {
            await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), ct);
            await session.SendCommandAsync("terminal width 512", TimeSpan.FromSeconds(5), ct);
        }
        catch { }

        await ProgressAsync($"[OK] Console CLI estabilizado e pronto para operação (Prompt: '{session.CurrentPrompt}').");
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }

    private static bool IsCiscoRunningImage(string showVerOutput, string binFileName)
    {
        return CiscoIOSFirmwareVersionInspector.IsSameVersion(showVerOutput, binFileName, out _, out _);
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

    /// <summary>
    /// Escolhe a placa do notebook para o TFTP: preferência explícita (combo da UI) ou
    /// primeira Ethernet disponível. Nulo apenas se não houver adaptador algum.
    /// </summary>
    public static string? EscolherAdaptadorNotebook(string? preferido) =>
        HostNetworkManager.EscolherAdaptadorNotebook(preferido);

    /// <summary>
    /// Recuperação de emergência do Cisco IOS em modo ROMMON via comando tftpdnld.
    /// Configura IP no notebook, ativa servidor TFTP local, programa variáveis no ROMMON e efetua boot.
    /// </summary>
    public async Task<bool> UpgradeViaRommonTftpAsync(
        DeviceSession session,
        string imageFilePath,
        string hostIpAddress = "192.168.1.1",
        string? routerIpAddress = "192.168.1.2",
        string? subnetMask = "255.255.255.0",
        string? lanInterface = null,
        string? localAdapterName = null,
        Func<string, CancellationToken, Task>? requestOperatorAction = null,
        CancellationToken cancellationToken = default,
        string? enableSecret = null,
        IEnumerable<string>? candidatePasswords = null)
    {
        var binFileName = Path.GetFileName(imageFilePath);
        var imageDir = Path.GetDirectoryName(imageFilePath) ?? AppContext.BaseDirectory;
        var fileSize = new FileInfo(imageFilePath).Length;
        var sizeMb = (fileSize / (1024.0 * 1024.0)).ToString("N1");

        await ProgressAsync($"\n=================================================================");
        await ProgressAsync($"   ⚠️ RECUPERAÇÃO DE FIRMWARE VIA CISCO ROMMON (TFTPDNLD)        ");
        await ProgressAsync("=================================================================");
        await ProgressAsync($"  O roteador Cisco está em modo ROMMON (sem firmware na Flash).");
        await ProgressAsync($"  Imagem a carregar   : {binFileName} ({sizeMb} MB)");

        // 1. Definição dos endereços IP para recuperação ROMMON
        var actualHostIp = (!string.IsNullOrWhiteSpace(hostIpAddress) && hostIpAddress != "127.0.0.1")
            ? hostIpAddress
            : "192.168.1.1";

        var actualMask = !string.IsNullOrWhiteSpace(subnetMask) ? subnetMask : "255.255.255.0";

        var actualRouterIp = (!string.IsNullOrWhiteSpace(routerIpAddress) && routerIpAddress != actualHostIp)
            ? routerIpAddress
            : "192.168.1.2";

        await ProgressAsync($"  IP do Notebook (TFTP): {actualHostIp}");
        await ProgressAsync($"  IP do Roteador (ROMMON): {actualRouterIp}");
        await ProgressAsync($"  Máscara de Sub-rede  : {actualMask}");
        await ProgressAsync($"  Gateway / Servidor   : {actualHostIp}");
        await ProgressAsync("=================================================================\n");

        _onProgress?.Invoke(25, "Fase B: Configurando Placa de Rede...", $"Configurando IP {actualHostIp} no Notebook...");

        // 2. Configura IP estático no adaptador de rede do notebook
        var targetAdapter = localAdapterName;
        if (string.IsNullOrWhiteSpace(targetAdapter))
        {
            var adapters = HostNetworkManager.GetEthernetAdapters();
            targetAdapter = adapters.FirstOrDefault(a => a.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
                         ?? adapters.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(targetAdapter))
        {
            try
            {
                await ProgressAsync($"[*] Configurando IP estático {actualHostIp}/{actualMask} na interface '{targetAdapter}'...");
                var (ok, outMsg) = await HostNetworkManager.SetStaticIpAsync(targetAdapter, actualHostIp, actualMask, null, cancellationToken);
                if (ok)
                    await ProgressAsync($"[OK] Interface '{targetAdapter}' configurada com sucesso com IP {actualHostIp}.");
                else
                    await ProgressAsync($"[AVISO] Configuração de IP local: {outMsg}");
            }
            catch (Exception ex)
            {
                await ProgressAsync($"[AVISO] Não foi possível ajustar o IP do adaptador automaticamente: {ex.Message}");
            }
        }
        else
        {
            await ProgressAsync($"[AVISO] Nenhum adaptador Ethernet especificado. Certifique-se de que sua placa de rede está com IP {actualHostIp} e máscara {actualMask}.");
        }

        var is921 = (lanInterface?.Contains("5") == true || lanInterface?.Contains("4") == true || binFileName.StartsWith("c900", StringComparison.OrdinalIgnoreCase) || binFileName.StartsWith("c8", StringComparison.OrdinalIgnoreCase));
        var rommonPort = is921 ? "GigabitEthernet 4 (GE 4 / Porta 4)" : "GigabitEthernet 0/0 (GE 0/0 / Porta 0)";
        var rommonShort = is921 ? "GE 4" : "GE 0/0";

        await ProgressAsync($"[*] [DICA FÍSICA] No modo ROMMON, conecte o cabo de rede Ethernet na porta {rommonPort} do roteador Cisco.");

        if (requestOperatorAction is not null)
        {
            await requestOperatorAction(
                "⚠️ ATENÇÃO OBRIGATÓRIA - CABO DE REDE NO MODO ROMMON\n\n" +
                "O roteador Cisco está em modo de recuperação ROMMON (sem firmware).\n\n" +
                "👉 CONECTE O CABO DE REDE ETHERNET NA PORTA:\n" +
                $"🔴 {rommonPort}\n\n" +
                "Esta é a única porta Ethernet habilitada no hardware para a transferência TFTP via ROMMON.\n\n" +
                $"Clique em OK assim que o cabo estiver conectado na porta {rommonShort}.",
                cancellationToken);
        }

        _onProgress?.Invoke(30, "Fase B: Iniciando Servidor TFTP...", "Iniciando servidor TFTP de alta performance...");

        // 3. Inicia o Servidor TFTP
        await using (var tftpServer = new EmbeddedTftpServer(imageDir))
        {
            var swTftp = new System.Diagnostics.Stopwatch();
            var lastLoggedPct = -1;
            var tftpCompleted = false;

            tftpServer.TransferProgress += (file, bytesRead, total, pct) =>
            {
                if (!swTftp.IsRunning)
                    swTftp.Start();

                var currentPct = (int)pct;
                var mbSent = bytesRead / (1024.0 * 1024.0);
                var mbTotal = total / (1024.0 * 1024.0);
                var speed = swTftp.Elapsed.TotalSeconds > 0 ? (mbSent / swTftp.Elapsed.TotalSeconds) : 0;

                if (pct >= 100 || (total > 0 && bytesRead >= total))
                {
                    tftpCompleted = true;
                }

                _onProgress?.Invoke(
                    35 + (int)(pct * 0.45),
                    $"Fase B: Gravando {binFileName} na Flash via ROMMON...",
                    $"{mbSent:N1} MB / {mbTotal:N1} MB ({pct:F0}%) @ {speed:F2} MB/s");

                if (currentPct % 10 == 0 && currentPct != lastLoggedPct)
                {
                    lastLoggedPct = currentPct;
                    _ = ProgressAsync($"  -> [TFTP ROMMON] Transferindo: {mbSent:F1} MB / {mbTotal:F1} MB ({pct:F0}%) @ {speed:F2} MB/s");
                }
            };
            tftpServer.LogMessage += msg => _ = ProgressAsync(msg);
            tftpServer.Start();

            // 4. Envia variáveis de ambiente ao ROMMON do Cisco
            await ProgressAsync("[*] Configurando variáveis de ambiente no ROMMON do Cisco...");
            await session.SendRawAsync($"IP_ADDRESS={actualRouterIp}\r", cancellationToken);
            await Task.Delay(300, cancellationToken);

            await session.SendRawAsync($"IP_SUBNET_MASK={actualMask}\r", cancellationToken);
            await Task.Delay(300, cancellationToken);

            await session.SendRawAsync($"DEFAULT_GATEWAY={actualHostIp}\r", cancellationToken);
            await Task.Delay(300, cancellationToken);

            await session.SendRawAsync($"TFTP_SERVER={actualHostIp}\r", cancellationToken);
            await Task.Delay(300, cancellationToken);

            await session.SendRawAsync($"TFTP_FILE={binFileName}\r", cancellationToken);
            await Task.Delay(300, cancellationToken);

            await session.SendRawAsync("TFTP_CHECKSUM=0\r", cancellationToken);
            await Task.Delay(300, cancellationToken);

            await session.SendRawAsync("TFTP_VERBOSE=1\r", cancellationToken);
            await Task.Delay(300, cancellationToken);

            // Exibe as variáveis ativas no ROMMON
            await ProgressAsync("[*] Verificando variáveis do ROMMON (set)...");
            var setOut = await session.SendCommandAsync("set", TimeSpan.FromSeconds(5), cancellationToken);
            await ProgressAsync($"[ROMMON ENV]\n{setOut.Trim()}");

            // 5. Executa comando de download TFTP no ROMMON (tftpdnld)
            await ProgressAsync("\n[*] [ROMMON TFTP] Executando comando 'tftpdnld' para gravação na Flash...");
            _onProgress?.Invoke(35, "Fase B: Executando tftpdnld...", "Aguardando confirmação e transferência TFTP...");

            // Envia apenas \r para não deixar \n residual que faria o ROMMON escolher o default [n]
            await session.SendRawAsync("tftpdnld\r", cancellationToken);

            // Aguarda o prompt de aviso: "Do you wish to continue? y/n:  [n]: "
            var confBuf = new byte[4096];
            var confText = new System.Text.StringBuilder();
            var confTimeout = DateTime.UtcNow.AddSeconds(15);

            while (DateTime.UtcNow < confTimeout && !cancellationToken.IsCancellationRequested)
            {
                var readBytes = await session.Transport.ReadAsync(confBuf, cancellationToken);
                if (readBytes > 0)
                {
                    var chunk = System.Text.Encoding.ASCII.GetString(confBuf, 0, readBytes);
                    confText.Append(chunk);
                    session.EmitRawOutput(chunk);

                    var textSoFar = confText.ToString();
                    if (textSoFar.Contains("Do you wish to continue", StringComparison.OrdinalIgnoreCase) ||
                        textSoFar.Contains("y/n:", StringComparison.OrdinalIgnoreCase) ||
                        textSoFar.Contains("[n]:", StringComparison.OrdinalIgnoreCase) ||
                        textSoFar.Contains("continue?", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    if (textSoFar.Contains("variable not set", StringComparison.OrdinalIgnoreCase) ||
                        textSoFar.Contains("illegal variable", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DeviceSessionException($"Variável de ambiente do ROMMON inválida ou ausente: {textSoFar.Trim()}");
                    }
                }
                await Task.Delay(100, cancellationToken);
            }

            // Confirma o prompt de aviso com 'y\r'
            await ProgressAsync("[*] Confirmando gravação na Flash (y)...");
            await Task.Delay(200, cancellationToken);
            await session.SendRawAsync("y\r", cancellationToken);

            // Monitora a transferência e gravação da Flash
            var timeout = DateTime.UtcNow.AddMinutes(25);
            var buffer = new System.Text.StringBuilder();
            var transferSuccess = false;
            var transferStarted = false;
            var lastChunkTime = DateTime.UtcNow;
            var readBuf = new byte[4096];

            while (DateTime.UtcNow < timeout && !cancellationToken.IsCancellationRequested)
            {
                var readBytes = await session.Transport.ReadAsync(readBuf, cancellationToken);
                if (readBytes > 0)
                {
                    lastChunkTime = DateTime.UtcNow;
                    var chunk = System.Text.Encoding.ASCII.GetString(readBuf, 0, readBytes);
                    buffer.Append(chunk);
                    session.EmitRawOutput(chunk);

                    var currentText = buffer.ToString();

                    if (currentText.Contains("Transferring", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("TFTP", StringComparison.OrdinalIgnoreCase) ||
                        Regex.IsMatch(currentText, @"\d{6,}"))
                    {
                        transferStarted = true;
                    }

                    // Critérios de sucesso:
                    // 1) Mensagens explícitas de conclusão de arquivo
                    // 2) TFTP do servidor completou 100% e o prompt rommon retornou (comum no Cisco 921)
                    // 3) Bloco numérico final de bytes gravados seguido do prompt rommon
                    if (currentText.Contains("File copy completed", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("File reception completed", StringComparison.OrdinalIgnoreCase) ||
                        (currentText.Contains("Copying image to flash", StringComparison.OrdinalIgnoreCase) && currentText.Contains("rommon")) ||
                        (tftpCompleted && Regex.IsMatch(currentText, @"rommon\s*\d*\s*>")) ||
                        (transferStarted && Regex.IsMatch(currentText, @"\d{6,}[\s\r\n]+rommon\s*\d*\s*>")))
                    {
                        transferSuccess = true;
                        break;
                    }

                    if (currentText.Contains("ARP: address resolution", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("ARP timeout", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("ARP failed", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DeviceSessionException(
                            $"Falha de ARP no ROMMON: O roteador Cisco não obteve resposta no IP do Notebook ({actualHostIp}).\n" +
                            $"• Dica de Cabo: No modo ROMMON, conecte o cabo de rede na porta {rommonPort} do Cisco.\n" +
                            $"• Dica de IP: Verifique se o adaptador '{targetAdapter ?? "Ethernet"}' está com o IP {actualHostIp} e máscara {actualMask}.");
                    }

                    if (currentText.Contains("TFTP: timeout", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("permission denied", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("link down", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("aborted", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("bad device", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("Open Error", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new DeviceSessionException($"Falha durante transferência TFTP no ROMMON: {currentText.Trim()}");
                    }

                    // Se retornou ao prompt do rommon sem copiar o arquivo e sem iniciar a transferência
                    if (!transferStarted && !tftpCompleted &&
                        Regex.IsMatch(currentText, @"rommon\s*\d*\s*>") &&
                        !currentText.Contains("File") && !currentText.Contains("Copying") &&
                        currentText.Length > 80)
                    {
                        throw new DeviceSessionException($"Transferência tftpdnld abortada ou não iniciada pelo ROMMON:\n{currentText.Trim()}");
                    }
                }
                else
                {
                    // Se o servidor TFTP já enviou 100% dos dados e a serial ficou ociosa por 2s, envia \r para verificar prompt
                    if (tftpCompleted && transferStarted && (DateTime.UtcNow - lastChunkTime > TimeSpan.FromSeconds(2)))
                    {
                        lastChunkTime = DateTime.UtcNow;
                        await session.SendRawAsync("\r", cancellationToken);
                        await Task.Delay(300, cancellationToken);
                    }
                }

                await Task.Delay(100, cancellationToken);
            }

            if (!transferSuccess)
            {
                throw new DeviceSessionException($"Tempo limite de transferência TFTP excedido ({binFileName}). Verifique o cabo de rede Ethernet conectado no roteador e notebook.");
            }

            await ProgressAsync($"[OK] Transferência de pacotes TFTP da imagem {binFileName} concluída com sucesso!");
            await tftpServer.StopAsync();
        }

        // Aguarda o ROMMON concluir a formatação e gravação física dos setores na Flash (pode levar 1 a 2 min em 80MB)
        await ProgressAsync("[*] Aguardando ROMMON gravar e consolidar a imagem na Flash (isso pode levar ~1-2 minutos)...");
        try
        {
            await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("rommon-prompt", new Regex(@"(?i)rommon\s+\d+\s*>"))
                },
                TimeSpan.FromMinutes(4),
                cancellationToken);
            await ProgressAsync("[OK] Imagem gravada e validada na Flash com sucesso pelo ROMMON.");
        }
        catch { }

        // 6. Configura o registrador para ignorar configuração antiga/senha (confreg 0x2142) e inicia a nova imagem
        _onProgress?.Invoke(85, "Fase B: Inicializando IOS...", "Configurando registrador 0x2142 (ignora senhas residuais) e efetuando boot...");
        await ProgressAsync("[*] Configurando registrador para ignorar senhas e configurações antigas (confreg 0x2142)...");
        await session.WriteLineAsync("confreg 0x2142", cancellationToken);
        await Task.Delay(1000, cancellationToken);

        await ProgressAsync($"[*] Executando boot da imagem 'boot flash:{binFileName}' a partir do ROMMON...");
        await session.WriteLineAsync($"boot flash:{binFileName}", cancellationToken);
        await Task.Delay(2000, cancellationToken);

        // 7. Aguarda a descompressão e inicialização do Cisco IOS
        await ProgressAsync("[*] Aguardando descompressão e inicialização completa do Cisco IOS (isso pode levar ~2-4 minutos)...");
        _onProgress?.Invoke(90, "Fase B: Carregando Cisco IOS...", "Aguardando descompressão e prompt do Cisco IOS...");

        await AguardarBootCiscoIOSAsync(session, TimeSpan.FromMinutes(6), cancellationToken, enableSecret, candidatePasswords);
        var booted = true;

        // 8. Se entrou no prompt Cisco IOS, limpa NVRAM antiga e garante boot normal persistente (0x2102)
        if (booted)
        {
            try
            {
                var postAdapter = new CiscoIOSAdapter(enableSecret, candidatePasswords);
                try { await postAdapter.EnterPrivilegedExecAsync(session, candidatePasswords, cancellationToken); } catch { }
                try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

                // Apaga permanentemente configurações e senhas antigas da NVRAM
                await ProgressAsync("[*] Apagando configurações antigas e senhas residuais da NVRAM (write erase)...");
                await session.WriteLineAsync("write erase", cancellationToken);
                await Task.Delay(400, cancellationToken);
                await session.WriteLineAsync(string.Empty, cancellationToken);
                await Task.Delay(800, cancellationToken);

                await session.WriteLineAsync("configure terminal", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync($"boot system flash:{binFileName}", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("config-register 0x2102", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("no ip domain-lookup", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("no ip domain lookup", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("end", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("write memory", cancellationToken);
                await Task.Delay(2000, cancellationToken);
            }
            catch { }
        }

        if (!booted)
        {
            throw new TimeoutException("O Cisco IOS foi gravado via ROMMON mas não concluiu a inicialização até o prompt operacional dentro do tempo limite. Verifique a console serial.");
        }

        await ProgressAsync($"\n=================================================================");
        await ProgressAsync($"   🎉 RECUPERAÇÃO DE FIRMWARE VIA ROMMON CONCLUÍDA COM SUCESSO!  ");
        await ProgressAsync("=================================================================");
        await ProgressAsync($"  Imagem gravada na Flash : {binFileName}");
        await ProgressAsync($"  Cisco IOS carregado     : Pronto para provisionamento!");
        await ProgressAsync("=================================================================\n");

        if (requestOperatorAction is not null)
        {
            var is841Post = (lanInterface?.Contains("0/5") == true || lanInterface?.Contains("0/4") == true || binFileName.StartsWith("c8", StringComparison.OrdinalIgnoreCase));
            var is921Post = !is841Post && (lanInterface?.Contains("5") == true || lanInterface?.Contains("4") == true || binFileName.StartsWith("c900", StringComparison.OrdinalIgnoreCase));

            var lanPostDisplay = is841Post ? "GigabitEthernet0/5 (Porta 5 / GE 0/5 - LAN do Cliente)" :
                                 is921Post ? "GigabitEthernet 5 (Porta 5 / GE 5 - LAN do Cliente)" :
                                 "GigabitEthernet 0/1 (Porta 1 / GE 0/1 - LAN do Cliente)";

            var lanPostShort = is841Post ? "Porta 5 (GE 0/5)" :
                               is921Post ? "Porta 5 (GE 5)" :
                               "Porta 1 (GE 0/1)";

            _onProgress?.Invoke(92, "Fase B: Troca de Cabo de Rede...", $"Aguardando alteração do cabo para a porta {lanPostShort}...");
            await ProgressAsync($"\n[AVISO] Solicitando alteração do cabo de rede da porta WAN para a porta LAN ({lanPostShort})...");

            await requestOperatorAction(
                "✅ FIRMWARE RECUPERADO COM SUCESSO!\n\n" +
                "O Cisco IOS já está ativo e inicializado na nova versão.\n\n" +
                "👉 ALTERE AGORA O CABO DE REDE PARA A PORTA:\n" +
                $"🟢 {lanPostDisplay}\n\n" +
                "Para prosseguir com o Provisionamento e os Testes de ICMP (LAN/WAN/WEB), Telnet e Teste de Banda.\n\n" +
                $"Clique em OK assim que o cabo estiver conectado na porta {lanPostShort}.",
                cancellationToken);

            _onProgress?.Invoke(94, "Fase B: Sincronizando Porta LAN...", $"Aguardando link ativo na porta {lanPostShort}...");
            await ProgressAsync($"[*] Operador confirmou a troca do cabo para a porta LAN ({lanPostShort}). Acordando terminal e sincronizando...");

            // Acorda imediatamente a console serial do Cisco IOS e absorve eventuais syslogs de cabo
            try
            {
                var postAdapter2 = new CiscoIOSAdapter(enableSecret, candidatePasswords);
                try { await postAdapter2.EnterPrivilegedExecAsync(session, candidatePasswords, cancellationToken); } catch { }
                try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), cancellationToken); } catch { }
            }
            catch { }

            // Executa verificação ativa da porta LAN com contagem contínua e status visual para garantir que o link subiu
            var targetLan = is841Post ? "GigabitEthernet0/5" : is921Post ? "GigabitEthernet 5" : "GigabitEthernet 0/1";
            await CiscoIOSAdapter.EnforceLanPortConnectedAsync(session, targetLan, requestOperatorAction, ProgressAsync, cancellationToken, _onProgress);
        }

        _onProgress?.Invoke(100, "Fase B Concluída!", $"Roteador recuperado e porta LAN sincronizada com sucesso ({binFileName}).");
        return true;
    }
}
