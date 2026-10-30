using NetworkDevice.Core.Domain;

namespace NetworkDevice.Core.Detection;

// Porta WAN esperada por modelo + parsers de status (para a critica da Fase 5b).
public sealed record WanPortaInfo(
    string ModeloExibicao,
    string InterfaceExibicao,
    string[] NomesBusca,
    bool IsHpe,
    bool IsForti = false);

/// <summary>
/// Mapeamento bidirecional completo de portas físicas (LAN e WAN) por modelo de equipamento
/// para diagnóstico didático e proteção contra inversão de cabos.
/// </summary>
public sealed record RoteadorPortasInfo(
    string ModeloExibicao,
    string WanInterfaceExibicao,
    string[] WanNomesBusca,
    string LanInterfaceExibicao,
    string[] LanNomesBusca,
    bool IsHpe,
    bool IsForti = false)
{
    public WanPortaInfo ToWanPortaInfo() =>
        new WanPortaInfo(ModeloExibicao, WanInterfaceExibicao, WanNomesBusca, IsHpe, IsForti);
}

/// <summary>
/// Resultado da inspeção de portas e análise de inversão de cabeamento.
/// </summary>
public sealed record PortasDiagnosticoResultado(
    string Modelo,
    string WanInterfaceExibicao,
    string LanInterfaceExibicao,
    bool WanEncontrada,
    bool WanDown,
    bool LanEncontrada,
    bool LanDown,
    bool PossivelInversaoCabos,
    string MensagemDiagnostico,
    string? DetalhesArp = null)
{
    public bool WanUp => WanEncontrada && !WanDown;
    public bool LanUp => LanEncontrada && !LanDown;
}

/// <summary>
/// Decisão tomada pelo operador no diálogo didático de diagnóstico de portas WAN/LAN.
/// </summary>
public enum WanActionDecision
{
    Retry,              // Conectou cabo e deseja repetir o teste de ICMP/WAN
    ProceedInBenchMode, // Seguir em modo bancada (pular testes WAN sem apontar falha)
    ProceedOffline,     // Seguir normalmente marcando WAN como offline
    Cancel              // Cancelar teste
}

public static class WanPortInspector
{
    public static RoteadorPortasInfo? ObterRoteadorPortasPorSerie(DeviceSeries series)
    {
        return series switch
        {
            DeviceSeries.Isr1100 => ObterRoteadorPortasInfo("c1111"),
            DeviceSeries.Isr841 => ObterRoteadorPortasInfo("c841"),
            DeviceSeries.Isr921 => ObterRoteadorPortasInfo("921"),
            DeviceSeries.Series1900 => ObterRoteadorPortasInfo("1900"),
            DeviceSeries.Series2900 => ObterRoteadorPortasInfo("2900"),
            DeviceSeries.Msr930 => ObterRoteadorPortasInfo("930"),
            DeviceSeries.Msr954 => ObterRoteadorPortasInfo("954"),
            DeviceSeries.Msr1002 => ObterRoteadorPortasInfo("1002"),
            DeviceSeries.FortiGate40F => ObterRoteadorPortasInfo("forti"),
            _ => ObterRoteadorPortasInfo(series.ToString())
        };
    }

