using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NetworkDevice.Core.Session;
using NetworkDevice.Protocols.Tftp;

namespace NetworkDevice.Fortinet;

/// <summary>
/// Orquestrador de recuperação autônoma de firmware para Fortinet FortiGate (FortiGate 40F)
/// em nível de BIOS / FortiBootLoader via TFTP quando o firmware estiver ausente ou corrompido.
/// </summary>
public sealed class FortiOsBootLoaderRecovery
{
    private static readonly Regex BiosMenuPromptRegex = new(
        @"(?i)(?:\[G\]:\s*Get\s+firmware|\[T\]:\s*Enter\s+TFTP|Enter\s+C,R,T,F,I,B,Q,or\s+H:|Enter\s+P,D,I,S,G,V,T,F,E,R,N,Q,or\s+H:|Enter\s+P,D,I,S|Enter\s+Selection:)",
        RegexOptions.Compiled);

    private static readonly Regex TftpOptionGRegex = new(
        @"(?i)(?:\[G\]:\s*Get\s+firmware|Get\s+firmware\s+image)",
        RegexOptions.Compiled);

    private static readonly Regex TftpOptionTRegex = new(
        @"(?i)(?:\[T\]:\s*Enter\s+TFTP|Enter\s+C,R,T)",
        RegexOptions.Compiled);

    private static readonly Regex BiosInterruptPromptRegex = new(
        @"(?i)(?:Press\s+any\s+key\s+to\s+display\s+configuration\s+menu|FortiBootLoader|Initializing\s+boot\s+device|Initializing\s+MAC|Ver:0500)",
        RegexOptions.Compiled);

    private static readonly Regex TftpServerPromptRegex = new(
        @"(?i)(?:Enter\s+TFTP\s+server\s+address|server\s+address\s*\[)",
        RegexOptions.Compiled);

    private static readonly Regex LocalIpPromptRegex = new(
        @"(?i)(?:Enter\s+Local\s+address|Local\s+address\s*\[)",
        RegexOptions.Compiled);

    private static readonly Regex FileNamePromptRegex = new(
        @"(?i)(?:Enter\s+File\s+name|File\s+name\s*\[)",
        RegexOptions.Compiled);

    private static readonly Regex SaveDefaultPromptRegex = new(
        @"(?i)(?:FOS\s+signature\s+Verification\s+OK|Image\s+Received|Save\s+as\s+Default\s+firmware/Backup\s+firmware|Save\s+as\s+Default\s+firmware|\(D/B/R\)|\[D/B/R\]|\[D\]\?|Default\s+firmware/Backup)",
        RegexOptions.Compiled);

    private static readonly Regex RelayoutPromptRegex = new(
        @"(?i)(?:re-layout\s+the\s+boot\s+device|Continue\s*:\s*\[Y/N\]|\(Y/N\)\?|\[Y/N\]\?)",
        RegexOptions.Compiled);

    private static readonly Regex ProgrammingFlashRegex = new(
        @"(?i)(?:Programming\s+flash|Writing\s+to\s+flash|Saving\s+Default\s+firmware|Writing\s+image|Erasing\s+flash|re-layout|partitioning|formatting)",
        RegexOptions.Compiled);

    private static readonly Regex FlashDoneRegex = new(
        @"(?i)(?:Programming\s+flash\.\.\.Done|Done\.|Booting\s+Default\s+firmware|Booting\s+OS|Rebooting|Resetting|Reading\s+boot\s+image)",
        RegexOptions.Compiled);

    private static readonly Regex FortiOsLoginRegex = new(
        @"(?i)(?:(?:FortiGate|FGT|[A-Za-z0-9_\-]{3,30})\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$|login\s*:\s*$|Welcome\s+to\s+FortiOS)",
        RegexOptions.Compiled);

    private static readonly Regex FortiOsBootFailureRegex = new(
        @"(?i)(?:Neither\s+DEFAULT\s+nor\s+BACKUP|System\s+halted|FOS\s+boot\s+failed|Please\s+power\s+cycle|boot\s+failed)",
        RegexOptions.Compiled);

    public static readonly Regex CableDisconnectedRegex = new(
        @"(?i)(?:failed\s+to\s+complete\s+copper\s+auto-nego|copper\s+auto-nego|no\s+link\s+on\s+wan|link\s+is\s+down|no\s+carrier|failed\s+to\s+complete\s+auto-nego)",
        RegexOptions.Compiled);

    private static readonly Regex TransferErrorRegex = new(
        @"(?i)(?:TFTP\s+transfer\s+failed|TFTP\s+timeout|bad\s+checksum|Reading\s+boot\s+image\s+\.\.\.\s+failed|Loading\s+failed|Cannot\s+load|ARP\s+timeout|Retry\s+count\s+exceeded|failed\s+to\s+send|cannot\s+find\s+host|host\s+unreachable|Error:)",
        RegexOptions.Compiled);

    public event Action<int, string, string>? ProgressUpdated;

    /// <summary>
    /// Executa a recuperação completa do firmware FortiOS via BIOS / FortiBootLoader TFTP.
    /// </summary>
    public async Task<bool> RecoverFirmwareAsync(
        DeviceSession session,
        string firmwareFilePath,
        string hostIp,
        string routerIp,
        Action<int, string, string>? progress = null,
        Func<string, Task>? log = null,
        CancellationToken ct = default,
        string? subnetMask = null,
        Func<string, CancellationToken, Task>? instructOperator = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(firmwareFilePath) || !File.Exists(firmwareFilePath))
            throw new FileNotFoundException("Arquivo de firmware FortiOS (.out) não encontrado.", firmwareFilePath);

        var fileName = Path.GetFileName(firmwareFilePath);
        var fileDir = Path.GetDirectoryName(firmwareFilePath) ?? AppContext.BaseDirectory;
        var mask = !string.IsNullOrWhiteSpace(subnetMask) ? subnetMask : "255.255.255.0";

        Task SafeLog(string msg) => log?.Invoke(msg) ?? Task.CompletedTask;
        void SafeProgress(int pct, string title, string detail)
        {
            ProgressUpdated?.Invoke(pct, title, detail);
            progress?.Invoke(pct, title, detail);
        }

