using System.Text;
using System.Text.RegularExpressions;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Fortinet;

/// <summary>
/// Quebra de senha e reset de fábrica do FortiGate 40F via console serial.
/// Porte fiel do caminho "direto formatação" da janela WPF (FortiGateAutoRecoveryWindow):
/// reset físico pelo botão RESET traseiro (LED STATUS devagar -&gt; segurar ~3s até piscar
/// rápido), com fallback de formatação via menu da BIOS ([F] + 'yes', depois [Q] p/ boot),
/// e validação final com 'admin' + senha em branco. Opera sobre <see cref="ITransport"/>
/// (bytes crus), portanto reutilizável no Windows e no Android (USB OTG).
/// Nenhum sinal Break é necessário — apenas bytes seriais comuns.
/// </summary>
public sealed class FortiGatePasswordRecovery
{
    private static readonly Regex AnsiEscape = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);

    private static readonly Regex Serial16Regex = new(@"(?i)(?:Serial\s*number|S/N|SN)\s*[:=]?\s*([A-Z0-9]{16})", RegexOptions.Compiled);
    private static readonly Regex SerialFgtRegex = new(@"\b(FGT40F[A-Z0-9]{10})\b", RegexOptions.Compiled);
    private static readonly Regex LoginPromptRegex = new(@"(?i)(?:FortiGate|FGT)[A-Za-z0-9_\-]*\s+login\s*[:?]", RegexOptions.Compiled);
    private static readonly Regex PasswordPromptRegex = new(@"(?i)Password\s*[:?]", RegexOptions.Compiled);
    private static readonly Regex StrictCliPromptRegex = new(@"(?m)(?:^|[\r\n])(?:FortiGate|FGT|[A-Za-z0-9_\-]{3,30})\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$", RegexOptions.Compiled);

    private readonly Func<string, Task>? _progress;

    /// <summary>Número de série capturado do stream de boot (quando anunciado pelo equipamento).</summary>
    public string? DetectedSerial { get; private set; }

    public event Action<string>? SerialDetected;

    public FortiGatePasswordRecovery(Func<string, Task>? progress = null)
    {
        _progress = progress;
    }

    /// <summary>
    /// Executa o monitoramento autônomo de reset físico + validação de fábrica.
    /// </summary>
    /// <param name="transport">Transporte serial aberto (9600 8-N-1).</param>
    /// <param name="instructOperator">
    /// Chamado para instruções que exigem ação física do operador (reiniciar fonte,
    /// segurar RESET). No Windows era só log; no mobile recomenda-se modal com OK.
    /// </param>
    /// <returns>True quando o login 'admin' em branco foi confirmado (fábrica).</returns>
    public async Task<bool> RecoverAndResetAsync(
        ITransport transport,
        Func<string, CancellationToken, Task>? instructOperator = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (!transport.IsOpen)
            await transport.OpenAsync(cancellationToken);

        await ProgressAsync("================================================================");
        await ProgressAsync("  RESET E QUEBRA DE SENHA — FORTIGATE 40F");
        await ProgressAsync("   1. Reinicie o equipamento (desligue e ligue a fonte de energia).");
        await ProgressAsync("   2. Aguarde a inicialização: observe o LED 'STATUS' no painel frontal.");
        await ProgressAsync("   3. Quando o LED STATUS começar a piscar devagar, aperte e segure o botão no furo 'RESET' (traseira).");
        await ProgressAsync("   4. Segure o botão reset (~3s) até o LED STATUS começar a piscar rápido, e então solte.");
        await ProgressAsync("   5. O roteador detectará o reset físico e reiniciará com as configurações de fábrica.");
        await ProgressAsync("   6. Quebra de senha realizada: a senha retornou à default. Siga com o provisionamento padrão.");
        await ProgressAsync("================================================================");

        if (instructOperator is not null)
        {
            await instructOperator(
                "Desligue e ligue a fonte do FortiGate 40F agora.\n\n" +
                "Quando o LED STATUS começar a piscar devagar, aperte e SEGURE o botão RESET (furo traseiro, ~3s) até o LED piscar rápido, então solte.",
                cancellationToken);
        }

        var rxBuffer = new byte[2048];
        var accumulator = new StringBuilder();

        var bootDetected = false;
        var loginSent = false;
        var passwordSent = false;
        var passwordResetSent = false;
        var promptVelhoAvisado = false;
        var flashFormatada = false;
        var biosFormatRequested = false;

        var swMaintainerWait = System.Diagnostics.Stopwatch.StartNew();
        var maintainerRetries = 0;
        var swPasswordWait = System.Diagnostics.Stopwatch.StartNew();
        var passwordRetries = 0;

        try { await WriteAsync(transport, "\r\n", cancellationToken); } catch { }

        while (!cancellationToken.IsCancellationRequested)
        {
            int bytesRead;
            try
            {
                bytesRead = await transport.ReadAsync(rxBuffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await ProgressAsync($"[!] Erro de leitura serial: {ex.Message}");
                await Task.Delay(200, cancellationToken);
                continue;
            }

            if (bytesRead > 0)
            {
                var chunk = Encoding.UTF8.GetString(rxBuffer, 0, bytesRead).Replace("\uFFFD", "");
                accumulator.Append(chunk);
                if (_progress is not null)
                    await _progress(chunk);
            }

            var current = accumulator.ToString();
            if (string.IsNullOrWhiteSpace(current))
            {
                await Task.Delay(50, cancellationToken);
                continue;
            }

            var clean = AnsiEscape.Replace(current, "").Trim();

            // 1. Número de série no stream de boot
            if (string.IsNullOrWhiteSpace(DetectedSerial))
            {
                var matchSn = Serial16Regex.Match(current);
                if (!matchSn.Success)
                    matchSn = SerialFgtRegex.Match(current);
                if (matchSn.Success)
                {
                    DetectedSerial = matchSn.Groups[1].Value.ToUpperInvariant();
                    SerialDetected?.Invoke(DetectedSerial);
                    await ProgressAsync($"[>] Serial Number detectado no console: {DetectedSerial}");
                }
            }

            // 2. Confirmação de formatação da flash [yes/no]
            var isEraseConfirmation = current.Contains("erase data in boot device", StringComparison.OrdinalIgnoreCase) ||
                                      current.Contains("Continue? [yes/no]", StringComparison.OrdinalIgnoreCase) ||
                                      current.Contains("[Y/N]", StringComparison.OrdinalIgnoreCase) ||
                                      current.Contains("(y/n)", StringComparison.OrdinalIgnoreCase) ||
                                      current.Contains("format boot device", StringComparison.OrdinalIgnoreCase);
            if (isEraseConfirmation)
            {
                biosFormatRequested = false;
                flashFormatada = true;
                await ProgressAsync("[OK] Confirmação da BIOS ('Continue? [yes/no]'). Enviando 'yes'...");
                accumulator.Clear();
                await Task.Delay(250, cancellationToken);
                await WriteAsync(transport, "yes\r", cancellationToken);
                continue;
            }

            // 3. Menu da BIOS ativo ([F] formatar / [Q] continuar boot)
            var isBiosMenuPrompt = current.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase) ||
                                   current.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                                   Regex.IsMatch(current, @"(?i)Enter\s+.*[C,R,T,F,I,B,Q,H].*:") ||
                                   (current.Contains("[B]:", StringComparison.OrdinalIgnoreCase) &&
                                    current.Contains("[F]:", StringComparison.OrdinalIgnoreCase));
            if (isBiosMenuPrompt)
            {
                if (!flashFormatada)
                {
                    biosFormatRequested = true;
                    await ProgressAsync("[FORMATAÇÃO FLASH] Menu da BIOS. Enviando [F]...");
                    accumulator.Clear();
                    await Task.Delay(250, cancellationToken);
                    await WriteAsync(transport, "F\r", cancellationToken);
                    continue;
                }
                else
                {
                    await ProgressAsync("[CONTINUAR BOOT] Saindo da BIOS pós-formatação ('Q')...");
                    accumulator.Clear();
                    await Task.Delay(250, cancellationToken);
                    await WriteAsync(transport, "Q\r", cancellationToken);
                    continue;
                }
            }

            // 4. Mensagens de boot (kernel/u-boot) — ainda carregando; descarta prompt antigo
            var bootMsgDetected = current.Contains("Scanning /dev/", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("mmcblk", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("Verifying Checksum", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("Verifying image", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("U-Boot", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("Reading boot image", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("System is starting", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("FortiOS kernel", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("Booting", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("Linux version", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("INIT:", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("Starting system", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("runlevel", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("Press any key", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("Loading kernel", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("unpacking", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("EXT4-fs", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("squashfs", StringComparison.OrdinalIgnoreCase) ||
                                  current.Contains("formatting", StringComparison.OrdinalIgnoreCase);
            if (bootMsgDetected)
            {
                if (!bootDetected)
                {
                    bootDetected = true;
                    await ProgressAsync("[>] Boot do FortiGate 40F em andamento!");
                    await ProgressAsync("    >> AÇÃO: Segure o botão 'RESET' (traseira) até o LED 'STATUS' piscar rapidamente.");
                    if (instructOperator is not null)
                    {
                        await instructOperator(
                            "Boot detectado! SEGURE o botão RESET traseiro (clipe) até o LED STATUS piscar rápido, então solte.",
                            cancellationToken);
                    }
                }

                loginSent = false;
                passwordSent = false;
                maintainerRetries = 0;
                accumulator.Clear();
                continue;
            }

            // 5. Prompt de login pós-boot — valida reset com 'admin'
            if (!loginSent && LoginPromptRegex.IsMatch(current))
            {
                if (!bootDetected)
                {
                    if (!promptVelhoAvisado)
                    {
                        promptVelhoAvisado = true;
                        await ProgressAsync("[*] Terminal em espera: reinicie a fonte do FortiGate 40F e segure o RESET ao piscar.");
                    }
                    accumulator.Clear();
                    await Task.Delay(200, cancellationToken);
                    continue;
                }

                loginSent = true;
                await ProgressAsync("[>] Login pós-boot. Validando reset (admin + senha em branco)...");
                accumulator.Clear();
                await Task.Delay(300, cancellationToken);
                await WriteAsync(transport, "admin\r", cancellationToken);
                swMaintainerWait.Restart();
                maintainerRetries = 1;
            }

            // 6. Prompt de senha — envia em branco
            if (loginSent && !passwordSent)
            {
                if (PasswordPromptRegex.IsMatch(current))
                {
                    passwordSent = true;
                    await ProgressAsync("[>] Enviando senha em branco (padrão de fábrica)...");
                    accumulator.Clear();
                    await Task.Delay(150, cancellationToken);
                    await WriteAsync(transport, "\r", cancellationToken);
                    swPasswordWait.Restart();
                    passwordRetries = 1;
                    continue;
                }
                else if (swMaintainerWait.ElapsedMilliseconds > 2000 && maintainerRetries < 4)
                {
                    maintainerRetries++;
                    swMaintainerWait.Restart();
                    await WriteAsync(transport, "\r", cancellationToken);
                    await Task.Delay(100, cancellationToken);
                    await WriteAsync(transport, "admin\r", cancellationToken);
                    continue;
                }
            }

            // 7. Watchdog pós-senha
            if (loginSent && passwordSent && !passwordResetSent)
            {
                if (swPasswordWait.ElapsedMilliseconds > 2500 && passwordRetries < 3 &&
                    !current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) &&
                    !current.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
                {
                    passwordRetries++;
                    swPasswordWait.Restart();
                    await WriteAsync(transport, "\r", cancellationToken);
                    continue;
                }
            }

            // 8. Falha de login — reset físico não acionado; reinstrui e rearma
            if (loginSent && passwordSent && !passwordResetSent &&
                (current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) ||
                 current.Contains("Login failed", StringComparison.OrdinalIgnoreCase)))
            {
                await ProgressAsync("================================================================");
                await ProgressAsync("  RESET FÍSICO NÃO DETECTADO — A SENHA ANTIGA AINDA ESTÁ ATIVA.");
                await ProgressAsync("   >> Desligue e ligue a fonte; segure o RESET até o LED piscar rápido!");
                await ProgressAsync("================================================================");

                if (instructOperator is not null)
                {
                    await instructOperator(
                        "Reset NÃO detectado (senha antiga ainda ativa).\n\nDesligue e ligue a fonte e segure o RESET até o LED STATUS piscar rápido.",
                        cancellationToken);
                }

                loginSent = false;
                passwordSent = false;
                bootDetected = false;
                accumulator.Clear();
                await Task.Delay(400, cancellationToken);
                await WriteAsync(transport, "\r\n", cancellationToken);
                continue;
            }

            // 9. Sucesso: fábrica confirmada (Welcome! / # / $ / troca forçada de senha)
            var isStrictCliPrompt = StrictCliPromptRegex.IsMatch(clean);
            var hasWelcomeOrCli = clean.Contains("Welcome!") || clean.EndsWith("#") || clean.EndsWith("$") || isStrictCliPrompt;
            var hasForcedPasswordChange = current.Contains("forced to change your password", StringComparison.OrdinalIgnoreCase) ||
                                          current.Contains("New Password", StringComparison.OrdinalIgnoreCase) ||
                                          current.Contains("Please change your password", StringComparison.OrdinalIgnoreCase) ||
                                          current.Contains("Please input a new password", StringComparison.OrdinalIgnoreCase);
            var loginIncorrect = current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) ||
                                 current.Contains("Login failed", StringComparison.OrdinalIgnoreCase);
            var factoryDefaultConfirmed = !bootMsgDetected && !loginIncorrect && (hasForcedPasswordChange || hasWelcomeOrCli);

            if (loginSent && passwordSent && !passwordResetSent && factoryDefaultConfirmed)
            {
                passwordResetSent = true;
                accumulator.Clear();

                await ProgressAsync("================================================================");
                await ProgressAsync("  [SUCESSO] RESET DE FÁBRICA CONFIRMADO!");
                await ProgressAsync("   O 40F aceitou 'admin' sem senha. Siga com o provisionamento padrão.");
                await ProgressAsync("================================================================");

                try { await WriteAsync(transport, "\x03\r\n", cancellationToken); } catch { }
                return true;
            }

            await Task.Delay(60, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private static async Task WriteAsync(ITransport transport, string data, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(data);
        await transport.WriteAsync(bytes, ct);
    }

    private async Task ProgressAsync(string message)
    {
        if (_progress is not null)
            await _progress(message);
    }
}