    public static RoteadorPortasInfo? ObterRoteadorPortasInfo(string? tag)
    {
        var t = (tag ?? "").ToLowerInvariant();
        if (t.Contains("c1111") || t.Contains("c1100") || t.Contains("1111") || t.Contains("1100"))
            return new RoteadorPortasInfo(
                "Cisco Série 1100 / C1111-4P",
                "GE0/0/0 (Porta WAN)",
                new[] { "gigabitethernet0/0/0", "gigabitethernet 0/0/0", "ge0/0/0" },
                "GE0/1/0 (Porta LAN / VLAN 1)",
                new[] { "gigabitethernet0/1/0", "gigabitethernet 0/1/0", "ge0/1/0", "vlan1", "gigabitethernet0/1/1", "gigabitethernet0/1/2", "gigabitethernet0/1/3" },
                false);

        if (t.Contains("c841") || t.Contains("c800"))
            return new RoteadorPortasInfo(
                "Cisco Série 800 / C841M",
                "GE0/4 (Porta 4)",
                new[] { "gigabitethernet0/4", "gigabitethernet 0/4", "ge0/4" },
                "GE0/5 (Porta 5 / LAN)",
                new[] { "gigabitethernet0/5", "gigabitethernet 0/5", "ge0/5", "vlan1", "gigabitethernet0/0", "gigabitethernet0/1", "gigabitethernet0/2", "gigabitethernet0/3" },
                false);

        if (t.Contains("c900") || t.Contains("921"))
            return new RoteadorPortasInfo(
                "Cisco Série 900 / C921-4P",
                "GE4 (Porta 4)",
                new[] { "gigabitethernet4", "gigabitethernet 4", "ge4" },
                "GE5 (Porta 5 / LAN)",
                new[] { "gigabitethernet5", "gigabitethernet 5", "ge5", "vlan1", "gigabitethernet0", "gigabitethernet1", "gigabitethernet2", "gigabitethernet3" },
                false);

        if (t.Contains("1900"))
            return new RoteadorPortasInfo(
                "Cisco Série 1900",
                "GE0/0",
                new[] { "gigabitethernet0/0", "gigabitethernet 0/0" },
                "Porta GE0/1 (LAN)",
                new[] { "gigabitethernet0/1", "gigabitethernet 0/1" },
                false);

        if (t.Contains("2900") || t.Contains("2901") || t.Contains("2911") || t.Contains("2921") || t.Contains("2951"))
            return new RoteadorPortasInfo(
                "Cisco Série 2900",
                "GE0/0",
                new[] { "gigabitethernet0/0", "gigabitethernet 0/0" },
                "Porta GE0/1 (LAN)",
                new[] { "gigabitethernet0/1", "gigabitethernet 0/1" },
                false);

        if (t.Contains("1002") || t.Contains("1003") || t.Contains("1000") || t.Contains("100x"))
            return new RoteadorPortasInfo(
                "HPE MSR 1002 / 1003",
                "GE0/0",
                new[] { "ge0/0", "gigabitethernet0/0" },
                "Porta GE0/1 (LAN)",
                new[] { "ge0/1", "gigabitethernet0/1", "vlan-interface1" },
                true);

        if (t.Contains("930") || t.Contains("931") || t.Contains("935"))
            return new RoteadorPortasInfo(
                "HPE MSR 930",
                "GE0/0",
                new[] { "ge0/0", "gigabitethernet0/0" },
                "Portas 1 a 4 (GE0/1 a GE0/4 / LAN)",
                new[] { "ge0/1", "ge0/2", "ge0/3", "ge0/4", "vlan-interface1" },
                true);

        if (t.Contains("954") || t.Contains("958") || t.Contains("hpe") || t.Contains("msr"))
            return new RoteadorPortasInfo(
                "HPE MSR 954",
                "GE0/0",
                new[] { "ge0/0", "gigabitethernet0/0" },
                "Porta GE0/1 (LAN)",
                new[] { "ge0/1", "gigabitethernet0/1", "vlan-interface1" },
                true);

        if (t.Contains("forti") || t.Contains("fgt") || t.Contains("40f"))
            return new RoteadorPortasInfo(
                "Fortinet FortiGate 40F",
                "WAN (Porta WAN)",
                new[] { "wan", "wan1" },
                "Portas 1 a 3 (internal / LAN)",
                new[] { "internal", "lan", "lan1", "lan2", "lan3" },
                false,
                true);

        return null;
    }

    public static WanPortaInfo? ObterPorSerie(DeviceSeries series)
    {
        return ObterRoteadorPortasPorSerie(series)?.ToWanPortaInfo() ?? (series switch
        {
            DeviceSeries.Isr1100 => PorTagModelo("c1111"),
            DeviceSeries.Isr841 => PorTagModelo("c841"),
            DeviceSeries.Isr921 => PorTagModelo("921"),
            DeviceSeries.Series1900 => PorTagModelo("1900"),
            DeviceSeries.Series2900 => PorTagModelo("2900"),
            DeviceSeries.Msr930 => PorTagModelo("930"),
            DeviceSeries.Msr954 => PorTagModelo("954"),
            DeviceSeries.Msr1002 => PorTagModelo("1002"),
            DeviceSeries.FortiGate40F => PorTagModelo("forti"),
            _ => PorTagModelo(series.ToString())
        });
    }