        SafeProgress(5, "Iniciando BIOS TFTP...", "Validando ambiente e conectando ao FortiBootLoader...");
        await SafeLog("\n=================================================================");
        await SafeLog("  🚑 INICIANDO RECUPERAÇÃO DE FIRMWARE FORTIGATE (BIOS TFTP)");
        await SafeLog($"  Imagem Alvo  : {fileName}");
        await SafeLog($"  Servidor TFTP: {hostIp}");
        await SafeLog($"  IP FortiGate : {routerIp}");
        await SafeLog($"  Máscara Subnet: {mask}");
        await SafeLog("=================================================================\n");

        // 1. Inicia o servidor TFTP embutido com monitoramento de progresso idêntico ao processo de atualização
        await using var tftpServer = new EmbeddedTftpServer(fileDir);
        tftpServer.LogMessage += async msg => await SafeLog($"  {msg}");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var lastLogPct = -1;
        var lastUiTime = DateTime.MinValue;
        long lastSentBytes = 0;
        bool tftpCompleted = false;

        tftpServer.TransferProgress += (file, sent, total, pct) =>
        {
            Interlocked.Exchange(ref lastSentBytes, sent);
            if (sent >= total && total > 0)
                tftpCompleted = true;

            var now = DateTime.UtcNow;
            var sentMb = sent / (1024.0 * 1024.0);
            var totalMb = total / (1024.0 * 1024.0);
            var elapsedSec = stopwatch.Elapsed.TotalSeconds;
            var speedMbSec = elapsedSec > 0.5 ? sentMb / elapsedSec : 0;
            var remainingSec = speedMbSec > 0 ? (totalMb - sentMb) / speedMbSec : 0;
            var etaStr = remainingSec > 0 ? $" | Restam ~{TimeSpan.FromSeconds(remainingSec):mm\\:ss}" : "";

            var mappedPct = 25 + (int)(pct * 0.45);

            if ((now - lastUiTime).TotalMilliseconds >= 250 || pct >= 100)
            {
                lastUiTime = now;
                SafeProgress(
                    mappedPct,
                    $"⚠️ NÃO DESLIGUE! Transferindo Firmware TFTP ({pct:N1}%)...",
                    $"{sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) — {speedMbSec:N1} MB/s{etaStr}");
            }

            var step = (int)(pct / 5) * 5;
            if (step > lastLogPct)
            {
                lastLogPct = step;
                var barLength = 20;
                var filled = (int)Math.Round((pct / 100.0) * barLength);
                var bar = new string('█', Math.Clamp(filled, 0, barLength)) + new string('░', Math.Max(0, barLength - filled));
                _ = SafeLog($"    -> [TFTP] [{bar}] {sentMb:N1} MB / {totalMb:N1} MB ({pct:N1}%) | {speedMbSec:N1} MB/s{etaStr}");
            }
        };

        tftpServer.Start();
        await SafeLog($"[*] Servidor TFTP de alta velocidade ativo no diretório '{fileDir}'.");

        try
        {
            // 2. Interceptar / Entrar no Menu da BIOS
            SafeProgress(10, "Acessando Menu BIOS...", "Aguardando prompt de seleção do FortiBootLoader...");
            var menuText = await ObterMenuBiosAsync(session, SafeLog, ct, instructOperator, SafeProgress);
            await SafeLog("[OK] Menu de inicialização da BIOS / FortiBootLoader ativo.");

            // 3. Escolher Opção de TFTP e Configurar Parâmetros de Rede
            bool is40FSubmenuStyle = menuText.Contains("Enter C,R,T", StringComparison.OrdinalIgnoreCase) ||
                                     menuText.Contains("Enter P,D,I,S", StringComparison.OrdinalIgnoreCase) ||
                                     menuText.Contains("[C]: Configure TFTP parameters", StringComparison.OrdinalIgnoreCase);

            if (is40FSubmenuStyle)
            {
                SafeProgress(18, "Configurando TFTP...", "Ajustando parâmetros de rede no submenu da BIOS (FortiGate 40F)...");
                await ConfigurarParametrosBios40FAsync(session, hostIp, routerIp, fileName, mask, SafeLog, ct);
            }
            else
            {
                string tftpOption = TftpOptionGRegex.IsMatch(menuText) ? "G" : "T";
                await SafeLog($"[*] Enviando opção [{tftpOption}] (Download via TFTP)...");
                SafeProgress(15, "Selecionando TFTP...", $"Enviando comando [{tftpOption}] para a BIOS...");
                await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(tftpOption + "\r"), ct);
                await Task.Delay(300, ct);

                SafeProgress(18, "Configurando Rede...", "Enviando endereço IP do Servidor TFTP...");
                await PreencherParametrosRedeBiosAsync(session, hostIp, routerIp, fileName, SafeLog, ct);
            }

            // 5. Monitorar Transferência TFTP da BIOS
            SafeProgress(25, "Transferindo Firmware...", "Aguardando blocos de imagem transferidos...");
            await SafeLog("[*] Aguardando transferência dos blocos de firmware da BIOS...");
            var transferSuccess = await MonitorarTransferenciaBiosAsync(
                session, 
                SafeProgress, 
                SafeLog, 
                ct, 
                () => Interlocked.Read(ref lastSentBytes), 
                () => tftpCompleted,
                instructOperator);
            if (!transferSuccess)
            {
                await SafeLog("[ERRO] Falha na transferência TFTP indicada pela BIOS.");
                return false;
            }

            // 6. Confirmar e Gravar na Flash como Default Firmware ('D')
            SafeProgress(75, "Gravando na Flash...", "Confirmando gravação do firmware na memória Flash (D)...");
            var flashOk = await ConfirmarEGravarFlashAsync(session, SafeProgress, SafeLog, ct);
            if (!flashOk)
            {
                await SafeLog("\n🚨 [FALHA NA GRAVAÇÃO DA FLASH]");
                await SafeLog("   A BIOS não confirmou a gravação do firmware na memória permanente (NAND Flash).");
                await SafeLog("   O equipamento não pode inicializar sem a gravação concluída.\n");
                return false;
            }

            // 7. Aguardar Reinicialização e Boot do FortiOS
            SafeProgress(88, "Inicializando FortiOS...", "Aguardando carga do kernel e sistema operacional...");
            await SafeLog("[*] FortiGate 40F reiniciando com o novo firmware na Flash...");
            await SafeLog("    Aguardando inicialização completa do FortiOS (pode levar de 60 a 120s)...");

