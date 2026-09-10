namespace NetworkDevice.Core.Detection;

// Porta WAN esperada por modelo + parsers de status (para a critica da Fase 5b).
public sealed record WanPortaInfo(
    string ModeloExibicao,
    string InterfaceExibicao,
    string[] NomesBusca,
    bool IsHpe);

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
}
