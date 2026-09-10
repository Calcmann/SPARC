using System.IO;
using System.Net;
using System.Net.Sockets;

namespace NetworkDevice.UI.Beta;

// Relogio da beta: NTP oportunista (nunca bloqueia campo offline) + anti-rollback via lastseen.
internal static class BetaClock
{
    private static readonly TimeSpan NtpTimeout = TimeSpan.FromSeconds(2.5);

    public static DateTime EffectiveUtcNow()
    {
        var local = DateTime.UtcNow;
        try
        {
            var net = QueryNtpUtc("time.windows.com");
            if (net.HasValue && (net.Value - local).Duration() > TimeSpan.FromMinutes(10))
                return net.Value;
        }
        catch { }
        return local;
    }

    public static bool CheckAndUpdateLastSeen(DateTime now)
    {
        try
        {
            Directory.CreateDirectory(BetaConfig.DataDir);
            if (File.Exists(BetaConfig.LastSeenPath))
            {
                var txt = File.ReadAllText(BetaConfig.LastSeenPath).Trim();
                if (long.TryParse(txt, out var ticks))
                {
                    var last = new DateTime(ticks, DateTimeKind.Utc);
                    if (now < last - TimeSpan.FromMinutes(5)) return false;
                    if (now <= last) return true;
                }
            }
            File.WriteAllText(BetaConfig.LastSeenPath, now.Ticks.ToString());
            return true;
        }
        catch { return true; }
    }

    public static void Touch(DateTime now)
    {
        try { Directory.CreateDirectory(BetaConfig.DataDir); File.WriteAllText(BetaConfig.LastSeenPath, now.Ticks.ToString()); }
        catch { }
    }

    private static DateTime? QueryNtpUtc(string host)
    {
        var data = new byte[48];
        data[0] = 0x1B;
        using var udp = new UdpClient();
        udp.Client.ReceiveTimeout = (int)NtpTimeout.TotalMilliseconds;
        udp.Connect(host, 123);
        udp.Send(data, data.Length);
        var ep = new IPEndPoint(IPAddress.Any, 0);
        var resp = udp.Receive(ref ep);
        if (resp.Length < 48) return null;
        ulong intPart = ((ulong)resp[40] << 24) | ((ulong)resp[41] << 16) | ((ulong)resp[42] << 8) | resp[43];
        ulong fracPart = ((ulong)resp[44] << 24) | ((ulong)resp[45] << 16) | ((ulong)resp[46] << 8) | resp[47];
        var ms = intPart * 1000 + fracPart * 1000 / 0x100000000L;
        return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds((long)ms);
    }
}
