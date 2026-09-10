using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace NetworkDevice.Core.Provisioning;

public sealed record AdapterSnapshot(
    string AdapterName,
    DateTime CapturedAtUtc,
    bool DhcpEnabled,
    string? IpAddress,
    string? SubnetMask,
    string? Gateway,
    List<string> DnsServers)
{
    public string Descrever() => DhcpEnabled
        ? $"DHCP automático{(string.IsNullOrWhiteSpace(IpAddress) ? "" : $" (atual {IpAddress})")}"
        : $"Fixo {IpAddress ?? "?"} / {SubnetMask ?? "?"} gw {Gateway ?? "-"} dns {(DnsServers.Count > 0 ? string.Join(",", DnsServers) : "-")}";
}

public class WindowsHostNetworkService : IHostNetworkService
{
    public IReadOnlyList<string> GetAvailableAdapters()
    {
        return HostNetworkManager.GetEthernetAdapters();
    }

    public Task<(bool success, string output)> SetStaticIpAsync(
        string adapterName,
        string ipAddress,
        string subnetMask,
        string? gateway = null,
        CancellationToken cancellationToken = default)
    {
        return HostNetworkManager.SetStaticIpAsync(adapterName, ipAddress, subnetMask, gateway, cancellationToken);
    }

    public Task<(bool success, string output)> SetDhcpAsync(
        string adapterName,
        CancellationToken cancellationToken = default)
    {
        return HostNetworkManager.SetDhcpAsync(adapterName, cancellationToken);
    }
}

public class AndroidHostNetworkGuidance : IHostNetworkService
{
    public IReadOnlyList<string> GetAvailableAdapters()
    {
        return new List<string> { "Ethernet OTG (eth0)", "Wi-Fi (wlan0)" };
    }

    public Task<(bool success, string output)> SetStaticIpAsync(
        string adapterName,
        string ipAddress,
        string subnetMask,
        string? gateway = null,
        CancellationToken cancellationToken = default)
    {
        var msg = $"[Android Guidance] No Android, configure o IP estático em Configurações > Rede > {adapterName}:\n" +
                  $"IP: {ipAddress}\n" +
                  $"Máscara: {subnetMask}\n" +
                  $"Gateway: {gateway ?? "N/A"}\n" +
                  $"DNS Primário: 1.1.1.1\n" +
                  $"DNS Secundário: 8.8.8.8";
        return Task.FromResult((true, msg));
    }

    public Task<(bool success, string output)> SetDhcpAsync(
        string adapterName,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult((true, "[Android Guidance] Configure o modo IP para DHCP (Automático) nas configurações de rede do Android."));
    }
}

