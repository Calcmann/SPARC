using Android.Content;
using Android.Net;
using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
#pragma warning disable CA1422

namespace NetworkDevice.Android.Services;

/// <summary>
/// Gerenciador de interface de rede Ethernet OTG no Android.
/// Responsável por identificar adaptadores USB-Ethernet cabeados (chipsets Realtek RTL8152/8153, ASIX AX88179, etc.),
/// realizar o binding estrito de processo (process network binding) para prevenir vazamento e falso positivo
/// via Wi-Fi/4G/5G durante os testes técnicos de ativação, e auxiliar na configuração de IP local.
/// </summary>
public sealed class AndroidEthernetManager
{
    private static readonly Lazy<AndroidEthernetManager> _instance = new(() => new AndroidEthernetManager());
    public static AndroidEthernetManager Instance => _instance.Value;

    private readonly ConnectivityManager? _connectivityManager;
    private Network? _boundEthernetNetwork;

    public bool IsBoundToEthernet => _boundEthernetNetwork != null;

    private AndroidEthernetManager()
    {
        try
        {
            _connectivityManager = global::Android.App.Application.Context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        }
        catch
        {
            _connectivityManager = null;
        }
    }

    /// <summary>
    /// Verifica se há alguma interface física Ethernet conectada e operacional no Android (cabo conectado via OTG).
    /// </summary>
    public bool IsEthernetConnected()
    {
        if (_connectivityManager != null)
        {
            try
            {
                var networks = _connectivityManager.GetAllNetworks();
                foreach (var net in networks)
                {
                    var caps = _connectivityManager.GetNetworkCapabilities(net);
                    if (caps != null && caps.HasTransport(TransportType.Ethernet))
                    {
                        return true;
                    }
                }
            }
            catch { }
        }

        // Fallback rigoroso: Somente interfaces físicas cabeada ("eth" ou "usb"), excluindo estritamente modems celulares e WiFi
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(ni => (ni.Name.StartsWith("eth", StringComparison.OrdinalIgnoreCase) ||
                            ni.Name.StartsWith("usb", StringComparison.OrdinalIgnoreCase)) &&
                           !ni.Name.StartsWith("rmnet", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("ccmni", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("dummy", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("tun", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("p2p", StringComparison.OrdinalIgnoreCase) &&
                           ni.OperationalStatus == OperationalStatus.Up);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Verifica se há hardware de interface Ethernet (adaptador USB-Ethernet cabeado via HUB ou OTG)
    /// presente no smartphone, mesmo que o cabo RJ45 ainda esteja desconectado ou sem link.
    /// </summary>
    public bool HasEthernetHardwareInterface()
    {
        // 1. Consulta dispositivos USB diretamente (Adaptadores USB Ethernet ou HUBs multiporta)
        try
        {
            var usbDevices = DeviceConnectionManager.Instance.ScanUsbDevices();
            if (usbDevices.Count > 0)
            {
                if (usbDevices.Any(DeviceConnectionManager.IsNetworkOrHubDevice))
                {
                    return true;
                }
                // Se houver mais de 1 dispositivo USB conectado no smartphone, obrigatoriamente está usando um HUB USB
                if (usbDevices.Count > 1)
                {
                    return true;
                }
            }
        }
        catch { }

        // 2. ConnectivityManager
        if (_connectivityManager != null)
        {
            try
            {
                var networks = _connectivityManager.GetAllNetworks();
                foreach (var net in networks)
                {
                    var caps = _connectivityManager.GetNetworkCapabilities(net);
                    if (caps != null && caps.HasTransport(TransportType.Ethernet))
                    {
                        return true;
                    }
                }
            }
            catch { }
        }

        // 3. NetworkInterface
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(ni => (ni.Name.StartsWith("eth", StringComparison.OrdinalIgnoreCase) ||
                            ni.Name.StartsWith("usb", StringComparison.OrdinalIgnoreCase)) &&
                           !ni.Name.StartsWith("rmnet", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("ccmni", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("dummy", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("tun", StringComparison.OrdinalIgnoreCase) &&
                           !ni.Name.StartsWith("p2p", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Avalia se o smartphone está operando com HUB USB contendo adaptador Serial e Ethernet
    /// conectados simultaneamente.
    /// </summary>
    public bool IsHubUsbSimultaneousActive(bool isSerialConnected)
    {
        // Se a serial está conectada ou identificada
        if (!isSerialConnected && !DeviceConnectionManager.Instance.HasSupportedSerialConnected())
            return false;

        // Se há interface Ethernet física detectada (enlace ativo ou adaptador plugado no USB)
        if (IsEthernetConnected() || HasEthernetHardwareInterface())
            return true;

        // Se há múltiplos dispositivos USB conectados (HUB USB com serial + rede)
        try
        {
            var usbDevices = DeviceConnectionManager.Instance.ScanUsbDevices();
            if (usbDevices.Count > 1)
                return true;
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Retorna o IPv4 atual atribuído à interface Ethernet cabeada no dispositivo Android.
    /// Retorna null se a interface ainda não tiver recebido IP (DHCP pendente ou IP estático não configurado).
    /// </summary>
    public string? GetEthernetIpAddress()
    {
        try
        {
            var ethInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => (ni.Name.StartsWith("eth", StringComparison.OrdinalIgnoreCase) ||
                              ni.Name.StartsWith("usb", StringComparison.OrdinalIgnoreCase)) &&
                             !ni.Name.StartsWith("rmnet", StringComparison.OrdinalIgnoreCase) &&
                             !ni.Name.StartsWith("ccmni", StringComparison.OrdinalIgnoreCase) &&
                             !ni.Name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase) &&
                             !ni.Name.StartsWith("dummy", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var ni in ethInterfaces)
            {
                var ip = ni.GetIPProperties().UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                                        !System.Net.IPAddress.IsLoopback(a.Address))?
                    .Address.ToString();

                if (!string.IsNullOrWhiteSpace(ip))
                    return ip;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Vincula o processo do aplicativo Android estritamente à rede Ethernet (Process Network Binding).
    /// Garante 100% que TODOS os sockets (HTTP :8080, FTP :2121, Ping ICMP, Telnet TCP :23 e Teste de Banda)
    /// transitem exclusivamente pelo adaptador OTG Ethernet conectado ao roteador, impedindo vazamentos
    /// e falsos positivos via Wi-Fi ou dados móveis (4G/5G).
    /// </summary>
    public bool BindProcessToEthernet(Action<string>? logger = null)
    {
        if (_connectivityManager == null)
        {
            logger?.Invoke("[AVISO ISOLAMENTO] ConnectivityManager indisponível para binding de rede.");
            return false;
        }

        try
        {
            var networks = _connectivityManager.GetAllNetworks();
            Network? targetEth = null;

            foreach (var net in networks)
            {
                var caps = _connectivityManager.GetNetworkCapabilities(net);
                if (caps != null && caps.HasTransport(TransportType.Ethernet))
                {
                    targetEth = net;
                    break;
                }
            }

            if (targetEth != null)
            {
                var bound = _connectivityManager.BindProcessToNetwork(targetEth);
                if (bound)
                {
                    _boundEthernetNetwork = targetEth;
                    logger?.Invoke("[✓ ISOLAMENTO ATIVO] Tráfego do SPARC vinculado estritamente à interface Ethernet OTG (eth0). Dados móveis/Wi-Fi bloqueados para os testes.");
                    return true;
                }
                else
                {
                    logger?.Invoke("[!] BindProcessToNetwork retornou falso para a interface Ethernet.");
                }
            }
            else
            {
                logger?.Invoke("[!] Nenhuma interface com transporte Ethernet detectada pelo sistema Android.");
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[!] Falha ao vincular processo à interface Ethernet: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Remove o vínculo de processo com a interface Ethernet, restaurando o roteamento padrão do Android.
    /// </summary>
    public void UnbindProcessFromNetwork(Action<string>? logger = null)
    {
        if (_connectivityManager == null || _boundEthernetNetwork == null) return;
        try
        {
            _connectivityManager.BindProcessToNetwork(null);
            _boundEthernetNetwork = null;
            logger?.Invoke("[*] Vínculo de rede Ethernet liberado. Conectividade padrão do sistema restabelecida.");
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[!] Falha ao desvincular processo da Ethernet: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispara um Intent para abrir as configurações de rede do Android (para ajuste de IP estático na interface Ethernet).
    /// </summary>
    public Task OpenEthernetSettingsAsync()
    {
        var context = global::Android.App.Application.Context;

        // 1. Tenta Intent específico de Ethernet (AOSP / Android standard)
        try
        {
            var intent = new Intent("android.settings.ETHERNET_SETTINGS");
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
            return Task.CompletedTask;
        }
        catch { }

        // 2. Tenta componente direto de Ethernet (Samsung / AOSP SubSettings)
        try
        {
            var intent = new Intent("android.intent.action.MAIN");
            intent.SetClassName("com.android.settings", "com.android.settings.SubSettings");
            intent.PutExtra(":settings:show_fragment", "com.samsung.android.settings.connection.EthernetSettings");
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
            return Task.CompletedTask;
        }
        catch { }

        // 3. Tenta tela de conexões de rede (Wireless Settings)
        try
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionWirelessSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
            return Task.CompletedTask;
        }
        catch { }

        // 4. Fallback final para configurações gerais
        try
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionSettings);
            intent.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch { }

        return Task.CompletedTask;
    }
}
