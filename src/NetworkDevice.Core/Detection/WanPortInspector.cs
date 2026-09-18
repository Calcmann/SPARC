namespace NetworkDevice.Core.Detection;

// Porta WAN esperada por modelo + parsers de status (para a critica da Fase 5b).
public sealed record WanPortaInfo(
    string ModeloExibicao,
    string InterfaceExibicao,
    string[] NomesBusca,
    bool IsHpe,
    bool IsForti = false);

public static class WanPortInspector
{
    public static WanPortaInfo? PorTagModelo(string? tag)
    {
        var t = (tag ?? "").ToLowerInvariant();
        if (t.Contains("c841") || t.Contains("c800"))
            return new WanPortaInfo("Cisco Série 800 / C841M", "GE0/4 (Porta 4)",
                new[] { "gigabitethernet0/4" }, false);
        if (t.Contains("c900") || t.Contains("921"))
            return new WanPortaInfo("Cisco Série 900 / C921-4P", "GE4 (Porta 4)",
                new[] { "gigabitethernet4", "gigabitethernet 4" }, false);
        if (t.Contains("1900"))
            return new WanPortaInfo("Cisco Série 1900", "GE0/0",
                new[] { "gigabitethernet0/0", "gigabitethernet 0/0" }, false);
        if (t.Contains("1002") || t.Contains("1003") || t.Contains("1000"))
            return new WanPortaInfo("HPE MSR 1002 / 1003", "GE0/0",
                new[] { "ge0/0", "gigabitethernet0/0" }, true);
        if (t.Contains("930") || t.Contains("931") || t.Contains("935"))
            return new WanPortaInfo("HPE MSR 930", "GE0/0",
                new[] { "ge0/0", "gigabitethernet0/0" }, true);
        if (t.Contains("954") || t.Contains("958") || t.Contains("hpe") || t.Contains("msr"))
            return new WanPortaInfo("HPE MSR 954", "GE0/0",
                new[] { "ge0/0", "gigabitethernet0/0" }, true);
        if (t.Contains("forti") || t.Contains("fgt") || t.Contains("40f"))
            return new WanPortaInfo("Fortinet FortiGate 40F", "WAN (Porta WAN)",
                new[] { "wan", "wan1" }, false, true);
        return null;
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
}
