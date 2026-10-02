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

    public CiscoIOSAdapter(string? enableSecret = null)
    {
        _enableSecret = enableSecret;
    }

    public string Vendor => "Cisco";

    public static SessionOptions CreateSessionOptions(string? enableSecret, string? username = null, string? password = null) =>
        new()
        {
            PromptMatcher = RegexPromptMatcher.CiscoIos(),
            Username = username,
            Password = password
        };

    public async Task EnterPrivilegedExecAsync(DeviceSession session, CancellationToken cancellationToken = default)
    {
        if (session.Mode is ExecMode.PrivilegedExec or ExecMode.GlobalConfig or ExecMode.Rommon ||
            (session.CurrentPrompt != null && (session.CurrentPrompt.EndsWith("#") || session.CurrentPrompt.Contains("rommon", StringComparison.OrdinalIgnoreCase))))
            return;

        var result = await session.SendExpectAsync(
            "enable",
            new StopCondition[] { new StopCondition.Contains("Password", "Password:"), new StopCondition.Prompt() },
            TimeSpan.FromSeconds(20),
            cancellationToken);

        if (result.Matched is StopCondition.Contains)
        {
            if (_enableSecret is null)
                throw new DeviceSessionException("Dispositivo solicitou senha de enable (modo privilegiado protegido), mas nenhuma foi configurada.");
            await session.SendCommandAsync(_enableSecret, cancellationToken: cancellationToken);
        }

        if (session.Mode == ExecMode.Rommon || session.CurrentPrompt?.Contains("rommon", StringComparison.OrdinalIgnoreCase) == true)
            return;

        if (session.Mode is not (ExecMode.PrivilegedExec or ExecMode.GlobalConfig) && (session.CurrentPrompt == null || !session.CurrentPrompt.EndsWith("#")))
        {
            // Se o output terminou com '#' ou '>'
            if (result.Output.TrimEnd().EndsWith("#") || result.Output.Contains("rommon"))
                return;

            throw new DeviceSessionException("Falha ao entrar em modo privilegiado (enable).");
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
        CancellationToken cancellationToken = default)
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

        for (var attempt = 1; attempt <= 60; attempt++)
        {
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
                    await progress($"[OK] Link físico confirmado na porta LAN ({displayLan}) — status UP/UP.");
                return true;
            }

            if (isWanUp && !isLanUp)
            {
                if (progress is not null && (attempt % 5 == 1))
                    await progress($"[CRÍTICA DE PORTA] Cabo de rede detectado na porta WAN ({shortWan}) ao invés da porta LAN ({shortLan})!");

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
                    await session.WriteLineAsync(string.Empty, cancellationToken);
                    await Task.Delay(1500, cancellationToken);
                }
            }
            else if (!isLanUp)
            {
                if (progress is not null && (attempt % 5 == 1))
                    await progress($"[AVISO] Porta LAN ({displayLan}) sem link físico (DOWN). Aguardando conexão do cabo de rede ({attempt}/60)...");

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
                    await session.WriteLineAsync(string.Empty, cancellationToken);
                    await Task.Delay(1500, cancellationToken);
                }
            }

            await Task.Delay(2000, cancellationToken);
        }

        if (progress is not null)
            await progress($"[AVISO CRÍTICO] Tempo limite de espera para link na porta LAN ({displayLan}) esgotado.");

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
