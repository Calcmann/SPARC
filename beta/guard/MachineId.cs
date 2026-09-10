using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace NetworkDevice.UI.Beta;

internal sealed record MachineIdentity(string MachineGuid, string Fingerprint, string DisplayId);

internal static class MachineId
{
    public static MachineIdentity Current()
    {
        var guid = ReadMachineGuid();
        var mac = ReadPrimaryMac();
        var vol = ReadSystemVolumeSerial();
        var fp = Sha256Hex(guid + "|" + mac + "|" + vol);
        var display = fp.Substring(0, 4) + "-" + fp.Substring(4, 4) + "-" + fp.Substring(8, 4) + "-" + fp.Substring(12, 4);
        return new MachineIdentity(guid, fp, display);
    }

    public static string BuildRequest(MachineIdentity id)
    {
        var json = "{\"g\":\"" + id.MachineGuid + "\",\"f\":\"" + id.Fingerprint + "\"}";
        return "SPBREQ." + Base64Url.Encode(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryParseRequest(string req, out string guid, out string fp)
    {
        guid = ""; fp = "";
        try
        {
            req = req.Trim();
            if (!req.StartsWith("SPBREQ.", StringComparison.Ordinal)) return false;
            var payload = Base64Url.Decode(req.Substring("SPBREQ.".Length));
            using var doc = JsonDocument.Parse(payload);
            guid = doc.RootElement.GetProperty("g").GetString() ?? "";
            fp = doc.RootElement.GetProperty("f").GetString() ?? "";
            return !string.IsNullOrWhiteSpace(guid) && !string.IsNullOrWhiteSpace(fp);
        }
        catch { return false; }
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var v = k?.GetValue("MachineGuid")?.ToString();
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        }
        catch { }
        return "UNKNOWN-GUID";
    }

    private static string ReadPrimaryMac()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                    && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Where(n => n.Description.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) < 0
                    && n.Description.IndexOf("VPN", StringComparison.OrdinalIgnoreCase) < 0
                    && n.Description.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) < 0
                    && n.Description.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) < 0
                    && n.Description.IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(n => n.Description)
                .ToList();
            var mac = nics.FirstOrDefault()?.GetPhysicalAddress().ToString();
            if (!string.IsNullOrWhiteSpace(mac)) return mac;
            var any = NetworkInterface.GetAllNetworkInterfaces()
                .Select(n => n.GetPhysicalAddress().ToString())
                .FirstOrDefault(s => s.Length == 12);
            return string.IsNullOrWhiteSpace(any) ? "NOMAC" : any;
        }
        catch { return "NOMAC"; }
    }

    private static string ReadSystemVolumeSerial()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            if (GetVolumeInformationW(root, null, 0, out var serial, out _, out _, null, 0))
                return serial.ToString("X8");
        }
        catch { }
        return "NOVOL";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(string root, StringBuilder? volName, int volNameSize, out uint serial, out uint maxComp, out uint flags, StringBuilder? fs, int fsSize);

    internal static string Sha256Hex(string s)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    }
}

internal static class Base64Url
{
    public static string Encode(byte[] b)
    {
        return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Decode(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; }
        return Convert.FromBase64String(t);
    }
}