    public static WanPortaInfo? PorTagModelo(string? tag)
    {
        return ObterRoteadorPortasInfo(tag)?.ToWanPortaInfo();
    }

    private static string Norm(string s) =>
        s.Replace(" ", "").Replace("\t", "").ToLowerInvariant();

    // 'show ip interface brief' -> (encontrada, down)
    public static (bool Encontrada, bool Down) CiscoWanStatus(string brief, string[] nomes)
    {
        var wants = new HashSet<string>(nomes.Select(Norm));
        foreach (var raw in brief.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            var tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 3 || !wants.Contains(Norm(tok[0]))) continue;
            if (Norm(line).Contains("administrativelydown")) return (true, true);
            var status = Norm(tok[tok.Length - 2]);
            var proto = Norm(tok[tok.Length - 1]);
            return (true, status == "down" || proto == "down");
        }
        return (false, false);
    }

    // 'display interface brief' -> (encontrada, down)
    public static (bool Encontrada, bool Down) HpeWanStatus(string brief, string[] nomes)
    {
        var wants = new HashSet<string>(nomes.Select(Norm));
        foreach (var raw in brief.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            var tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 3 || !wants.Contains(Norm(tok[0]))) continue;
            if (Norm(line).Contains("administratively")) return (true, true);
            return (true, Norm(tok[1]).Contains("down") || Norm(tok[2]).Contains("down"));
        }
        return (false, false);
    }

    // 'get system interface physical' ou 'diagnose netlink interface list' -> (encontrada, down)
    public static (bool Encontrada, bool Down) FortiWanStatus(string output, string[] nomes)
    {
        if (string.IsNullOrWhiteSpace(output)) return (false, false);

        foreach (var nome in nomes)
        {
            // 1. Bloco de 'get system interface physical' (ex.: ==[wan] ... status: up/down)
            var match = System.Text.RegularExpressions.Regex.Match(
                output,
                $@"(?i)==\s*\[\s*{System.Text.RegularExpressions.Regex.Escape(nome)}\s*\](?<content>[\s\S]*?)(?:==\s*\[|\z)");
            if (match.Success)
            {
                var content = match.Groups["content"].Value;
                var isDown = System.Text.RegularExpressions.Regex.IsMatch(content, @"(?i)\bstatus:\s*down\b");
                var isUp = System.Text.RegularExpressions.Regex.IsMatch(content, @"(?i)\bstatus:\s*up\b");
                if (isDown) return (true, true);
                if (isUp) return (true, false);
                return (true, true);
            }

            // 2. Bloco de 'diagnose netlink interface list' (ex.: if=wan ... carrier: ON/OFF)
            var nlMatch = System.Text.RegularExpressions.Regex.Match(
                output,
                $@"(?i)\bif={System.Text.RegularExpressions.Regex.Escape(nome)}\b(?<content>[\s\S]*?)(?:\bif=|\z)");
            if (nlMatch.Success)
            {
                var content = nlMatch.Groups["content"].Value;
                var hasNoCarrier = content.Contains("no_carrier", StringComparison.OrdinalIgnoreCase);
                var isDown = System.Text.RegularExpressions.Regex.IsMatch(content, @"(?i)\bstate:\s*down\b") ||
                             System.Text.RegularExpressions.Regex.IsMatch(content, @"(?i)\bcarrier:\s*off\b") ||
                             hasNoCarrier;
                var isUp = System.Text.RegularExpressions.Regex.IsMatch(content, @"(?i)\bstate:\s*up\b") ||
                           System.Text.RegularExpressions.Regex.IsMatch(content, @"(?i)\bcarrier:\s*on\b");
                if (isDown) return (true, true);
                if (isUp) return (true, false);
                return (true, true);
            }
        }

        return (false, false);
    }

    /// <summary>
    /// Avalia a tabela ARP do roteador para verificar se o IP/MAC do computador local
    /// foi aprendido indevidamente na porta WAN (prova contundente de inversão física de cabos).
    /// </summary>
    public static bool AvaliarInversaoViaArp(string? arpOutput, string? sourceIp, string[] wanNomes)
    {
        if (string.IsNullOrWhiteSpace(arpOutput) || string.IsNullOrWhiteSpace(sourceIp)) return false;
        var cleanSource = sourceIp.Trim();
        var wants = new HashSet<string>(wanNomes.Select(Norm));

        foreach (var raw in arpOutput.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (!line.Contains(cleanSource, StringComparison.OrdinalIgnoreCase)) continue;
            var tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3) continue;
            var lastToken = Norm(tokens[tokens.Length - 1]);
            if (wants.Contains(lastToken)) return true;
        }
        return false;
    }

    /// <summary>
    /// Avaliação combinada de portas LAN e WAN para roteadores Cisco IOS.
    /// </summary>
    public static PortasDiagnosticoResultado AvaliarPortasCisco(
        string brief, RoteadorPortasInfo info, string? arpOutput = null, string? sourceIp = null)
    {
        var (wanEncontrada, wanDown) = CiscoWanStatus(brief, info.WanNomesBusca);

        // Avalia se pelo menos uma porta LAN ou Vlan1 está com link UP
        bool lanEncontrada = false;
        bool lanDown = true;
        var lanWants = new HashSet<string>(info.LanNomesBusca.Select(Norm));

        foreach (var raw in brief.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            var tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 3 || !lanWants.Contains(Norm(tok[0]))) continue;

            lanEncontrada = true;
            var status = Norm(tok[tok.Length - 2]);
            var proto = Norm(tok[tok.Length - 1]);
            var isUp = (status == "up" || status == "connected") && (proto == "up" || proto == "connected");
            if (isUp)
            {
                lanDown = false;
                break; // Se pelo menos uma porta LAN tem link físico, o computador está conectado na LAN
            }
        }

        var arpInvertido = AvaliarInversaoViaArp(arpOutput, sourceIp, info.WanNomesBusca);
        bool inversao = false;
        string msg;

        if (arpInvertido)
        {
            inversao = true;
            msg = $"🚨 INVERSÃO DE CABOS DETECTADA: O roteador registrou o computador ({sourceIp}) na porta WAN ({info.WanInterfaceExibicao})! Mova o cabo para a porta LAN ({info.LanInterfaceExibicao}).";
        }
        else if (wanEncontrada && !wanDown && lanEncontrada && lanDown)
        {
            inversao = true;
            msg = $"⚠️ POSSÍVEL INVERSÃO DE CABOS: A porta WAN ({info.WanInterfaceExibicao}) está com link ativo, mas a porta LAN ({info.LanInterfaceExibicao}) está desconectada. Verifique se o cabo do computador não foi plugado na porta WAN por engano!";
        }
        else if (wanDown && (!lanDown && lanEncontrada))
        {
            msg = $"Cabo do computador conectado à porta LAN (OK). A porta WAN ({info.WanInterfaceExibicao}) está sem link físico com o acesso da operadora.";
        }
        else if (!wanDown && (!lanDown && lanEncontrada))
        {
            msg = "Ambas as portas (LAN e WAN) estão com link físico ativo. Falha de ping pode ser de rota, VLAN ou gateway na operadora.";
        }
        else
        {
            msg = $"Porta WAN ({info.WanInterfaceExibicao}) sem link físico.";
        }

        return new PortasDiagnosticoResultado(
            info.ModeloExibicao,
            info.WanInterfaceExibicao,
            info.LanInterfaceExibicao,
            wanEncontrada,
            wanDown,
            lanEncontrada,
            lanDown,
            inversao,
            msg,
            arpInvertido ? $"IP de origem {sourceIp} identificado na porta WAN." : null);
    }

    /// <summary>
    /// Avaliação combinada de portas LAN e WAN para roteadores HPE Comware.
    /// </summary>
    public static PortasDiagnosticoResultado AvaliarPortasHpe(
        string brief, RoteadorPortasInfo info, string? arpOutput = null, string? sourceIp = null)
    {
        var (wanEncontrada, wanDown) = HpeWanStatus(brief, info.WanNomesBusca);

        bool lanEncontrada = false;
        bool lanDown = true;
        var lanWants = new HashSet<string>(info.LanNomesBusca.Select(Norm));

        foreach (var raw in brief.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) continue;
            var tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 3 || !lanWants.Contains(Norm(tok[0]))) continue;

            lanEncontrada = true;
            var isUp = Norm(tok[1]).Contains("up") || Norm(tok[2]).Contains("up");
            if (isUp)
            {
                lanDown = false;
                break;
            }
        }

        var arpInvertido = AvaliarInversaoViaArp(arpOutput, sourceIp, info.WanNomesBusca);
        bool inversao = false;
        string msg;

        if (arpInvertido)
        {
            inversao = true;
            msg = $"🚨 INVERSÃO DE CABOS DETECTADA: O roteador registrou o computador ({sourceIp}) na porta WAN ({info.WanInterfaceExibicao})! Mova o cabo para a porta LAN ({info.LanInterfaceExibicao}).";
        }
        else if (wanEncontrada && !wanDown && lanEncontrada && lanDown)
        {
            inversao = true;
            msg = $"⚠️ POSSÍVEL INVERSÃO DE CABOS: A porta WAN ({info.WanInterfaceExibicao}) está com link ativo, mas a porta LAN ({info.LanInterfaceExibicao}) está desconectada. Verifique se o cabo do computador não foi plugado na porta WAN por engano!";
        }
        else if (wanDown && (!lanDown && lanEncontrada))
        {
            msg = $"Cabo do computador conectado à porta LAN (OK). A porta WAN ({info.WanInterfaceExibicao}) está sem link físico com o acesso da operadora.";
        }
        else
        {
            msg = $"Porta WAN ({info.WanInterfaceExibicao}) sem link físico.";
        }

        return new PortasDiagnosticoResultado(
            info.ModeloExibicao,
            info.WanInterfaceExibicao,
            info.LanInterfaceExibicao,
            wanEncontrada,
            wanDown,
            lanEncontrada,
            lanDown,
            inversao,
            msg,
            arpInvertido ? $"IP de origem {sourceIp} identificado na porta WAN." : null);
    }

    /// <summary>
    /// Avaliação combinada de portas LAN e WAN para Fortinet FortiGate.
    /// </summary>
    public static PortasDiagnosticoResultado AvaliarPortasForti(
        string physicalOutput, RoteadorPortasInfo info, string? arpOutput = null, string? sourceIp = null)
    {
        var (wanEncontrada, wanDown) = FortiWanStatus(physicalOutput, info.WanNomesBusca);
        var (lanEncontrada, lanDown) = FortiWanStatus(physicalOutput, info.LanNomesBusca);

        var arpInvertido = AvaliarInversaoViaArp(arpOutput, sourceIp, info.WanNomesBusca);
        bool inversao = false;
        string msg;

        if (arpInvertido)
        {
            inversao = true;
            msg = $"🚨 INVERSÃO DE CABOS DETECTADA: O FortiGate registrou o computador ({sourceIp}) na porta WAN ({info.WanInterfaceExibicao})! Mova o cabo para a porta LAN ({info.LanInterfaceExibicao}).";
        }
        else if (wanEncontrada && !wanDown && lanEncontrada && lanDown)
        {
            inversao = true;
            msg = $"⚠️ POSSÍVEL INVERSÃO DE CABOS: A porta WAN ({info.WanInterfaceExibicao}) está com link ativo, mas a porta LAN ({info.LanInterfaceExibicao}) está desconectada. Verifique se o cabo do computador não foi plugado na porta WAN por engano!";
        }
        else if (wanDown && (!lanDown && lanEncontrada))
        {
            msg = $"Cabo do computador conectado à porta LAN (OK). A porta WAN ({info.WanInterfaceExibicao}) está sem link físico com o acesso da operadora.";
        }
        else
        {
            msg = $"Porta WAN ({info.WanInterfaceExibicao}) sem link físico.";
        }

        return new PortasDiagnosticoResultado(
            info.ModeloExibicao,
            info.WanInterfaceExibicao,
            info.LanInterfaceExibicao,
            wanEncontrada,
            wanDown,
            lanEncontrada,
            lanDown,
            inversao,
            msg,
            arpInvertido ? $"IP de origem {sourceIp} identificado na porta WAN." : null);
    }
}
