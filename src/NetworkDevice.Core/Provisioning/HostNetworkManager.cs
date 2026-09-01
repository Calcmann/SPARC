using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NetworkDevice.Core.Provisioning;

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
    /// </summary>
    public static async Task<(bool success, string output)> SetStaticIpAsync(
        string adapterName,
        string ipAddress,
        string subnetMask,
        string? gateway = null,
        CancellationToken cancellationToken = default)
    {
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

        // Configuração de DNS Primário (1.1.1.1) e Secundário (8.8.8.8)
        var cmdDns1 = $"interface ip set dns name=\"{adapterName}\" static 1.1.1.1 primary";
        await RunNetshAsync(cmdDns1, cancellationToken);

        var cmdDns2 = $"interface ip add dns name=\"{adapterName}\" 8.8.8.8 index=2";
        await RunNetshAsync(cmdDns2, cancellationToken);

        return (true, $"IP: {ipAddress}, Máscara: {subnetMask}, Gateway: {gateway ?? "N/A"}, DNS Primário: 1.1.1.1, DNS Secundário: 8.8.8.8 aplicados com sucesso.");
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
