using System.Text.RegularExpressions;
using NetworkDevice.Core.Provisioning;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Protocols.Hpe;

/// <summary>
/// Upgrade de Comware via FTP servido pelo celular (<c>ftp servidor porta</c> — o cliente
/// FTP do Comware aceita service-port explícita, doc oficial H3C).
/// Mesma auditoria do <see cref="HpeComwareUpgrader"/> (TFTP :69), mas sem servidor em
/// porta privilegiada — que apps Android não-root não podem abrir. Modo CLI apenas;
/// BootWare (sem SO) segue por notebook/USB.
/// </summary>
public sealed class HpeFtpUpgrader
{
    private static readonly Regex FtpPromptRegex = new(@"(?i)^ftp>\s*$", RegexOptions.Compiled);
    private static readonly Regex LoginPromptRegex = new(@"(?i)(?:username|login)\s*[:?]\s*$", RegexOptions.Compiled);
    private static readonly Regex PasswordPromptRegex = new(@"(?i)password\s*[:?]\s*$", RegexOptions.Compiled);
    private static readonly Regex YesNoRegex = new(@"(?i)\[Y/N\][\s?:]*$", RegexOptions.Compiled);
    private static readonly Regex ErrorRegex = new(
        @"(?i)(?:Unknown host|Can't connect|Login incorrect|Connection (refused|timed out|closed)|Failed|No route to host|%Error|Invalid)",
        RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;

    public HpeFtpUpgrader(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <param name="session">Sessão de console já aberta.</param>
    /// <param name="fileName">Nome base do .ipe servido pelo FTP do celular.</param>
    /// <param name="phoneIp">IP do celular na LAN (adaptador Ethernet OTG).</param>
    /// <param name="phonePort">Porta efetiva do FTP embarcado.</param>
    /// <param name="ftpUser">Usuário (aceito pelo servidor embarcado).</param>
    /// <param name="ftpPass">Senha.</param>
    /// <param name="fileSizeBytes">Dimensiona o timeout do download.</param>
    public async Task<bool> UpgradeAsync(
        DeviceSession session,
        string fileName,
        string phoneIp,
        int phonePort,
        string ftpUser,
        string ftpPass,
        long fileSizeBytes = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Firmware inválido.", nameof(fileName));

        await ProgressAsync($"[*] [FASE B] UPGRADE COMWARE VIA FTP ({fileName}) <- {phoneIp}:{phonePort}...");

        // 1. Normaliza para User View (<HPE>), saindo de ftp> ou sub-views
        await HpeSaipConfigurator.EnsureUserViewAsync(session, _progress, cancellationToken);
        try { await session.SendCommandAsync("screen-length disable", TimeSpan.FromSeconds(5), cancellationToken); } catch { }

        // 2. Auditoria: versão, flash, boot-loader
        var versionText = string.Empty;
        var flashDir = string.Empty;
        var bootLoader = string.Empty;
        try { versionText = await session.SendCommandAsync("display version", TimeSpan.FromSeconds(15), cancellationToken); } catch { }
        try { flashDir = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken); } catch { }
        try { bootLoader = await session.SendCommandAsync("display boot-loader", TimeSpan.FromSeconds(15), cancellationToken); } catch { }

        var fileOnFlash = flashDir.Contains(fileName, StringComparison.OrdinalIgnoreCase);
        var bootOk = bootLoader.Contains(fileName, StringComparison.OrdinalIgnoreCase);
        await ProgressAsync($"[*] Versão: {FirstLine(versionText)} | Na flash: {(fileOnFlash ? "SIM" : "NÃO")} | Boot: {(bootOk ? "OK" : "PENDENTE")}");

        if (fileOnFlash && bootOk)
        {
            await ProgressAsync("[OK] Firmware já na flash e boot configurado — nada a fazer.");
            return true;
        }

        // 3. Download FTP interativo (ftp> é subshell: user/pass/binary/get/quit)
        if (!fileOnFlash)
        {
            await GarantirEspacoFlashParaFtpAsync(session, fileName, flashDir, cancellationToken);
            await FtpGetAsync(session, fileName, phoneIp, phonePort, ftpUser, ftpPass, fileSizeBytes, cancellationToken);
        }

        flashDir = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken);
        if (!flashDir.Contains(fileName, StringComparison.OrdinalIgnoreCase))
            throw new DeviceSessionException($"Arquivo {fileName} não localizado na flash: após o FTP.");
        await ProgressAsync($"[OK] {fileName} transferido via FTP e validado na flash:!");

        // 3b. Limpeza de versões concorrentes na Flash para liberar espaço e garantir boot exclusivo na versão indicada
        var targetTag = ExtrairVersaoDeNomeArquivo(fileName);
        if (!string.IsNullOrWhiteSpace(targetTag))
        {
            await LimparVersoesDiferentesAsync(session, targetTag, flashDir, cancellationToken);
        }

        // 4. boot-loader + save
        await ProgressAsync($"[*] Configurando boot-loader para 'flash:/{fileName}'...");
        await session.WriteLineAsync($"boot-loader file flash:/{fileName} slot 1 main", cancellationToken);
        await AnswerYesNoAsync(session, "boot-loader", TimeSpan.FromMinutes(3), cancellationToken);

        await ProgressAsync("[*] Salvando configuração (save)...");
        await SaveAsync(session, cancellationToken);

        // 4b. Zeramento conjunto de configuração no mesmo reload (aproveita o reboot obrigatório de upgrade do SO)
        await ProgressAsync("[*] Zerando configurações salvas do Comware (reset saved-configuration) para boot limpo...");
        try
        {
            await session.WriteLineAsync("reset saved-configuration", cancellationToken);
            await AnswerYesNoAsync(session, "reset saved-configuration", TimeSpan.FromSeconds(10), cancellationToken, acceptSilenceAsDone: true);
        }
        catch { }

        // 5. Reboot + verificação de retorno
        await ProgressAsync("[*] [RELOAD AUTOMÁTICO CONJUNTO] Reiniciando HPE Comware (novo firmware + base limpa)...");
        await session.WriteLineAsync("reboot", cancellationToken);
        await AnswerYesNoAsync(session, "reboot", TimeSpan.FromSeconds(30), cancellationToken, acceptSilenceAsDone: true);

        await ProgressAsync("[*] Aguardando retorno do Comware (até 6 min)...");
        var deadline = DateTime.UtcNow.AddMinutes(6);
        var back = false;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var res = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.Prompt(),
                        new StopCondition.LineRegex("hpe", new Regex(@"^[<\[][A-Za-z0-9_\-\.]+[>\]]\s*$")),
                    },
                    TimeSpan.FromSeconds(15), cancellationToken);
                back = true;
                break;
            }
            catch (SessionTimeoutException) { await ProgressAsync("[*] Aguardando boot do Comware..."); }
        }

        if (!back)
        {
            await ProgressAsync("[AVISO] Console não retornou em 6 min — valide fisicamente.");
            return false;
        }

        try
        {
            var verAfter = await session.SendCommandAsync("display version", TimeSpan.FromSeconds(30), cancellationToken);
            await ProgressAsync($"[OK] UPGRADE CONCLUÍDO. Versão pós-boot: {FirstLine(verAfter)}");
        }
        catch { await ProgressAsync("[OK] UPGRADE CONCLUÍDO (versão pós-boot não capturada)."); }
        return true;
    }

    private async Task FtpGetAsync(DeviceSession session, string fileName, string phoneIp, int phonePort,
        string ftpUser, string ftpPass, long fileSizeBytes, CancellationToken ct)
    {
        await ProgressAsync($"[*] Conectando FTP {phoneIp}:{phonePort}...");
        await session.WriteLineAsync($"ftp {phoneIp.Trim()} {phonePort}", ct);

        // 3a. Alcança o subshell ftp> (passando por Username:/Password: se pedidos)
        var atFtp = false;
        var loginDeadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < loginDeadline && !ct.IsCancellationRequested && !atFtp)
        {
            var res = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("ftp", FtpPromptRegex),
                    new StopCondition.LineRegex("login", LoginPromptRegex),
                    new StopCondition.LineRegex("pass", PasswordPromptRegex),
                    new StopCondition.LineRegex("err", ErrorRegex),
                    new StopCondition.Prompt(),
                },
                TimeSpan.FromSeconds(10), ct);

            if (res.Matched is StopCondition.LineRegex lr)
            {
                if (lr.Name == "ftp") { atFtp = true; break; }
                if (lr.Name == "login") { await session.WriteLineAsync(ftpUser, ct); continue; }
                if (lr.Name == "pass") { await session.WriteLineAsync(ftpPass, ct); continue; }
                if (lr.Name == "err") throw new DeviceSessionException($"FTP recusado: {res.Output.Trim()}");
            }
            else if (res.Matched is StopCondition.Prompt)
            {
                throw new DeviceSessionException($"Cliente FTP não abriu (voltou ao prompt): {res.Output.Trim()}");
            }
        }
        if (!atFtp)
            throw new DeviceSessionException("Timeout aguardando o subshell ftp>.");

        // 3b. binary + get (monitor longo) + quit
        await session.WriteLineAsync("binary", ct);
        await session.WaitForAsync(
            new StopCondition[] { new StopCondition.LineRegex("ftp", FtpPromptRegex) },
            TimeSpan.FromSeconds(10), ct);

        await ProgressAsync($"[*] Baixando {fileName} (get)...");
        await session.WriteLineAsync($"get {fileName}", ct);

        var minutes = 5 + (fileSizeBytes > 0 ? fileSizeBytes / (20L * 1024 * 1024) : 2);
        var getDeadline = DateTime.UtcNow.AddMinutes(Math.Min(minutes, 20));
        var done = false;
        while (DateTime.UtcNow < getDeadline && !ct.IsCancellationRequested)
        {
            try
            {
                var res = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("ftp", FtpPromptRegex),
                        new StopCondition.LineRegex("err", ErrorRegex),
                    },
                    TimeSpan.FromSeconds(5), ct);
                if (res.Matched is StopCondition.LineRegex lr && lr.Name == "ftp") { done = true; break; }
                throw new DeviceSessionException($"Falha no download FTP: {res.Output.Trim()}");
            }
            catch (SessionTimeoutException) { continue; }
        }
        if (!done)
            throw new DeviceSessionException($"Timeout no download FTP de {fileName}.");

        await session.WriteLineAsync("quit", ct);
        await session.WaitForAsync(
            new StopCondition[] { new StopCondition.Prompt() },
            TimeSpan.FromSeconds(15), ct);
        await ProgressAsync("[OK] Download FTP concluído.");
    }

    /// <summary>
    /// Responde [Y/N] com 'y' até o prompt voltar. Timeouts de leitura sem match não
    /// estouram de imediato: com <paramref name="acceptSilenceAsDone"/> (pós-reboot, quando
    /// o equipamento emudece), o silêncio encerra como aceito; senão, envia ENTER e repete
    /// até o deadline.
    /// </summary>
    private static async Task AnswerYesNoAsync(DeviceSession session, string context, TimeSpan timeout,
        CancellationToken ct, bool acceptSilenceAsDone = false)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            DeviceSession.ExpectResult res;
            try
            {
                res = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("yesno", YesNoRegex),
                        new StopCondition.Prompt(),
                    },
                    TimeSpan.FromSeconds(10), ct);
            }
            catch (SessionTimeoutException)
            {
                if (acceptSilenceAsDone)
                    return;
                try { await session.WriteLineAsync(string.Empty, ct); } catch { }
                continue;
            }

            if (res.Matched is StopCondition.Prompt)
                return;
            await session.WriteLineAsync("y", ct);
            await Task.Delay(500, ct);
        }

        if (acceptSilenceAsDone)
            return;
        throw new DeviceSessionException($"Sem retorno do prompt após '{context}'.");
    }

    private static async Task SaveAsync(DeviceSession session, CancellationToken ct)
    {
        await session.WriteLineAsync("save", ct);
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                var res = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("yesno", YesNoRegex),
                        new StopCondition.LineRegex("filename", new Regex(@"(?i)file\s*name.*[:?]\s*$")),
                        new StopCondition.Prompt(),
                    },
                    TimeSpan.FromSeconds(10), ct);
                if (res.Matched is StopCondition.Prompt)
                    return;
                if (res.Matched is StopCondition.LineRegex lr && lr.Name == "filename")
                    await session.WriteLineAsync(string.Empty, ct);
                else
                    await session.WriteLineAsync("y", ct);
                await Task.Delay(500, ct);
            }
            catch (SessionTimeoutException) { return; }
        }
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }

    private async Task GarantirEspacoFlashParaFtpAsync(DeviceSession session, string targetFileName, string dirOut, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dirOut)) return;

        var fileMatches = Regex.Matches(dirOut, @"(?i)\b(?<file>[A-Za-z0-9_\-\.]+\.(?:ipe|bin))\b");
        var targetTag = ExtrairVersaoDeNomeArquivo(targetFileName);
        var filesToDelete = new List<string>();

        foreach (Match m in fileMatches)
        {
            var f = m.Groups["file"].Value;
            if (string.IsNullOrWhiteSpace(f) || f.Equals(targetFileName, StringComparison.OrdinalIgnoreCase)) continue;

            if (f.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".license", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
                continue;

            // Remove .ipe antigo
            if (f.EndsWith(".ipe", StringComparison.OrdinalIgnoreCase))
            {
                filesToDelete.Add(f);
                continue;
            }

            // Se for bin de versão diferente da alvo, remove
            if (!string.IsNullOrWhiteSpace(targetTag) && !f.Contains(targetTag, StringComparison.OrdinalIgnoreCase))
            {
                filesToDelete.Add(f);
            }
        }

        if (filesToDelete.Count > 0)
        {
            foreach (var f in filesToDelete)
            {
                await ProgressAsync($"[*] Removendo arquivo legado na Flash para liberar espaço de gravação: {f}...");
                await DeletarArquivoFlashPermanenteAsync(session, f, ct);
            }
            await EnviarComandoComConfirmacaoAsync(session, "reset recycle-bin", ct);
        }
    }

    private async Task LimparVersoesDiferentesAsync(DeviceSession session, string targetVersionTag, string dirOut, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(targetVersionTag) || string.IsNullOrWhiteSpace(dirOut)) return;
        var targetNorm = targetVersionTag.TrimStart('R', 'r');

        var fileMatches = Regex.Matches(dirOut, @"(?i)\b(?<file>[A-Za-z0-9_\-\.]+\.(?:bin|ipe))\b");
        var filesToDelete = new List<string>();

        foreach (Match m in fileMatches)
        {
            var file = m.Groups["file"].Value;
            if (string.IsNullOrWhiteSpace(file)) continue;

            if (file.Contains(targetNorm, StringComparison.OrdinalIgnoreCase) ||
                file.Contains(targetVersionTag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (file.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".license", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filesToDelete.Add(file);
        }

        if (filesToDelete.Count > 0)
        {
            await ProgressAsync($"[*] [Flash] Liberando versões diferentes de {targetVersionTag} na Flash para garantir boot exclusivo...");
            foreach (var file in filesToDelete)
            {
                await ProgressAsync($"[*] Apagando permanentemente arquivo legado na Flash: {file}...");
                await DeletarArquivoFlashPermanenteAsync(session, file, ct);
            }
            await EnviarComandoComConfirmacaoAsync(session, "reset recycle-bin", ct);
        }
    }

    private static async Task DeletarArquivoFlashPermanenteAsync(DeviceSession session, string fileName, CancellationToken ct)
    {
        try
        {
            var path = fileName.StartsWith("flash:/", StringComparison.OrdinalIgnoreCase) ? fileName : $"flash:/{fileName}";
            var res = await session.SendExpectAsync(
                $"delete /unreserved {path}",
                new StopCondition[]
                {
                    new StopCondition.Contains("[Y/N]", "[Y/N]"),
                    new StopCondition.Contains("Continue?", "Continue?"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(15),
                ct);

            if (res.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
                res.Output.Contains("Continue", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", ct);
                await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(15), ct);
                await Task.Delay(500, ct);
            }
        }
        catch { }
    }

    private static async Task EnviarComandoComConfirmacaoAsync(DeviceSession session, string cmd, CancellationToken ct)
    {
        try
        {
            var res = await session.SendExpectAsync(
                cmd,
                new StopCondition[]
                {
                    new StopCondition.Contains("[Y/N]", "[Y/N]"),
                    new StopCondition.Contains("Continue?", "Continue?"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(10),
                ct);

            if (res.Output.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
                res.Output.Contains("Continue", StringComparison.OrdinalIgnoreCase))
            {
                await session.WriteLineAsync("Y", ct);
                await Task.Delay(500, ct);
                await session.WaitForAsync(new StopCondition[] { new StopCondition.Prompt() }, TimeSpan.FromSeconds(15), ct);
            }
        }
        catch { }
    }

    private static string ExtrairVersaoDeNomeArquivo(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
        var match = Regex.Match(fileName, @"(?i)(?:[-_.]|\b)(?<ver>R\d+(?:P\d+)?)(?:[-_.]|\.ipe|\.bin|$)", RegexOptions.Compiled);
        if (match.Success) return match.Groups["ver"].Value.ToUpperInvariant();
        var matchCmw = Regex.Match(fileName, @"(?i)CMW\d+[-_](?<ver>R\d+(?:P\d+)?)", RegexOptions.Compiled);
        if (matchCmw.Success) return matchCmw.Groups["ver"].Value.ToUpperInvariant();
        var matchNumeric = Regex.Match(fileName, @"(?i)(?:[-_.]|\b)(?<ver>\d{4}(?:P\d+)?)(?:[-_.]|\.ipe|\.bin|$)", RegexOptions.Compiled);
        if (matchNumeric.Success)
        {
            var v = matchNumeric.Groups["ver"].Value.ToUpperInvariant();
            return v.StartsWith("R") ? v : "R" + v;
        }
        return string.Empty;
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "?";
        return text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "?";
    }
}
