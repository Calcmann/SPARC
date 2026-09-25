using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace NetworkDevice.Protocols.Serial;

public static class SerialPorts
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryDosDeviceW")]
    private static extern uint QueryDosDevice(string? lpDeviceName, IntPtr lpTargetPath, uint ucchMax);

    /// <summary>
    /// Retorna todas as portas seriais COM disponíveis no sistema operacional.
    /// Utiliza múltiplas fontes de descoberta para máxima compatibilidade:
    /// 1. System.IO.Ports.SerialPort.GetPortNames() (.NET padrão)
    /// 2. Win32 QueryDosDeviceW (método nativo do Windows / PuTTY para listar \DosDevices\COM*)
    /// 3. Registry HKLM\HARDWARE\DEVICEMAP\SERIALCOMM
    /// 4. Registry HKLM\SYSTEM\CurrentControlSet\Enum\USB (PnP Device Parameters\PortName)
    /// </summary>
    public static IReadOnlyList<string> Available()
    {
        var ports = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Método padrão .NET
        try
        {
            foreach (var p in SerialPort.GetPortNames())
            {
                var clean = CleanPortName(p);
                if (!string.IsNullOrEmpty(clean))
                    ports.Add(clean);
            }
        }
        catch { }

        // Fontes exclusivas de Windows (onde drivers modernos como CH340 v3.9/v4.0 operam)
        if (OperatingSystem.IsWindows())
        {
            // 2. Win32 QueryDosDeviceW (O mesmo mecanismo de baixo nível utilizado por PuTTY e pelo Windows Explorer)
            try
            {
                QueryDosDeviceComPorts(ports);
            }
            catch { }

            // 3. Scan direto no registro em SERIALCOMM
            try
            {
                ScanRegistrySerialComm(ports);
            }
            catch { }

            // 4. Scan PnP em HKLM\SYSTEM\CurrentControlSet\Enum\USB (gravação garantida pelo Windows Plug-and-Play)
            try
            {
                ScanRegistryUsbEnum(ports);
            }
            catch { }
        }

        return ports
            .OrderBy(p => int.TryParse(p.Replace("COM", "", StringComparison.OrdinalIgnoreCase), out var n) ? n : 999)
            .ToList();
    }

    private static string? CleanPortName(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
            return null;

        var clean = portName.Trim().TrimEnd('\0').ToUpperInvariant();
        if (clean.StartsWith("COM") && int.TryParse(clean.AsSpan(3), out var num) && num > 0 && num <= 256)
            return clean;

        return null;
    }

    private static void QueryDosDeviceComPorts(HashSet<string> ports)
    {
        const int bufferSize = 65536;
        var buffer = Marshal.AllocHGlobal(bufferSize * sizeof(char));
        try
        {
            var charsWritten = QueryDosDevice(null, buffer, bufferSize);
            if (charsWritten <= 0)
                return;

            var sb = new StringBuilder();
            for (int offset = 0; offset < charsWritten; offset++)
            {
                char c = (char)Marshal.ReadInt16(buffer, offset * sizeof(char));
                if (c == '\0')
                {
                    if (sb.Length > 0)
                    {
                        var clean = CleanPortName(sb.ToString());
                        if (!string.IsNullOrEmpty(clean))
                            ports.Add(clean);
                        sb.Clear();
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ScanRegistrySerialComm(HashSet<string> ports)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key == null)
                return;

            foreach (var valName in key.GetValueNames())
            {
                var val = key.GetValue(valName)?.ToString();
                var clean = CleanPortName(val);
                if (!string.IsNullOrEmpty(clean))
                    ports.Add(clean);
            }
        }
        catch { }
    }

    private static bool IsDosDevicePresent(string portName)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        const int bufSize = 512;
        var buf = Marshal.AllocHGlobal(bufSize * sizeof(char));
        try
        {
            return QueryDosDevice(portName, buf, bufSize) > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ScanRegistryUsbEnum(HashSet<string> ports)
    {
        try
        {
            using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum");
            if (enumKey == null)
                return;

            foreach (var bus in enumKey.GetSubKeyNames())
            {
                // Varrer barramentos USB, FTDIBUS, ACPI, PCI e outros barramentos PnP
                if (bus.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
                    bus.Equals("FTDIBUS", StringComparison.OrdinalIgnoreCase) ||
                    bus.Equals("ACPI", StringComparison.OrdinalIgnoreCase) ||
                    bus.Equals("PCI", StringComparison.OrdinalIgnoreCase) ||
                    bus.Contains("WCH", StringComparison.OrdinalIgnoreCase))
                {
                    using var busKey = enumKey.OpenSubKey(bus);
                    if (busKey == null)
                        continue;

                    foreach (var devId in busKey.GetSubKeyNames())
                    {
                        using var devKey = busKey.OpenSubKey(devId);
                        if (devKey == null)
                            continue;

                        foreach (var inst in devKey.GetSubKeyNames())
                        {
                            using var instKey = devKey.OpenSubKey(inst);
                            if (instKey == null)
                                continue;

                            // 1. Device Parameters\PortName (método padrão de portas seriais virtuais)
                            using var paramsKey = instKey.OpenSubKey("Device Parameters");
                            var portName = paramsKey?.GetValue("PortName")?.ToString();
                            var clean = CleanPortName(portName);
                            if (!string.IsNullOrEmpty(clean) && IsDosDevicePresent(clean))
                                ports.Add(clean);

                            // 2. Extração via FriendlyName (ex.: "USB-SERIAL CH340 (COM3)", "CH341 USB to Serial (COM4)")
                            var friendly = instKey.GetValue("FriendlyName")?.ToString();
                            if (!string.IsNullOrEmpty(friendly))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(friendly, @"\((COM\d+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    var cleanF = CleanPortName(match.Groups[1].Value);
                                    if (!string.IsNullOrEmpty(cleanF) && IsDosDevicePresent(cleanF))
                                        ports.Add(cleanF);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Identifica se a porta COM especificada pertence a um adaptador com chip CH340/CH341.
    /// Utilizado para acionar a quebra de enquadramento calibrada (1200 bps) recomendada pela Cisco,
    /// já que o driver Windows do CH340 ignora o comando SetCommBreak nativo da Win32.
    /// </summary>
    public static bool IsCh340Port(string? portName)
    {
        if (string.IsNullOrWhiteSpace(portName) || !OperatingSystem.IsWindows())
            return false;

        var clean = CleanPortName(portName);
        if (string.IsNullOrEmpty(clean))
            return false;

        try
        {
            using var enumKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (enumKey == null)
                return false;

            foreach (var devId in enumKey.GetSubKeyNames())
            {
                if (!devId.Contains("VID_1A86", StringComparison.OrdinalIgnoreCase) &&
                    !devId.Contains("CH340", StringComparison.OrdinalIgnoreCase) &&
                    !devId.Contains("CH341", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var devKey = enumKey.OpenSubKey(devId);
                if (devKey == null)
                    continue;

                foreach (var inst in devKey.GetSubKeyNames())
                {
                    using var instKey = devKey.OpenSubKey(inst);
                    if (instKey == null)
                        continue;

                    using var paramsKey = instKey.OpenSubKey("Device Parameters");
                    var p = paramsKey?.GetValue("PortName")?.ToString();
                    if (string.Equals(CleanPortName(p), clean, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var friendly = instKey.GetValue("FriendlyName")?.ToString();
                    if (!string.IsNullOrEmpty(friendly) &&
                        friendly.Contains(clean, StringComparison.OrdinalIgnoreCase) &&
                        (friendly.Contains("CH340", StringComparison.OrdinalIgnoreCase) ||
                         friendly.Contains("CH341", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }
}
