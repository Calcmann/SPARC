using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NetworkDevice.Core.Domain;

namespace NetworkDevice.Core.Detection;

public sealed record InterfaceDetail(
    string InterfaceName,
    string? IpAddress,
    bool IsAdminUp,
    bool IsPhysicalUp,
    string StatusSummary);

public sealed record InterfacesVerificationResult(
    InterfaceDetail? Wan,
    InterfaceDetail? Lan,
    bool WanIpMatches,
    bool LanIpMatches,
    bool AllInterfacesOk,
    string RawOutput,
    string Summary);

public static class InterfaceStatusInspector
{
    private static string Norm(string s) =>
        s.Replace(" ", "").Replace("\t", "").ToLowerInvariant();

    /// <summary>
    /// Resolve o nome padrão das interfaces WAN e LAN para cada série de equipamento.
    /// </summary>
    public static (string WanIface, string LanIface) ResolveExpectedInterfaces(DeviceSeries series) => series switch
    {
        DeviceSeries.Isr841 => ("GigabitEthernet0/4", "GigabitEthernet0/5"),
        DeviceSeries.Isr921 => ("GigabitEthernet4", "Vlan1"),
        DeviceSeries.Series1900 => ("GigabitEthernet0/0", "GigabitEthernet0/1"),
        DeviceSeries.Series2900 => ("GigabitEthernet0/0", "GigabitEthernet0/1"),
        DeviceSeries.Msr930 => ("GE0/0", "GE0/1"),
        DeviceSeries.Msr954 => ("GE0/0", "GE0/1"),
        DeviceSeries.Msr1002 => ("GE0/0", "GE0/1"),
        DeviceSeries.FortiGate40F => ("wan", "lan"),
        _ => ("GigabitEthernet0/0", "GigabitEthernet0/1")
    };

    /// <summary>
    /// Analisa a saída de 'show ip interface brief' do Cisco IOS.
    /// </summary>
    public static InterfacesVerificationResult ParseCiscoBrief(
        string briefOutput,
        string expectedWanIface,
        string expectedLanIface,
        string? expectedWanIp,
        string? expectedLanIp)
    {
        var wanDetail = FindCiscoInterface(briefOutput, expectedWanIface);
        var lanDetail = FindCiscoInterface(briefOutput, expectedLanIface);

        return BuildResult(wanDetail, lanDetail, expectedWanIp, expectedLanIp, briefOutput);
    }

    private static InterfaceDetail? FindCiscoInterface(string briefOutput, string targetIface)
    {
        if (string.IsNullOrWhiteSpace(briefOutput)) return null;

        var targetNorm = Norm(targetIface);
        // Abreviações Cisco comuns (ex: gi0/0 para GigabitEthernet0/0, fa0/0 para FastEthernet0/0)
        var shortTarget = targetNorm
            .Replace("gigabitethernet", "gi")
            .Replace("fastethernet", "fa");

        foreach (var rawLine in briefOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("Interface", StringComparison.OrdinalIgnoreCase)) continue;

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 4) continue;

            var ifaceToken = tokens[0];
            var ifaceNorm = Norm(ifaceToken);
            var shortIface = ifaceNorm
                .Replace("gigabitethernet", "gi")
                .Replace("fastethernet", "fa");

