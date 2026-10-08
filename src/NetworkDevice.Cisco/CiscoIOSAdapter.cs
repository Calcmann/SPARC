using System.Text.RegularExpressions;
using NetworkDevice.Core.Device;
using NetworkDevice.Core.Session;

namespace NetworkDevice.Cisco;

public sealed class CiscoIOSAdapter : IDeviceAdapter
{
    private static readonly Regex ModelNumber = new(
        @"\b(?:system\s+)?model\s+number\s*:?\s*(?<m>[A-Za-z0-9._/-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModelBanner = new(
        @"\bcisco\s+(?<m>[A-Za-z0-9._/-]+)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModelUdiTable = new(
        @"(?m)^\*?\d+\s+(?<m>CISCO[A-Za-z0-9._/-]+)\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ModelUdi = new(
        @"(?i)(?:PID|Product\s+ID)\s*:\s*(?<m>[A-Za-z0-9._/-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex VersionRegex = new(
        @"\bVersion\s+(?<v>[\d()A-Za-z.-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SerialRegex = new(
        @"\b(?:system\s+)?serial\s+number\s*:?\s*(?<s>[A-Za-z0-9._-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SerialProcessorBoard = new(
        @"(?i)Processor\s+board\s+ID\s+(?<s>[A-Za-z0-9._-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SerialUdiTable = new(
        @"(?m)^\*?\d+\s+\S+\s+(?<s>[A-Za-z0-9._-]+)\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string? _enableSecret;
    private readonly List<string> _candidatePasswords = new();

    public string? ResolvedEnableSecret { get; private set; }

    public CiscoIOSAdapter(string? enableSecret = null, IEnumerable<string>? candidatePasswords = null)
    {
        _enableSecret = enableSecret;
        if (candidatePasswords != null)
        {
            foreach (var p in candidatePasswords)
            {
                if (!string.IsNullOrWhiteSpace(p) && !_candidatePasswords.Contains(p.Trim()))
                    _candidatePasswords.Add(p.Trim());
            }
        }
    }

    public string Vendor => "Cisco";

    public static SessionOptions CreateSessionOptions(string? enableSecret, string? username = null, string? password = null) =>
        new()
        {
            PromptMatcher = RegexPromptMatcher.CiscoIos(),
            Username = username,
            Password = password
        };

    public Task EnterPrivilegedExecAsync(DeviceSession session, CancellationToken cancellationToken = default) =>
        EnterPrivilegedExecAsync(session, candidatePasswords: null, cancellationToken);

    public async Task EnterPrivilegedExecAsync(DeviceSession session, IEnumerable<string>? candidatePasswords, CancellationToken cancellationToken = default)
    {
        if (session.Mode == ExecMode.Rommon || session.CurrentPrompt?.Contains("rommon", StringComparison.OrdinalIgnoreCase) == true)
            return;

        // Se estiver em submodo de configuração (ex: (config)#, (config-if)#), volta para privileged exec
        var curPrompt = session.CurrentPrompt?.Trim() ?? string.Empty;
        if (curPrompt.Contains("(config", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await session.SendCommandAsync("end", TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch { }
        }

        // 1. Se o prompt termina com '#', verifica se já é realmente nível 15
        // (roteadores com 'prompt hostname#' podem exibir '#' mesmo em nível 1).
        if (curPrompt.EndsWith("#"))
        {
            try
            {
                var privCheck = await session.SendCommandAsync("show privilege", TimeSpan.FromSeconds(5), cancellationToken);
                if (privCheck.Contains("privilege level is 15", StringComparison.OrdinalIgnoreCase))
                {
                    return; // Já está no nível 15!
                }
            }
            catch { }
        }

        // 2. DISPARA COMANDO 'enable'
        // Testamos se o roteador entra direto sem senha ou se exige senha
        var passwordRegex = new Regex(@"(?i)(?:password|secret)\s*[:?]");

        var result = await session.SendExpectAsync(
            "enable",
            new StopCondition[]
            {
                new StopCondition.LineRegex("password", passwordRegex),
                new StopCondition.Contains("Password", "Password:"),
                new StopCondition.Contains("password_lower", "password:"),
                new StopCondition.Prompt()
            },
            TimeSpan.FromSeconds(15),
            cancellationToken);

        bool requestedPassword = result.Matched is StopCondition.Contains ||
                                 result.Matched is StopCondition.LineRegex ||
                                 result.Output.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                                 passwordRegex.IsMatch(result.Output);

        // SITUAÇÃO 1: SEM SENHA
        // O roteador não solicitou senha e retornou diretamente o prompt privilegiado
        if (!requestedPassword)
        {
            // Valida se subiu para level 15
            var privAfter = "";
            try { privAfter = await session.SendCommandAsync("show privilege", TimeSpan.FromSeconds(5), cancellationToken); } catch { }
            if (privAfter.Contains("privilege level is 15", StringComparison.OrdinalIgnoreCase) ||
                session.CurrentPrompt?.EndsWith("#") == true)
            {
                return; // Sucesso sem senha!
            }
        }

        // SITUAÇÃO 2: COM SENHA
        // Monta lista de senhas candidatas em ordem de prioridade
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_enableSecret)) candidates.Add(_enableSecret.Trim());
        if (!string.IsNullOrWhiteSpace(ResolvedEnableSecret)) candidates.Add(ResolvedEnableSecret.Trim());
        if (candidatePasswords != null)
        {
            foreach (var p in candidatePasswords)
            {
                if (!string.IsNullOrWhiteSpace(p) && !candidates.Contains(p.Trim()))
                    candidates.Add(p.Trim());
            }
        }
        foreach (var p in _candidatePasswords)
        {
            if (!string.IsNullOrWhiteSpace(p) && !candidates.Contains(p.Trim()))
                candidates.Add(p.Trim());
        }

        // Padrão Embratel / Telecom / SPARC
        if (!candidates.Contains("PRO1AN")) candidates.Add("PRO1AN");
        // Padrões comuns de fábrica
        if (!candidates.Contains("cisco")) candidates.Add("cisco");
        if (!candidates.Contains("cisco123")) candidates.Add("cisco123");
        if (!candidates.Contains("admin")) candidates.Add("admin");
        if (!candidates.Contains("CQMR")) candidates.Add("CQMR");

        bool authSuccess = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            var pass = candidates[i];
            if (cancellationToken.IsCancellationRequested) break;

            var passResult = await session.SendExpectAsync(
                pass,
                new StopCondition[]
                {
                    new StopCondition.LineRegex("password", passwordRegex),
                    new StopCondition.Contains("Password", "Password:"),
                    new StopCondition.Contains("password_lower", "password:"),
                    new StopCondition.Contains("BadSecrets", "% Bad secrets"),
                    new StopCondition.Prompt()
                },
                TimeSpan.FromSeconds(8),
                cancellationToken);

            if (passResult.Output.Contains("% Bad secrets", StringComparison.OrdinalIgnoreCase))
            {
                // Cisco encerrou o bloco de 3 tentativas e retornou para o prompt do usuário (ex: Router>).
                // Se ainda restam candidatos a testar, enviamos 'enable' novamente para abrir nova rodada!
                if (i < candidates.Count - 1)
                {
                    try
                    {
                        var reEnable = await session.SendExpectAsync(
                            "enable",
                            new StopCondition[]
                            {
                                new StopCondition.LineRegex("password", passwordRegex),
                                new StopCondition.Contains("Password", "Password:"),
                                new StopCondition.Prompt()
                            },
                            TimeSpan.FromSeconds(5),
                            cancellationToken);

                        if (reEnable.Matched is StopCondition.Prompt &&
                            !passwordRegex.IsMatch(reEnable.Output) &&
                            !reEnable.Output.Contains("Password", StringComparison.OrdinalIgnoreCase))
                        {
                            if (session.CurrentPrompt?.EndsWith("#") == true)
                            {
                                authSuccess = true;
                                break;
                            }
                        }
                    }
                    catch { }
                    continue;
                }
                break;
            }

            if (passResult.Output.Contains("Password:", StringComparison.OrdinalIgnoreCase) ||
                passwordRegex.IsMatch(passResult.Output))
            {
                // Senha incorreta, Cisco solicitou novamente
                continue;
            }

            // Chegou a um prompt sem solicitar mais senha!
            // Confirma privilégio
            var privVerif = "";
            try { privVerif = await session.SendCommandAsync("show privilege", TimeSpan.FromSeconds(5), cancellationToken); } catch { }
            if (privVerif.Contains("privilege level is 15", StringComparison.OrdinalIgnoreCase) ||
                session.CurrentPrompt?.EndsWith("#") == true)
            {
                ResolvedEnableSecret = pass;
                authSuccess = true;
                break;
            }
        }

        // Limpeza de segurança: se ainda estiver preso no prompt de senha, envia Ctrl+C + Enter
        if (!authSuccess)
        {
            try
            {
                await session.SendCtrlCAsync(cancellationToken);
                await Task.Delay(100, cancellationToken);
                await session.SendRawAsync("\r\n", cancellationToken);
                await Task.Delay(200, cancellationToken);
            }
            catch { }

            throw new DeviceSessionException(
                "O roteador Cisco exige senha para entrar em modo privilegiado (enable). " +
                "As senhas testadas ('PRO1AN', senhas de console e padrões) não foram aceitas.");
        }
    }

