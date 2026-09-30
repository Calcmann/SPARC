using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Fortinet;

/// <summary>
/// Restauração de firmware do FortiGate via FTP servido pelo celular
/// (<c>execute restore image ftp arquivo servidor:porta [user] [pass]</c> — sintaxe com
/// porta explícita documentada pela Fortinet). Sem servidor em porta privilegiada.
/// O comando reescreve a partição ativa e reinicia IMEDIATAMENTE (sem staging).
/// </summary>
public sealed class FortiGateFirmwareUpgrader
{
    private readonly Func<string, Task>? _progress;

    public FortiGateFirmwareUpgrader(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <param name="session">Sessão de console já aberta e autenticada (#).</param>
    /// <param name="fileName">Nome base do .out servido pelo FTP do celular.</param>
    /// <param name="phoneIp">IP do celular na LAN (adaptador Ethernet OTG).</param>
    /// <param name="phonePort">Porta efetiva do FTP embarcado.</param>
    public async Task<bool> UpgradeAsync(
        DeviceSession session,
        string fileName,
        string phoneIp,
        int phonePort,
        string? ftpUser = null,
        string? ftpPass = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("Firmware inválido.", nameof(fileName));

        var target = $"{phoneIp.Trim()}:{phonePort}";
        var cmd = string.IsNullOrWhiteSpace(ftpUser)
            ? $"execute restore image ftp {fileName} {target}"
            : $"execute restore image ftp {fileName} {target} {ftpUser} {ftpPass ?? string.Empty}".TrimEnd();

        await ProgressAsync($"[*] RESTORE DE FIRMWARE FORTIOS VIA FTP ({fileName} <- {target})...");
        await ProgressAsync("[AVISO] O FortiGate grava a partição ativa e REINICIA imediatamente.");

        await session.WriteLineAsync(cmd, cancellationToken);

        // Confirmação (y/n) + início da transferência/gravação
        var confirmed = false;
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested && !confirmed)
        {
            var res = await session.WaitForAsync(
                new StopCondition[]
                {
                    new StopCondition.LineRegex("yn", new Regex(@"(?i)\(y/n\)")),
                    new StopCondition.LineRegex("confirm", new Regex(@"(?i)Do you want to continue")),
                    new StopCondition.LineRegex("err", new Regex(@"(?i)(?:Command fail|Unknown action|not found|Connection|timed out|Invalid|failed|No such file)")),
                    new StopCondition.LineRegex("rebooting", new Regex(@"(?i)(?:reboot|shutting down|System is rebooting|writing|Upgrading)")),
                    new StopCondition.Prompt(),
                },
                TimeSpan.FromSeconds(10), cancellationToken);

            if (res.Matched is StopCondition.Prompt)
            {
                // Voltou ao prompt sem pedir confirmação: comando rejeitado de imediato
                throw new DeviceSessionException($"Restore recusado: {res.Output.Trim()}");
            }
            if (res.Matched is StopCondition.LineRegex lr && lr.Name == "err")
                throw new DeviceSessionException($"Falha no restore FTP: {res.Output.Trim()}");
            if (res.Matched is StopCondition.LineRegex yn && (yn.Name == "yn" || yn.Name == "confirm"))
            {
                await ProgressAsync("[*] Confirmando restore ('y') — gravação + reboot iminentes...");
                await session.WriteLineAsync("y", cancellationToken);
                confirmed = true;
                break;
            }
            if (res.Matched is StopCondition.LineRegex rb && rb.Name == "rebooting")
            {
                confirmed = true;
                break;
            }
        }

        if (!confirmed)
            throw new DeviceSessionException("Restore não confirmado pelo FortiGate (timeout).");

        // Aguarda o reboot completar: retorno do prompt de login prova boot íntegro
        await ProgressAsync("[*] Aguardando reboot com o novo firmware (até 8 min)...");
        var bootDeadline = DateTime.UtcNow.AddMinutes(8);
        while (DateTime.UtcNow < bootDeadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var res = await session.WaitForAsync(
                    new StopCondition[]
                    {
                        new StopCondition.LineRegex("login", new Regex(@"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s+login\s*[:?]")),
                        new StopCondition.Prompt(),
                    },
                    TimeSpan.FromSeconds(15), cancellationToken);
                await ProgressAsync("[OK] FortiGate reiniciado com o novo firmware (login disponível). Valide 'get system status'.");
                return true;
            }
            catch (SessionTimeoutException)
            {
                await ProgressAsync("[*] Aguardando conclusão do reboot...");
            }
        }

        await ProgressAsync("[AVISO] Login não retornou em 8 min — valide fisicamente.");
        return false;
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }
}