public static class HostNetworkManager
{
    /// <summary>
    /// Lista os nomes dos adaptadores de rede físicos Ethernet/Wi-Fi disponíveis, priorizando SEMPRE adaptadores Ethernet cabeados.
    /// </summary>
    public static IReadOnlyList<string> GetEthernetAdapters()
    {
        var list = new List<(string name, bool isUp, bool isEthernet)>();
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    if (ni.Description.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                        ni.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                        ni.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
                        ni.Description.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                        ni.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                        ni.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
                        ni.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) ||
                        ni.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var isUp = ni.OperationalStatus == OperationalStatus.Up;
                    var isEth = ni.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                                !ni.Description.Contains("Wireless", StringComparison.OrdinalIgnoreCase) &&
                                !ni.Description.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) &&
                                !ni.Description.Contains("802.11", StringComparison.OrdinalIgnoreCase) &&
                                !ni.Name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase);
                    list.Add((ni.Name, isUp, isEth));
                }
            }
        }
        catch { }
        if (list.Count == 0) return new List<string> { "Ethernet" };
        // Prioriza: Ethernet cabeada (Up > Down) > Wi-Fi (Up > Down)
        return list.OrderByDescending(x => x.isEthernet).ThenByDescending(x => x.isUp).Select(x => x.name).ToList();
    }

    /// <summary>
    /// Configura endereço IP estático no adaptador de rede do Windows via netsh com DNS 1.1.1.1 e 8.8.8.8.
    /// Antes da primeira alteração, salva snapshot da configuração anterior (ver EnsureSnapshot).
    /// </summary>
    public static async Task<(bool success, string output)> SetStaticIpAsync(
        string adapterName,
        string ipAddress,
        string subnetMask,
        string? gateway = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? dnsServers = null,
        bool isRestore = false)
    {
        try { EnsureSnapshot(adapterName); } catch { }
        if (!isRestore)
        {
            lock (_snapLock) { _lastApplied[adapterName] = (ipAddress, subnetMask); }
        }
        NetLog($"SetStaticIp adapter='{adapterName}' ip={ipAddress} mask={subnetMask} gw={gateway ?? "-"} restore={isRestore}");

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return (true, $"[Aviso] Configuração automática de IP via netsh suportada no Windows. IP: {ipAddress}, Máscara: {subnetMask}, Gateway: {gateway}, DNS: 1.1.1.1 e 8.8.8.8");
        }

        var gatewayArg = string.IsNullOrWhiteSpace(gateway) ? "" : $" {gateway} 1";
        var cmdIp = $"interface ip set address name=\"{adapterName}\" static {ipAddress} {subnetMask}{gatewayArg}";

        var (ipSuccess, ipOutput) = await RunNetshAsync(cmdIp, cancellationToken);
        if (!ipSuccess)
        {
            // Se o netsh acusou que o objeto já existe, confere se o adaptador já está com o IP desejado
            var currentIp = GetCurrentIpForAdapter(adapterName);
            if (currentIp == ipAddress ||
                ipOutput.Contains("objeto j", StringComparison.OrdinalIgnoreCase) ||
                ipOutput.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"IP: {ipAddress}, Máscara: {subnetMask} já atribuído à interface '{adapterName}'.");
            }
            return (false, $"Falha ao configurar IP na interface '{adapterName}': {ipOutput}");
        }

        // Configuração de DNS (restauração usa os originais; provisionamento usa 1.1.1.1 e 8.8.8.8)
        var dns1 = dnsServers is { Count: > 0 } && !string.IsNullOrWhiteSpace(dnsServers[0]) ? dnsServers[0]! : "1.1.1.1";
        var dns2 = dnsServers is { Count: > 1 } && !string.IsNullOrWhiteSpace(dnsServers[1]) ? dnsServers[1]! : "8.8.8.8";
        var cmdDns1 = $"interface ip set dns name=\"{adapterName}\" static {dns1} primary";
        await RunNetshAsync(cmdDns1, cancellationToken);

        var cmdDns2 = $"interface ip add dns name=\"{adapterName}\" {dns2} index=2";
        await RunNetshAsync(cmdDns2, cancellationToken);

        return (true, $"IP: {ipAddress}, Máscara: {subnetMask}, Gateway: {gateway ?? "N/A"}, DNS Primário: {dns1}, DNS Secundário: {dns2} aplicados com sucesso.");
    }

    /// <summary>
    /// Retorna o adaptador de rede do Windows para DHCP automático via netsh.
    /// </summary>
    public static async Task<(bool success, string output)> SetDhcpAsync(
        string adapterName,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return (true, "[Aviso] Configuração de DHCP automático suportada no Windows.");
        }

        var cmdIp = $"interface ip set address name=\"{adapterName}\" dhcp";
        await RunNetshAsync(cmdIp, cancellationToken);

        var cmdDns = $"interface ip set dns name=\"{adapterName}\" dhcp";
        await RunNetshAsync(cmdDns, cancellationToken);

        return (true, $"Interface '{adapterName}' retornada para DHCP (IP e DNS automáticos).");
    }

    /// <summary>
    /// Escolhe a placa do notebook: preferência explícita (combo da UI) ou primeira Ethernet.
    /// Nulo apenas se não houver adaptador algum.
    /// </summary>
    public static string? EscolherAdaptadorNotebook(string? preferido)
    {
        if (!string.IsNullOrWhiteSpace(preferido)) return preferido.Trim();
        var adapters = GetEthernetAdapters();
        return adapters.FirstOrDefault(a => a.Contains("Ethernet", StringComparison.OrdinalIgnoreCase))
            ?? adapters.FirstOrDefault();
    }

    public static string SnapshotDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SPARC", "netbackup");

    private static readonly HashSet<string> _snapshotted = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (string ip, string mask)> _lastApplied = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _snapLock = new();

    /// <summary>Log persistente do fluxo de rede (fatos, não adivinhação).</summary>
    public static void NetLog(string message)
    {
        try
        {
            Directory.CreateDirectory(SnapshotDirectory);
            File.AppendAllText(Path.Combine(SnapshotDirectory, "restore.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\n");
        }
        catch { }
    }

    /// <summary>True se ESTA sessão alterou alguma placa (restauração na saída faz sentido).</summary>
    public static bool NeedsRestoreOnExit
    {
        get { lock (_snapLock) { return _lastApplied.Count > 0; } }
    }

    private static string SnapshotPath(string adapterName, string? directory = null)
    {
        var safe = string.Concat(adapterName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        if (string.IsNullOrWhiteSpace(safe)) safe = "adapter";
        return Path.Combine(directory ?? SnapshotDirectory, safe + "__latest.json");
    }

    /// <summary>
    /// Captura a configuração atual do adaptador (IP/máscara/gateway/DNS/DHCP). Retorna null se sem IPv4.
    /// </summary>
    public static AdapterSnapshot? CaptureSnapshot(string adapterName)
    {
        try
        {
            var ni = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase));
            if (ni == null) return null;
            var props = ni.GetIPProperties();
            var uni = props.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address));
            if (uni == null) return null;
            string? mask = null;
            try { mask = PrefixLengthToMask(uni.PrefixLength); } catch { }
            var gw = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var dns = props.DnsAddresses
                .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                .Select(d => d.ToString()).Distinct().ToList();
            var dhcp = true;
            try { dhcp = props.GetIPv4Properties().IsDhcpEnabled; } catch { }
            return new AdapterSnapshot(adapterName, DateTime.UtcNow, dhcp, uni.Address.ToString(), mask, gw, dns);
        }
        catch { return null; }
    }

    internal static string PrefixLengthToMask(int prefixLength)
    {
        uint m = prefixLength <= 0 ? 0u : prefixLength >= 32 ? 0xFFFFFFFFu : 0xFFFFFFFFu << (32 - prefixLength);
        var b = BitConverter.GetBytes(m);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return $"{b[0]}.{b[1]}.{b[2]}.{b[3]}";
    }

    internal static void SaveSnapshot(AdapterSnapshot snap, string? directory = null)
    {
        var dir = directory ?? SnapshotDirectory;
        Directory.CreateDirectory(dir);
        File.WriteAllText(SnapshotPath(snap.AdapterName, dir),
            JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static AdapterSnapshot? LoadSnapshot(string adapterName, string? directory = null)
    {
        try
        {
            var path = SnapshotPath(adapterName, directory);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<AdapterSnapshot>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    /// <summary>
    /// Garante snapshot do estado ORIGINAL: usa o salvo em disco se existir (nunca sobrescreve um
    /// original com valores já alterados pelo SPARC); senão captura e salva o atual.
    /// Chamado automaticamente antes de qualquer SetStaticIpAsync.
    /// </summary>
    public static AdapterSnapshot? EnsureSnapshot(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName)) return null;
        try
        {
            lock (_snapLock)
            {
                var saved = LoadSnapshot(adapterName);
                if (saved != null)
                {
                    _snapshotted.Add(adapterName);
                    NetLog($"EnsureSnapshot '{adapterName}': usando salvo em disco ({saved.Descrever()})");
                    return saved;
                }
                var cur = CaptureSnapshot(adapterName);
                if (cur != null)
                {
                    SaveSnapshot(cur);
                    _snapshotted.Add(adapterName);
                    NetLog($"EnsureSnapshot '{adapterName}': capturado novo ({cur.Descrever()})");
                }
                else
                {
                    NetLog($"EnsureSnapshot '{adapterName}': sem IPv4 para capturar");
                }
                return cur;
            }
        }
        catch { return null; }
    }

    public static AdapterSnapshot? LoadLatestSnapshot(string adapterName)
    {
        try { return LoadSnapshot(adapterName); } catch { return null; }
    }

    /// <summary>Snapshot mais recente entre todos os adaptadores (para oferta de restauração).</summary>
    public static AdapterSnapshot? LoadLatestAny()
    {
        try
        {
            var dir = SnapshotDirectory;
            if (!Directory.Exists(dir)) return null;
            AdapterSnapshot? best = null;
            foreach (var f in Directory.GetFiles(dir, "*__latest.json"))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<AdapterSnapshot>(File.ReadAllText(f));
                    if (s != null && (best == null || s.CapturedAtUtc > best.CapturedAtUtc)) best = s;
                }
                catch { }
            }
            return best;
        }
        catch { return null; }
    }

    /// <summary>True se a config atual do adaptador já equivale ao snapshot (nada a restaurar).</summary>
    public static bool ConfigEqualsSnapshot(string adapterName, AdapterSnapshot snap)
    {
        try
        {
            var cur = CaptureSnapshot(adapterName);
            if (cur == null) return false;
            return cur.DhcpEnabled == snap.DhcpEnabled
                && string.Equals(cur.IpAddress ?? "", snap.IpAddress ?? "", StringComparison.OrdinalIgnoreCase)
                && string.Equals(cur.SubnetMask ?? "", snap.SubnetMask ?? "", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// True se o snapshot tem o IP que o próprio SPARC aplicou (captura tardia, inválido como "original").
    /// Compara com o último IP aplicado nesta sessão e com o IP aplicado informado (ex: HostLanIp da ficha).
    /// </summary>
    public static bool SnapshotEhSparc(AdapterSnapshot snap, string? sparcAppliedIp)
    {
        if (snap.DhcpEnabled || string.IsNullOrWhiteSpace(snap.IpAddress)) return false;
        lock (_snapLock)
        {
            if (_lastApplied.TryGetValue(snap.AdapterName, out var a) &&
                string.Equals(a.ip, snap.IpAddress, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return !string.IsNullOrWhiteSpace(sparcAppliedIp) &&
               string.Equals(sparcAppliedIp.Trim(), snap.IpAddress, StringComparison.OrdinalIgnoreCase);
    }

    internal static void InvalidateSnapshot(string adapterName)
    {
        try
        {
            var path = SnapshotPath(adapterName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
        lock (_snapLock) { _snapshotted.Remove(adapterName); }
    }

    /// <summary>
    /// Restauração SILENCIOSA (sem nenhum popup): volta a placa ao estado anterior ao SPARC.
    /// Sem snapshot confiável, volta para DHCP e registra tudo no log retornado.
    /// Ao final, anexa o status ATUAL da placa para conferência no terminal.
    /// </summary>
    public static async Task<(bool success, string log)> RestoreLastAsync(
        string? preferredAdapter, string? sparcAppliedIp, CancellationToken cancellationToken = default)
    {
        var (ok, log, adapter, dhcpPath) = await RestoreLastCoreAsync(preferredAdapter, sparcAppliedIp, cancellationToken);
        if (!string.IsNullOrWhiteSpace(adapter))
        {
            try { await Task.Delay(dhcpPath ? 4000 : 1200, cancellationToken); } catch { }
            try
            {
                var st = CaptureSnapshot(adapter);
                log += $"\n[REDE] Status atual da placa '{adapter}': {(st != null ? st.Descrever() : "não lido — confira com ipconfig")}";
            }
            catch { }
        }
        return (ok, log);
    }

    private static async Task<(bool success, string log, string? adapter, bool dhcpPath)> RestoreLastCoreAsync(
        string? preferredAdapter, string? sparcAppliedIp, CancellationToken cancellationToken = default)
    {
        AdapterSnapshot? snap = null;
        if (!string.IsNullOrWhiteSpace(preferredAdapter))
            snap = LoadLatestSnapshot(preferredAdapter);
        snap ??= LoadLatestAny();
        var adapter = snap?.AdapterName ?? preferredAdapter;

        if (snap == null)
        {
            if (string.IsNullOrWhiteSpace(adapter))
            {
                NetLog("Restore: sem snapshot e sem adaptador — nada a fazer.");
                return (false, "[REDE] Sem snapshot e sem adaptador — nada a restaurar.", null, false);
            }
            var (okDhcp, outDhcp) = await SetDhcpAsync(adapter, cancellationToken);
            NetLog($"Restore: sem snapshot, DHCP em '{adapter}' ok={okDhcp}: {outDhcp}");
            return (okDhcp, $"[REDE] Sem snapshot confiável da placa '{adapter}' — aplicada volta para DHCP.\n[REDE] {outDhcp}", adapter, true);
        }

        // Ordem importa: primeiro invalida snapshot SPARC-made (mesmo que o atual seja igual a ele),
        // senão o poison congela tudo em no-op para sempre.
        if (!snap.DhcpEnabled && SnapshotEhSparc(snap, sparcAppliedIp))
        {
            InvalidateSnapshot(snap.AdapterName);
            var (okDhcp, outDhcp) = await SetDhcpAsync(snap.AdapterName, cancellationToken);
            NetLog($"Restore: snapshot SPARC-made descartado em '{snap.AdapterName}', DHCP ok={okDhcp}: {outDhcp}");
            return (okDhcp,
                $"[REDE] Snapshot da placa '{snap.AdapterName}' era inválido (capturado após alteração do próprio SPARC) — descartado.\n" +
                $"[REDE] Placa voltada para DHCP.\n[REDE] {outDhcp}", snap.AdapterName, true);
        }

        if (ConfigEqualsSnapshot(snap.AdapterName, snap))
        {
            NetLog($"Restore: '{snap.AdapterName}' já está na original — no-op.");
            return (true, $"[REDE] Placa '{snap.AdapterName}' já está na configuração original ({snap.Descrever()}) — nada a fazer.", snap.AdapterName, false);
        }

        var (ok, outMsg) = await RestoreSnapshotAsync(snap, cancellationToken);
        NetLog($"Restore: snapshot aplicado em '{snap.AdapterName}' ok={ok}: {outMsg}");
        return (ok, (ok ? "[REDE] Placa restaurada para a configuração anterior: " : "[REDE][FALHA] ") + outMsg, snap.AdapterName, snap.DhcpEnabled);
    }
    /// <summary>
    /// Restaura um snapshot válido (DHCP volta a DHCP; fixo reaplica IP/máscara/gateway/DNS).
    /// Não atualiza o marcador de último aplicado (para não invalidar o próprio snapshot).
    /// </summary>
    public static async Task<(bool success, string output)> RestoreSnapshotAsync(
        AdapterSnapshot snap, CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return (true, "[Aviso] Restauração automática de rede suportada no Windows.");
        if (snap.DhcpEnabled)
            return await SetDhcpAsync(snap.AdapterName, cancellationToken);
        if (string.IsNullOrWhiteSpace(snap.IpAddress) || string.IsNullOrWhiteSpace(snap.SubnetMask))
            return (false, "Snapshot sem IP fixo válido para restaurar.");
        return await SetStaticIpAsync(snap.AdapterName, snap.IpAddress, snap.SubnetMask,
            snap.Gateway, cancellationToken, snap.DnsServers, isRestore: true);
    }

    public static async Task EnsureTftpFirewallRuleAsync(CancellationToken ct = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        var args = "advfirewall firewall add rule name=\"SPARC TFTP 69\" dir=in action=allow protocol=UDP localport=69 profile=any";
        await RunNetshAsync(args, ct);
    }

    public static string? GetCurrentIpForAdapter(string adapterName)
    {
        try
        {
            var ni = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => n.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase));
            if (ni == null) return null;
            var ip = ni.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            return ip?.Address.ToString();
        }
        catch { return null; }
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(id);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static async Task<(bool success, string output)> RunNetshAsync(string arguments, CancellationToken cancellationToken = default)
    {
        // Se não é admin, tenta elevar via runas UAC; se usuário cancelar, falha com instrução
        if (!IsAdministrator())
        {
            var elevated = await TryRunNetshElevatedAsync(arguments, cancellationToken);
            if (elevated.HasValue) return elevated.Value;
            // fallback tenta direto (gerará erro de acesso negado tratável)
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (false, "Falha ao iniciar processo netsh.");

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var combined = (stdout + "\n" + stderr).Trim();
            var isSuccess = process.ExitCode == 0 && !combined.Contains("error", StringComparison.OrdinalIgnoreCase);
            if (!isSuccess && combined.Contains("administrador", StringComparison.OrdinalIgnoreCase))
                combined += "\n[DICA] Execute o Killtech como Administrador (clique direito > Executar como administrador) ou aceite o prompt UAC.";

            return (isSuccess, combined);
        }
        catch (Exception ex)
        {
            return (false, ex.Message + " — execute como Administrador.");
        }
    }

    private static async Task<(bool success, string output)?> TryRunNetshElevatedAsync(string arguments, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            // Quando UseShellExecute=true não há redirect; usa cmd /c para capturar saída
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c netsh {arguments} 2>&1";
            using var p = Process.Start(psi);
            if (p == null) return null;
            await p.WaitForExitAsync(ct);
            return p.ExitCode == 0 ? (true, "Executado elevado via UAC.") : null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, "Operação cancelada — UAC de administrador negado. Clique direito > Executar como administrador e tente novamente.");
        }
        catch { return null; }
    }
}