    public async Task<DeviceInfo> IdentifyAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        if (session.Mode == ExecMode.Rommon ||
            session.CurrentPrompt?.Trim().StartsWith("rommon", StringComparison.OrdinalIgnoreCase) == true ||
            session.CurrentPrompt?.Contains("rommon", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new DeviceInfo(
                "Cisco",
                "Cisco Router (Modo ROMMON - Sem Firmware)",
                "ROMMON Bootloader",
                "ROMMON (Flash vazia / sem IOS)",
                "N/A",
                "rommon");
        }

        await EnterPrivilegedExecAsync(session, cancellationToken);
        try { await DisablePaginationAsync(session, cancellationToken); } catch { }

        var output = await session.SendCommandAsync("show version", TimeSpan.FromSeconds(45), cancellationToken);

        var model = ParseModel(output)
            ?? throw new DeviceSessionException("Modelo não identificado em 'show version'.");

        return new DeviceInfo(
            "Cisco",
            model,
            "Cisco IOS",
            ParseVersion(output),
            ParseSerial(output),
            CleanHostname(session.CurrentPrompt));
    }

    public async Task<string> GetRunningConfigAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        await EnterPrivilegedExecAsync(session, cancellationToken);
        await DisablePaginationAsync(session, cancellationToken);

        var output = await session.SendCommandAsync(
            "show running-config",
            TimeSpan.FromSeconds(120),
            cancellationToken);