            var bootOk = await AguardarBootFortiOsAsync(session, SafeProgress, SafeLog, ct);
            if (bootOk)
            {
                SafeProgress(100, "Recuperação Concluída!", "FortiGate 40F recuperado com sucesso e pronto para provisionamento.");
                await SafeLog("\n=================================================================");
                await SafeLog("  ✅ [SUCESSO] RECUPERAÇÃO DE FIRMWARE FORTIGATE CONCLUÍDA!");
                await SafeLog("     O FortiOS inicializou limpo com o novo firmware na Flash.");
                await SafeLog("=================================================================\n");
                return true;
            }
            else
            {
                await SafeLog("\n🚨 [FALHA DE BOOT FORTIOS]");
                await SafeLog("   O equipamento não alcançou o prompt de login após a gravação da Flash.");
                return false;
            }
        }
        finally
        {
            try { await tftpServer.StopAsync(); } catch { }
        }
    }

    private static async Task<string> ObterMenuBiosAsync(
        DeviceSession session,
        Func<string, Task> log,
        CancellationToken ct,
        Func<string, CancellationToken, Task>? instructOperator = null,
        Action<int, string, string>? progress = null)
    {
        var buffer = new byte[1024];
        var accumulator = new StringBuilder();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        bool rebootSent = false;

        // Tenta enviar Enter/espaços para acordar o prompt de menu
        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("   \r\n"), ct);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var read = await session.Transport.ReadAsync(buffer, ct);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                accumulator.Append(text);
                session.EmitRawOutput(text);

                var full = accumulator.ToString();
                if (BiosMenuPromptRegex.IsMatch(full))
                {
                    return full;
                }

                // Se o console estiver parado em disclaimer/termo de uso
                if (!rebootSent && full.Contains("(Press 'a' to accept)", StringComparison.OrdinalIgnoreCase))
                {
                    await log("[*] Detectado aviso legal (disclaimer). Enviando 'a'...");
                    await session.Transport.WriteAsync(new byte[] { 0x61 }, ct);
                    await Task.Delay(500, ct);
                    accumulator.Clear();
                    continue;
                }

                // Se o console estiver parado no prompt de login
                if (!rebootSent && (full.EndsWith("login: ") || full.EndsWith("login:") || full.Contains("login:")))
                {
                    await log("[*] Detectado prompt de login FortiOS. Enviando 'admin'...");
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("admin\r"), ct);
                    await Task.Delay(500, ct);
                    accumulator.Clear();
                    continue;
                }

                // Se o console estiver pedindo senha
                if (!rebootSent && (full.EndsWith("Password: ") || full.EndsWith("Password:") || full.Contains("Password:")))
                {
                    await log("[*] Detectado prompt de senha FortiOS. Enviando Enter...");
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("\r"), ct);
                    await Task.Delay(1000, ct);
                    accumulator.Clear();
                    continue;
                }

                // Se o equipamento estiver rodando o FortiOS CLI operacional (# ou $) e precisamos da BIOS
                if (!rebootSent && (Regex.IsMatch(full, @"(?m)[A-Za-z0-9_\-\.]+\s*(?:\([^\r\n]+\))?\s*[#$]\s*$") ||
                                    full.EndsWith("# ") || full.EndsWith("#") || full.EndsWith("$ ") || full.EndsWith("$")))
                {
                    await log("[*] Equipamento em execução no FortiOS CLI. Verificando link físico da porta WAN antes do reload...");
                    progress?.Invoke(12, "Verificando Porta WAN...", "Validando link físico na porta WAN antes de reiniciar para a BIOS...");

                    var wanOk = await FortiOsSaipConfigurator.EnforceWanPortConnectedAsync(
                        session,
                        instructOperator,
                        log,
                        ct,
                        pollDelayMs: 0,
                        onProgress: progress);

                    if (!wanOk)
                    {
                        await log("\n🚨 [RELOAD ABORTADO] A porta WAN do FortiGate 40F não possui link físico (down).");
                        await log("   O comando 'execute reboot' NÃO foi enviado para evitar falha de TFTP e boot indevido na BIOS.\n");
                        throw new InvalidOperationException("Porta WAN do FortiGate 40F sem link físico (down). Conecte o cabo na porta WAN e reinicie o procedimento.");
                    }

                    rebootSent = true;
                    await log("[OK] Link físico Ethernet confirmado na porta WAN! Enviando 'execute reboot' para reiniciar e acessar a BIOS...");
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("execute reboot\r"), ct);
                    await Task.Delay(500, ct);
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("y\r"), ct);
                    deadline = DateTime.UtcNow.AddSeconds(90); // expande deadline para o ciclo de reboot do hardware
                    accumulator.Clear();
                    continue;
                }

                // Confirmação de reboot caso ainda esteja pendente
                if (rebootSent && full.Contains("Do you want to continue? (y/n)", StringComparison.OrdinalIgnoreCase))
                {
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("y\r"), ct);
                    accumulator.Clear();
                    continue;
                }

                if (full.Contains("CTRL+D", StringComparison.OrdinalIgnoreCase) ||
                    full.Contains("Neither DEFAULT nor BACKUP", StringComparison.OrdinalIgnoreCase) ||
                    full.Contains("FOS boot failed", StringComparison.OrdinalIgnoreCase) ||
                    full.Contains("System halted", StringComparison.OrdinalIgnoreCase) ||
                    full.Contains("power cycle", StringComparison.OrdinalIgnoreCase))
                {
                    await log("[*] Equipamento FortiGate sem firmware em halt ('Neither DEFAULT nor BACKUP FOS works').");
                    await log("[*] Enviando sinal CTRL+D (0x04) para reiniciar o equipamento e capturar o menu da BIOS...");
                    await session.Transport.WriteAsync(new byte[] { 0x04 }, ct);
                    rebootSent = true;
                    accumulator.Clear();

                    // Após enviar CTRL+D, o hardware do FortiGate 40F leva de 2 a 4 segundos para reiniciar o SoC
                    // e iniciar a contagem regressiva da BIOS / U-Boot ("Press any key to display configuration menu...").
                    // Enviamos pulsos periódicos de espaço durante 8 segundos para garantir a captura imediata do menu da BIOS.
                    var catchDeadline = DateTime.UtcNow.AddSeconds(8);
                    while (DateTime.UtcNow < catchDeadline && !ct.IsCancellationRequested)
                    {
                        var r = await session.Transport.ReadAsync(buffer, ct);
                        if (r > 0)
                        {
                            var t = Encoding.ASCII.GetString(buffer, 0, r);
                            accumulator.Append(t);
                            session.EmitRawOutput(t);
                            var cur = accumulator.ToString();
                            if (BiosMenuPromptRegex.IsMatch(cur))
                            {
                                await log("\n[OK] Menu da BIOS / FortiBootLoader interceptado com sucesso após CTRL+D!");
                                return cur;
                            }
                        }

                        // Pulso de espaço para interceptar o menu da BIOS
                        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(" "), ct);
                        await Task.Delay(120, ct);
                    }
                    continue;
                }

                if (BiosInterruptPromptRegex.IsMatch(full))
                {
                    await log("[*] Detectado prompt de interrupção da BIOS. Enviando espaços para acessar o menu...");
                    for (int i = 0; i < 5; i++)
                    {
                        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(" "), ct);
                        await Task.Delay(50, ct);
                    }
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("\r"), ct);
                    await Task.Delay(200, ct);
                }
            }
            else
            {
                await Task.Delay(100, ct);
                await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(" "), ct);
            }
        }

        // Se timeout, verifica o acumulado
        var final = accumulator.ToString();
        if (BiosMenuPromptRegex.IsMatch(final))
            return final;

        throw new TimeoutException("Não foi possível alcançar o menu de inicialização da BIOS / FortiBootLoader.");
    }

    private static async Task ConfigurarParametrosBios40FAsync(
        DeviceSession session,
        string hostIp,
        string routerIp,
        string fileName,
        string subnetMask,
        Func<string, Task> log,
        CancellationToken ct)
    {
        var buffer = new byte[2048];
        var accumulator = new StringBuilder();

        async Task<(bool ok, string text)> WaitForAnyPromptWithCaptureAsync(string[] matches, TimeSpan timeout)
        {
            var captured = new StringBuilder();
            var deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                var r = await session.Transport.ReadAsync(buffer, ct);
                if (r > 0)
                {
                    var t = Encoding.ASCII.GetString(buffer, 0, r);
                    accumulator.Append(t);
                    captured.Append(t);
                    session.EmitRawOutput(t);
                    var acc = accumulator.ToString();
                    if (matches.Any(m => acc.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        accumulator.Clear();
                        return (true, captured.ToString());
                    }
                }
                else
                {
                    await Task.Delay(50, ct);
                }
            }
            return (false, captured.ToString());
        }

        async Task<bool> WaitForAnyPromptAsync(string[] matches, TimeSpan timeout)
        {
            var (ok, _) = await WaitForAnyPromptWithCaptureAsync(matches, timeout);
            return ok;
        }

        async Task SendParamAsync(string optionKey, string[] promptMatches, string value, string label)
        {
            await log($"[*] [BIOS] Configurando {label}: {value}");
            accumulator.Clear();
            await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(optionKey + "\r"), ct);
            await WaitForAnyPromptAsync(promptMatches, TimeSpan.FromSeconds(3));
            await Task.Delay(200, ct);
            await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(value + "\r"), ct);
            await WaitForAnyPromptAsync(new[] { "Enter P,D,I,S", "Enter C,R,T", "or H:" }, TimeSpan.FromSeconds(4));
            await Task.Delay(150, ct);
        }

        // 1. Se estiver no menu principal, entra no submenu [C]
        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("C\r"), ct);
        await Task.Delay(250, ct);
        await WaitForAnyPromptAsync(new[] { "Enter P,D,I,S", "or H:" }, TimeSpan.FromSeconds(6));

        // 2. Configurar TFTP Server IP [T] (Padrão nativo BIOS: 192.168.1.100)
        await SendParamAsync("T", new[] { "TFTP", "server", "remote", "address", ":" }, hostIp, "TFTP Server IP");

        // 3. Configurar Local IP [I] (Padrão nativo BIOS: 192.168.1.99)
        await SendParamAsync("I", new[] { "local", "IP", "address", ":" }, routerIp, "Local IP Address");

        // 4. Configurar Subnet Mask [S] (Padrão nativo BIOS: 255.255.255.0)
        var mask = !string.IsNullOrWhiteSpace(subnetMask) ? subnetMask : "255.255.255.0";
        await SendParamAsync("S", new[] { "subnet", "mask", ":" }, mask, "Subnet Mask");

        // 5. Configurar Firmware File Name [F]
        await SendParamAsync("F", new[] { "firmware", "file", "name", "image", ":" }, fileName, "Firmware File Name");

        // 6. Revisar Parâmetros [R] para diagnóstico e log
        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("R\r"), ct);
        var (okR, reviewText) = await WaitForAnyPromptWithCaptureAsync(new[] { "Enter P,D,I,S", "or H:" }, TimeSpan.FromSeconds(4));
        if (okR && !string.IsNullOrWhiteSpace(reviewText))
        {
            var reviewLines = reviewText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rline in reviewLines)
            {
                var tr = rline.Trim();
                if (!tr.StartsWith("Enter P,D", StringComparison.OrdinalIgnoreCase) && tr.Length > 2)
                {
                    await log($"  [BIOS-CONFIG] {tr}");
                }
            }
        }

        // 7. Retornar ao menu principal [Q]
        await log("[*] [BIOS] Retornando ao menu principal (opção [Q])...");
        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("Q\r"), ct);
        await WaitForAnyPromptAsync(new[] { "Enter C,R,T", "F,I,B,Q", "or H:" }, TimeSpan.FromSeconds(4));
        await Task.Delay(300, ct);

        // 8. Disparar a transferência TFTP no menu principal [T]
        await log("[*] [BIOS] Disparando download do firmware via TFTP (opção [T])...");
        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("T\r"), ct);
        await Task.Delay(400, ct);
    }

    private static async Task PreencherParametrosRedeBiosAsync(
        DeviceSession session,
        string hostIp,
        string routerIp,
        string fileName,
        Func<string, Task> log,
        CancellationToken ct)
    {
        var buffer = new byte[1024];
        var accumulator = new StringBuilder();
        var deadline = DateTime.UtcNow.AddSeconds(20);

        bool sentHost = false;
        bool sentRouter = false;
        bool sentFile = false;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var read = await session.Transport.ReadAsync(buffer, ct);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                accumulator.Append(text);
                session.EmitRawOutput(text);

                var current = accumulator.ToString();

                // 1. IP do Servidor TFTP
                if (!sentHost && TftpServerPromptRegex.IsMatch(current))
                {
                    sentHost = true;
                    await log($"[*] [BIOS] Configurando TFTP Server: {hostIp}");
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(hostIp + "\r"), ct);
                    accumulator.Clear();
                    await Task.Delay(300, ct);
                    continue;
                }

                // 2. IP Local do FortiGate
                if (sentHost && !sentRouter && LocalIpPromptRegex.IsMatch(current))
                {
                    sentRouter = true;
                    await log($"[*] [BIOS] Configurando Local Address: {routerIp}");
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(routerIp + "\r"), ct);
                    accumulator.Clear();
                    await Task.Delay(300, ct);
                    continue;
                }

                // 3. Nome do Arquivo .out
                if (sentRouter && !sentFile && FileNamePromptRegex.IsMatch(current))
                {
                    sentFile = true;
                    await log($"[*] [BIOS] Configurando File Name: {fileName}");
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(fileName + "\r"), ct);
                    accumulator.Clear();
                    await Task.Delay(300, ct);
                    break;
                }
            }

            await Task.Delay(80, ct);
        }

        if (!sentFile)
        {
            throw new TimeoutException("Timeout ao preencher os parâmetros de rede na BIOS do FortiGate.");
        }
    }

    internal static async Task<bool> MonitorarTransferenciaBiosAsync(
        DeviceSession session,
        Action<int, string, string> progress,
        Func<string, Task> log,
        CancellationToken ct,
        Func<long>? tftpBytesSentProvider = null,
        Func<bool>? isTftpFinishedProvider = null,
        Func<string, CancellationToken, Task>? instructOperator = null)
    {
        var buffer = new byte[2048];
        var accumulator = new StringBuilder();
        var lineBuffer = new StringBuilder();
        var deadline = DateTime.UtcNow.AddMinutes(5);
        var startTime = DateTime.UtcNow;
        var hashCount = 0;
        var receivedAnyTftpData = false;
        var sawTftpPrompt = false;
        var cableDisconnectedDetected = false;
        var cableRetryCount = 0;
        const int maxCableRetries = 3;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var read = await session.Transport.ReadAsync(buffer, ct);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);

                // Ao detectar início do TFTP, limpa o buffer histórico anterior para descartar restos do menu
                if (!sawTftpPrompt && (text.Contains("TFTP", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Ethernet port", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Connect to", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("MAC:", StringComparison.OrdinalIgnoreCase)))
                {
                    sawTftpPrompt = true;
                    accumulator.Clear();
                }

                accumulator.Append(text);
                session.EmitRawOutput(text);

                // Detecta desconexão do cabo de rede / falha de auto-negociação Ethernet na porta WAN
                if (!cableDisconnectedDetected && (CableDisconnectedRegex.IsMatch(text) || CableDisconnectedRegex.IsMatch(accumulator.ToString())))
                {
                    cableDisconnectedDetected = true;
                    await log("\n🚨 [CABO DE REDE DESCONECTADO NA PORTA WAN]");
                    await log("   A BIOS do FortiGate 40F acusou falha física Ethernet: 'Failed to complete copper auto-nego'.");
                    await log("   Não foi detectado sinal de rede na porta WAN (cabo desconectado ou computador sem link).\n");
                    progress(20, "Cabo Desconectado na WAN!", "Verifique o cabo Ethernet na porta WAN do FortiGate 40F...");

                    // Envia Ctrl+C para acelerar a interrupção e retorno ao menu principal da BIOS
                    try { await session.Transport.WriteAsync(new byte[] { 0x03 }, ct); } catch { }
                }

                // Stream de linhas da BIOS para o console
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c == '#')
                    {
                        hashCount++;
                        receivedAnyTftpData = true;
                    }
                    else if (c == '\n' || c == '\r')
                    {
                        if (lineBuffer.Length > 0)
                        {
                            var line = lineBuffer.ToString().Trim();
                            lineBuffer.Clear();
                            if (!string.IsNullOrWhiteSpace(line) && !line.All(ch => ch == '#'))
                            {
                                await log($"  [BIOS] {line}");
                            }
                        }
                    }
                    else
                    {
                        lineBuffer.Append(c);
                    }
                }

                if (hashCount > 0)
                {
                    var pct = Math.Min(30 + (hashCount / 3), 74);
                    progress(pct, "Transferindo Firmware...", $"Download em andamento ({hashCount} blocos recebidos)...");
                }

                var current = accumulator.ToString();

                // 1. Sucesso! Imagem recebida e verificada pela BIOS
                var tftpBytes = tftpBytesSentProvider?.Invoke() ?? 0;
                var isTftpDone = isTftpFinishedProvider?.Invoke() ?? false;
                var isActivelyTransferring = tftpBytes > 0 && !isTftpDone;

                if (SaveDefaultPromptRegex.IsMatch(current) || (isTftpDone && (current.Contains("Verification OK", StringComparison.OrdinalIgnoreCase) || current.Contains("Image Received", StringComparison.OrdinalIgnoreCase))))
                {
                    await log("[OK] Imagem transferida e verificada pela BIOS com sucesso!");
                    return true;
                }

                // 2. Se a BIOS retornou ao menu principal ou prompt de seleção
                var isAtMenu = current.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                               current.Contains("Enter P,D,I,S,G,V,T,F,E,R,N,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                               (sawTftpPrompt && !isActivelyTransferring && !isTftpDone && (DateTime.UtcNow - startTime).TotalSeconds > 8 && BiosMenuPromptRegex.IsMatch(current));

                if (isAtMenu)
                {
                    // Se acusou cabo desconectado ou falha de auto-negociação
                    if (cableDisconnectedDetected || CableDisconnectedRegex.IsMatch(current))
                    {
                        if (cableRetryCount < maxCableRetries && instructOperator != null)
                        {
                            cableRetryCount++;
                            await log($"\n📢 [INTERVENÇÃO OPERACIONAL {cableRetryCount}/{maxCableRetries}] Aguardando averiguação do cabo na porta WAN...");
                            progress(20, "Aguardando Operador...", $"Conecte o cabo de rede na porta WAN ({cableRetryCount}/{maxCableRetries})...");

                            var promptMsg =
                                "⚠️ CABO DE REDE ETHERNET DESCONECTADO NA PORTA WAN DO FORTIGATE 40F\n\n" +
                                "A BIOS do FortiGate 40F detectou que a porta física Ethernet WAN está SEM LINK:\n" +
                                "'Failed to complete copper auto-nego' (auto-negociação física Ethernet falhou).\n\n" +
                                "👉 AÇÃO NECESSÁRIA:\n" +
                                "1. Conecte o cabo de rede Ethernet do computador EXCLUSIVAMENTE na porta WAN do FortiGate 40F.\n" +
                                "2. Verifique se a outra extremidade está conectada e ativada na placa de rede do seu computador.\n" +
                                "3. Certifique-se de que o LED da porta WAN está ACESO (verde/laranja).\n\n" +
                                $"Tentativa {cableRetryCount} de {maxCableRetries}. Clique em OK após conectar o cabo para reiniciar o download TFTP na BIOS.";

                            await instructOperator(promptMsg, ct);

                            await log("[*] Operador confirmou conexão física do cabo na porta WAN. Reenviando opção [T] para reiniciar download TFTP na BIOS...");
                            progress(25, "Reiniciando TFTP...", "Disparando novo download TFTP após conexão do cabo...");

                            accumulator.Clear();
                            lineBuffer.Clear();
                            cableDisconnectedDetected = false;
                            sawTftpPrompt = false;
                            receivedAnyTftpData = false;
                            hashCount = 0;
                            startTime = DateTime.UtcNow;
                            deadline = DateTime.UtcNow.AddMinutes(5);

                            await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("T\r"), ct);
                            await Task.Delay(500, ct);
                            continue;
                        }
                        else
                        {
                            await log("\n🚨 [FALHA CRÍTICA: CABO DE REDE DESCONECTADO NA PORTA WAN]");
                            await log("   A BIOS encerrou a tentativa por ausência de link físico Ethernet na porta WAN (Failed to complete copper auto-nego).");
                            await log("   Certifique-se de conectar o cabo na porta WAN (com LED aceso) e reinicie o procedimento.\n");
                            return false;
                        }
                    }

                    // Se retornou ao menu por outro motivo sem transferir
                    var elapsedSec = (DateTime.UtcNow - startTime).TotalSeconds;
                    if (!isActivelyTransferring && !isTftpDone && elapsedSec > 25 && sawTftpPrompt)
                    {
                        await log("\n🚨 [FALHA TFTP NA BIOS] A BIOS tentou o download e retornou ao menu principal sem carregar a imagem.");
                        await log("   Certifique-se de que o cabo de rede Ethernet está conectado na porta WAN física do FortiGate 40F.\n");
                        return false;
                    }
                }

                // 3. Detecção de Erros explícitos da BIOS (outros que não cabo)
                if (TransferErrorRegex.IsMatch(current) && !cableDisconnectedDetected && !CableDisconnectedRegex.IsMatch(current))
                {
                    await log("\n🚨 [FALHA TFTP NA BIOS]");
                    await log("   A BIOS do FortiGate indicou falha na transferência TFTP.");
                    await log("   Retorno da BIOS: " + current.Trim());
                    await log("   🚨 REQUISITO FÍSICO OBRIGATÓRIO:");
                    await log("   - Conecte o cabo de rede Ethernet do computador EXCLUSIVAMENTE na porta WAN do FortiGate 40F.");
                    await log("   - Verifique se o LED da porta WAN está aceso com link ativo.");
                    await log("   - Não utilize as portas LAN (1, 2, 3) nem a porta A para recuperação na BIOS.\n");
                    return false;
                }
            }

            // Timeout precoce: se passaram 45 segundos e nenhum bloco foi recebido nem na serial nem no TFTP
            var currentTftpBytes = tftpBytesSentProvider?.Invoke() ?? 0;
            if (!receivedAnyTftpData && currentTftpBytes == 0 && (DateTime.UtcNow - startTime).TotalSeconds > 45)
            {
                var check = accumulator.ToString();
                if (check.Contains("TFTP", StringComparison.OrdinalIgnoreCase) || check.Contains("Connect to", StringComparison.OrdinalIgnoreCase))
                {
                    if (cableDisconnectedDetected || CableDisconnectedRegex.IsMatch(check))
                    {
                        await log("\n🚨 [ERRO CRÍTICO: CABO DE REDE DESCONECTADO]");
                        await log("   A BIOS acusou ausência de link na porta WAN (Failed to complete copper auto-nego).");
                        await log("   Verifique a conexão física do cabo Ethernet na porta WAN do FortiGate 40F.\n");
                        return false;
                    }

                    await log("\n🚨 [TIMEOUT TFTP NA BIOS]");
                    await log("   Passaram 45 segundos sem resposta de pacotes TFTP entre a BIOS do 40F e o servidor.");
                    await log("   Certifique-se de conectar o cabo Ethernet na porta WAN do FortiGate 40F (com LED aceso).\n");
                    return false;
                }
            }

            await Task.Delay(100, ct);
        }

        return false;
    }

    private static async Task<bool> ConfirmarEGravarFlashAsync(
        DeviceSession session,
        Action<int, string, string> progress,
        Func<string, Task> log,
        CancellationToken ct)
    {
        var buffer = new byte[2048];
        var accumulator = new StringBuilder();
        var deadline = DateTime.UtcNow.AddMinutes(4);
        var lastSentD = DateTime.MinValue;
        bool flashWriteStarted = false;

        await log("[*] [BIOS] Aguardando prompt da BIOS para confirmação de gravação permanente (Default firmware)...");

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var read = await session.Transport.ReadAsync(buffer, ct);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                accumulator.Append(text);
                session.EmitRawOutput(text);

                var current = accumulator.ToString();

                // Detecta erro ou abort
                if (FortiOsBootFailureRegex.IsMatch(current) || current.Contains("System halted", StringComparison.OrdinalIgnoreCase))
                {
                    await log("\n🚨 [HALT DA BIOS] A BIOS entrou em halt antes de gravar na Flash.");
                    return false;
                }

                // Detecta prompt de confirmação de particionamento / re-layout da Flash: Continue:[Y/N]?
                if (RelayoutPromptRegex.IsMatch(current))
                {
                    await log("[*] [BIOS] Confirmando re-layout da partição de boot da Flash ('Y')...");
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("Y\r"), ct);
                    accumulator.Clear();
                    await Task.Delay(200, ct);
                    continue;
                }

                // Detecta início efetivo da gravação na Flash
                if (!flashWriteStarted && ProgrammingFlashRegex.IsMatch(current))
                {
                    flashWriteStarted = true;
                    await log("[*] [BIOS] Início da gravação na memória Flash detectado!");
                    progress(82, "Gravando Flash...", "Gravando firmware na memória permanente (NAND Flash)...");
                }

                // Conclusão da gravação na Flash
                if (FlashDoneRegex.IsMatch(current) || current.Contains("Booting Default firmware", StringComparison.OrdinalIgnoreCase))
                {
                    await log("[OK] Gravação na memória Flash concluída com sucesso!");
                    progress(87, "Flash Gravada!", "Firmware gravado permanentemente na memória Flash.");
                    return true;
                }
            }

            // Enquanto a gravação não começar, envia 'D\r' a cada 400ms para capturar o prompt [D/B/R]?
            // e também 'Y\r' caso esteja no prompt de Continue:[Y/N]?
            if (!flashWriteStarted && (DateTime.UtcNow - lastSentD).TotalMilliseconds >= 400)
            {
                lastSentD = DateTime.UtcNow;
                try
                {
                    var acc = accumulator.ToString();
                    if (RelayoutPromptRegex.IsMatch(acc))
                    {
                        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("Y\r"), ct);
                    }
                    else
                    {
                        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("D\r"), ct);
                    }
                }
                catch { }
            }

            await Task.Delay(60, ct);
        }

        return false;
    }

    public static async Task<bool> AguardarBootFortiOsAsync(
        DeviceSession session,
        Action<int, string, string> progress,
        Func<string, Task> log,
        CancellationToken ct,
        string defaultPassword = "CQMR")
    {
        var buffer = new byte[2048];
        var accumulator = new StringBuilder();
        var deadline = DateTime.UtcNow.AddMinutes(5);
        var bootStart = DateTime.UtcNow;
        var lastOutputTime = DateTime.UtcNow;
        var lastWakeup = DateTime.UtcNow;

        var loginSent = false;
        var passwordSent = false;
        var newPassSent = false;
        var confirmPassSent = false;
        var lastBiosQuitSent = DateTime.MinValue;
        var rebootDetected = false;

        await log("[*] Acompanhando inicialização do FortiOS (aguardando carregamento e liberação do console)...");

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var read = await session.Transport.ReadAsync(buffer, ct);
            if (read > 0)
            {
                var text = Encoding.ASCII.GetString(buffer, 0, read);
                accumulator.Append(text);
                session.EmitRawOutput(text);
                lastOutputTime = DateTime.UtcNow;

                var current = accumulator.ToString();

                // Marca que o processo de reinicialização foi efetivamente observado na console
                if (!rebootDetected)
                {
                    if (current.Contains("System is resetting", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("resetting to factory", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("rebooting", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Restarting system", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("FortiGate-40F (", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("FortiBootLoader", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Verifying SHA256", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Press any key to display configuration menu", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Booting OS", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Reading boot image", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Initializing", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Starting OS", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("Enter C,R,T,F,I,B,Q,or H", StringComparison.OrdinalIgnoreCase) ||
                        current.Contains("login:", StringComparison.OrdinalIgnoreCase))
                    {
                        rebootDetected = true;
                    }
                }

                // 1. Detecção de falha de boot / halt precoce
                if (FortiOsBootFailureRegex.IsMatch(current) ||
                    current.Contains("System halted", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Neither DEFAULT nor BACKUP", StringComparison.OrdinalIgnoreCase))
                {
                    await log("\n🚨 [FALHA DE BOOT] O FortiOS falhou durante a inicialização e entrou em System Halted.");
                    return false;
                }

                // 2. Detecção de Menu BIOS/Bootloader interrompido (Enter C,R,T,F,I,B,Q,or H: ou Enter Selection:)
                if (current.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase))
                {
                    rebootDetected = true;
                    if ((DateTime.UtcNow - lastBiosQuitSent).TotalSeconds >= 3)
                    {
                        lastBiosQuitSent = DateTime.UtcNow;
                        await log("\n[>] Menu BIOS/BootLoader detectado durante o boot do FortiGate. Enviando 'Q' para sair do menu e carregar o FortiOS...");
                        accumulator.Clear();
                        await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("Q\r"), ct);
                        await Task.Delay(500, ct);
                        continue;
                    }
                }

                // 3. Detecção de diálogo de aviso prévio pós-recuperação (PRE WARNING: ... Press 'a' to accept)
                if (NetworkDevice.Core.Detection.DeviceDetector.IsDisclaimerPrompt(current))
                {
                    rebootDetected = true;
                    await log("\n[>] Diálogo de aviso prévio pós-boot FortiGate detectado (PRE WARNING). Aceitando termo com 'a'...");
                    accumulator.Clear();
                    await session.Transport.WriteAsync(new byte[] { 0x61 }, ct);
                    await Task.Delay(400, ct);
                    continue;
                }

                // 4. Detecção de troca forçada de nova senha pós-reset (New Password: / Please input a new password)
                if (Regex.IsMatch(current, @"(?i)(?:new\s+password\s*[:?]|please\s+input\s+a\s+new\s+password|change\s+your\s+password)") && !newPassSent)
                {
                    rebootDetected = true;
                    newPassSent = true;
                    accumulator.Clear();
                    await log($"\n[>] Troca obrigatória de nova senha pós-reset detectada. Definindo nova senha ({defaultPassword})...");
                    await Task.Delay(150, ct);
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(defaultPassword + "\r"), ct);
                    await Task.Delay(400, ct);
                    continue;
                }

                // 5. Confirmação da nova senha (Confirm Password:)
                if (Regex.IsMatch(current, @"(?i)(?:confirm|verify|re-?enter)\s+(?:new\s+)?password\s*[:?]") && !confirmPassSent)
                {
                    rebootDetected = true;
                    confirmPassSent = true;
                    accumulator.Clear();
                    await log($"\n[>] Confirmando nova senha ({defaultPassword})...");
                    await Task.Delay(150, ct);
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(defaultPassword + "\r"), ct);
                    await Task.Delay(400, ct);
                    continue;
                }

                // 6. Detecção de Login FortiOS (login:)
                var isLoginPrompt = Regex.IsMatch(current, @"(?i)(?:FortiGate|FGT|FG|[A-Za-z0-9_\-]{3,30})\s+login\s*[:?]") ||
                                    (current.Contains("login:", StringComparison.OrdinalIgnoreCase) && !current.Contains("Password:"));
                if (isLoginPrompt && !loginSent)
                {
                    rebootDetected = true;
                    loginSent = true;
                    accumulator.Clear();
                    await log("\n[>] Prompt de login FortiOS pós-boot detectado. Autenticando com 'admin'...");
                    await Task.Delay(150, ct);
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("admin\r"), ct);
                    await Task.Delay(350, ct);
                    continue;
                }

                // 7. Detecção de Senha após Login (Password:)
                var isPasswordPrompt = loginSent && !passwordSent &&
                    Regex.IsMatch(current, @"(?i)(?:^|[\s\b])(?:Password|Login\s+password)\s*[:?]") &&
                    !Regex.IsMatch(current, @"(?i)(?:new|confirm)\s+password");
                if (isPasswordPrompt)
                {
                    rebootDetected = true;
                    passwordSent = true;
                    accumulator.Clear();
                    await log("\n[>] Prompt de senha detectado. Enviando senha em branco de fábrica...");
                    await Task.Delay(150, ct);
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("\r"), ct);
                    await Task.Delay(450, ct);
                    continue;
                }

                // 8. Se a senha em branco for rejeitada (Login incorrect), tenta com a senha padrão defaultPassword
                if (loginSent && passwordSent && !newPassSent &&
                    (current.Contains("Login incorrect", StringComparison.OrdinalIgnoreCase) || current.Contains("Login failed", StringComparison.OrdinalIgnoreCase)))
                {
                    rebootDetected = true;
                    await log($"\n[>] Senha em branco não aceita. Tentando senha padrão ({defaultPassword})...");
                    loginSent = false;
                    passwordSent = false;
                    accumulator.Clear();
                    await Task.Delay(300, ct);
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("admin\r"), ct);
                    await Task.Delay(300, ct);
                    await session.Transport.WriteAsync(Encoding.ASCII.GetBytes(defaultPassword + "\r"), ct);
                    await Task.Delay(450, ct);
                    continue;
                }

                // 9. Detecção de Prompt CLI operacional (# ou $)
                var lastCleanLine = current.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
                var isOperationalCliPrompt = !lastCleanLine.Contains("login:", StringComparison.OrdinalIgnoreCase) &&
                                             !lastCleanLine.Contains("Password:", StringComparison.OrdinalIgnoreCase) &&
                                             !lastCleanLine.Contains("C,R,T,F,I,B,Q,or H", StringComparison.OrdinalIgnoreCase) &&
                                             !lastCleanLine.Contains("Enter Selection:", StringComparison.OrdinalIgnoreCase) &&
                                             !lastCleanLine.Contains("NP6XLITE", StringComparison.OrdinalIgnoreCase) &&
                                             !lastCleanLine.Contains("Press any key", StringComparison.OrdinalIgnoreCase) &&
                                             !lastCleanLine.Contains("Booting OS", StringComparison.OrdinalIgnoreCase) &&
                                             !lastCleanLine.Contains("Initializing", StringComparison.OrdinalIgnoreCase) &&
                                             (Regex.IsMatch(lastCleanLine, @"(?i)(?:FortiGate|FGT|FG)[A-Za-z0-9_\-]*\s*(?:\([^()\r\n]*\))?\s*[#$]\s*$") ||
                                              Regex.IsMatch(lastCleanLine, @"(?i)[A-Za-z0-9_\-\.]+\s*(?:\([^()\r\n]*\))?\s*#\s*$"));

                // Cláusula de proteção estrita anti-falso-positivo:
                // O prompt CLI operacional pós-reset SOMENTE é aceito se o login já tiver sido enviado.
                // NUNCA aceitar prompt se o login não foi enviado, pois FortiOS reiniciado SEMPRE exige login: admin.
                if (isOperationalCliPrompt && loginSent)
                {
                    await log($"\n[OK] FortiOS inicializado e autenticado com sucesso! Prompt operacional liberado: '{lastCleanLine}'");
                    session.SetAuthenticatedState("admin", (newPassSent || confirmPassSent) ? defaultPassword : "", lastCleanLine, ExecMode.PrivilegedExec);
                    return true;
                }
            }

            // Envio cauteloso de CRLF apenas se o boot estiver demorando e SILENCIOSO
            // NUNCA enviar nos primeiros 65 segundos para não acionar o menu da BIOS ("Press Any Key...")
            var elapsedTotal = (DateTime.UtcNow - bootStart).TotalSeconds;
            var quietTime = (DateTime.UtcNow - lastOutputTime).TotalSeconds;
            var timeSinceWakeup = (DateTime.UtcNow - lastWakeup).TotalSeconds;

            if (elapsedTotal >= 65 && quietTime >= 10 && timeSinceWakeup >= 8)
            {
                var cur = accumulator.ToString();
                if (!cur.Contains("Enter C,R,T,F,I,B,Q,or H:", StringComparison.OrdinalIgnoreCase) &&
                    !cur.Contains("Press Any Key", StringComparison.OrdinalIgnoreCase) &&
                    !cur.Contains("Verifying password", StringComparison.OrdinalIgnoreCase) &&
                    !Regex.IsMatch(cur, @"(?i)(?:new|confirm)\s+password"))
                {
                    lastWakeup = DateTime.UtcNow;
                    try { await session.Transport.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), ct); } catch { }
                }
            }

            await Task.Delay(100, ct);
        }

        await log("\n[ERRO] Tempo limite de 5 minutos esgotado aguardando o boot completo e autenticação do FortiOS.");
        return false;
    }
}
