using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Telnet;

namespace NetworkDevice.Cisco;

/// <summary>
/// Upgrade de IOS via HTTP servido pelo celular (<c>copy http://celular:porta/arquivo flash:</c>).
/// Mesma esteira do <c>CiscoIOSUpgrader</c> (TFTP), mas sem servidor na porta 69 — que apps
/// Android não-root não podem abrir (bloqueio de kernel). O IOS aceita porta não-padrão
/// no copy http (doc oficial usa :8080). Transport-agnóstico: roda sobre console serial
/// ou Telnet (híbrido). ROMMON (flash vazia, só tftpdnld :69) orienta uso de notebook.
/// </summary>
public sealed class CiscoHttpUpgrader
{
    private static readonly Regex PromptConfirmRegex = new(
        @"(?i)(?:Address or name of remote host|Source filename|Destination filename|erase flash|over-write|continue\?|\[confirm\]|\?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex PromptLineRegex = new(
        @"^[A-Za-z0-9_\-\.]+(?:\(config[^\)]*\))?\s*[>#]\s*$",
        RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;

    public CiscoHttpUpgrader(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    public static string BuildCopyCommand(string phoneIp, int phonePort, string fileName, string fsPrefix = "flash:")
    {
        var pfx = !string.IsNullOrWhiteSpace(fsPrefix) ? fsPrefix.Trim() : "flash:";
        if (!pfx.EndsWith(":")) pfx += ":";
        return $"copy http://{phoneIp}:{phonePort}/{fileName} {pfx}{fileName}";
    }

    /// <param name="session">Sessão (console ou Telnet) já aberta no transporte.</param>
    /// <param name="fileName">Nome base do .bin servido pelo HTTP do celular.</param>
    /// <param name="phoneIp">IP do celular na LAN do roteador (adaptador Ethernet OTG).</param>
    /// <param name="phonePort">Porta efetiva do servidor HTTP embarcado.</param>
    /// <param name="fileSizeBytes">Tamanho do .bin (dimensiona o timeout da cópia).</param>
    /// <param name="routerTempIp">IP temporário p/ LAN quando o roteador está zerado (null = já tem IP).</param>
    /// <param name="routerTempMask">Máscara do IP temporário.</param>
    /// <param name="lanInterface">Interface LAN p/ IP temporário (null = auto-detecta via show).</param>
    /// <param name="expectedMd5">MD5 opcional p/ 'verify /md5' pós-cópia.</param>
    public async Task<bool> UpgradeAsync(
        DeviceSession session,
        string fileName,
        string phoneIp,
        int phonePort,
        long fileSizeBytes = 0,
        string? routerTempIp = null,
        string? routerTempMask = null,
        string? lanInterface = null,
        string? expectedMd5 = null,
        CancellationToken cancellationToken = default,
        string? enableSecret = null,
        IEnumerable<string>? candidatePasswords = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Nome do firmware inválido.", nameof(fileName));
        if (string.IsNullOrWhiteSpace(phoneIp)) throw new ArgumentException("IP do celular inválido.", nameof(phoneIp));

        // 0. ROMMON não tem cliente HTTP (só tftpdnld :69, que o celular não pode servir)
        var prompt0 = (session.CurrentPrompt ?? string.Empty).Trim();
        if (session.Mode == ExecMode.Rommon || prompt0.StartsWith("rommon", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeviceSessionException(
                "Equipamento em ROMMON (flash vazia): o modo ROMMON só transfere via 'tftpdnld' (porta 69), " +
                "que o celular não pode servir. Use um notebook com servidor TFTP " +
                "(variáveis: IP_ADDRESS=<ip-router>, IP_SUBNET_MASK=<máscara>, DEFAULT_GATEWAY=<gw>, " +
                "TFTP_SERVER=<ip-notebook>, TFTP_FILE=<arquivo>, comando 'tftpdnld -r').");
        }

        await ProgressAsync($"[*] INICIANDO UPGRADE DE IOS VIA HTTP ({fileName})...");
        await ProgressAsync($"[*] Servidor do celular: http://{phoneIp}:{phonePort}/{fileName}");

        // 1. Modo privilegiado + sem paginação (reusa a normalização fiel do provisionamento)
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(enableSecret)) candidates.Add(enableSecret);
        if (candidatePasswords != null) candidates.AddRange(candidatePasswords);
        await CiscoSaipConfigurator.EnsurePrivilegedExecAsync(session, candidates, cancellationToken);
        try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

        // 2. Estado atual: versão, flash, boot
        var showVer = await CiscoSaipConfigurator.SendShowAsync(session, "show version", TimeSpan.FromSeconds(15), cancellationToken);
        var fsPrefix = CiscoIOSFlashParser.DetectFlashFilesystemPrefix(showVer);
        var dirFlash = await CiscoSaipConfigurator.SendShowAsync(session, $"dir {fsPrefix}", TimeSpan.FromSeconds(15), cancellationToken);
        if (dirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase) || dirFlash.Contains("% Error", StringComparison.OrdinalIgnoreCase))
            dirFlash = await CiscoSaipConfigurator.SendShowAsync(session, "dir flash:", TimeSpan.FromSeconds(15), cancellationToken);
        if (dirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase) || dirFlash.Contains("% Error", StringComparison.OrdinalIgnoreCase))
            dirFlash = await CiscoSaipConfigurator.SendShowAsync(session, "dir", TimeSpan.FromSeconds(15), cancellationToken);
        var showBoot = await CiscoSaipConfigurator.SendShowAsync(session, "show running-config | include boot", TimeSpan.FromSeconds(10), cancellationToken);

        var isRunningTarget = CiscoIOSFirmwareVersionInspector.IsSameVersion(showVer, fileName, out var curVer, out _);
        var isFileOnFlash = dirFlash.Contains(fileName, StringComparison.OrdinalIgnoreCase);

        // Se o arquivo já existe na flash, valida se o tamanho confere (evita aceitar arquivos incompletos/truncados de tentativas anteriores)
        if (isFileOnFlash && fileSizeBytes > 0)
        {
            var existingSize = CiscoIOSFlashParser.ParseFileSize(dirFlash, fileName);
            if (existingSize > 0 && existingSize < (fileSizeBytes * 0.95))
            {
                await ProgressAsync($"[!] Arquivo {fileName} encontrado na Flash porém incompleto ({existingSize / (1024.0 * 1024.0):F1} MB vs {fileSizeBytes / (1024.0 * 1024.0):F1} MB esperados). Excluindo para regravação limpa...");
                await DeleteFileFromFlashAsync(session, fileName, fsPrefix, cancellationToken);
                isFileOnFlash = false;
            }
        }

        var isBootConfigured = showBoot.Contains(fileName, StringComparison.OrdinalIgnoreCase);
        await ProgressAsync($"[*] Versão atual: {curVer.DisplayString} | Arquivo na flash: {(isFileOnFlash ? "SIM" : "NÃO")} | Boot configurado: {(isBootConfigured ? "SIM" : "NÃO")}");

        // Se a imagem já existe na Flash mas há versões concorrentes antigas, exclui para garantir boot exclusivo no alvo
        if (isFileOnFlash)
        {
            var conflictingOnFlash = CiscoIOSFlashParser.GetConflictingFirmwareFiles(dirFlash, fileName);
            if (conflictingOnFlash.Count > 0)
            {
                await ProgressAsync($"[*] [Flash] Detectada(s) {conflictingOnFlash.Count} versão(ões) concorrente(s) na Flash. Limpando para garantir boot exclusivo em {fileName}...");
                foreach (var cf in conflictingOnFlash)
                {
                    await DeleteFileFromFlashAsync(session, cf, fsPrefix, cancellationToken);
                }
            }
        }

        if (isRunningTarget && isBootConfigured && isFileOnFlash)
        {
            await ProgressAsync("[OK] Firmware alvo já em execução e boot configurado — nada a fazer.");
            return true;
        }

        // 3. Cliente HTTP disponível na imagem?
        var httpClientOut = await CiscoSaipConfigurator.SendShowAsync(session, "show ip http client all", TimeSpan.FromSeconds(10), cancellationToken);
        if (httpClientOut.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase) ||
            httpClientOut.Contains("% Incomplete", StringComparison.OrdinalIgnoreCase))
        {
            // Valida se o comando 'copy ?' lista http: antes de emitir erro impeditivo
            var copyHelp = await CiscoSaipConfigurator.SendShowAsync(session, "copy ?", TimeSpan.FromSeconds(10), cancellationToken);
            if (!copyHelp.Contains("http:", StringComparison.OrdinalIgnoreCase))
            {
                throw new DeviceSessionException(
                    "Esta imagem IOS não possui suporte a cliente HTTP ('show ip http client all' inválido e 'copy http:' indisponível). " +
                    "Use um notebook com servidor TFTP (:69) e 'copy tftp:' para este equipamento.");
            }
        }

