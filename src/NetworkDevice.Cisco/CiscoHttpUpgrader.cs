using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

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
        @"^[A-Za-z0-9_\-\.]+\s*[>#]",
        RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;

    public CiscoHttpUpgrader(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    public static string BuildCopyCommand(string phoneIp, int phonePort, string fileName) =>
        $"copy http://{phoneIp}:{phonePort}/{fileName} flash:{fileName}";

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
        CancellationToken cancellationToken = default)
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
        await CiscoSaipConfigurator.EnsurePrivilegedExecAsync(session, cancellationToken);
        try { await session.SendCommandAsync("terminal length 0", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

        // 2. Estado atual: versão, flash, boot
        var showVer = await CiscoSaipConfigurator.SendShowAsync(session, "show version", TimeSpan.FromSeconds(15), cancellationToken);
        var dirFlash = await CiscoSaipConfigurator.SendShowAsync(session, "dir flash:", TimeSpan.FromSeconds(15), cancellationToken);
        if (dirFlash.Contains("% Invalid", StringComparison.OrdinalIgnoreCase))
            dirFlash = await CiscoSaipConfigurator.SendShowAsync(session, "dir", TimeSpan.FromSeconds(15), cancellationToken);
        var showBoot = await CiscoSaipConfigurator.SendShowAsync(session, "show running-config | include boot", TimeSpan.FromSeconds(10), cancellationToken);

        var isRunningTarget = CiscoIOSFirmwareVersionInspector.IsSameVersion(showVer, fileName, out var curVer, out _);
        var isFileOnFlash = dirFlash.Contains(fileName, StringComparison.OrdinalIgnoreCase);
        var isBootConfigured = showBoot.Contains(fileName, StringComparison.OrdinalIgnoreCase);
        await ProgressAsync($"[*] Versão atual: {curVer.DisplayString} | Arquivo na flash: {(isFileOnFlash ? "SIM" : "NÃO")} | Boot configurado: {(isBootConfigured ? "SIM" : "NÃO")}");

        if (isRunningTarget && isBootConfigured && isFileOnFlash)
        {
            await ProgressAsync("[OK] Firmware alvo já em execução e boot configurado — nada a fazer.");
            return true;
        }

        // 3. Cliente HTTP disponível na imagem? (show ip http client all)
        var httpClientOut = await CiscoSaipConfigurator.SendShowAsync(session, "show ip http client all", TimeSpan.FromSeconds(10), cancellationToken);
        if (httpClientOut.Contains("% Invalid input", StringComparison.OrdinalIgnoreCase) ||
            httpClientOut.Contains("% Incomplete", StringComparison.OrdinalIgnoreCase))
        {
            throw new DeviceSessionException(
                "Esta imagem IOS não possui cliente HTTP ('show ip http client all' inválido). " +
                "Use um notebook com servidor TFTP (:69) e 'copy tftp:' para este equipamento.");
        }

        // 4. IP temporário no roteador (caso zerado) + teste de alcance ao celular
        if (!string.IsNullOrWhiteSpace(routerTempIp))
        {
            lanInterface ??= await DetectLanInterfaceAsync(session, cancellationToken);
            var mask = string.IsNullOrWhiteSpace(routerTempMask) ? "255.255.255.0" : routerTempMask.Trim();
            await ProgressAsync($"[*] Aplicando IP temporário {routerTempIp}/{mask} em {lanInterface}...");
            await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync($"interface {lanInterface}", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync($"ip address {routerTempIp.Trim()} {mask}", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync("no shutdown", TimeSpan.FromSeconds(10), cancellationToken);
            await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
            await Task.Delay(1500, cancellationToken);
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
            await CopyHttpAsync(session, phoneIp, phonePort, fileName, fileSizeBytes, cancellationToken);

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

        // 7. Boot system + register + save
        await ProgressAsync($"[*] Configurando boot system para 'flash:{fileName}'...");
        await session.SendCommandAsync("configure terminal", TimeSpan.FromSeconds(10), cancellationToken);
        await session.SendCommandAsync("no boot system", TimeSpan.FromSeconds(10), cancellationToken);
        await session.SendCommandAsync($"boot system flash:{fileName}", TimeSpan.FromSeconds(10), cancellationToken);
        await session.SendCommandAsync("config-register 0x2102", TimeSpan.FromSeconds(10), cancellationToken);
        await session.SendCommandAsync("end", TimeSpan.FromSeconds(10), cancellationToken);
        await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(30), cancellationToken);

        // 8. Reload + verificação da versão pós-boot
        await ProgressAsync("[*] Recarregando o equipamento (reload)...");
        await session.WriteLineAsync("reload", cancellationToken);
        try
        {
            var reloadRes = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.Contains("Proceed with reload? [confirm]", "Proceed with reload? [confirm]"),
                    new StopCondition.Contains("System configuration has been modified", "System configuration has been modified"),
                    new StopCondition.Prompt(),
                },
                TimeSpan.FromSeconds(20), cancellationToken);
            var tail = reloadRes.Output;
            if (tail.Contains("System configuration has been modified", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("yes", cancellationToken); // salva antes do reload
                await session.WaitForAsync(
                    new StopCondition[] { new StopCondition.Contains("reload-confirm", "Proceed with reload? [confirm]") },
                    TimeSpan.FromSeconds(15), cancellationToken);
            }
            await session.WriteLineAsync(string.Empty, cancellationToken); // confirma [confirm]
        }
        catch (SessionTimeoutException)
        {
            await ProgressAsync("[AVISO] Confirmação de reload não detectada — o equipamento pode já estar reiniciando.");
        }

        await ProgressAsync("[*] Aguardando boot do novo IOS (até 8 min)...");
        var deadline = DateTime.UtcNow.AddMinutes(8);
        var back = false;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                await session.WriteLineAsync(string.Empty, cancellationToken);
                await session.WaitForAsync(
                    new StopCondition[] { new StopCondition.Prompt() },
                    TimeSpan.FromSeconds(15), cancellationToken);
                back = true;
                break;
            }
            catch (SessionTimeoutException) { await ProgressAsync("[*] Aguardando retorno do console pós-reload..."); }
        }

        if (!back)
        {
            await ProgressAsync("[AVISO] Console não retornou em 8 min — verifique fisicamente e valide 'show version'.");
            return false;
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
        string fileName, long fileSizeBytes, CancellationToken ct)
    {
        var copyCmd = BuildCopyCommand(phoneIp, phonePort, fileName);
        await ProgressAsync($"[*] Copiando: {copyCmd} ...");
        await session.WriteLineAsync(copyCmd, ct);
        var full = new System.Text.StringBuilder();

        // 5a. Confirms iniciais (Destination filename / [confirm])
        try
        {
            var confirm = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("confirm", PromptConfirmRegex),
                    new StopCondition.LineRegex("prompt", PromptLineRegex),
                    new StopCondition.Contains("error", "%Error"),
                    new StopCondition.Contains("invalid", "% Invalid"),
                },
                TimeSpan.FromSeconds(20), ct);
            full.Append(confirm.Output);
            if (confirm.Matched is StopCondition.LineRegex lr && lr.Name == "confirm")
                await session.WriteLineAsync(string.Empty, ct);
            else if (confirm.Matched is StopCondition.LineRegex p && p.Name == "prompt")
            {
                // Retornou direto ao prompt: cópia recusada de imediato ou erro já impresso
                CheckCopyErrors(full.ToString(), fileName);
                return;
            }
        }
        catch (SessionTimeoutException) { /* sem confirms: segue monitorando */ }

        // 5b. Monitora transferência até o prompt (timeout dimensionado pelo tamanho)
        var minutes = 5 + (fileSizeBytes > 0 ? fileSizeBytes / (20L * 1024 * 1024) : 2);
        var copyDeadline = DateTime.UtcNow.AddMinutes(Math.Min(minutes, 20));
        while (DateTime.UtcNow < copyDeadline && !ct.IsCancellationRequested)
        {
            try
            {
                var exp = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("prompt", PromptLineRegex),
                        new StopCondition.Contains("error", "%Error"),
                        new StopCondition.Contains("invalid", "% Invalid"),
                        new StopCondition.Contains("timedout", "Timed out"),
                        new StopCondition.Contains("refused", "Connection refused"),
                        new StopCondition.Contains("unreachable", "unreachable"),
                        new StopCondition.Contains("unknownhost", "Unknown host"),
                    },
                    TimeSpan.FromSeconds(5), ct);
                full.Append(exp.Output);

                if (exp.Matched is StopCondition.LineRegex lr && lr.Name == "prompt")
                    break;

                if (exp.Matched is StopCondition.Contains)
                    throw new DeviceSessionException($"Falha na cópia HTTP de {fileName}: {exp.Output.Trim()}");
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

    private static async Task<string> DetectLanInterfaceAsync(DeviceSession session, CancellationToken ct)
    {
        try
        {
            var brief = await CiscoSaipConfigurator.SendShowAsync(session, "show ip interface brief", TimeSpan.FromSeconds(15), ct);
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
