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
            await FtpGetAsync(session, fileName, phoneIp, phonePort, ftpUser, ftpPass, fileSizeBytes, cancellationToken);

        flashDir = await session.SendCommandAsync("dir flash:", TimeSpan.FromSeconds(15), cancellationToken);
        if (!flashDir.Contains(fileName, StringComparison.OrdinalIgnoreCase))
            throw new DeviceSessionException($"Arquivo {fileName} não localizado na flash: após o FTP.");
        await ProgressAsync($"[OK] {fileName} transferido via FTP e validado na flash:!");

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

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "?";
        return text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "?";
    }
}
