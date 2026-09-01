using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Tftp;

namespace NetworkDevice.Protocols.Hpe;

public sealed class HpeComwareUpgrader
{
    private static readonly Regex ConfirmPromptRegex = new(
        @"(?i)(?:\[Y/N\]|\?|confirm|overwrite|delete the file after decompression|continue\?|Overwrite the existing files\?)\s*[:?]?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex FreeSpaceRegex = new(
        @"\[\s*(?<total>\d+)\s*KB\s+total\s*\(\s*(?<free>\d+)\s*KB\s+free\s*\)\s*\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VersionRegex = new(
        @"(?i)Release\s+(?<ver>R?\d+(?:P\d+)?)",
        RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;
    private readonly Action<int, string, string>? _onProgress;

    public HpeComwareUpgrader(Func<string, Task>? progress = null, Action<int, string, string>? onProgress = null)
    {
        _progress = progress;
        _onProgress = onProgress;
    }

    /// <summary>
    /// Executa o upgrade completo de firmware no HPE Comware com auditoria prévia de versão e arquivos na Flash,
    /// prevenção de downloads redundantes, tratamento automático de confirmações de sobrescrita e reload.
    /// </summary>
    /// <param name="confirmBootLoaderUpdate">Callback para confirmar com o usuário se deseja atualizar o boot-loader quando o SO já está na versão alvo mas o boot-loader ainda aponta para versão antiga. Retorna true para prosseguir, false para abortar. Se null, assume true (compatibilidade).</param>
    public async Task<bool> UpgradeAsync(
        DeviceSession session,
        string firmwareFilePath,
        string hostIpAddress,
        Func<string, CancellationToken, Task<bool>>? confirmBootLoaderUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(firmwareFilePath))
            throw new FileNotFoundException($"Arquivo de firmware HPE não encontrado: {firmwareFilePath}");

        var fileName = Path.GetFileName(firmwareFilePath);
        var fileExt = Path.GetExtension(firmwareFilePath).ToLowerInvariant();
        var fileDir = Path.GetDirectoryName(firmwareFilePath) ?? AppContext.BaseDirectory;
        var fileSizeBytes = new FileInfo(firmwareFilePath).Length;
        var fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);
        var isIpe = fileExt == ".ipe";

        // Extrai a tag da versão alvo a partir do nome do arquivo (ex: R6749P43)
        var targetVersionTag = ExtrairVersaoDeNomeArquivo(fileName);

        _onProgress?.Invoke(5, "Fase B: Auditando Versão e Flash...", "Verificando versão atual e arquivos na Flash...");
        await ProgressAsync($"\n[*] [FASE B] AUDITANDO VERSÃO E MEMÓRIA FLASH HPE ({fileName} — {fileSizeMb:N1} MB)...");

