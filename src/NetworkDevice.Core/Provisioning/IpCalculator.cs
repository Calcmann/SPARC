using System.Net;

namespace NetworkDevice.Core.Provisioning;

public static class IpCalculator
{
    /// <summary>
    /// Normaliza um endereço IPv4 removendo zeros à esquerda nos octetos (ex: 201.030.010.018 -> 201.30.10.18).
    /// </summary>
    public static string NormalizeIp(string ipString)
    {
        if (string.IsNullOrWhiteSpace(ipString))
            return string.Empty;

        var parts = ipString.Trim().Split('.');
        if (parts.Length != 4)
            return ipString.Trim();

        var normalized = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (int.TryParse(parts[i], out var val))
                normalized[i] = val;
            else
                return ipString.Trim();
        }

        return $"{normalized[0]}.{normalized[1]}.{normalized[2]}.{normalized[3]}";
    }

    /// <summary>
    /// Converte prefixo CIDR (ex: 30) em máscara decimal pontilhada (ex: 255.255.255.252).
    /// </summary>
    public static string CidrToSubnetMask(int cidr)
    {
        if (cidr < 0 || cidr > 32)
            throw new ArgumentOutOfRangeException(nameof(cidr), "CIDR deve estar entre 0 e 32.");

        uint mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
        byte[] bytes = BitConverter.GetBytes(mask);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{bytes[3]}";
    }

    /// <summary>
    /// Calcula o primeiro IP útil de um bloco (ex: 189.16.20.80/29 -> 189.16.20.81).
    /// </summary>
    public static string CalculateFirstUsableIp(string networkIp, int cidr)
    {
        var normalized = NormalizeIp(networkIp);
        if (!IPAddress.TryParse(normalized, out var ip))
            return normalized;

        var bytes = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        uint ipNum = BitConverter.ToUInt32(bytes, 0);
        uint mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
        uint networkNum = ipNum & mask;
        uint firstUsable = networkNum + 1;

        byte[] resBytes = BitConverter.GetBytes(firstUsable);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(resBytes);

        return $"{resBytes[0]}.{resBytes[1]}.{resBytes[2]}.{resBytes[3]}";
    }

    /// <summary>
    /// Calcula o gateway WAN para sub-redes /30:
    /// Se o IP do usuário for o segundo IP útil (ex: .18), o gateway é o primeiro (.17).
    /// Se o IP do usuário for o primeiro útil (ex: .17), o gateway é o segundo (.18).
    /// </summary>
    public static string CalculateWanGateway(string userWanIp, int cidr)
    {
        var normalized = NormalizeIp(userWanIp);
        if (cidr != 30 || !IPAddress.TryParse(normalized, out var ip))
        {
            // Para outros prefixos, assume primeiro IP da sub-rede como gateway
            return CalculateFirstUsableIp(userWanIp, cidr);
        }

        var bytes = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        uint ipNum = BitConverter.ToUInt32(bytes, 0);
        uint networkNum = ipNum & 0xFFFFFFFC; // máscara /30
        uint firstUsable = networkNum + 1;
        uint secondUsable = networkNum + 2;

        // Se o IP do usuário for o segundo (.18), o gateway do roteador de borda (PE) é o primeiro (.17)
        uint gatewayNum = (ipNum == secondUsable) ? firstUsable : secondUsable;

        byte[] resBytes = BitConverter.GetBytes(gatewayNum);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(resBytes);

        return $"{resBytes[0]}.{resBytes[1]}.{resBytes[2]}.{resBytes[3]}";
    }

    /// <summary>
    /// Calcula o IP do Host PC a ser configurado na mesma rede LAN (segundo IP útil).
    /// </summary>
    public static string CalculateHostLanIp(string networkIp, int cidr)
    {
        var normalized = NormalizeIp(networkIp);
        if (!IPAddress.TryParse(normalized, out var ip))
            return "192.168.1.2";

        var bytes = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        uint ipNum = BitConverter.ToUInt32(bytes, 0);
        uint mask = cidr == 0 ? 0 : uint.MaxValue << (32 - cidr);
        uint networkNum = ipNum & mask;
        uint hostIpNum = networkNum + 2; // Segundo IP útil

        byte[] resBytes = BitConverter.GetBytes(hostIpNum);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(resBytes);

        return $"{resBytes[0]}.{resBytes[1]}.{resBytes[2]}.{resBytes[3]}";
    }
}
