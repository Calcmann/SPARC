namespace NetworkDevice.Core.Provisioning;

public interface IHostNetworkService
{
    /// <summary>
    /// Lista os adaptadores de rede disponíveis no sistema operacional atual.
    /// </summary>
    IReadOnlyList<string> GetAvailableAdapters();

    /// <summary>
    /// Configura endereço IP estático no adaptador de rede selecionado.
    /// </summary>
    Task<(bool success, string output)> SetStaticIpAsync(
        string adapterName,
        string ipAddress,
        string subnetMask,
        string? gateway = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restaura a interface para DHCP automático se suportado.
    /// </summary>
    Task<(bool success, string output)> SetDhcpAsync(
        string adapterName,
        CancellationToken cancellationToken = default);
}
