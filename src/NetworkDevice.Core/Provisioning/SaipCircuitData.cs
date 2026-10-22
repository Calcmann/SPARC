namespace NetworkDevice.Core.Provisioning;

public enum SaipServiceType
{
    BusinessLinkDirect, // BLD (Internet Simétrica - Padrão)
    IpVpn,              // IP VPN (MPLS Corporativo)
    VozDigitalE1,       // VOZ Tronco Digital (R2D / ISDN em E1 no roteador)
    VozSipIp,           // VOZ Tronco SIP / PABX IP (com WAN e LAN padrão)
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

    // VOZ e Telefonia Corporativa (Tronco SIP / Tronco Digital E1)
    public string? Sinalizacao { get; init; }       // Ex.: PABX IP, PABX TDM.R2D, ISDN
    public string? IpPabx { get; init; }             // Ex.: 189.042.012.018 ou 200.247.251.170
    public string? NomeSbc { get; init; }            // Ex.: hsbc01/02.fnspo ou rsbca1/a2.ctamr
    public string? Softswitch { get; init; }         // Ex.: SPO MGC IG2
    public string? IpSipAcesso { get; init; }        // Ex.: 189.52.73.100 ou 200.209.124.213
    public string? IpSoftswitch { get; init; }       // Ex.: 10.252.82.7
    public string? IpSipSoftswitch { get; init; }    // Ex.: 10.5.33.233
    public string? FaixaRamais { get; init; }        // Ex.: 4832032150
    public string? NumeroChave { get; init; }        // Ex.: 4832032150
    public string? Cnl { get; init; }                // Ex.: FNS

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
    public string? TipoAcesso { get; init; }          // Ex.: FO EDD, GPON

    // Imagem de boot (opcional / override)
    public string? BootImage { get; init; }

    public string RawSource { get; init; } = string.Empty;

    /// <summary>
    /// Descritivo amigável da categoria de serviço.
    /// </summary>
    public string TipoServicoDescricao => TipoServico switch
    {
        SaipServiceType.BusinessLinkDirect => "BLD (Business Link Direct - Internet)",
        SaipServiceType.IpVpn => "MPLS (IP VPN Corporativa)",
        SaipServiceType.VozDigitalE1 => $"VOZ Tronco Digital E1 ({Sinalizacao ?? "R2D/ISDN"})",
        SaipServiceType.VozSipIp => $"VOZ Tronco SIP / PABX IP ({Sinalizacao ?? "SIP"})",
        _ => "Outro Serviço"
    };

    /// <summary>
    /// Obtém o melhor IP de destino de voz (Media Gateway / Softswitch / SBC / PABX Claro ou do Cliente).
    /// </summary>
    public string? MediaGatewayIp =>
        !string.IsNullOrWhiteSpace(IpSoftswitch) ? IpSoftswitch :
        !string.IsNullOrWhiteSpace(IpSipSoftswitch) ? IpSipSoftswitch :
        !string.IsNullOrWhiteSpace(IpSipAcesso) ? IpSipAcesso :
        !string.IsNullOrWhiteSpace(IpPabx) ? IpPabx :
        WanGateway;

    /// <summary>
    /// Verifica se o serviço é de telefonia/voz corporativa (Tronco Digital E1 ou Tronco SIP).
    /// </summary>
    public bool IsServicoVoz =>
        TipoServico == SaipServiceType.VozDigitalE1 || TipoServico == SaipServiceType.VozSipIp;

    /// <summary>
    /// Verifica se o serviço é de rede privada corporativa MPLS (IP VPN).
    /// </summary>
    public bool IsServicoMpls =>
        TipoServico == SaipServiceType.IpVpn;

    /// <summary>
    /// Verifica se o serviço é de Internet pública (BLD).
    /// </summary>
    public bool IsServicoInternet =>
        TipoServico == SaipServiceType.BusinessLinkDirect;

    /// <summary>
    /// Indica se o circuito é uma rede privada/fechada (MPLS, Voz E1 ou Tronco SIP) sem tráfego direto para Internet pública.
    /// Nestes formatos, a validação de banda é efetuada via Certidão de Nascimento (ITU-T Y.1564) e o ping 5c não busca DNS Web público.
    /// </summary>
    public bool IsCircuitoPrivadoSemInternet =>
        TipoServico == SaipServiceType.IpVpn || TipoServico == SaipServiceType.VozDigitalE1 || TipoServico == SaipServiceType.VozSipIp;
}
