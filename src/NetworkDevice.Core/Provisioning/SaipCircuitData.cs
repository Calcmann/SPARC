namespace NetworkDevice.Core.Provisioning;

public enum SaipServiceType
{
    BusinessLinkDirect, // BLD (Internet Simétrica)
    IpVpn,              // IP VPN (MPLS)
    Outro
}

public sealed record SaipCircuitData
{
    // Serviço / Produto da Ficha SAIP
    public string? Produto { get; init; }
    public SaipServiceType TipoServico { get; init; } = SaipServiceType.BusinessLinkDirect;
    public bool IsLanArbitrada { get; init; }

    public string? ClienteRazaoSocial { get; init; }
    public string? DesignacaoIp { get; init; }
    public string? NumeroOts { get; init; }
    public string? DescriptionRoteador { get; init; }

    // WAN (Porta Giga 5 / GE4 / GE0/0)
    public string WanIp { get; init; } = string.Empty;
    public int WanCidr { get; init; } = 30;
    public string WanSubnetMask { get; init; } = "255.255.255.252";
    public string WanGateway { get; init; } = string.Empty;

    // IPv6 WAN
    public string? WanIpv6 { get; init; }
    public int? WanIpv6Prefix { get; init; }
    public string? WanIpv6Gateway { get; init; }

    // LAN (Porta Giga 4 / GE5 / GE0/1)
    public string LanBlockNetwork { get; init; } = string.Empty;
    public int LanCidr { get; init; } = 29;
    public string LanIp { get; init; } = string.Empty;
    public string LanSubnetMask { get; init; } = "255.255.255.248";
    public string HostLanIp { get; init; } = string.Empty;

    // IPv6 LAN
    public string? LanIpv6Block { get; init; }
    public int? LanIpv6Prefix { get; init; }
    public string? LanIpv6 { get; init; }

    // Banda da ficha SAIP
    public long? BandaKbps { get; init; }
    public long? BandaBps { get; init; }
    // Banda nominal em Mbps. Ex: ficha "Banda 50000" (kbps) => 50 Mbps.
    public double? BandaMbpsNominal { get; init; }

    // Informações de Acesso / Roteamento
    public string? PeRouter { get; init; }
    public string? PeLoopbackIp { get; init; }
    public string? PeIpv6Loopback { get; init; }
    public string? VlanCliente { get; init; }

    // Imagem de boot (opcional / override)
    public string? BootImage { get; init; }

    public string RawSource { get; init; } = string.Empty;
}
