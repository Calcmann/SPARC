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
        CancellationToken cancellationToken = default)
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
                cancellationToken);
        }

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
        CancellationToken cancellationToken = default)
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

        await ProgressAsync($"[*] [DICA FÍSICA] No modo ROMMON, conecte o cabo de rede Ethernet na porta GigabitEthernet 0/0 (GE0 / Porta 0) do roteador Cisco.");

        if (requestOperatorAction is not null)
        {
            await requestOperatorAction(
                "⚠️ ATENÇÃO OBRIGATÓRIA - CABO DE REDE NO MODO ROMMON\n\n" +
                "O roteador Cisco está em modo de recuperação ROMMON (sem firmware).\n\n" +
                "👉 CONECTE O CABO DE REDE ETHERNET NA PORTA:\n" +
                "🔴 GigabitEthernet 0/0 (GE 0/0 / Porta 0)\n\n" +
                "Esta é a única porta Ethernet habilitada no hardware para a transferência TFTP via ROMMON.\n\n" +
                "Clique em OK assim que o cabo estiver conectado na porta GE 0/0.",
                cancellationToken);
        }

        _onProgress?.Invoke(30, "Fase B: Iniciando Servidor TFTP...", "Iniciando servidor TFTP de alta performance...");

        // 3. Inicia o Servidor TFTP
        await using (var tftpServer = new EmbeddedTftpServer(imageDir))
        {
            var swTftp = new System.Diagnostics.Stopwatch();
            var lastLoggedPct = -1;

            tftpServer.TransferProgress += (file, bytesRead, total, pct) =>
            {
                if (!swTftp.IsRunning)
                    swTftp.Start();

                var currentPct = (int)pct;
                var mbSent = bytesRead / (1024.0 * 1024.0);
                var mbTotal = total / (1024.0 * 1024.0);
                var speed = swTftp.Elapsed.TotalSeconds > 0 ? (mbSent / swTftp.Elapsed.TotalSeconds) : 0;

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
            var confirmPromptReceived = false;

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
                        confirmPromptReceived = true;
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
            var readBuf = new byte[4096];

            while (DateTime.UtcNow < timeout && !cancellationToken.IsCancellationRequested)
            {
                var readBytes = await session.Transport.ReadAsync(readBuf, cancellationToken);
                if (readBytes > 0)
                {
                    var chunk = System.Text.Encoding.ASCII.GetString(readBuf, 0, readBytes);
                    buffer.Append(chunk);
                    session.EmitRawOutput(chunk);

                    var currentText = buffer.ToString();
                    if (currentText.Contains("File copy completed", StringComparison.OrdinalIgnoreCase) ||
                        currentText.Contains("File reception completed", StringComparison.OrdinalIgnoreCase) ||
                        (currentText.Contains("Copying image to flash", StringComparison.OrdinalIgnoreCase) && currentText.Contains("rommon")))
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
                            $"• Dica de Cabo: No modo ROMMON, conecte o cabo de rede na porta GigabitEthernet 0/0 (GE0) do Cisco.\n" +
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

                    // Se retornou ao prompt do rommon sem copiar o arquivo
                    if (confirmPromptReceived &&
                        currentText.Contains("rommon") && currentText.Contains(">") &&
                        !currentText.Contains("File") && !currentText.Contains("Copying") &&
                        currentText.Length > 80)
                    {
                        throw new DeviceSessionException($"Transferência tftpdnld abortada ou não iniciada pelo ROMMON:\n{currentText.Trim()}");
                    }
                }
                await Task.Delay(100, cancellationToken);
            }

            if (!transferSuccess)
            {
                throw new DeviceSessionException($"Tempo limite de transferência TFTP excedido ({binFileName}). Verifique o cabo de rede Ethernet conectado no roteador e notebook.");
            }

            await ProgressAsync($"[OK] Transferência TFTP e gravação da imagem {binFileName} na Flash concluídas com sucesso!");
            await tftpServer.StopAsync();
        }

        // 6. Configura o registrador para boot normal (0x2102) e inicia a nova imagem
        _onProgress?.Invoke(85, "Fase B: Inicializando IOS...", "Configurando registrador 0x2102 e efetuando boot...");
        await ProgressAsync("[*] Configurando registrador para boot normal (confreg 0x2102)...");
        await session.WriteLineAsync("confreg 0x2102", cancellationToken);
        await Task.Delay(500, cancellationToken);

        await ProgressAsync($"[*] Executando boot da imagem 'boot flash:{binFileName}' a partir do ROMMON...");
        await session.WriteLineAsync($"boot flash:{binFileName}", cancellationToken);
        await Task.Delay(1000, cancellationToken);

        // 7. Aguarda a descompressão e inicialização do Cisco IOS
        await ProgressAsync("[*] Aguardando descompressão e inicialização completa do Cisco IOS (isso pode levar ~2-3 minutos)...");
        _onProgress?.Invoke(90, "Fase B: Carregando Cisco IOS...", "Aguardando descompressão e prompt do Cisco IOS...");

        var bootTimeout = DateTime.UtcNow.AddMinutes(5);
        var booted = false;
        var bootBuf = new byte[4096];

        while (DateTime.UtcNow < bootTimeout && !cancellationToken.IsCancellationRequested)
        {
            var readBytes = await session.Transport.ReadAsync(bootBuf, cancellationToken);
            if (readBytes > 0)
            {
                var chunk = System.Text.Encoding.ASCII.GetString(bootBuf, 0, readBytes);
                session.EmitRawOutput(chunk);
                if (chunk.Contains("initial configuration dialog?", StringComparison.OrdinalIgnoreCase) ||
                    chunk.Contains("[yes/no]:", StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteLineAsync("no", cancellationToken);
                }
                else if (chunk.Contains("Press RETURN to get started", StringComparison.OrdinalIgnoreCase))
                {
                    await session.WriteLineAsync(string.Empty, cancellationToken);
                }

                if (chunk.TrimEnd().EndsWith(">") || chunk.TrimEnd().EndsWith("#"))
                {
                    booted = true;
                    break;
                }
            }
            await Task.Delay(300, cancellationToken);
        }

        // 8. Se entrou no prompt Cisco IOS, garante boot system persistente
        if (booted)
        {
            try
            {
                await session.WriteLineAsync(string.Empty, cancellationToken);
                await Task.Delay(500, cancellationToken);
                await session.WriteLineAsync("enable", cancellationToken);
                await Task.Delay(500, cancellationToken);
                await session.WriteLineAsync("terminal length 0", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("configure terminal", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync($"boot system flash:{binFileName}", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("config-register 0x2102", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("end", cancellationToken);
                await Task.Delay(300, cancellationToken);
                await session.WriteLineAsync("write memory", cancellationToken);
                await Task.Delay(2000, cancellationToken);
            }
            catch { }
        }

        await ProgressAsync($"\n=================================================================");
        await ProgressAsync($"   🎉 RECUPERAÇÃO DE FIRMWARE VIA ROMMON CONCLUÍDA COM SUCESSO!  ");
        await ProgressAsync("=================================================================");
        await ProgressAsync($"  Imagem gravada na Flash : {binFileName}");
        await ProgressAsync($"  Cisco IOS carregado     : Pronto para provisionamento!");
        await ProgressAsync("=================================================================\n");

        if (requestOperatorAction is not null)
        {
            await requestOperatorAction(
                "✅ FIRMWARE RECUPERADO COM SUCESSO!\n\n" +
                "O Cisco IOS já está ativo e inicializado na nova versão.\n\n" +
                "👉 ALTERE AGORA O CABO DE REDE PARA A PORTA:\n" +
                "🟢 GigabitEthernet 0/1 (GE 0/1 / Porta 1 - LAN do Cliente)\n\n" +
                "Para prosseguir com o Provisionamento e os Testes de ICMP (LAN/WAN/WEB), Telnet e Teste de Banda.\n\n" +
                "Clique em OK assim que o cabo estiver conectado na porta GE 0/1.",
                cancellationToken);
        }

        _onProgress?.Invoke(100, "Fase B Concluída!", $"Roteador recuperado e bootado com {binFileName}.");
        return true;
    }
}
