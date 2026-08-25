using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace NetworkDevice.Core.Diagnostics;

public sealed record PingPacketInfo(
    int Sequence,
    IPStatus Status,
    long RoundtripTimeMs,
    int? Ttl,
    int? BufferSize);

public sealed record ConnectivityTestResult(
    string Target,
    int PacketsSent,
    int PacketsReceived,
    double PacketLossPercentage,
    long MinRttMs,
    long MaxRttMs,
    double AvgRttMs,
    double JitterMs,
    bool IsSuccess,
    IReadOnlyList<PingPacketInfo> Packets);

public class ConnectivityService
{
    private readonly Func<string, Task>? _logger;

    public ConnectivityService(Func<string, Task>? logger = null)
    {
        _logger = logger;
    }

    private async Task LogAsync(string message)
    {
        if (_logger != null)
        {
            await _logger(message);
        }
    }

    /// <summary>
    /// Executa uma sequência de testes ICMP Ping para o endereço IP ou host informado.
    /// <summary>
    /// Executa uma sequência de testes ICMP Ping para o endereço IP ou host informado,
    /// com suporte a vincular o tráfego estritamente à interface de teste conectada ao roteador (sourceIpAddress).
    /// </summary>
    public async Task<ConnectivityTestResult> TestPingAsync(
        string hostOrIp,
        int count = 4,
        int timeoutMs = 2500,
        int bufferSizeBytes = 32,
        string? sourceIpAddress = null,
        Action<PingPacketInfo>? onPacketReceived = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp))
            throw new ArgumentException("O endereço de destino não pode ser vazio.", nameof(hostOrIp));

        // Limpa espaços ou múltiplos IPs separados por vírgula/ponto-e-vírgula
        var cleanTarget = hostOrIp.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        count = Math.Max(1, Math.Min(count, 50));
        var buffer = new byte[Math.Max(1, Math.Min(bufferSizeBytes, 1472))];
        new Random().NextBytes(buffer);

        // Se uma interface/IP de origem foi informada no Windows, força saída estrita por ela (-S) e NÃO permite vazamento por Wi-Fi
        if (!string.IsNullOrWhiteSpace(sourceIpAddress) && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            await LogAsync($"[*] Direcionando ICMP estritamente via interface conectada ao roteador (IP Origem: {sourceIpAddress}) para isolar Wi-Fi/outras redes...");
            var cliResult = await RunCliPingFallbackAsync(cleanTarget, count, timeoutMs, sourceIpAddress, cancellationToken);
            if (cliResult != null)
            {
                return cliResult;
            }
            return new ConnectivityTestResult(cleanTarget, count, 0, 100, 0, 0, 0, 0, false, new List<PingPacketInfo>());
        }

        var packets = new List<PingPacketInfo>();
        var rtts = new List<long>();

        await LogAsync($"[*] Iniciando teste de conectividade ICMP para {cleanTarget} ({count} pacotes, timeout {timeoutMs}ms)...");

        using var ping = new Ping();
        var isParsedIp = System.Net.IPAddress.TryParse(cleanTarget, out var ipAddress);

        for (var i = 1; i <= count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                PingReply reply;
                if (isParsedIp && ipAddress != null)
                {
                    reply = await ping.SendPingAsync(ipAddress, timeoutMs, buffer);
                }
                else
                {
                    reply = await ping.SendPingAsync(cleanTarget, timeoutMs, buffer);
                }

                var packet = new PingPacketInfo(
                    i,
                    reply.Status,
                    reply.Status == IPStatus.Success ? reply.RoundtripTime : 0,
                    reply.Status == IPStatus.Success ? reply.Options?.Ttl : null,
                    reply.Status == IPStatus.Success ? reply.Buffer.Length : null);

                packets.Add(packet);
                onPacketReceived?.Invoke(packet);

                if (reply.Status == IPStatus.Success)
                {
                    rtts.Add(reply.RoundtripTime);
                    await LogAsync($"  Resposta {i}/{count} de {cleanTarget}: bytes={reply.Buffer.Length} tempo={reply.RoundtripTime}ms TTL={reply.Options?.Ttl}");
                }
                else
                {
                    await LogAsync($"  Resposta {i}/{count} de {cleanTarget}: Status={reply.Status}");
                }
            }
            catch (Exception ex)
            {
                var packet = new PingPacketInfo(i, IPStatus.Unknown, 0, null, null);
                packets.Add(packet);
                onPacketReceived?.Invoke(packet);
                await LogAsync($"  Falha no pacote {i}/{count} para {cleanTarget}: {ex.Message}");
            }

            if (i < count)
            {
                await Task.Delay(200, cancellationToken);
            }
        }

        // Se todos falharam no .NET Ping e estamos no Windows, tenta fallback nativo via ping.exe
        if (rtts.Count == 0 && System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            var fallback = await RunCliPingFallbackAsync(cleanTarget, count, timeoutMs, sourceIpAddress, cancellationToken);
            if (fallback != null && fallback.PacketsReceived > 0)
            {
                return fallback;
            }
        }

        var received = rtts.Count;
        var lossPct = ((count - received) / (double)count) * 100.0;
        var minRtt = received > 0 ? rtts.Min() : 0;
        var maxRtt = received > 0 ? rtts.Max() : 0;
        var avgRtt = received > 0 ? rtts.Average() : 0.0;

        var jitter = 0.0;
        if (rtts.Count > 1)
        {
            var diffSum = 0.0;
            for (var j = 0; j < rtts.Count - 1; j++)
            {
                diffSum += Math.Abs(rtts[j + 1] - rtts[j]);
            }
            jitter = diffSum / (rtts.Count - 1);
        }

        var isSuccess = received > 0 && lossPct < 50.0;

        await LogAsync($"--- Estatísticas de Ping para {cleanTarget} ---");
        await LogAsync($"  Pacotes: Enviados = {count}, Recebidos = {received}, Perdidos = {count - received} ({lossPct:F0}% de perda)");
        if (received > 0)
        {
            await LogAsync($"  RTT Mínimo = {minRtt}ms, Máximo = {maxRtt}ms, Médio = {avgRtt:F1}ms, Jitter = {jitter:F1}ms");
        }

        return new ConnectivityTestResult(
            cleanTarget,
            count,
            received,
            lossPct,
            minRtt,
            maxRtt,
            avgRtt,
            jitter,
            isSuccess,
            packets);
    }

    private async Task<ConnectivityTestResult?> RunCliPingFallbackAsync(
        string target,
        int count,
        int timeoutMs,
        string? sourceIpAddress,
        CancellationToken ct)
    {
        try
        {
            var srcArg = !string.IsNullOrWhiteSpace(sourceIpAddress) ? $"-S {sourceIpAddress.Trim()} " : "";
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ping.exe",
                Arguments = $"{srcArg}-n {count} -w {timeoutMs} {target}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var rtts = new List<long>();
            var packets = new List<PingPacketInfo>();
            var seq = 1;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    line,
                    @"(?i)(?:tempo|time)[=<]\s*(\d+)\s*ms");

                if (match.Success && long.TryParse(match.Groups[1].Value, out var ms))
                {
                    rtts.Add(ms);
                    packets.Add(new PingPacketInfo(seq++, IPStatus.Success, ms, null, 32));
                    await LogAsync($"  [Driver Nativo Windows] Resposta {seq - 1}/{count} de {target}: tempo={ms}ms");
                }
            }

            if (rtts.Count > 0)
            {
                var received = rtts.Count;
                var lossPct = ((count - received) / (double)count) * 100.0;
                var minRtt = rtts.Min();
                var maxRtt = rtts.Max();
                var avgRtt = rtts.Average();

                await LogAsync($"[OK] ICMP {target} confirmado via driver nativo Windows: {received}/{count} pacotes, RTT Médio: {avgRtt:F1}ms.");

                return new ConnectivityTestResult(
                    target,
                    count,
                    received,
                    lossPct,
                    minRtt,
                    maxRtt,
                    avgRtt,
                    0,
                    true,
                    packets);
            }
        }
        catch { }

        return null;
    }

    public sealed record TelnetTestResult(
        string Host,
        int Port,
        bool IsSuccess,
        long LatencyMs,
        string Banner,
        string Transcript,
        string? Error);

    /// <summary>
    /// Testa acesso remoto via Telnet (TCP 23) com tela de login visível.
    /// Fluxo: conecta → lê banner → envia Username → 1s → lê prompt Password → envia Password → 1s → lê prompt HPE/Cisco.
    /// Cada etapa é logada no terminal para visibilidade do operador.
    /// </summary>
    /// <summary>
    /// Testa acesso remoto via Telnet (TCP 23) com tela de login visível e negociação de opções RFC 854 IAC.
    /// Fluxo: conecta TCP -> negocia IAC -> lê banner -> envia Username -> lê prompt Password -> envia Password -> valida prompt HPE/Cisco.
    /// </summary>
    public async Task<TelnetTestResult> TestTelnetAsync(
        string hostOrIp,
        int port = 23,
        string? username = "EBT",
        string? password = "PRO1AN",
        int timeoutMs = 10000,
        string? sourceIpAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp))
            throw new ArgumentException("Host não pode ser vazio.", nameof(hostOrIp));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var transcript = new StringBuilder();
        void AppendTranscript(string label, string data)
        {
            var clean = data.Replace("\r", "").Trim();
            if (clean.Length > 400) clean = clean[..400] + "...";
            transcript.AppendLine($"[{label}] {clean}");
        }

        await LogAsync($"");
        await LogAsync($"=================================================================");
        await LogAsync($"          TESTE DE ACESSO REMOTO TELNET {hostOrIp}:{port}          ");
        await LogAsync($"=================================================================");
        await LogAsync($"  Alvo    : {hostOrIp}:{port}");
        await LogAsync($"  Usuário : {username} | Senha: {(string.IsNullOrEmpty(password) ? "(vazia)" : new string('*', password.Length))}");
        if (!string.IsNullOrEmpty(sourceIpAddress))
            await LogAsync($"  Origem  : {sourceIpAddress} (Interface de Teste)");
        await LogAsync($"-----------------------------------------------------------------");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            await LogAsync($"[1/5] Conectando TCP em {hostOrIp}:{port} ...");
            TcpClient? client = null;
            long latency = 0;
            var connectDeadline = DateTime.UtcNow.AddSeconds(8);
            Exception? lastConnEx = null;

            while (DateTime.UtcNow < connectDeadline && !cts.IsCancellationRequested)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(sourceIpAddress) && System.Net.IPAddress.TryParse(sourceIpAddress, out var srcIp))
                    {
                        try
                        {
                            client = new TcpClient(new System.Net.IPEndPoint(srcIp, 0));
                            var connSw = System.Diagnostics.Stopwatch.StartNew();
                            await client.ConnectAsync(hostOrIp, port, cts.Token);
                            connSw.Stop();
                            latency = connSw.ElapsedMilliseconds;
                            break;
                        }
                        catch
                        {
                            client?.Dispose();
                            client = null;
                        }
                    }

                    client = new TcpClient();
                    var connSw2 = System.Diagnostics.Stopwatch.StartNew();
                    await client.ConnectAsync(hostOrIp, port, cts.Token);
                    connSw2.Stop();
                    latency = connSw2.ElapsedMilliseconds;
                    break;
                }
                catch (Exception ex)
                {
                    lastConnEx = ex;
                    client?.Dispose();
                    client = null;
                    await Task.Delay(1000, cts.Token);
                }
            }

            if (client == null || !client.Connected)
            {
                throw lastConnEx ?? new SocketException((int)SocketError.HostUnreachable);
            }

            sw.Stop();
            await LogAsync($"      → Conectado! Latência TCP: {latency}ms");

            using var activeClient = client;
            var stream = activeClient.GetStream();

            // 2 - Ler banner e negociar opções IAC
            await LogAsync($"[2/5] Aguardando banner / prompt de login...");
            var banner = await ReadAndNegotiateTelnetAsync(stream, 4000, cts.Token);
            if (string.IsNullOrEmpty(banner) || (!banner.Contains("login", StringComparison.OrdinalIgnoreCase) && !banner.Contains("Username", StringComparison.OrdinalIgnoreCase)))
            {
                // Provoca o banner enviando CRLF
                await WriteTelnetAsync(stream, "\r\n", cts.Token);
                var extra = await ReadAndNegotiateTelnetAsync(stream, 2500, cts.Token);
                banner += "\n" + extra;
            }

            if (!string.IsNullOrEmpty(banner))
            {
                AppendTranscript("BANNER", banner);
                await LogAsync($"      ← Recebido: {banner.Trim().Replace("\n", " | ")}");
            }

            // 3 - Enviar Username
            if (!string.IsNullOrEmpty(username))
            {
                await LogAsync($"[3/5] Enviando Username: {username}");
                await WriteTelnetAsync(stream, username + "\r\n", cts.Token);
                await Task.Delay(800, cts.Token);

                var afterUser = await ReadAndNegotiateTelnetAsync(stream, 3500, cts.Token);
                if (!string.IsNullOrEmpty(afterUser))
                {
                    AppendTranscript("APÓS USER", afterUser);
                    await LogAsync($"      ← Resposta: {afterUser.Trim().Replace("\n", " | ")}");
                    banner += "\n" + afterUser;
                }
            }

            // 4 - Enviar Password (tenta senha informada e fallback para senha de complexidade PRO1ANPRO1AN)
            var passAttempts = new List<string>();
            if (!string.IsNullOrEmpty(password)) passAttempts.Add(password);
            if (password == "PRO1AN" && !passAttempts.Contains("PRO1ANPRO1AN")) passAttempts.Add("PRO1ANPRO1AN");
            if (password == "PRO1ANPRO1AN" && !passAttempts.Contains("PRO1AN")) passAttempts.Add("PRO1AN");

            var loginOk = false;
            foreach (var pass in passAttempts)
            {
                await LogAsync($"[4/5] Enviando Password: {new string('*', pass.Length)}");
                await WriteTelnetAsync(stream, pass + "\r\n", cts.Token);
                await Task.Delay(1000, cts.Token);

                var afterPass = await ReadAndNegotiateTelnetAsync(stream, 4000, cts.Token);
                if (!string.IsNullOrEmpty(afterPass))
                {
                    AppendTranscript("APÓS PASS", afterPass);
                    await LogAsync($"      ← Resposta: {afterPass.Trim().Replace("\n", " | ")}");
                    banner += "\n" + afterPass;

                    if (IsTelnetPrompt(afterPass) || afterPass.Contains("<HPE>") || afterPass.Contains("[HPE]") || afterPass.Contains(">") || afterPass.Contains("#"))
                    {
                        loginOk = true;
                        break;
                    }
                }

                if (IsTelnetPrompt(banner))
                {
                    loginOk = true;
                    break;
                }
            }

            // 5 - Validar prompt final (<HPE>, [HPE], HPE>, #)
            await LogAsync($"[5/5] Validando prompt de acesso...");
            var success = loginOk || IsTelnetPrompt(banner);
            if (success)
            {
                await LogAsync($"[OK] Telnet {hostOrIp}:{port} AUTENTICADO com sucesso — {latency}ms!");
                await LogAsync($"=================================================================\n");
                return new TelnetTestResult(hostOrIp, port, true, latency, banner.Trim(), transcript.ToString(), null);
            }

            if (banner.Contains("Username", StringComparison.OrdinalIgnoreCase) || banner.Contains("login", StringComparison.OrdinalIgnoreCase) || banner.Contains("Password", StringComparison.OrdinalIgnoreCase))
            {
                await LogAsync($"[OK] Telnet {hostOrIp}:{port} acessível (Porta Aberta e Respondendo) — {latency}ms.");
                await LogAsync($"=================================================================\n");
                return new TelnetTestResult(hostOrIp, port, true, latency, banner.Trim(), transcript.ToString(), null);
            }

            await LogAsync($"[FALHA] Telnet {hostOrIp}:{port} — sem prompt válido após login. Transcript acima.");
            await LogAsync($"=================================================================\n");
            return new TelnetTestResult(hostOrIp, port, false, latency, banner.Trim(), transcript.ToString(), "Sem prompt após login — verifique credenciais.");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            var msg = $"Timeout após {timeoutMs}ms — porta {port} não respondeu em {hostOrIp}.";
            await LogAsync($"[FALHA TELNET] {msg}");
            await LogAsync($"=================================================================\n");
            return new TelnetTestResult(hostOrIp, port, false, sw.ElapsedMilliseconds, string.Empty, transcript.ToString(), msg);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var msg = $"{ex.GetType().Name}: {ex.Message}";
            await LogAsync($"[FALHA TELNET] {hostOrIp}:{port} — {msg}");
            await LogAsync($"=================================================================\n");
            return new TelnetTestResult(hostOrIp, port, false, sw.ElapsedMilliseconds, string.Empty, transcript.ToString(), msg);
        }
    }

    private static async Task<string> ReadAndNegotiateTelnetAsync(NetworkStream stream, int timeoutMs, CancellationToken ct)
    {
        var buf = new byte[4096];
        var responseSb = new StringBuilder();
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (!stream.DataAvailable)
            {
                await Task.Delay(150, ct);
                if (!stream.DataAvailable)
                {
                    var cur = responseSb.ToString();
                    if (cur.Contains("Username:", StringComparison.OrdinalIgnoreCase) ||
                        cur.Contains("login:", StringComparison.OrdinalIgnoreCase) ||
                        cur.Contains("Password:", StringComparison.OrdinalIgnoreCase) ||
                        cur.Contains("password:", StringComparison.OrdinalIgnoreCase) ||
                        cur.Contains(">") || cur.Contains("#") || cur.Contains("]"))
                    {
                        break;
                    }
                    continue;
                }
            }

            var read = await stream.ReadAsync(buf, 0, buf.Length, ct);
            if (read <= 0) break;

            var i = 0;
            while (i < read)
            {
                var b = buf[i++];
                if (b == 0xFF) // IAC (RFC 854)
                {
                    if (i >= read) break;
                    var verb = buf[i++];
                    if (verb is 251 or 252 or 253 or 254) // WILL, WONT, DO, DONT
                    {
                        if (i >= read) break;
                        var opt = buf[i++];

                        if (verb == 253) // DO -> responde WONT
                        {
                            var reply = new byte[] { 255, 252, opt };
                            await stream.WriteAsync(reply, 0, reply.Length, ct);
                        }
                        else if (verb == 251) // WILL -> responde DO para ECHO/SGA ou DONT para outros
                        {
                            var replyCode = (opt is 1 or 3) ? (byte)253 : (byte)254;
                            var reply = new byte[] { 255, replyCode, opt };
                            await stream.WriteAsync(reply, 0, reply.Length, ct);
                        }
                    }
                    else if (verb == 250) // SB subnegotiation
                    {
                        while (i < read && buf[i] != 240) i++;
                        if (i < read) i++;
                    }
                }
                else if (b != 0)
                {
                    responseSb.Append((char)b);
                }
            }

            var current = responseSb.ToString();
            if (current.Contains("Username:", StringComparison.OrdinalIgnoreCase) ||
                current.Contains("login:", StringComparison.OrdinalIgnoreCase) ||
                current.Contains("Password:", StringComparison.OrdinalIgnoreCase) ||
                current.Contains("<HPE>", StringComparison.OrdinalIgnoreCase) ||
                current.Contains("[HPE]", StringComparison.OrdinalIgnoreCase) ||
                current.Contains(">") || current.Contains("#") || current.Contains("]"))
            {
                await Task.Delay(200, ct);
                if (!stream.DataAvailable) break;
            }
        }

        return responseSb.ToString();
    }

    private static async Task WriteTelnetAsync(NetworkStream stream, string text, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        await stream.WriteAsync(bytes, 0, bytes.Length, ct);
        await stream.FlushAsync(ct);
    }

    private static string StripTelnetIAC(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\0') continue;
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static bool IsTelnetPrompt(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        return t.EndsWith("<HPE>", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("[HPE]", StringComparison.OrdinalIgnoreCase)
            || t.Contains("<HPE>", StringComparison.OrdinalIgnoreCase)
            || t.Contains("[HPE]", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith(">")
            || t.EndsWith("#")
            || t.EndsWith("]")
            || (t.Contains("<") && t.Contains(">"))
            || (t.Contains("[") && t.Contains("]"))
            || System.Text.RegularExpressions.Regex.IsMatch(t, @"(?i)(?:<.+?>|\[.+?\]|[A-Za-z0-9_.\-/: ]+[#>])\s*$");
    }
}
