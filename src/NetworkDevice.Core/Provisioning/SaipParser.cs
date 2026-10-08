using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace NetworkDevice.Core.Provisioning;

public static class SaipParser
{
    private static readonly Regex RegexWanIp = new(
        @"(?i)IP\s*Serial\s*(?:Usu[áa]rio)?\s*(?:\(\s*IPv4\s*\))?\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})\s*/\s*(\d{1,2})",
        RegexOptions.Compiled);

    private static readonly Regex RegexWanIpv6 = new(
        @"(?i)IP\s*Serial\s*(?:Usu[áa]rio)?\s*(?:\(\s*IPv6\s*\))\s*[:\t]?\s*([0-9a-fA-F:]+)\s*/\s*(\d{1,3})",
        RegexOptions.Compiled);

    private static readonly Regex RegexLanBlock = new(
        @"(?i)Blocos?\s*IPv4\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})\s*/\s*(\d{1,2})",
        RegexOptions.Compiled);

    private static readonly Regex RegexLanIpv6Block = new(
        @"(?i)Blocos?\s*IPv6\s*[:\t]?\s*([0-9a-fA-F:]+)\s*/\s*(\d{1,3})",
        RegexOptions.Compiled);

    private static readonly Regex RegexProduto = new(
        @"(?i)Produto\s*[:\t]?\s*([^\r\n\t]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexRazaoSocial = new(
        @"(?i)Raz[ãa]o\s*Social\s*[:\t]?\s*([^\r\n\t]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexDesignacaoIp = new(
        @"(?i)Designa[çc][ãa]o\s*IP\s*[:\t]?\s*([A-Za-z0-9_/\-]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexNumeroOts = new(
        @"(?i)N[úu]mero\s*Ots\s*[:\t]?\s*([A-Za-z0-9_\-\/]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexDescriptionRoteador = new(
        @"(?i)Description\s*Roteador\s*[:\t]?\s*([^\r\n\t]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexPeRouter = new(
        @"(?i)Roteador\s*[:\t]?\s*([A-Za-z0-9_\-\.]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexPeLoopback = new(
        @"(?i)(?:IP\s*)?Loopback(?:\s*PE)?\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})",
        RegexOptions.Compiled);

    private static readonly Regex RegexBandaKbps = new(
        @"(?im)^\s*Banda\s*[:\t]?\s*([0-9]+(?:[.,][0-9]+)?)",
        RegexOptions.Compiled);

    /// <summary>
    /// Carrega e extrai os dados de uma Ficha SAIP a partir de um arquivo .txt ou .pdf.
    /// </summary>
    public static async Task<SaipCircuitData> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException($"Arquivo de Ficha SAIP não encontrado: {filePath}");

