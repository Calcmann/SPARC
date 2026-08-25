namespace NetworkDevice.Core.Provisioning;

public sealed record SaipCircuitData
{
    public string? ClienteRazaoSocial { get; init; }
    public string? DesignacaoIp { get; init; }
    public string? NumeroOts { get; init; }
    public string? DescriptionRoteador { get; init; }

    // WAN (Porta Giga 5)
    public string WanIp { get; init; } = string.Empty;
    public int WanCidr { get; init; } = 30;
    public string WanSubnetMask { get; init; } = "255.255.255.252";
    public string WanGateway { get; init; } = string.Empty;

    // LAN (Porta Giga 4)
    public string LanBlockNetwork { get; init; } = string.Empty;
    public int LanCidr { get; init; } = 29;
    public string LanIp { get; init; } = string.Empty;
    public string LanSubnetMask { get; init; } = "255.255.255.248";
    public string HostLanIp { get; init; } = string.Empty;

    // Informações de Acesso / Roteamento
    public string? PeRouter { get; init; }
    public string? VlanCliente { get; init; }

    public string RawSource { get; init; } = string.Empty;
}