        // 4. IP temporário no roteador (caso zerado) + teste de alcance ao celular
        if (!string.IsNullOrWhiteSpace(routerTempIp))
        {
            var brief = await CiscoSaipConfigurator.SendShowAsync(session, "show ip interface brief", TimeSpan.FromSeconds(15), cancellationToken);
            if (!brief.Contains(routerTempIp.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                lanInterface ??= await DetectLanInterfaceAsync(session, cancellationToken);
                var mask = string.IsNullOrWhiteSpace(routerTempMask) ? "255.255.255.0" : routerTempMask.Trim();
                await ProgressAsync($"[*] Aplicando IP temporário {routerTempIp}/{mask} em {lanInterface}...");
                await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(15), cancellationToken);
                await session.SendCommandAsync($"interface {lanInterface}", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync($"ip address {routerTempIp.Trim()} {mask}", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync("no shutdown", TimeSpan.FromSeconds(10), cancellationToken);
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
                await Task.Delay(1500, cancellationToken);
            }
            else
            {
                await ProgressAsync($"[*] IP temporário {routerTempIp} já configurado e ativo no roteador.");
            }
        }

        try
        {
            var ping = await session.SendCommandAsync($"ping {phoneIp}", TimeSpan.FromSeconds(15), cancellationToken);
            if (ping.Contains("!!!!", StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(ping, @"(?i)success rate is 100|Success rate is 100"))
                await ProgressAsync($"[OK] Celular alcançável ({phoneIp}).");
            else
                await ProgressAsync($"[AVISO] Ping ao celular sem 100% de êxito — prosseguindo mesmo assim.\n{ping.Trim()}");
        }
        catch (Exception ex)
        {
            await ProgressAsync($"[AVISO] Ping falhou ({ex.Message}) — prosseguindo com a cópia HTTP...");
        }

        // 5. Cópia HTTP (interativa: Destination filename + [confirm] + transferência longa)
        if (!isFileOnFlash)
        {
            var availBytes = CiscoIOSFlashParser.ParseAvailableBytes(dirFlash);
            var confFilesPre = CiscoIOSFlashParser.GetConflictingFirmwareFiles(dirFlash, fileName);
            if (confFilesPre.Count > 0 && (availBytes > 0 && fileSizeBytes > 0 && availBytes < fileSizeBytes * 1.05))
            {
                var availMb = (availBytes / (1024.0 * 1024.0)).ToString("N1");
                var sizeMb = (fileSizeBytes / (1024.0 * 1024.0)).ToString("N1");
                await ProgressAsync($"[!] Espaço livre na Flash ({availMb} MB) insuficiente para o novo firmware ({sizeMb} MB).");
                await ProgressAsync($"[*] Excluindo versão(ões) concorrente(s) na Flash para liberar espaço...");
                foreach (var cf in confFilesPre)
                {
                    await DeleteFileFromFlashAsync(session, cf, fsPrefix, cancellationToken);
                }
            }

            await CopyHttpAsync(session, phoneIp, phonePort, fileName, fileSizeBytes, fsPrefix, cancellationToken);

            dirFlash = await CiscoSaipConfigurator.SendShowAsync(session, $"dir {fsPrefix}", TimeSpan.FromSeconds(15), cancellationToken);
            if (dirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase) || dirFlash.Contains("% Error", StringComparison.OrdinalIgnoreCase))
                dirFlash = await CiscoSaipConfigurator.SendShowAsync(session, "dir flash:", TimeSpan.FromSeconds(15), cancellationToken);

            if (!dirFlash.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                throw new DeviceSessionException($"Arquivo {fileName} não localizado na flash: após a transferência.");
            await ProgressAsync($"[OK] {fileName} transferido via HTTP e validado na flash:!");
        }
        else
        {
            await ProgressAsync($"[*] Arquivo já na flash: — pulando transferência ({fileName}).");
        }

        // 6. MD5 opcional
        if (!string.IsNullOrWhiteSpace(expectedMd5))
        {
            await ProgressAsync("[*] Verificando MD5 da imagem (1-2 min)...");
            var md5 = await session.SendCommandAsync($"verify /md5 flash:{fileName}", TimeSpan.FromMinutes(3), cancellationToken);
            await ProgressAsync(md5.Contains(expectedMd5.Trim(), StringComparison.OrdinalIgnoreCase)
                ? "[OK] MD5 conferido."
                : $"[AVISO] MD5 divergente ou não confirmado:\n{md5.Trim()}");
        }

        // 6b. Limpeza pós-transferência de qualquer outra versão presente na Flash para garantir boot exclusivo no firmware indicado
        var curDirFlash = await CiscoSaipConfigurator.SendShowAsync(session, $"dir {fsPrefix}", TimeSpan.FromSeconds(15), cancellationToken);
        if (curDirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase) || curDirFlash.Contains("% Error", StringComparison.OrdinalIgnoreCase))
            curDirFlash = await CiscoSaipConfigurator.SendShowAsync(session, "dir flash:", TimeSpan.FromSeconds(15), cancellationToken);

        var remainingConf = CiscoIOSFlashParser.GetConflictingFirmwareFiles(curDirFlash, fileName);
        if (remainingConf.Count > 0)
        {
            await ProgressAsync($"[*] [Flash] Limpando {remainingConf.Count} versão(ões) concorrente(s) na Flash para liberar espaço e garantir boot exclusivo em {fileName}...");
            foreach (var cf in remainingConf)
            {
                await DeleteFileFromFlashAsync(session, cf, fsPrefix, cancellationToken);
            }
        }

        // 7a. Configuração exclusiva de boot para o novo firmware homologado
        await ProgressAsync($"[*] Configurando boot system exclusivo para 'flash:{fileName}'...");
        try
        {
            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync("no boot system", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync($"boot system flash:{fileName}", TimeSpan.FromSeconds(10), cancellationToken);
            if (fsPrefix.StartsWith("usbflash", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCommandAsync($"boot system {fsPrefix}{fileName}", TimeSpan.FromSeconds(10), cancellationToken);
            }
            await session.SendCommandAsync("config-register 0x2102", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
        }
        catch (Exception exBoot)
        {
            await ProgressAsync($"[AVISO] Configuração de boot system: {exBoot.Message}");
        }

        // 7b. Zeramento da NVRAM para garantir que o roteador reinicie 100% limpo em padrão de fábrica
        // ATENÇÃO: NÃO executar 'write memory' após o 'write erase', para não sobrescrever a NVRAM limpa com a running-config suja!
        await ProgressAsync("[*] Zerando configurações antigas e senhas residuais da NVRAM (write erase)...");
        try
        {
            await session.WriteLineAsync("write erase", cancellationToken);
            await Task.Delay(500, cancellationToken);
            await session.WriteLineAsync(string.Empty, cancellationToken); // Confirma [confirm] com Enter
            await Task.Delay(1000, cancellationToken);
        }
        catch { }

        // 8. Reload único conjunto (novo firmware + base limpa em um só boot)
        await ProgressAsync("[*] [RELOAD AUTOMÁTICO CONJUNTO] Reiniciando roteador Cisco (novo firmware + base limpa)...");
        await session.WriteLineAsync("reload", cancellationToken);
        try
        {
            var reloadRes = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("Proceed with reload? [confirm]", "Proceed with reload? [confirm]"),
                    new StopCondition.Contains("[confirm]", "[confirm]"),
                    new StopCondition.Contains("System configuration has been modified", "System configuration has been modified"),
                },
                TimeSpan.FromSeconds(20), cancellationToken);
            var tail = reloadRes.Output;
            if (tail.Contains("System configuration has been modified", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("no", cancellationToken); // não salva running-config sobre a NVRAM limpa
                await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.Contains("Proceed with reload? [confirm]", "Proceed with reload? [confirm]"),
                        new StopCondition.Contains("[confirm]", "[confirm]")
                    },
                    TimeSpan.FromSeconds(15), cancellationToken);
            }
            await session.WriteLineAsync(string.Empty, cancellationToken); // confirma [confirm]
            session.ResetPrompt();
            await Task.Delay(10000, cancellationToken); // Aguarda reset físico da CPU do roteador
        }
        catch (Exception exReload)
        {
            await ProgressAsync($"[AVISO] Confirmação de reload: {exReload.Message} — equipamento reiniciando.");
            await Task.Delay(5000, cancellationToken);
        }

        // Se a sessão for Telnet (sem console serial ativo), o reload desconecta a rede e o roteador reinicia limpo.
        // A checagem da versão e retomada do provisionamento acontecerá assim que o operador reconectar a serial.
        if (session.Transport is TcpTelnetTransport)
        {
            await ProgressAsync($"[✓] Firmware {fileName} gravado com sucesso na flash, boot system definido e roteador reiniciando (base limpa)!");
            return true;
        }

        await ProgressAsync("[*] Aguardando boot do novo IOS (até 8 min)...");
        try
        {
            var upgraderHelper = new CiscoIOSUpgrader(_progress, null);
            await upgraderHelper.AguardarBootCiscoIOSAsync(session, TimeSpan.FromMinutes(8), cancellationToken, enableSecret, candidatePasswords);
            await upgraderHelper.EstabilizarCliPosBootAsync(session, enableSecret, candidatePasswords, cancellationToken);
        }
        catch (Exception ex)
        {
            await ProgressAsync($"[AVISO] Boot monitoring Cisco IOS: {ex.Message}");
        }

        await Task.Delay(2000, cancellationToken);
        var verAfter = await CiscoSaipConfigurator.SendShowAsync(session, "show version", TimeSpan.FromSeconds(30), cancellationToken);
        if (CiscoIOSFirmwareVersionInspector.IsSameVersion(verAfter, fileName, out _, out _))
        {
            await ProgressAsync($"[OK] UPGRADE CONCLUÍDO — {fileName} em execução!");
            return true;
        }

        await ProgressAsync("[AVISO] Boot retornou, mas a versão não confere — valide 'show version' manualmente.");
        return false;
    }

    private async Task CopyHttpAsync(DeviceSession session, string phoneIp, int phonePort,
        string fileName, long fileSizeBytes, string fsPrefix, CancellationToken ct)
    {
        var copyCmd = BuildCopyCommand(phoneIp, phonePort, fileName, fsPrefix);
        await ProgressAsync($"[*] Copiando: {copyCmd} ...");
        await session.WriteLineAsync(copyCmd, ct);
        var full = new System.Text.StringBuilder();

        var copyConds = new StopCondition[]
        {
            new StopCondition.LineRegex("confirm", PromptConfirmRegex),
            new StopCondition.LineRegex("prompt", PromptLineRegex),
            new StopCondition.Contains("accessing", "Accessing http:"),
            new StopCondition.Contains("loading", "Loading "),
            new StopCondition.Contains("ok", "[OK"),
            new StopCondition.Contains("bytes_copied", "bytes copied"),
            new StopCondition.Contains("error", "%Error"),
            new StopCondition.Contains("error_sp", "% Error"),
            new StopCondition.Contains("invalid", "% Invalid"),
            new StopCondition.Contains("timedout", "Timed out"),
            new StopCondition.Contains("refused", "Connection refused"),
            new StopCondition.Contains("unreachable", "unreachable"),
            new StopCondition.Contains("unknownhost", "Unknown host")
        };

        // 5a. Responde a eventuais confirmações sequenciais do Cisco IOS (Destination filename, overwrite, erase flash, etc.)
        var confirmDeadline = DateTime.UtcNow.AddSeconds(45);
        bool transferStarted = false;
        while (DateTime.UtcNow < confirmDeadline && !ct.IsCancellationRequested)
        {
            try
            {
                var exp = await session.WaitForAsync(copyConds, TimeSpan.FromSeconds(5), ct);
                full.Append(exp.Output);

                if (exp.Matched is StopCondition.LineRegex lrConf && lrConf.Name == "confirm")
                {
                    var lastLine = exp.Output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "confirmação";
                    await ProgressAsync($"[*] Confirmação solicitada pelo Cisco ({lastLine.Trim()}) — confirmando com ENTER...");
                    await session.WriteLineAsync(string.Empty, ct);
                    await Task.Delay(300, ct);
                    continue;
                }

                if (exp.Matched is StopCondition.Contains cStart && (cStart.Text == "Accessing http:" || cStart.Text == "Loading "))
                {
                    transferStarted = true;
                    await ProgressAsync("[*] Transferência HTTP iniciada e recebendo dados no Cisco IOS...");
                    break;
                }

                if (exp.Matched is StopCondition.Contains cErr && (cErr.Text == "%Error" || cErr.Text == "% Error" || cErr.Text == "% Invalid" || cErr.Text == "Timed out" || cErr.Text == "Connection refused" || cErr.Text == "unreachable" || cErr.Text == "Unknown host"))
                {
                    throw new DeviceSessionException($"Falha ao iniciar cópia HTTP no Cisco: {exp.Output.Trim()}");
                }

                if (exp.Matched is StopCondition.LineRegex lrPrompt && lrPrompt.Name == "prompt")
                {
                    CheckCopyErrors(full.ToString(), fileName);
                    break;
                }
            }
            catch (SessionTimeoutException)
            {
                if (transferStarted)
                    break;
            }
        }

        // 5b. Monitora transferência do arquivo até a conclusão total (timeout dimensionado pelo tamanho)
        var minutes = 5 + (fileSizeBytes > 0 ? fileSizeBytes / (20L * 1024 * 1024) : 2);
        var copyDeadline = DateTime.UtcNow.AddMinutes(Math.Min(minutes, 20));
        bool isCopying = true;
        while (isCopying && DateTime.UtcNow < copyDeadline && !ct.IsCancellationRequested)
        {
            try
            {
                var exp = await session.WaitForAsync(copyConds, TimeSpan.FromSeconds(5), ct);
                full.Append(exp.Output);

                // Confirmação tardia inesperada
                if (exp.Matched is StopCondition.LineRegex lrConf && lrConf.Name == "confirm")
                {
                    await session.WriteLineAsync(string.Empty, ct);
                    continue;
                }

                if (exp.Matched is StopCondition.Contains cOk && (cOk.Text == "[OK" || cOk.Text == "bytes copied"))
                {
                    await ProgressAsync("[OK] Cisco acusou recebimento e gravação completa do arquivo via HTTP!");
                    try
                    {
                        var pExp = await session.WaitForAsync(new StopCondition[] { new StopCondition.LineRegex("prompt", PromptLineRegex) }, TimeSpan.FromSeconds(15), ct);
                        full.Append(pExp.Output);
                    }
                    catch (SessionTimeoutException) { }
                    isCopying = false;
                    break;
                }

                if (exp.Matched is StopCondition.LineRegex lr && lr.Name == "prompt")
                {
                    break;
                }

                if (exp.Matched is StopCondition.Contains cErr && (cErr.Text == "%Error" || cErr.Text == "% Error" || cErr.Text == "% Invalid" || cErr.Text == "Timed out" || cErr.Text == "Connection refused" || cErr.Text == "unreachable" || cErr.Text == "Unknown host"))
                {
                    throw new DeviceSessionException($"Falha na cópia HTTP de {fileName}: {exp.Output.Trim()}");
                }
            }
            catch (SessionTimeoutException)
            {
                continue; // transferência em andamento (série de '!' na console)
            }
        }

        var text = full.ToString();
        CheckCopyErrors(text, fileName);
        await ProgressAsync($"[*] Transferência HTTP finalizada ({text.Length} caracteres de log).");
    }

    private static void CheckCopyErrors(string output, string fileName)
    {
        if (output.Contains("%Error", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("% Error", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("% Invalid", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Timed out", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Unknown host", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("unreachable", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Aborted", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("No such file", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeviceSessionException($"Falha na cópia HTTP de {fileName}. Resposta: {output.Trim()}");
        }
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

    private static async Task<string> DetectLanInterfaceAsync(DeviceSession session, CancellationToken ct)
    {
        try
        {
            var brief = await CiscoSaipConfigurator.SendShowAsync(session, "show ip interface brief", TimeSpan.FromSeconds(15), ct);
            var physicallyConnected = CiscoSaipConfigurator.DetectConnectedPhysicalInterface(brief);
            if (!string.IsNullOrWhiteSpace(physicallyConnected))
            {
                return physicallyConnected;
            }
            var (_, lan) = CiscoSaipConfigurator.DetectInterfaces(brief);
            return lan;
        }
        catch
        {
            return "GigabitEthernet0/1";
        }
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }
}