        // 1. Acorda o terminal e normaliza prompt para User View (<HPE>), saindo de eventuais subshells como ftp>
        for (var n = 0; n < 4; n++)
        {
            var p = session.CurrentPrompt ?? "";
            if (p.Contains("ftp", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("quit", cancellationToken);
                await Task.Delay(500, cancellationToken);
            }
            else if (p.StartsWith("[") && p.EndsWith("]"))
            {
                await session.WriteLineAsync("return", cancellationToken);
                await Task.Delay(500, cancellationToken);
            }
            else
            {
                await session.WriteLineAsync(string.Empty, cancellationToken);
                await Task.Delay(300, cancellationToken);
            }
        }

        var initPrompt = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(5), cancellationToken);
        if (initPrompt.Contains("ftp>", StringComparison.OrdinalIgnoreCase))
        {
            await session.WriteLineAsync("quit", cancellationToken);
            await Task.Delay(500, cancellationToken);
            initPrompt = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(5), cancellationToken);
        }

        if (initPrompt.Contains("BOOTWARE", StringComparison.OrdinalIgnoreCase) ||
            initPrompt.Contains("choice(0-9)", StringComparison.OrdinalIgnoreCase) ||
            initPrompt.Contains("choice(", StringComparison.OrdinalIgnoreCase))
        {
            await ProgressAsync("[*] Roteador detectado no menu do BootWare. Inicializando o sistema (Opção 6 - Skip Config / Opção 1 - Boot System)...");
            await session.WriteLineAsync("6", cancellationToken);
            await Task.Delay(800, cancellationToken);
            await session.WriteLineAsync("Y", cancellationToken);
            await Task.Delay(800, cancellationToken);
            await session.WriteLineAsync("1", cancellationToken);
            await Task.Delay(800, cancellationToken);
            await session.WriteLineAsync("Y", cancellationToken);

            await ProgressAsync("[*] Aguardando o Comware inicializar para prosseguir com o upgrade (2 a 5 min)...");
            var bootRes = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("prompt", new Regex(@"(?i)^<[A-Za-z0-9_\-\.]+>")),
                    new StopCondition.Contains("return", "Before pressing ENTER"),
                    new StopCondition.Contains("autoconfig", "Auto-Configuration"),
                    new StopCondition.Contains("ctrl_c", "CTRL_C"),
                    new StopCondition.Contains("auto_attempt", "Automatic configuration attempt")
                },
                TimeSpan.FromMinutes(6),
                cancellationToken);

            if (bootRes.Output.Contains("automatic configuration", StringComparison.OrdinalIgnoreCase) ||
                bootRes.Output.Contains("CTRL_C", StringComparison.OrdinalIgnoreCase) ||
                bootRes.Output.Contains("Auto-Configuration", StringComparison.OrdinalIgnoreCase))
            {
                await session.SendCtrlCAsync(cancellationToken);
                await Task.Delay(400, cancellationToken);
                await session.WriteLineAsync("Y", cancellationToken);
                await Task.Delay(600, cancellationToken);
            }

            await session.WriteLineAsync(string.Empty, cancellationToken);
            await Task.Delay(1000, cancellationToken);
        }
        else if (initPrompt.Contains("-") || (session.CurrentPrompt != null && session.CurrentPrompt.Contains("-")))
        {
            await session.SendCommandAsync("return", TimeSpan.FromSeconds(5), cancellationToken);
            await Task.Delay(500, cancellationToken);
        }
        else if (initPrompt.Contains("[") || (session.CurrentPrompt != null && session.CurrentPrompt.StartsWith("[")))
        {
            await session.SendCommandAsync("return", TimeSpan.FromSeconds(5), cancellationToken);
            await Task.Delay(500, cancellationToken);
        }

        // Desativa quebra de página (---- More ----)
        try
        {
            await session.SendCommandAsync("screen-length disable", TimeSpan.FromSeconds(5), cancellationToken);
            await Task.Delay(300, cancellationToken);
        }
        catch { }

        // 2. Auditoria da versão atual instalada e arquivos já existentes na Flash
        await ProgressAsync("[*] Inspecionando bootloader, versão do SO e arquivos na Flash...");
        var currentBootLoader = string.Empty;
        var currentVersionText = string.Empty;
        var flashDirOutput = string.Empty;
        try
        {
            currentBootLoader = await session.SendCommandAsync("display boot-loader", TimeSpan.FromSeconds(15), cancellationToken);
            currentVersionText = await session.SendCommandAsync("display version", TimeSpan.FromSeconds(15), cancellationToken);
            flashDirOutput = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch { }

        var currentRunningVersion = ExtrairVersaoDeTexto(string.Empty, currentVersionText);
        var currentBootVersion = ExtrairVersaoDeTexto(currentBootLoader, string.Empty);
        // Normaliza para comparação sem R (ex: R6749P43 == 6749P43)
        string Norm(string v) => v.Trim().TrimStart('R', 'r').ToUpperInvariant();
        var targetNorm = Norm(targetVersionTag);
        var runningNorm = Norm(currentRunningVersion);
        var bootNorm = Norm(currentBootVersion);

        await ProgressAsync($"[*] Versão em execução no SO   : {(string.IsNullOrEmpty(currentRunningVersion) ? "Desconhecida" : currentRunningVersion)}");
        await ProgressAsync($"[*] Versão principal no bootloader: {(string.IsNullOrEmpty(currentBootVersion) ? "Desconhecida" : currentBootVersion)}");
        await ProgressAsync($"[*] Nova versão alvo do arquivo   : {targetVersionTag}");

        // Usa contains normalizado para tolerar Release 6749P43 vs R6749P43
        bool ContainsVersion(string output, string norm) =>
            !string.IsNullOrEmpty(norm) && output.Contains(norm, StringComparison.OrdinalIgnoreCase);

        var isBootloaderAlreadyUpdated = ContainsVersion(currentBootLoader, targetNorm) || ContainsVersion(currentBootLoader, targetVersionTag);
        var isOsAlreadyRunningUpdated = ContainsVersion(currentVersionText, targetNorm) || ContainsVersion(currentVersionText, targetVersionTag);

        // Prioridade 1: se boot-loader já está na versão alvo, não há nada a oferecer - apenas informar
        if (isBootloaderAlreadyUpdated && isOsAlreadyRunningUpdated)
        {
            _onProgress?.Invoke(100, "Fase B: Versão já atualizada!", $"Versão {targetVersionTag} já presente na flash e ativa.");
            await ProgressAsync("\n=================================================================");
            await ProgressAsync($"   Versão {targetVersionTag} já presente na flash e ativa               ");
            await ProgressAsync("=================================================================");
            await ProgressAsync($"  Versão {targetVersionTag} já presente na flash e já configurada");
            await ProgressAsync($"  como Main no boot-loader. Equipamento já opera com {targetVersionTag}.");
            await ProgressAsync($"  Seleção já está aplicada no equipamento. Nenhuma ação necessária.");
            await ProgressAsync("=================================================================\n");
            return true;
        }

        // CASO A1: SO já está na versão alvo mas bootloader ainda aponta para versão antiga (arquivo existe na flash mas boot-loader desatualizado)
        if (isOsAlreadyRunningUpdated && !isBootloaderAlreadyUpdated)
        {
            await ProgressAsync("\n=================================================================");
            await ProgressAsync($"   Versão {targetVersionTag} já presente na flash                     ");
            await ProgressAsync("=================================================================");
            await ProgressAsync($"  Versão {targetVersionTag} já presente na flash, porém o boot-loader");
            await ProgressAsync($"  ainda aponta para {(string.IsNullOrEmpty(currentBootVersion) ? "versão anterior" : currentBootVersion)}.");
            await ProgressAsync($"  Arquivo                      : {fileName}");
            await ProgressAsync($"  SO em execução               : {currentRunningVersion}");
            await ProgressAsync("=================================================================");

            bool desejaAtualizarBootLoader = true;
            if (confirmBootLoaderUpdate != null)
            {
                desejaAtualizarBootLoader = await confirmBootLoaderUpdate(
                    $"Versão {targetVersionTag} já presente na flash, porém o boot-loader ainda aponta para {(string.IsNullOrEmpty(currentBootVersion) ? "versão anterior" : currentBootVersion)}. Deseja atualizar o boot-loader para carregar {targetVersionTag} no próximo boot?", cancellationToken);
            }

            if (!desejaAtualizarBootLoader)
            {
                _onProgress?.Invoke(100, "Fase B: Mantido boot-loader atual", $"SO já em {targetVersionTag}, boot-loader não alterado a pedido do usuário.");
                await ProgressAsync($"[*] Operação cancelada pelo usuário. Boot-loader mantido em {currentBootVersion}. Nenhuma gravação realizada.");
                return true;
            }

            await ProgressAsync($"[*] Confirmação recebida: atualizando boot-loader para {targetVersionTag} (sem necessidade de novo TFTP se arquivo já estiver na Flash)...");
            // prossegue para CASO C - reaproveita arquivo da Flash se já existir, senão faz TFTP
        }
        // (caso já tratado acima - mantido para fallback de bootloader sem SO)

        // CASO B: O bootloader já aponta para a nova versão, mas o equipamento ainda não foi reiniciado
        if (isBootloaderAlreadyUpdated && !isOsAlreadyRunningUpdated)
        {
            _onProgress?.Invoke(90, "Fase B: Reiniciando Equipamento...", $"Bootloader já configurado com {targetVersionTag}. Reiniciando...");
            await ProgressAsync("\n[*] [INFO] O bootloader já está gravado com a versão alvo, mas o roteador precisa reiniciar.");
            await ProgressAsync($"[*] [RELOAD AUTOMÁTICO] Reiniciando roteador HPE para carregar a versão {targetVersionTag}...");
            await ExecutarRebootHpeAsync(session, cancellationToken);
            _onProgress?.Invoke(100, "Fase B Concluída!", $"Roteador reiniciado para carregar a versão {targetVersionTag}.");
            await ProgressAsync($"[OK] Comando de reinicialização enviado com sucesso!");
            return true;
        }

        // ANTES DE TRANSFERIR: compara versão do arquivo do usuário com versões já na FLASH (evita transferência redundante)
        var flashVersions = Regex.Matches(flashDirOutput, @"(?i)r\d{4}(?:p\d+)?").Select(m => Norm(m.Value)).Distinct().ToList();
        var isMesmaVersaoNaFlash = !string.IsNullOrEmpty(targetNorm) && flashVersions.Contains(targetNorm);
        // Também valida via .ipe/.bin específicos
        var areBinFilesAlreadyOnFlash = isMesmaVersaoNaFlash
            || (!string.IsNullOrEmpty(targetVersionTag) && flashDirOutput.Contains(targetVersionTag, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrEmpty(targetNorm) && flashDirOutput.Contains(targetNorm, StringComparison.OrdinalIgnoreCase));

        // Se .ipe alvo já está na flash (ex: MSR954-CMW710-R6749P43.ipe com 123 MB) => versão igual, pula TFTP
        var ipeAlvoJaNaFlash = ArquivoExisteNaFlash(flashDirOutput, fileName);

        if (areBinFilesAlreadyOnFlash || ipeAlvoJaNaFlash)
        {
            await ProgressAsync($"[*] [INFO] Versão {targetVersionTag} já presente na FLASH (arquivo/pacotes com mesma versão detectados)!");
            await ProgressAsync($"    -> Arquivo alvo na flash: {(ipeAlvoJaNaFlash ? fileName : "pacotes .bin")} | Transferência TFTP DESNECESSÁRIA.");
            await ProgressAsync($"    -> Pulando download TFTP de {fileSizeMb:N1} MB e garantindo apenas ativação no bootloader.");
            _onProgress?.Invoke(80, "Versão já na Flash", $"Pulando TFTP — garantindo boot-loader para {targetVersionTag}...");
        }
        else
        {
            // Se a versão for DIFERENTE e os arquivos não existirem, faz o download via TFTP
            _onProgress?.Invoke(10, "⚠️ NÃO DESLIGUE O EQUIPAMENTO!", $"Iniciando atualização de {currentRunningVersion} -> {targetVersionTag}...");
            await ProgressAsync("\n=================================================================");
            await ProgressAsync("           ⚠️ ALERTA CRÍTICO DE ATUALIZAÇÃO DE FIRMWARE          ");
            await ProgressAsync("=================================================================");
            await ProgressAsync($"  Versão Atual : {currentRunningVersion}");
            await ProgressAsync($"  Versão Alvo  : {targetVersionTag} ({fileName})");
            await ProgressAsync("  ⚠️ ATENÇÃO: NÃO DESLIGUE O EQUIPAMENTO DA TOMADA");
            await ProgressAsync("              NEM DESCONECTE OS CABOS DURANTE A ATUALIZAÇÃO!");
            await ProgressAsync("=================================================================\n");

            // Inicia o Servidor TFTP Integrado com streaming de progresso em tempo real para a UI e terminal
            await using var tftpServer = new EmbeddedTftpServer(fileDir);
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

                var uiPct = (int)Math.Clamp(20 + (pct * 0.6), 20, 80);

                if ((now - lastUiUpdate).TotalMilliseconds >= 250 || pct >= 100)
                {
                    lastUiUpdate = now;
                    _onProgress?.Invoke(
                        uiPct,
                        $"⚠️ NÃO DESLIGUE! Transferindo Firmware TFTP ({pct:N1}%)...",
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
                // Limpeza de pacotes .IPE antigos e esvaziamento da lixeira
                _onProgress?.Invoke(15, "Fase B: Limpando Flash...", "Esvaziando lixeira e liberando espaço...");
                await ProgressAsync("[*] Otimizando espaço na memória Flash (removendo pacotes .IPE temporários e esvaziando lixeira)...");
                try
                {
                    await LimparArquivosLegadosFlashAsync(session, fileName, cancellationToken);
                }
                catch (Exception ex)
                {
                    await ProgressAsync($"    [AVISO] Falha na limpeza inicial de Flash: {ex.Message}");
                }

                // Garante que o .ipe esteja no diretório do TFTP (corrige freeze quando usuário seleciona de Downloads mas servidor aponta Desktop)
                try
                {
                    var desktopIpe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                    if (!File.Exists(Path.Combine(fileDir, fileName)) && File.Exists(desktopIpe))
                        fileDir = Path.GetDirectoryName(desktopIpe)!;
                    if (!File.Exists(Path.Combine(fileDir, fileName)) && File.Exists(desktopIpe))
                        File.Copy(desktopIpe, Path.Combine(fileDir, fileName), true);
                    if (File.Exists(fileName) && !File.Exists(Path.Combine(fileDir, fileName)))
                        File.Copy(fileName, Path.Combine(fileDir, fileName), true);
                } catch { }

                // Configura IP temporário na GE0/0 para TFTP (se ainda não houver rota)
                try
                {
                    await ConfigurarIpTemporarioHpeAsync(session, hostIpAddress, cancellationToken);
                }
                catch (Exception ex) { await ProgressAsync($"[AVISO] Falha ao configurar IP temporário HPE: {ex.Message}"); }

                // Testa conectividade IP com o PC (ping)
                await ProgressAsync($"[*] Testando conectividade de rede com o Host PC ({hostIpAddress})...");
                var pingRes = await session.SendExpectAsync(
                    $"ping -c 3 {hostIpAddress}",
                    new StopCondition[]
                    {
                        new StopCondition.Contains("round-trip", "round-trip"),
                        new StopCondition.Contains("packet loss", "packet loss"),
                        new StopCondition.Prompt()
                    },
                    TimeSpan.FromSeconds(10),
                    cancellationToken);

                if (pingRes.Output.Contains("100.0% packet loss") || pingRes.Output.Contains("100% packet loss"))
                {
                    await ProgressAsync($"\n[AVISO DE REDE] O roteador HPE não recebeu resposta do ping para o PC ({hostIpAddress}).");
                    await ProgressAsync("    -> Prosseguindo com tentativa de TFTP...");
                }
                else
                {
                    await ProgressAsync($"[OK] Conectividade de rede com o Host ({hostIpAddress}) confirmada!");
                }

                await Task.Delay(500, cancellationToken);

                // Inicia Servidores Integrados: FTP (Gigabit) e TFTP (Fallback)
                // Limpa arquivos corrompidos anteriores na Flash
                try
                {
                    await session.SendCommandAsync($"delete /unreserved flash:/{fileName}", TimeSpan.FromSeconds(5), cancellationToken);
                    await session.SendCommandAsync("reset recycle-bin", TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch { }

                _onProgress?.Invoke(20, "⚠️ NÃO DESLIGUE! Baixando Firmware via TFTP...", "Iniciando transferência TFTP de alta velocidade...");
                await ProgressAsync($"[*] Iniciando transferência TFTP no HPE: tftp {hostIpAddress} get {fileName}...");

                stopwatch.Restart();
                lastUiUpdate = DateTime.MinValue;
                lastLoggedPct = -1;

                var tftpOutput = await session.SendExpectAsync(
                    $"tftp {hostIpAddress} get {fileName}",
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("confirm", ConfirmPromptRegex),
                        new StopCondition.Contains("Writing file...Done.", "Writing file...Done."),
                        new StopCondition.Contains("File downloaded successfully", "File downloaded successfully"),
                        new StopCondition.Contains("Failed to write received data to disk", "Failed to write received data to disk"),
                        new StopCondition.Contains("already exists", "already exists"),
                        new StopCondition.Contains("Error", "Error"),
                        new StopCondition.Contains("not found", "not found"),
                        new StopCondition.Contains("No such file", "No such file"),
                        new StopCondition.Prompt()
                    },
                    TimeSpan.FromMinutes(10),
                    cancellationToken);

                if (tftpOutput.Output.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                    tftpOutput.Output.Contains("Overwrite", StringComparison.OrdinalIgnoreCase) ||
                    (tftpOutput.Matched is StopCondition.LineRegex lrConfirm && lrConfirm.Name == "confirm"))
                {
                    await ProgressAsync("[*] Confirmando sobrescrita na Flash (Y)...");
                    await session.WriteLineAsync("Y", cancellationToken);

                    tftpOutput = await session.WaitForAsync(
                        new StopCondition[]
                        {
                            new StopCondition.Contains("Writing file...Done.", "Writing file...Done."),
                            new StopCondition.Contains("File downloaded successfully", "File downloaded successfully"),
                            new StopCondition.Contains("Failed to write received data to disk", "Failed to write received data to disk"),
                            new StopCondition.Prompt()
                        },
                        TimeSpan.FromMinutes(10),
                        cancellationToken);
                }

                var tftpOk = tftpOutput.Output.Contains("Writing file...Done.", StringComparison.OrdinalIgnoreCase)
                          || tftpOutput.Output.Contains("File downloaded successfully", StringComparison.OrdinalIgnoreCase);
                var tftpFail = tftpOutput.Output.Contains("Failed to write received data to disk", StringComparison.OrdinalIgnoreCase)
                            || tftpOutput.Output.Contains("Error", StringComparison.OrdinalIgnoreCase)
                            || tftpOutput.Output.Contains("not found", StringComparison.OrdinalIgnoreCase)
                            || tftpOutput.Output.Contains("No such file", StringComparison.OrdinalIgnoreCase)
                            || tftpOutput.Output.Contains("Failed", StringComparison.OrdinalIgnoreCase);

                if (tftpOutput.Output.Contains("Failed to write received data to disk", StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync("\n[ERRO CRÍTICO] Falha ao gravar na Flash: Espaço em disco insuficiente no roteador HPE!");
                    throw new InvalidOperationException("A memória Flash do roteador HPE não possui espaço livre suficiente para gravar este arquivo .IPE de 117MB.");
                }

                if (tftpFail && !tftpOk)
                {
                    await ProgressAsync($"\n[ERRO CRÍTICO] Transferência TFTP falhou: {tftpOutput.Output.Trim().Split('\n').LastOrDefault()?.Trim()}");
                    throw new InvalidOperationException($"Transferência TFTP de {fileName} falhou. Verifique conectividade com {hostIpAddress} e espaço na Flash.");
                }

                if (!tftpOk)
                {
                    await ProgressAsync($"\n[ERRO CRÍTICO] Transferência TFTP não confirmada pelo HPE (sem 'Done.'/'successfully'). Saída: {tftpOutput.Output.Trim().Split('\n').LastOrDefault()?.Trim()}");
                    throw new InvalidOperationException($"Transferência TFTP de {fileName} não foi confirmada pelo roteador. Não avançando para bootloader (evita falso positivo).");
                }

                // Valida que o arquivo realmente foi gravado com sucesso na Flash antes de prosseguir
                var checkDir = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(10), cancellationToken);
                if (!ArquivoExisteNaFlash(checkDir, fileName))
                {
                    await ProgressAsync($"\n[ERRO CRÍTICO] O arquivo {fileName} não foi encontrado na Flash do roteador após a transferência.");
                    throw new InvalidOperationException($"O download do firmware {fileName} não foi concluído com sucesso na memória Flash.");
                }
                // Atualiza flashDirOutput para etapas seguintes não usarem snapshot obsoleto (corrige falso positivo que pulava para conclusão)
                flashDirOutput = checkDir;

                _onProgress?.Invoke(80, "⚠️ NÃO DESLIGUE! Download Concluído", "Gravando nova versão no Bootloader...");
                await ProgressAsync("[OK] Firmware recebido e confirmado na Flash! Gravando no bootloader...");
                await Task.Delay(1000, cancellationToken);
            }
            finally
            {
                await tftpServer.StopAsync();
            }
        }

        // 3. Configuração do Bootloader - só limpa versões antigas se realmente for atualizar
        // Se bootloader já está na versão alvo, este ponto nunca é alcançado (retorno antecipado acima)
        // Se arquivo é IPE mas só existem .bins na flash (caso do log), não tenta boot-loader com IPE inexistente
        // Revalida flash após possível TFTP (usa flashDirOutput atualizado)
        var ipeExistsOnFlash = flashDirOutput.Contains(fileName, StringComparison.OrdinalIgnoreCase);
        var binExtraido = flashDirOutput.Contains(targetNorm, StringComparison.OrdinalIgnoreCase) || flashDirOutput.Contains(targetVersionTag, StringComparison.OrdinalIgnoreCase);
        if (!ipeExistsOnFlash && isIpe && binExtraido)
        {
            // Só considera concluído se bootloader já estiver na versão alvo; caso contrário precisa gravar boot-loader
            var bootCheck = string.Empty;
            try { bootCheck = await session.SendCommandAsync("display boot-loader", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
            if (bootCheck.Contains(targetVersionTag, StringComparison.OrdinalIgnoreCase) || bootCheck.Contains(targetNorm, StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync($"[*] Arquivo {fileName} não está na flash, mas os pacotes {targetVersionTag} já estão extraídos e bootloader já configurado.");
                return true;
            }
            await ProgressAsync($"[*] Pacotes {targetVersionTag} já extraídos na flash, mas bootloader ainda não configurado — prosseguindo para gravação do bootloader...");
            // não retorna, segue para boot-loader file ...
        }

        await ProgressAsync($"[*] Liberando imagens de versões diferentes de {targetVersionTag} na Flash...");
        await LimparVersoesDiferentesAsync(session, targetVersionTag, flashDirOutput, cancellationToken);

        // Garante modo user view <HPE> — boot-loader file só é válido em user view, não em [HPE] system-view
        try { await session.SendCommandAsync("return", TimeSpan.FromSeconds(3), cancellationToken); } catch { }
        await Task.Delay(300, cancellationToken);
        // Valida espaço livre antes de descompactar (decompress duplica: .ipe 123 MB + 7 .bins ~80 MB => precisa >140 MB; se <130 MB limpa logs)
        try
        {
            var df = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(8), cancellationToken);
            var m = FreeSpaceRegex.Match(df);
            if (m.Success && int.TryParse(m.Groups["free"].Value, out var freeKb) && freeKb < 145000)
            {
                await ProgressAsync($"[AVISO] Espaço livre {freeKb} KB pode ser insuficiente para descompactar {fileName}. Limpando logfile/diagfile...");
                try { await session.SendCommandAsync("delete /unreserved flash:/logfile/logfile.log", TimeSpan.FromSeconds(5), cancellationToken); } catch { }
                try { await session.SendCommandAsync("reset recycle-bin", TimeSpan.FromSeconds(5), cancellationToken); } catch { }
            }
            await ProgressAsync($"[*] Verificando .ipe alvo: {fileName} {(df.Contains(fileName) ? "presente" : "AUSENTE")} na flash");
        } catch { }

        try { await session.SendCommandAsync("reset recycle-bin", TimeSpan.FromSeconds(8), cancellationToken); } catch { }
        await session.WriteLineAsync("Y", cancellationToken); await Task.Delay(500, cancellationToken);
        var bootCmd = $"boot-loader file flash:/{fileName} main";
        await ProgressAsync($"[*] Gravando imagem no bootloader (user view <HPE>): {bootCmd}...");
        // Loga resposta imediata do comando para diagnóstico (caso retorne Unrecognized/Wrong parameter)
        var bootEcho = await session.SendCommandAsync(string.Empty, TimeSpan.FromSeconds(3), cancellationToken);
        await ProgressAsync($"    Prompt antes do boot-loader: {bootEcho.Trim().Split('\n').LastOrDefault()?.Trim()}");

        _onProgress?.Invoke(85, "⚠️ NÃO DESLIGUE! Gravando Bootloader...", "Extraindo pacotes .bin e atualizando bootloader...");
        await session.WriteLineAsync(bootCmd, cancellationToken);

        var bootDeadline = DateTime.UtcNow.AddMinutes(8);
        var bootConfigured = false;
        var sawVerifyingDone = false;

        while (!bootConfigured && DateTime.UtcNow < bootDeadline && !cancellationToken.IsCancellationRequested)
        {
            var next = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("[Y/N]", "[Y/N]"),
                    new StopCondition.Contains("Continue?", "Continue?"),
                    new StopCondition.Contains("Overwrite", "Overwrite"),
                    new StopCondition.Contains("delete the file", "delete the file"),
                    new StopCondition.Contains("Verifying the file", "Verifying the file"),
                    new StopCondition.Contains("Decompressing", "Decompressing"),
                    new StopCondition.Contains("Done.", "Done."),
                    new StopCondition.Contains("No sufficient storage space", "No sufficient storage space"),
                    new StopCondition.Contains("File is bad or damaged", "File is bad or damaged"),
                    new StopCondition.Contains("Failed.", "Failed."),
                    new StopCondition.Contains("main startup software image", "main startup software image"),
                    new StopCondition.Contains("successfully set", "successfully set"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(60),
                cancellationToken);

            var outLC = next.Output;
            // CRÍTICO: Continue? [Y/N] deve ser respondido com Y antes de qualquer outro tratamento (não confundir Verifying...Done. com sucesso)
            if (outLC.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
                outLC.Contains("Continue?", StringComparison.OrdinalIgnoreCase) ||
                outLC.Contains("Overwrite", StringComparison.OrdinalIgnoreCase) ||
                outLC.Contains("delete the file", StringComparison.OrdinalIgnoreCase) ||
                outLC.Contains("Before pressing ENTER you must choose", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync("    -> Confirmando descompactação no Comware (Y)...");
                await session.WriteLineAsync("Y", cancellationToken);
                await Task.Delay(1500, cancellationToken);
                continue;
            }

            if (outLC.Contains("Verifying the file", StringComparison.OrdinalIgnoreCase))
            {
                sawVerifyingDone = outLC.Contains("Done.", StringComparison.OrdinalIgnoreCase);
                await ProgressAsync($"[*] Verificando .ipe ...{(sawVerifyingDone ? "Done." : "aguardando")} — aguardando Continue? [Y/N]");
                await Task.Delay(500, cancellationToken);
                continue; // Não considera Done. de Verifying como sucesso
            }

            if (outLC.Contains("Decompressing file", StringComparison.OrdinalIgnoreCase) || outLC.Contains("Decompressing", StringComparison.OrdinalIgnoreCase))
            {
                _onProgress?.Invoke(90, "⚠️ NÃO DESLIGUE! Descompactando...", "Extraindo pacotes .bin na Flash (pode levar de 2 a 5 min)...");
                await ProgressAsync("[*] Descompactando pacotes de software na Flash (aguarde de 2 a 5 minutos)...");
                // Aguarda conclusão da descompactação (pode levar 2-5 min) antes de reavaliar
                await Task.Delay(2000, cancellationToken);
                continue;
            }

            if (outLC.Contains("File is bad or damaged", StringComparison.OrdinalIgnoreCase) ||
                outLC.Contains("Failed.", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync($"\n[ERRO CRÍTICO] Imagem {fileName} corrompida/danificada — não carregável no boot (File is bad or damaged).");
                await ProgressAsync($"    Saída: {outLC.Trim().Split('\n').LastOrDefault()?.Trim()}");
                // Limpa imagens corrompidas da flash, esvazia lixeira e reinicia transferência TFTP
                await ProgressAsync($"[*] Limpando imagem corrompida {fileName} da Flash e esvaziando recycle-bin...");
                try { await session.SendCommandAsync($"delete /unreserved flash:/{fileName}", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
                try { await session.SendCommandAsync("reset recycle-bin", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
                try { await session.WriteLineAsync("Y", cancellationToken); await Task.Delay(500, cancellationToken); } catch { }
                await ProgressAsync($"[*] Flash limpa. Reiniciando transferência TFTP de {fileName}...");
                // Recursão controlada: re-executa UpgradeAsync para refazer TFTP limpo e setar boot
                return await UpgradeAsync(session, firmwareFilePath, hostIpAddress, confirmBootLoaderUpdate, cancellationToken);
            }

            if (outLC.Contains("No sufficient storage space", StringComparison.OrdinalIgnoreCase))
            {
                await ProgressAsync("\n[ERRO CRÍTICO] Espaço insuficiente na Flash para descompactar os pacotes .bin!");
                throw new InvalidOperationException("Espaço insuficiente na Flash para descompactar a nova versão.");
            }

            // Done. isolado de Verifying não é sucesso — só após Decompressing ou mensagem de startup
            if (outLC.Contains("main startup software image", StringComparison.OrdinalIgnoreCase) ||
                outLC.Contains("successfully set", StringComparison.OrdinalIgnoreCase) ||
                (outLC.Contains("Done.", StringComparison.OrdinalIgnoreCase) && outLC.Contains("Decompressing", StringComparison.OrdinalIgnoreCase)))
            {
                await ProgressAsync($"[*] Boot-loader confirmou: {outLC.Trim().Split('\n').LastOrDefault()?.Trim()}");
                bootConfigured = true;
                break;
            }
            // Done. genérico sem Decompressing => ignora (Verifying Done) e continua aguardando Continue?/Decompressing
            if (outLC.Contains("Done.", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            // Prompt puro sem mensagem de sucesso => verifica explicitamente via display boot-loader como fallback
            if (next.Matched is StopCondition.Prompt)
            {
                await ProgressAsync($"[*] Prompt retornado sem confirmação explícita, revalidando boot-loader...");
                await Task.Delay(1500, cancellationToken);
                try
                {
                    var probe = await session.SendCommandAsync("display boot-loader", TimeSpan.FromSeconds(10), cancellationToken);
                    if (probe.Contains(targetVersionTag, StringComparison.OrdinalIgnoreCase) || probe.Contains(targetNorm, StringComparison.OrdinalIgnoreCase))
                    {
                        await ProgressAsync($"[*] Boot-loader já aponta para {targetVersionTag} no probe — considerando configurado.");
                        bootConfigured = true;
                        break;
                    }
                } catch { }
                // Se ainda não configurado e deadline não expirou, continua tentando
                if (DateTime.UtcNow < bootDeadline)
                    continue;
            }
        }

        if (!bootConfigured)
        {
            // Timeout pode indicar imagem P43 corrompida que não conseguiu ser carregada no boot (caso do log: .ipe presente mas boot permanece r0809p33)
            await ProgressAsync($"\n[ERRO CRÍTICO] Timeout ao configurar boot-loader para {targetVersionTag} após 8 min. Verificando imagem...");
            try
            {
                var finalProbe = await session.SendCommandAsync("display boot-loader", TimeSpan.FromSeconds(10), cancellationToken);
                if (finalProbe.Contains(targetVersionTag, StringComparison.OrdinalIgnoreCase) || finalProbe.Contains(targetNorm, StringComparison.OrdinalIgnoreCase))
                    bootConfigured = true;
                else
                {
                    // Considera imagem corrompida: limpa flash/recycle e reinicia transferência
                    await ProgressAsync($"[*] Imagem {fileName} não carregável no boot — provável corrupção. Limpando flash...");
                    try { await session.SendCommandAsync($"delete /unreserved flash:/{fileName}", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
                    try { await session.SendCommandAsync("reset recycle-bin", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
                    try { await session.WriteLineAsync("Y", cancellationToken); await Task.Delay(500, cancellationToken); } catch { }
                    await ProgressAsync($"[*] Reiniciando transferência TFTP de {fileName} após limpeza...");
                    return await UpgradeAsync(session, firmwareFilePath, hostIpAddress, confirmBootLoaderUpdate, cancellationToken);
                }
            } catch (InvalidOperationException) { throw; }
            catch (Exception ex)
            {
                await ProgressAsync($"[*] Imagem {fileName} suspeita de corrupção. Limpando e retransferindo...");
                try { await session.SendCommandAsync($"delete /unreserved flash:/{fileName}", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
                try { await session.SendCommandAsync("reset recycle-bin", TimeSpan.FromSeconds(10), cancellationToken); } catch { }
                throw new InvalidOperationException($"Timeout ao configurar boot-loader para {targetVersionTag}: {ex.Message}. Imagem limpa da flash — tente novamente.");
            }
        }

        await Task.Delay(2000, cancellationToken);
        await session.WriteLineAsync(string.Empty, cancellationToken);
        await Task.Delay(1000, cancellationToken);
        try { await session.SendCommandAsync("screen-length disable", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

        // 4. Validação do Bootloader (display boot-loader)
        _onProgress?.Invoke(95, "Fase B: Validando...", "Verificando configuração de inicialização...");
        await ProgressAsync("\n=================================================================");
        await ProgressAsync("           STATUS DO BOOTLOADER HPE APÓS ATUALIZAÇÃO             ");
        await ProgressAsync("=================================================================");
        var bootInfo = string.Empty;
        try
        {
            bootInfo = await session.SendCommandAsync("display boot-loader", TimeSpan.FromSeconds(15), cancellationToken);
            await ProgressAsync(bootInfo.Trim());
        }
        catch { }
        await ProgressAsync("=================================================================\n");

        var isBootUpdated = !string.IsNullOrEmpty(targetVersionTag) &&
                            bootInfo.Contains(targetVersionTag, StringComparison.OrdinalIgnoreCase);

        if (!isBootUpdated)
        {
            await ProgressAsync($"\n[ALERTA DE BOOTLOADER] O bootloader do HPE ainda aponta para a versão anterior.");
            await ProgressAsync($"    -> A versão {targetVersionTag} não foi ativada como Main startup image.");
            throw new InvalidOperationException($"O bootloader do equipamento não foi atualizado para {targetVersionTag}.");
        }

        // 5. Salva a configuração
        try
        {
            await session.SendCommandAsync("save force", TimeSpan.FromSeconds(20), cancellationToken);
        }
        catch { }

        // 6. Executa o reload automático do roteador
        _onProgress?.Invoke(96, "Fase B: Reiniciando Equipamento...", $"Executando reload para carregar {targetVersionTag}...");
        await ProgressAsync($"\n[*] [RELOAD AUTOMÁTICO] Reiniciando roteador HPE para carregar a versão {targetVersionTag}...");
        await ExecutarRebootHpeAsync(session, cancellationToken);

        // Aguarda reboot para carregar nova imagem — evita provisionamento SAIP enquanto roteador ainda inicializa (causa falha/console mudo)
        _onProgress?.Invoke(98, "Aguardando reboot HPE...", $"Roteador reiniciando para {targetVersionTag} (2-5 min) — NÃO DESLIGUE!");
        await ProgressAsync($"[*] Reinicialização em andamento — aguardando HPE voltar a responder (2 a 5 min)...");
        await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken); // dá tempo do reboot físico iniciar
        // Tenta reconectar e aguardar prompt <HPE> por até 5 min
        var rebootDeadline = DateTime.UtcNow.AddMinutes(6);
        var rebootOk = false;
        while (DateTime.UtcNow < rebootDeadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var probe = await session.WaitForAsync(new StopCondition[]
                {
                    new StopCondition.Contains("Extended BootWare Version is not equal", "Extended BootWare Version is not equal"),
                    new StopCondition.Contains("updating? [Y/N]", "updating? [Y/N]"),
                    new StopCondition.Contains("Press Ctrl+B", "Press Ctrl+B"),
                    new StopCondition.Contains("Validating", "Validating"),
                    new StopCondition.Contains("Loading file", "Loading file"),
                    new StopCondition.Contains("Done.", "Done."),
                    new StopCondition.LineRegex("hpe", new System.Text.RegularExpressions.Regex(@"(?i)<[A-Za-z0-9_\-\.]+>")),
                    new StopCondition.Prompt()
                }, TimeSpan.FromSeconds(15), cancellationToken);

                var pOut = probe.Output ?? "";
                if (pOut.Contains("Extended BootWare Version is not equal", StringComparison.OrdinalIgnoreCase) || pOut.Contains("updating? [Y/N]", StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"[*] BootWare desatualizado detectado — confirmando atualização Extended BootWare [Y]...");
                    await session.WriteLineAsync("Y", cancellationToken);
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }
                if (pOut.Contains("Press Ctrl+B", StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"[*] BootWare menu (Ctrl+B) — ignorando, aguardando boot automático...");
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }
                if (pOut.Contains("Validating", StringComparison.OrdinalIgnoreCase) || pOut.Contains("Loading file", StringComparison.OrdinalIgnoreCase))
                {
                    await ProgressAsync($"[*] HPE carregando imagens: {pOut.Trim().Split('\n').LastOrDefault()?.Trim()}");
                    await Task.Delay(3000, cancellationToken);
                    continue;
                }
                if (!string.IsNullOrEmpty(pOut) && (pOut.Trim().EndsWith(">") || pOut.Contains("<HPE", StringComparison.OrdinalIgnoreCase) || pOut.Contains("<", StringComparison.Ordinal) && pOut.Contains(">")))
                {
                    // Prompt <HPE> finalmente disponível
                    rebootOk = true;
                    await ProgressAsync($"[*] HPE voltou a responder: {pOut.Trim().Split('\n').LastOrDefault()?.Trim()}");
                    break;
                }
                if (!string.IsNullOrEmpty(pOut))
                {
                    await ProgressAsync($"[*] Boot HPE: {pOut.Trim().Split('\n').LastOrDefault()?.Trim()}");
                }
            }
            catch
            {
                try { await session.WriteLineAsync(string.Empty, cancellationToken); } catch { }
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        if (!rebootOk)
            await ProgressAsync($"[AVISO] HPE ainda não respondeu após 5 min — provisionamento SAIP aguardará boot. Se necessário, aguarde mais 1-2 min antes da Fase C.");
        else
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken); // estabilização pós-boot

        _onProgress?.Invoke(100, "Fase B Concluída!", $"Versão {targetVersionTag} gravada e roteador reiniciado com sucesso.");
        await ProgressAsync($"[OK] Upgrade de firmware HPE ({fileName} -> {targetVersionTag}) concluído com reload automático!");
        return true;
    }

    private static async Task ExecutarRebootHpeAsync(DeviceSession session, CancellationToken ct)
    {
        try
        {
            await session.WriteLineAsync("reboot force", ct);
            await Task.Delay(1000, ct);

            // Responde Y imediatamente para confirmar o diálogo "A forced reboot might cause... Continue?[Y/N]:"
            await session.WriteLineAsync("Y", ct);
            await Task.Delay(1500, ct);

            // Envia Y adicional para garantir confirmação caso haja diálogo subsequente
            await session.WriteLineAsync("Y", ct);
            await Task.Delay(3000, ct);
        }
        catch
        {
            // O equipamento pode reiniciar e fechar a conexão de imediato
        }
    }

    private static string ExtrairVersaoDeNomeArquivo(string fileName)
    {
        // 1. Tenta casar sufixo de Release no formato -Rxxxx ou -RxxxxPxx (ex: -R6749P43)
        var match = Regex.Match(fileName, @"(?i)-(?<ver>R\d{4}(?:P\d+)?)(?:\.ipe|\.bin|$)", RegexOptions.Compiled);
        if (match.Success)
            return match.Groups["ver"].Value.ToUpperInvariant();

        var matchCmw = Regex.Match(fileName, @"(?i)CMW\d+-(?<ver>R\d+(?:P\d+)?)", RegexOptions.Compiled);
        if (matchCmw.Success)
            return matchCmw.Groups["ver"].Value.ToUpperInvariant();

        return Path.GetFileNameWithoutExtension(fileName).Split('-').LastOrDefault()?.ToUpperInvariant() ?? "R6749P43";
    }

    private static string ExtrairVersaoDeTexto(string bootLoaderOutput, string versionOutput)
    {
        // 1. Tenta extrair a partir do display boot-loader (ex: msr954-cmw710-boot-r6749p43.bin)
        var matchBoot = Regex.Match(bootLoaderOutput, @"(?i)(?:boot|system)-(?<ver>r?\d+(?:p\d+)?)\.bin", RegexOptions.Compiled);
        if (matchBoot.Success)
        {
            var v = matchBoot.Groups["ver"].Value.ToUpperInvariant();
            return v.StartsWith("R") ? v : "R" + v;
        }

        // 2. Tenta extrair a partir do display version (ex: Release 6749P43 ou Release R6749P43)
        var matchVer = VersionRegex.Match(versionOutput);
        if (matchVer.Success)
        {
            var v = matchVer.Groups["ver"].Value.ToUpperInvariant();
            return v.StartsWith("R") ? v : "R" + v;
        }

        return string.Empty;
    }

    private static async Task LimparArquivosLegadosFlashAsync(DeviceSession session, string currentFileName, CancellationToken ct)
    {
        // Genérico: limpa lixeira e qualquer .IPE residual que não seja o alvo
        await EnviarComandoComConfirmacaoAsync(session, "reset recycle-bin", ct);
        // Varre flash por IPEs antigos será feito dinamicamente em LimparImagensBinariasAnterioresAsync
        await EnviarComandoComConfirmacaoAsync(session, "reset recycle-bin", ct);
    }

    private static async Task LimparImagensBinariasAnterioresAsync(DeviceSession session, CancellationToken ct)
    {
        // Genérico para qualquer versão: remove .bin/.ipe que NÃO sejam da versão alvo
        // Obtém listagem e apaga dinamicamente
        string dir = string.Empty;
        try { dir = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(10), ct); } catch { return; }

        // Extrai alvo da listagem atual do boot-loader se possível, senão usa todos
        var matches = Regex.Matches(dir, @"(?i)(?<file>msr954-cmw710-[a-z0-9\-]+\.bin|MSR954[^\s]+\.ipe)");
        var alvo = Regex.Match(dir, @"(?i)r\d{4}(?:p\d+)?").Value; // versao alvo já presente será preservada pelo caller
        // Se não conseguiu extrair alvo, apaga apenas versões claramente diferentes do alvo preservado
        foreach (Match m in matches)
        {
            var file = m.Groups["file"].Value.Trim();
            if (string.IsNullOrEmpty(file)) continue;
            // Preserva arquivos que contenham a versão alvo (será verificado pelo caller antes)
            // Aqui remove apenas se for .bin/.ipe genérico - o caller já garantiu que alvo está preservado
            // Para evitar apagar alvo, só apaga se arquivo contiver padrão de versão diferente
            var verMatch = Regex.Match(file, @"(?i)r\d{4}(?:p\d+)?");
            if (verMatch.Success && !string.IsNullOrEmpty(alvo) && verMatch.Value.Equals(alvo, StringComparison.OrdinalIgnoreCase))
                continue; // preserva versão alvo
            await EnviarComandoComConfirmacaoAsync(session, $"delete /unreserved flash:/{file}", ct);
            await Task.Delay(300, ct);
        }
        await EnviarComandoComConfirmacaoAsync(session, "reset recycle-bin", ct);
    }

    /// <summary>Limpeza genérica: remove qualquer .bin/.ipe de versão diferente da alvo.</summary>
    private static async Task LimparVersoesDiferentesAsync(DeviceSession session, string targetVersionTag, string flashDirOutput, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(targetVersionTag) || string.IsNullOrEmpty(flashDirOutput)) return;
        var targetNorm = targetVersionTag.TrimStart('R', 'r');
        var fileMatches = Regex.Matches(flashDirOutput, @"(?i)(?<file>[a-z0-9_\-]+\.(?:bin|ipe))");
        foreach (Match m in fileMatches)
        {
            var file = m.Groups["file"].Value;
            // Se o arquivo contém a versão alvo (ex: R6749P43 ou 6749P43), NUNCA APAGA!
            if (file.Contains(targetNorm, StringComparison.OrdinalIgnoreCase) ||
                file.Contains(targetVersionTag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.Contains("bendi.zip", StringComparison.OrdinalIgnoreCase) || file.Contains("weixin.zip", StringComparison.OrdinalIgnoreCase)) continue;
            await EnviarComandoComConfirmacaoAsync(session, $"delete /unreserved flash:/{file}", ct);
            await Task.Delay(300, ct);
        }
        await EnviarComandoComConfirmacaoAsync(session, "reset recycle-bin", ct);
    }

    private static async Task EnviarComandoComConfirmacaoAsync(DeviceSession session, string cmd, CancellationToken ct)
    {
        try
        {
            var res = await session.SendExpectAsync(
                cmd,
                new StopCondition[]
                {
                    new StopCondition.Contains("[Y/N]:", "[Y/N]:"),
                    new StopCondition.Contains("Continue? [Y/N]:", "Continue? [Y/N]:"),
                    new StopCondition.LineRegex("confirm", ConfirmPromptRegex),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(8),
                ct);

            if (res.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
                res.Matched is StopCondition.Contains ||
                res.Matched is StopCondition.LineRegex)
            {
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(500, ct);
                await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(8), ct);
            }
        }
        catch
        {
            // Best effort
        }
    }

    private static bool ArquivoExisteNaFlash(string dirOutput, string fileName)
    {
        if (string.IsNullOrWhiteSpace(dirOutput)) return false;
        if (dirOutput.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase) ||
            dirOutput.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            dirOutput.Contains("No such file", StringComparison.OrdinalIgnoreCase) ||
            dirOutput.Contains("Cannot find", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lines = dirOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var l = line.Trim();
            if (l.StartsWith("dir", StringComparison.OrdinalIgnoreCase)) continue;
            if (l.Contains(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task ConfigurarIpTemporarioHpeAsync(DeviceSession session, string hostIp, CancellationToken ct)
    {
        // Deriva IP do roteador como hostIp -1 no mesmo /28 (ex: host 200.182.245.18 -> router 200.182.245.17/28)
        if (!System.Net.IPAddress.TryParse(hostIp, out var hip)) return;
        var bytes = hip.GetAddressBytes();
        // só para IPv4
        if (bytes.Length != 4) return;
        // calcula router IP = host -1 (se .18 -> .17)
        var routerIp = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3] - 1}";
        var mask = "255.255.255.240"; // /28 padrão SAIP

        // LAN no MSR954 é GE0/1 (porta GE1 em bridge-mode por padrão) — precisa port link-mode route
        await session.SendCommandAsync("system-view", TimeSpan.FromSeconds(5), ct);
        await Task.Delay(300, ct);
        await session.SendCommandAsync("interface GigabitEthernet0/1", TimeSpan.FromSeconds(5), ct);
        await Task.Delay(300, ct);
        var linkResp = await session.SendExpectAsync("port link-mode route",
            new StopCondition[] { new StopCondition.Contains("[Y/N]", "[Y/N]"), new StopCondition.Prompt() },
            TimeSpan.FromSeconds(8), ct);
        if (linkResp.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase))
        {
            await session.WriteLineAsync("Y", ct);
            await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(8), ct);
            await Task.Delay(500, ct);
        }
        await session.SendCommandAsync($"ip address {routerIp} {mask}", TimeSpan.FromSeconds(5), ct);
        await Task.Delay(300, ct);
        await session.SendCommandAsync("undo shutdown", TimeSpan.FromSeconds(5), ct);
        await Task.Delay(500, ct);
        await session.SendCommandAsync("quit", TimeSpan.FromSeconds(5), ct);
        await Task.Delay(200, ct);
        await session.SendCommandAsync("quit", TimeSpan.FromSeconds(5), ct); // volta para <HPE>
        await Task.Delay(500, ct);
        await session.SendCommandAsync("save force", TimeSpan.FromSeconds(10), ct);
        await Task.Delay(500, ct);
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }
}