        var text = await CarregarTextoAsync(filePath, cancellationToken);
        return ParseText(text);
    }

    public static async Task<string> CarregarTextoAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".pdf")
        {
            return await Task.Run(() => ExtractTextFromPdf(filePath), cancellationToken);
        }
        return await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
    }

    /// <summary>Valida se a ficha contém os IPs obrigatórios para provisionamento.</summary>
    public static (bool ok, string motivo) Validar(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (false, "Arquivo vazio ou ilegível.");

        var prodMatch = RegexProduto.Match(text);
        var isIpVpn = prodMatch.Success && (
            prodMatch.Groups[1].Value.Contains("IP VPN", StringComparison.OrdinalIgnoreCase) ||
            prodMatch.Groups[1].Value.Contains("IPVPN", StringComparison.OrdinalIgnoreCase) ||
            prodMatch.Groups[1].Value.Contains("MPLS", StringComparison.OrdinalIgnoreCase));

        var hasWan = RegexWanIp.IsMatch(text);
        var hasLan = RegexLanBlock.IsMatch(text);

        // Quando for IP VPN (MPLS), não temos o IP de LAN na ficha SAIP (arbitraremos LAN genérica)
        if (isIpVpn)
        {
            if (!hasWan) return (false, "Ficha IP VPN (MPLS) não contém IP Serial (WAN) - campo 'IP Serial Usuário (IPv4) X.X.X.X/YY' não encontrado.");
            return (true, string.Empty);
        }

        // Para Business Link Direct (BLD) ou padrão, WAN e LAN são obrigatórios
        if (!hasWan && !hasLan) return (false, "Ficha não contém Bloco IPv4 (LAN) nem IP Serial (WAN). Formato inválido.");
        if (!hasWan) return (false, "Ficha não contém IP Serial (WAN) - campo 'IP Serial Usuário (IPv4) X.X.X.X/YY' não encontrado.");
        if (!hasLan) return (false, "Ficha não contém Bloco IPv4 (LAN) - campo 'Blocos IPv4: X.X.X.X/YY' não encontrado.");
        return (true, string.Empty);
    }

    /// <summary>
    /// Interpreta o texto bruto de uma Ficha SAIP e retorna o modelo estruturado.
    /// </summary>
    public static SaipCircuitData ParseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new SaipCircuitData();

        // 0. Detecção de Produto / Tipo de Serviço
        var prodMatch = RegexProduto.Match(text);
        string? produto = prodMatch.Success ? CleanField(prodMatch.Groups[1].Value) : null;
        bool isIpVpn = produto != null && (
            produto.Contains("IP VPN", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("IPVPN", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("MPLS", StringComparison.OrdinalIgnoreCase));
        var tipoServico = isIpVpn ? SaipServiceType.IpVpn : SaipServiceType.BusinessLinkDirect;

        // 1. WAN (IP Serial Usuário IPv4)
        var wanMatch = RegexWanIp.Match(text);
        string wanIp = string.Empty;
        int wanCidr = 30;
        string wanMask = "255.255.255.252";
        string wanGateway = string.Empty;

        if (wanMatch.Success)
        {
            wanIp = IpCalculator.NormalizeIp(wanMatch.Groups[1].Value);
            wanCidr = int.Parse(wanMatch.Groups[2].Value);
            wanMask = IpCalculator.CidrToSubnetMask(wanCidr);
            wanGateway = IpCalculator.CalculateWanGateway(wanIp, wanCidr);
        }

        // 1b. WAN IPv6
        string? wanIpv6 = null;
        int? wanIpv6Prefix = null;
        string? wanIpv6Gateway = null;
        var wanIpv6Match = RegexWanIpv6.Match(text);
        if (wanIpv6Match.Success)
        {
            wanIpv6 = wanIpv6Match.Groups[1].Value.Trim();
            wanIpv6Prefix = int.Parse(wanIpv6Match.Groups[2].Value);
            wanIpv6Gateway = IpCalculator.CalculateWanIpv6Gateway(wanIpv6, wanIpv6Prefix.Value);
        }

        // 2. LAN (Blocos IPv4)
        var lanMatch = RegexLanBlock.Match(text);
        string lanBlock = string.Empty;
        int lanCidr = 29;
        string lanIp = string.Empty;
        string lanMask = "255.255.255.248";
        string hostLanIp;
        bool isLanArbitrada = false;

        if (lanMatch.Success && !isIpVpn)
        {
            lanBlock = IpCalculator.NormalizeIp(lanMatch.Groups[1].Value);
            lanCidr = int.Parse(lanMatch.Groups[2].Value);
            lanMask = IpCalculator.CidrToSubnetMask(lanCidr);
            lanIp = IpCalculator.CalculateFirstUsableIp(lanBlock, lanCidr);
            hostLanIp = IpCalculator.CalculateHostLanIp(lanBlock, lanCidr);
        }
        else if (lanMatch.Success && isIpVpn)
        {
            // Se a ficha IP VPN trouxer bloco LAN explicitamente, utiliza
            lanBlock = IpCalculator.NormalizeIp(lanMatch.Groups[1].Value);
            lanCidr = int.Parse(lanMatch.Groups[2].Value);
            lanMask = IpCalculator.CidrToSubnetMask(lanCidr);
            lanIp = IpCalculator.CalculateFirstUsableIp(lanBlock, lanCidr);
            hostLanIp = IpCalculator.CalculateHostLanIp(lanBlock, lanCidr);
        }
        else
        {
            // IP VPN (MPLS) sem LAN na ficha SAIP (ou fallback):
            // Arbitra rede LAN genérica (192.168.1.1/24) para viabilizar conexão LAN e testes locais,
            // permitindo que a equipe centralizada (GER CPE) acesse remotamente e conclua a aplicação do script.
            lanBlock = "192.168.1.0";
            lanCidr = 24;
            lanMask = "255.255.255.0";
            lanIp = "192.168.1.1";
            hostLanIp = "192.168.1.2";
            isLanArbitrada = true;
        }

        // 2b. LAN IPv6
        string? lanIpv6Block = null;
        int? lanIpv6Prefix = null;
        string? lanIpv6 = null;
        var lanIpv6Match = RegexLanIpv6Block.Match(text);
        if (lanIpv6Match.Success && !isLanArbitrada)
        {
            lanIpv6Block = lanIpv6Match.Groups[1].Value.Trim();
            lanIpv6Prefix = int.Parse(lanIpv6Match.Groups[2].Value);
            lanIpv6 = IpCalculator.CalculateFirstUsableIpv6(lanIpv6Block, lanIpv6Prefix.Value);
        }

        // 3. Metadados do Cliente e Circuito
        var razaoSocial = CleanRazaoSocial(RegexRazaoSocial.Match(text).Groups[1].Value);
        var designacaoIp = CleanField(RegexDesignacaoIp.Match(text).Groups[1].Value);
        var numeroOts = CleanField(RegexNumeroOts.Match(text).Groups[1].Value);
        var descRoteador = CleanField(RegexDescriptionRoteador.Match(text).Groups[1].Value);
        var peRouter = CleanField(RegexPeRouter.Match(text).Groups[1].Value);

        string? peLoopback = null;
        var peLoopbackMatch = RegexPeLoopback.Match(text);
        if (peLoopbackMatch.Success)
        {
            peLoopback = IpCalculator.NormalizeIp(peLoopbackMatch.Groups[1].Value);
        }

        // 4. Banda nominal da ficha (kbps -> Mbps e Bps). Ex: "Banda 50000" => 50 Mbps, 50.000.000 bps.
        long? bandaKbps = null;
        long? bandaBps = null;
        double? bandaMbps = null;
        var bandaMatch = RegexBandaKbps.Match(text);
        if (bandaMatch.Success &&
            double.TryParse(bandaMatch.Groups[1].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var kbps) && kbps > 0)
        {
            bandaKbps = (long)Math.Round(kbps);
            bandaBps = bandaKbps * 1000L;
            bandaMbps = Math.Round(kbps / 1000.0, 2);
        }

        return new SaipCircuitData
        {
            Produto = produto,
            TipoServico = tipoServico,
            IsLanArbitrada = isLanArbitrada,
            ClienteRazaoSocial = razaoSocial,
            DesignacaoIp = designacaoIp,
            NumeroOts = numeroOts,
            DescriptionRoteador = descRoteador,
            PeRouter = peRouter,
            PeLoopbackIp = peLoopback,
            WanIp = wanIp,
            WanCidr = wanCidr,
            WanSubnetMask = wanMask,
            WanGateway = wanGateway,
            WanIpv6 = wanIpv6,
            WanIpv6Prefix = wanIpv6Prefix,
            WanIpv6Gateway = wanIpv6Gateway,
            LanBlockNetwork = lanBlock,
            LanCidr = lanCidr,
            LanIp = lanIp,
            LanSubnetMask = lanMask,
            HostLanIp = hostLanIp,
            LanIpv6Block = lanIpv6Block,
            LanIpv6Prefix = lanIpv6Prefix,
            LanIpv6 = lanIpv6,
            BandaKbps = bandaKbps,
            BandaBps = bandaBps,
            BandaMbpsNominal = bandaMbps,
            RawSource = text
        };
    }

    private static string ExtractTextFromPdf(string pdfPath)
    {
        var sb = new StringBuilder();
        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }
        return sb.ToString();
    }

    private static string? CleanField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim().Trim(':', '\t', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    public static string? CleanRazaoSocial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = CleanField(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        // Corta de "CONTA CORRENTE" em diante e outros metadados adjacentes da ficha SAIP
        var cutPatterns = new[]
        {
            @"(?i)\s*CONTA[\s\-_]*CORRENTE.*",
            @"(?i)\s*CONTACORRENTE.*",
            @"(?i)\s*CONTA\s*\d+.*",
            @"(?i)\s*CNPJ.*",
            @"(?i)\s*\(GC/CS\).*",
            @"(?i)\s*ADMINISTRADOR.*",
            @"(?i)\s*TELEFONE.*",
            @"(?i)\s*EMAIL.*",
            @"(?i)\s*DESIGNA[ÇC][ÃA]O.*",
            @"(?i)\s*ESTA[ÇC][ÃA]O.*",
            @"(?i)\s*N[ÚU]MERO\s*OTS.*"
        };

        foreach (var pattern in cutPatterns)
        {
            cleaned = Regex.Replace(cleaned, pattern, string.Empty);
        }

        cleaned = cleaned.Trim().TrimEnd('-', ',', ';', ':', '/', '\\', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