        return StripEchoAndPrompt(output, "show running-config");
    }

    public async Task<string> GetStartupConfigAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        await EnterPrivilegedExecAsync(session, cancellationToken);
        await DisablePaginationAsync(session, cancellationToken);

        var output = await session.SendCommandAsync(
            "show startup-config",
            TimeSpan.FromSeconds(120),
            cancellationToken);

        return StripEchoAndPrompt(output, "show startup-config");
    }

    public async Task SaveConfigAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        await EnterPrivilegedExecAsync(session, cancellationToken);
        await session.SendCommandAsync("write memory", TimeSpan.FromSeconds(60), cancellationToken);
    }

    public async Task DisablePaginationAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        if (session.Mode is not (ExecMode.PrivilegedExec or ExecMode.GlobalConfig) && (session.CurrentPrompt == null || !session.CurrentPrompt.EndsWith("#")))
            throw new DeviceSessionException("'terminal length 0' exige modo privilegiado.");

        await session.SendCommandAsync("terminal length 0", cancellationToken: cancellationToken);
        await session.SendCommandAsync("terminal width 0", cancellationToken: cancellationToken);
    }

    private static string? ParseModel(string output)
    {
        var match = ModelNumber.Match(output);
        if (match.Success)
            return match.Groups["m"].Value;

        match = ModelBanner.Match(output);
        if (match.Success)
            return match.Groups["m"].Value;

        match = ModelUdiTable.Match(output);
        if (match.Success)
            return match.Groups["m"].Value;

        match = ModelUdi.Match(output);
        return match.Success ? match.Groups["m"].Value : null;
    }

    private static string? ParseVersion(string output)
    {
        var match = VersionRegex.Match(output);
        return match.Success ? match.Groups["v"].Value : null;
    }

    private static string? ParseSerial(string output)
    {
        var match = SerialRegex.Match(output);
        if (match.Success)
            return match.Groups["s"].Value;

        match = SerialProcessorBoard.Match(output);
        if (match.Success)
            return match.Groups["s"].Value;

        match = SerialUdiTable.Match(output);
        return match.Success ? match.Groups["s"].Value : null;
    }

    private static string CleanHostname(string? prompt)
    {
        if (string.IsNullOrEmpty(prompt))
            return string.Empty;

        var host = prompt;
        var paren = prompt.IndexOf('(');
        if (paren >= 0)
            host = prompt[..paren];

        return host.TrimEnd('#', '>');
    }

    public static async Task<bool> EnforceLanPortConnectedAsync(
        DeviceSession session,
        string lanInterface = "GigabitEthernet 0/1",
        Func<string, CancellationToken, Task>? requestOperatorAction = null,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default,
        Action<int, string, string>? onProgress = null)
    {
        var cleanLan = lanInterface.Replace(" ", ""); // ex: GigabitEthernet0/5, GigabitEthernet5, GigabitEthernet0/1
        var cleanWan = cleanLan.Contains("0/5") ? "GigabitEthernet0/4" :
                       cleanLan.Contains("5") ? "GigabitEthernet4" :
                       cleanLan.Contains("0/4") ? "GigabitEthernet0/5" :
                       cleanLan.Contains("4") ? "GigabitEthernet5" :
                       cleanLan.EndsWith("0/1") ? cleanLan.Replace("0/1", "0/0") : "GigabitEthernet0/0";

        var is841 = cleanLan.Contains("0/5") || cleanLan.Contains("0/4");
        var is921 = !is841 && (cleanLan.Contains("5") || cleanLan.Contains("4"));

        var displayLan = is841 ? "GigabitEthernet0/5 (Porta 5 / GE 0/5 - LAN do Cliente)" :
                         is921 ? "GigabitEthernet 5 (Porta 5 / GE 5 - LAN do Cliente)" :
                         cleanLan.Contains("0/1") ? "GigabitEthernet 0/1 (Porta 1 / GE 0/1 - LAN do Cliente)" :
                         $"{lanInterface} (LAN)";

        var displayWan = is841 ? "GigabitEthernet0/4 (Porta 4 / GE 0/4 - WAN / Uplink)" :
                         is921 ? "GigabitEthernet 4 (Porta 4 / GE 4 - WAN / Uplink)" :
                         cleanWan.Contains("0/0") ? "GigabitEthernet 0/0 (Porta 0 / GE 0/0 - WAN / Uplink)" :
                         $"{cleanWan} (WAN)";

        var shortLan = is841 ? "Porta 5 (GE 0/5)" : is921 ? "Porta 5 (GE 5)" : cleanLan.Contains("0/1") ? "Porta 1 (GE 0/1)" : cleanLan;
        var shortWan = is841 ? "Porta 4 (GE 0/4)" : is921 ? "Porta 4 (GE 4)" : cleanWan.Contains("0/0") ? "Porta 0 (GE 0/0)" : cleanWan;

        var lanPattern = BuildInterfaceRegex(cleanLan);
        var wanPattern = BuildInterfaceRegex(cleanWan);

        var operatorNotified = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (var attempt = 1; attempt <= 60; attempt++)
        {
            var elapsedSec = (int)sw.Elapsed.TotalSeconds;
            string output = string.Empty;
            try
            {
                output = await session.SendCommandAsync("show ip interface brief", TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (SessionTimeoutException)
            {
                // Se a console adormeceu durante a ausência do operador ou se um syslog de cabo conectado (%LINK-3-UPDOWN)
                // omitiu o prompt, envia Enter de despertar e tenta novamente sem abortar
                await session.WriteLineAsync(string.Empty, cancellationToken);
                await Task.Delay(1000, cancellationToken);
                continue;
            }
            
            var isLanUp = Regex.IsMatch(output, $@"(?im)^\s*{lanPattern}\s+[^\r\n]*\bup\s+up\b");
            var isWanUp = Regex.IsMatch(output, $@"(?im)^\s*{wanPattern}\s+[^\r\n]*\bup\s+up\b");

            if (isLanUp)
            {
                if (progress is not null)
                    await progress($"[OK] Link físico confirmado na porta LAN ({displayLan}) — status UP/UP ({elapsedSec}s)!");
                onProgress?.Invoke(55, "Porta LAN Conectada!", $"Link ativo confirmado na porta {shortLan} ({elapsedSec}s).");
                return true;
            }

            if (isWanUp && !isLanUp)
            {
                if (progress is not null && (attempt == 1 || attempt % 3 == 0))
                    await progress($"[AGUARDANDO TROCA DE CABO] Cabo detectado na porta WAN ({shortWan}) ao invés da LAN ({shortLan})... {elapsedSec}s decorridos (Tentativa {attempt}/60)");

                onProgress?.Invoke(50, "Aguardando Troca de Cabo...", $"Cabo na porta WAN ({shortWan}). Troque para a porta LAN ({shortLan}) ({elapsedSec}s)...");

                if (requestOperatorAction is not null && (!operatorNotified || attempt == 20 || attempt == 40))
                {
                    operatorNotified = true;
                    await requestOperatorAction(
                        $"❌ CABO DE REDE CONECTADO NA PORTA INCORRETA ({shortWan})!\n\n" +
                        $"O cabo Ethernet está conectado na porta {displayWan}.\n\n" +
                        $"👉 POR FAVOR, ALTERE O CABO DE REDE PARA A PORTA:\n" +
                        $"🟢 {displayLan}\n\n" +
                        $"Todos os procedimentos no Cisco IOS (Upgrade, Provisionamento, Testes ICMP e Banda) são executados EXCLUSIVAMENTE pela porta LAN ({shortLan}).\n\n" +
                        $"Clique em OK após conectar na porta {shortLan}.",
                        cancellationToken);

                    // Operador clicou em OK após trocar o cabo: acorda console do Cisco e absorve syslogs
                    if (progress is not null)
                        await progress($"[*] Operador confirmou a troca do cabo. Detectando sincronização na porta LAN ({shortLan})...");
                    onProgress?.Invoke(50, "Sincronizando Porta LAN...", $"Aguardando link ativo na porta {shortLan}... ({elapsedSec}s)");

                    await session.WriteLineAsync(string.Empty, cancellationToken);
                    await Task.Delay(1500, cancellationToken);
                }
            }
            else if (!isLanUp)
            {
                if (progress is not null && (attempt == 1 || attempt % 3 == 0))
                    await progress($"[AGUARDANDO CABO LAN] Detectando link físico na porta LAN ({shortLan})... {elapsedSec}s decorridos (Tentativa {attempt}/60)");

                onProgress?.Invoke(50, "Aguardando Conexão LAN...", $"Aguardando sincronização da porta {shortLan}... ({elapsedSec}s)");

                if (requestOperatorAction is not null && (!operatorNotified || attempt == 20 || attempt == 40))
                {
                    operatorNotified = true;
                    await requestOperatorAction(
                        $"⚠️ NENHUM CABO DETECTADO NA PORTA LAN ({shortLan})!\n\n" +
                        $"O link físico da porta {displayLan} está DOWN.\n\n" +
                        $"👉 Conecte o cabo de rede Ethernet do seu notebook na porta:\n" +
                        $"🟢 {displayLan}\n\n" +
                        $"Certifique-se de que o cabo está bem encaixado e o LED da porta física está aceso.\n\n" +
                        $"Clique em OK após conectar o cabo na porta {shortLan}.",
                        cancellationToken);

                    // Operador clicou em OK após conectar o cabo: acorda console do Cisco e absorve syslogs de porta UP
                    if (progress is not null)
                        await progress($"[*] Operador confirmou conexão do cabo. Detectando sincronização na porta LAN ({shortLan})...");
                    onProgress?.Invoke(50, "Sincronizando Porta LAN...", $"Aguardando link ativo na porta {shortLan}... ({elapsedSec}s)");

                    await session.WriteLineAsync(string.Empty, cancellationToken);
                    await Task.Delay(1500, cancellationToken);
                }
            }

            // Intervalo de verificação com atualização dinâmica de segundos para evitar qualquer sensação de travamento
            for (var d = 0; d < 2; d++)
            {
                await Task.Delay(1000, cancellationToken);
                if (onProgress is not null)
                {
                    var sec = (int)sw.Elapsed.TotalSeconds;
                    var statusSub = isWanUp
                        ? $"Cabo na porta WAN ({shortWan}). Troque para a porta LAN ({shortLan}) ({sec}s)..."
                        : $"Aguardando sinal na porta LAN ({shortLan})... ({sec}s)";
                    onProgress(50, isWanUp ? "Aguardando Troca de Cabo..." : "Aguardando Link LAN...", statusSub);
                }
            }
        }

        if (progress is not null)
            await progress($"[AVISO CRÍTICO] Tempo limite de espera para link na porta LAN ({displayLan}) esgotado ({(int)sw.Elapsed.TotalSeconds}s).");

        return false;
    }

    private static string BuildInterfaceRegex(string iface)
    {
        var clean = iface.Replace(" ", "");
        if (clean.Contains("0/5"))
            return @"(?:GigabitEthernet0/5|Gi0/5|GE0/5|FastEthernet0/5|Fa0/5|GigabitEthernet\s*0/5)";
        if (clean.Contains("0/4"))
            return @"(?:GigabitEthernet0/4|Gi0/4|GE0/4|FastEthernet0/4|Fa0/4|GigabitEthernet\s*0/4)";
        if (clean.EndsWith("5") || clean.Contains("5"))
            return @"(?:GigabitEthernet5|Gi5|GE5|GigabitEthernet\s*5|GigabitEthernet0/5|Gi0/5|GE0/5)";
        if (clean.EndsWith("4") || clean.Contains("4"))
            return @"(?:GigabitEthernet4|Gi4|GE4|GigabitEthernet\s*4|GigabitEthernet0/4|Gi0/4|GE0/4)";
        if (clean.Contains("0/1"))
            return @"(?:GigabitEthernet0/1|Gi0/1|GE0/1|FastEthernet0/1|Fa0/1|GigabitEthernet\s*0/1)";
        if (clean.Contains("0/0"))
            return @"(?:GigabitEthernet0/0|Gi0/0|GE0/0|FastEthernet0/0|Fa0/0|GigabitEthernet\s*0/0)";

        return Regex.Escape(clean);
    }

    private static string StripEchoAndPrompt(string output, string command)
    {
        var lines = output.Replace("\r", "").Split('\n').ToList();
        if (lines.Count > 0 && lines[0].Trim().Equals(command, StringComparison.Ordinal))
            lines.RemoveAt(0);

        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].Trim().Length == 0)
                continue;

            if (RegexPromptMatcher.CiscoIos().TryMatch(lines[i].Trim()) is not null)
                lines.RemoveAt(i);
            break;
        }

        return string.Join(Environment.NewLine, lines.Select(l => l.TrimEnd())).TrimEnd() + Environment.NewLine;
    }
}
