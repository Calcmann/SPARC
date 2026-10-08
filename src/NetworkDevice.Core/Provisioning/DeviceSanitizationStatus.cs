namespace NetworkDevice.Core.Provisioning;

/// <summary>
/// Resultado da inspeção de higienização do dispositivo (avaliação de configuração residual).
/// Usado para determinar se um equipamento necessita de zeramento/limpeza ou
/// se já se encontra em padrão de fábrica limpo ("zero lixo"), dispensando reload desnecessário.
/// </summary>
public sealed class DeviceSanitizationStatus
{
    /// <summary>
    /// Indica se o equipamento está limpo / virgem de fábrica (sem resíduos de outros serviços).
    /// Quando true, dispensa reinicializações desnecessárias.
    /// </summary>
    public bool IsClean { get; set; } = true;

    /// <summary>
    /// Diagnóstico legível do estado de higienização do equipamento.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Hostname detectado na caixa antes do provisionamento.
    /// </summary>
    public string? DetectedHostname { get; set; }

    /// <summary>
    /// Indica se o hostname detectado é personalizado (indício de serviço/cliente anterior).
    /// </summary>
    public bool HasCustomHostname { get; set; }

    /// <summary>
    /// Indica se há rotas estáticas residuais na caixa apontando para gateways antigos.
    /// </summary>
    public bool HasStaleRoutes { get; set; }

    /// <summary>
    /// Indica se há IPs estáticos atribuídos a interfaces que não sejam o padrão de bancada.
    /// </summary>
    public bool HasConfiguredInterfaces { get; set; }

    /// <summary>
    /// Indica se há usuários locais adicionais cadastrados no equipamento além dos padrões de fábrica.
    /// </summary>
    public bool HasResidualUsers { get; set; }

    /// <summary>
    /// Linhas ou detalhes específicos detectados como lixo/resíduos.
    /// </summary>
    public List<string> ResidualFindings { get; } = new();

    public static DeviceSanitizationStatus Clean(string summary = "Equipamento em padrão de fábrica (zero lixo detectado).") =>
        new() { IsClean = true, Summary = summary };

    public static DeviceSanitizationStatus Stale(string summary, IEnumerable<string>? findings = null)
    {
        var st = new DeviceSanitizationStatus { IsClean = false, Summary = summary };
        if (findings != null)
            st.ResidualFindings.AddRange(findings);
        return st;
    }
}