            if (ifaceNorm.Equals(targetNorm, StringComparison.OrdinalIgnoreCase) ||
                shortIface.Equals(shortTarget, StringComparison.OrdinalIgnoreCase) ||
                ifaceNorm.EndsWith(targetNorm, StringComparison.OrdinalIgnoreCase))
            {
                var ip = tokens[1].Equals("unassigned", StringComparison.OrdinalIgnoreCase) ? null : tokens[1];
                var lineLower = line.ToLowerInvariant();

                bool adminUp = !lineLower.Contains("administratively down");
                // Geralmente penúltimo token é Status e último é Protocol
                var statusToken = tokens[tokens.Length - 2].ToLowerInvariant();
                var protoToken = tokens[tokens.Length - 1].ToLowerInvariant();

                bool physicalUp = adminUp && statusToken == "up" && protoToken == "up";
                var summary = $"{ifaceToken}: IP {ip ?? "não atribuído"} | Admin: {(adminUp ? "UP" : "DOWN")} | Link: {(physicalUp ? "UP" : "DOWN")}";

                return new InterfaceDetail(ifaceToken, ip, adminUp, physicalUp, summary);
            }
        }

        return null;
    }

    /// <summary>
    /// Analisa a saída de 'display ip interface brief' do HPE Comware.
    /// </summary>
    public static InterfacesVerificationResult ParseHpeBrief(
        string briefOutput,
        string expectedWanIface,
        string expectedLanIface,
        string? expectedWanIp,
        string? expectedLanIp)
    {
        var wanDetail = FindHpeInterface(briefOutput, expectedWanIface);
        var lanDetail = FindHpeInterface(briefOutput, expectedLanIface);

        return BuildResult(wanDetail, lanDetail, expectedWanIp, expectedLanIp, briefOutput);
    }

    private static InterfaceDetail? FindHpeInterface(string briefOutput, string targetIface)
    {
        if (string.IsNullOrWhiteSpace(briefOutput)) return null;

        var targetNorm = Norm(targetIface)
            .Replace("gigabitethernet", "ge");

        foreach (var rawLine in briefOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("Interface", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Brief", StringComparison.OrdinalIgnoreCase)) continue;

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3) continue;

            var ifaceToken = tokens[0];
            var ifaceNorm = Norm(ifaceToken).Replace("gigabitethernet", "ge");

            if (ifaceNorm.Equals(targetNorm, StringComparison.OrdinalIgnoreCase))
            {
                var linkToken = tokens[1].ToUpperInvariant();
                var protoToken = tokens[2].ToUpperInvariant();
                string? ip = null;
                if (tokens.Length >= 4 && !tokens[3].Equals("--") && !tokens[3].Equals("unassigned", StringComparison.OrdinalIgnoreCase))
                {
                    ip = tokens[3];
                }

                bool adminUp = !linkToken.Contains("ADM");
                bool physicalUp = adminUp && linkToken == "UP" && protoToken == "UP";
                var summary = $"{ifaceToken}: IP {ip ?? "não atribuído"} | Link: {linkToken} | Proto: {protoToken}";

                return new InterfaceDetail(ifaceToken, ip, adminUp, physicalUp, summary);
            }
        }

        return null;
    }

    /// <summary>
    /// Analisa saídas de interfaces do Fortinet FortiOS.
    /// </summary>
    public static InterfacesVerificationResult ParseFortinetInterfaces(
        string physicalOutput,
        string ipOutput,
        string expectedWanIface,
        string expectedLanIface,
        string? expectedWanIp,
        string? expectedLanIp)
    {
        var wanDetail = FindFortinetInterface(physicalOutput, ipOutput, expectedWanIface);
        var lanDetail = FindFortinetInterface(physicalOutput, ipOutput, expectedLanIface);

        return BuildResult(wanDetail, lanDetail, expectedWanIp, expectedLanIp, $"{physicalOutput}\n{ipOutput}");
    }

    private static InterfaceDetail? FindFortinetInterface(string physicalOutput, string ipOutput, string targetIface)
    {
        var targetNorm = Norm(targetIface);
        string? ip = null;

        // Extrai IP de 'diagnose ip address list' ou 'get system interface'
        if (!string.IsNullOrWhiteSpace(ipOutput))
        {
            var matchDiag = Regex.Match(ipOutput, $@"(?im)IP=([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+).*?(?:dev_name|devname)={Regex.Escape(targetIface)}\b");
            if (matchDiag.Success)
            {
                ip = matchDiag.Groups[1].Value;
            }
            else
            {
                var matchDiag2 = Regex.Match(ipOutput, $@"(?im)(?:dev_name|devname)={Regex.Escape(targetIface)}\b.*?ip=([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                if (matchDiag2.Success)
                {
                    ip = matchDiag2.Groups[1].Value;
                }
                else
                {
                    var matchGet = Regex.Match(ipOutput, $@"(?im)==\s*\[\s*{Regex.Escape(targetIface)}\s*\][\s\S]*?ip:\s*([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)");
                    if (matchGet.Success) ip = matchGet.Groups[1].Value;
                }
            }
        }

        // Extrai status físico de 'get system interface physical'
        bool adminUp = true;
        bool physicalUp = false;

        if (!string.IsNullOrWhiteSpace(physicalOutput))
        {
            var matchPhysical = Regex.Match(physicalOutput, $@"(?im)==\s*\[\s*{Regex.Escape(targetIface)}\s*\](?<content>[\s\S]*?)(?:==\s*\[|\z)");
            if (matchPhysical.Success)
            {
                var content = matchPhysical.Groups["content"].Value;
                physicalUp = Regex.IsMatch(content, @"(?i)\bstatus:\s*up\b");
            }
        }

        var summary = $"{targetIface}: IP {ip ?? "não atribuído"} | Link: {(physicalUp ? "UP" : "DOWN")}";
        return new InterfaceDetail(targetIface, ip, adminUp, physicalUp, summary);
    }

    private static InterfacesVerificationResult BuildResult(
        InterfaceDetail? wan,
        InterfaceDetail? lan,
        string? expectedWanIp,
        string? expectedLanIp,
        string rawOutput)
    {
        // Limpa máscara CIDR do IP esperado se presente
        var cleanWanIp = CleanIp(expectedWanIp);
        var cleanLanIp = CleanIp(expectedLanIp);

        bool wanIpMatches = !string.IsNullOrWhiteSpace(cleanWanIp) &&
                            wan != null &&
                            string.Equals(wan.IpAddress, cleanWanIp, StringComparison.OrdinalIgnoreCase);

        bool lanIpMatches = !string.IsNullOrWhiteSpace(cleanLanIp) &&
                            lan != null &&
                            string.Equals(lan.IpAddress, cleanLanIp, StringComparison.OrdinalIgnoreCase);

        bool allOk = (wan != null && wan.IsAdminUp) && (lan != null && lan.IsAdminUp);

        var wanDesc = wan != null
            ? $"{wan.InterfaceName} (IP: {wan.IpAddress ?? "Sem IP"} - {(wan.IsPhysicalUp ? "UP/UP" : "UP/DOWN")})"
            : "WAN não localizada";

        var lanDesc = lan != null
            ? $"{lan.InterfaceName} (IP: {lan.IpAddress ?? "Sem IP"} - {(lan.IsPhysicalUp ? "UP/UP" : "UP/DOWN")})"
            : "LAN não localizada";

        var summary = $"WAN: {wanDesc} | LAN: {lanDesc}";

        return new InterfacesVerificationResult(
            wan,
            lan,
            wanIpMatches,
            lanIpMatches,
            allOk,
            rawOutput,
            summary);
    }

    private static string? CleanIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var idx = ip.IndexOf('/');
        return idx >= 0 ? ip.Substring(0, idx).Trim() : ip.Trim();
    }
}
