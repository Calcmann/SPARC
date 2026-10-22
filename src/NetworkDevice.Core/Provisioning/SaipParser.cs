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
        @"(?i)(?:^|\b|\s)Banda\s*[:\t]?\s*([0-9]+(?:[.,][0-9]+)?)",
        RegexOptions.Compiled);

    private static readonly Regex RegexVlanCliente = new(
        @"(?i)Vlan\s*Cliente\s*[:\t]?\s*(\d+)",
        RegexOptions.Compiled);

    // Expressões regulares para Serviços de Voz (Tronco SIP / Tronco Digital E1)
    private static readonly Regex RegexSinalizacao = new(
        @"(?i)Sinaliza[çc][ãa]o\s*[:\t]?\s*([^\r\n\t]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexIpPabx = new(
        @"(?i)IP\s*PABX\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})",
        RegexOptions.Compiled);

    private static readonly Regex RegexNomeSbc = new(
        @"(?i)Nome\s*do\s*SBC\s*[:\t]?\s*([^\r\n\t]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexSoftswitch = new(
        @"(?i)(?:^|\b|\s)Softswitch\s*[:\t]?\s*([^\r\n\t]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexIpSipAcesso = new(
        @"(?i)IP\s*SIP\s*Acesso\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})",
        RegexOptions.Compiled);

    private static readonly Regex RegexIpSoftswitch = new(
        @"(?i)IP\s*Softswitch\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})",
        RegexOptions.Compiled);

    private static readonly Regex RegexIpSipSoftswitch = new(
        @"(?i)IP\s*SIP\s*Softswitch\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})",
        RegexOptions.Compiled);

    private static readonly Regex RegexFaixaRamais = new(
        @"(?i)Faixa\s*de\s*Ramais\s*[:\t]?\s*([^\r\n\t]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexNumeroChave = new(
        @"(?i)N[úu]mero\s*Chave\s*[:\t]?\s*([0-9]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexCnl = new(
        @"(?i)CNL\s*[:\t]?\s*([A-Za-z0-9]+)",
        RegexOptions.Compiled);

    private static readonly Regex RegexTipoAcesso = new(
        @"(?i)Tipo\s*de\s*Acesso\s*[:\t]?\s*([^\r\n\t]+)",
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

    /// <summary>
    /// Detecta automaticamente a categoria/tipo de serviço (BLD, MPLS, VOZ E1 ou VOZ SIP)
    /// analisando produto, sinalização, tipo de acesso e assinaturas estruturais no texto da Ficha SAIP.
    /// </summary>
    public static SaipServiceType DetectarTipoServico(string text, string? produto = null, string? sinalizacao = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return SaipServiceType.BusinessLinkDirect;

        produto ??= RegexProduto.Match(text).Groups[1].Value;
        sinalizacao ??= RegexSinalizacao.Match(text).Groups[1].Value;
        var tipoAcesso = RegexTipoAcesso.Match(text).Groups[1].Value;

        // 1. Verificação por campos explícitos de produto, sinalização e tipo de acesso
        bool hasExplicitProduto = !string.IsNullOrWhiteSpace(produto);

        bool isBld = hasExplicitProduto && (
            produto!.Contains("Business Link Direct", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("BLD", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("Internet", StringComparison.OrdinalIgnoreCase));

        bool isIpVpn = (hasExplicitProduto && (
            produto!.Contains("IP VPN", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("IPVPN", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("MPLS", StringComparison.OrdinalIgnoreCase))) ||
            (!string.IsNullOrEmpty(tipoAcesso) && (
            tipoAcesso.Contains("IP VPN", StringComparison.OrdinalIgnoreCase) ||
            tipoAcesso.Contains("MPLS", StringComparison.OrdinalIgnoreCase)));

        bool isVozDigital = !string.IsNullOrEmpty(sinalizacao) && (
            sinalizacao.Contains("R2D", StringComparison.OrdinalIgnoreCase) ||
            sinalizacao.Contains("ISDN", StringComparison.OrdinalIgnoreCase) ||
            sinalizacao.Contains("TDM", StringComparison.OrdinalIgnoreCase) ||
            sinalizacao.Contains("CAS", StringComparison.OrdinalIgnoreCase));

        if (!isVozDigital && hasExplicitProduto && (
            produto!.Contains("TRONCO DIGITAL", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("DIGITAL E1", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("E1 R2D", StringComparison.OrdinalIgnoreCase) ||
            produto.Contains("E1 ISDN", StringComparison.OrdinalIgnoreCase)))
        {
            isVozDigital = true;
        }

        bool isVozSip = !isVozDigital && (
            (!string.IsNullOrEmpty(sinalizacao) && (
                sinalizacao.Contains("SIP", StringComparison.OrdinalIgnoreCase) ||
                sinalizacao.Contains("PABX IP", StringComparison.OrdinalIgnoreCase))) ||
            (hasExplicitProduto && (
                produto!.Contains("SIP", StringComparison.OrdinalIgnoreCase) ||
                produto.Contains("TRONCO SIP", StringComparison.OrdinalIgnoreCase) ||
                produto.Contains("PABX IP", StringComparison.OrdinalIgnoreCase))));

        if (isBld && !isVozDigital && !isVozSip) return SaipServiceType.BusinessLinkDirect;
        if (isIpVpn) return SaipServiceType.IpVpn;
        if (isVozDigital) return SaipServiceType.VozDigitalE1;
        if (isVozSip) return SaipServiceType.VozSipIp;

        // Se o campo Produto foi especificado de forma explícita e não é VPN/Voz, é BLD
        if (hasExplicitProduto)
            return SaipServiceType.BusinessLinkDirect;

        // 2. Verificação por assinaturas estruturais de Voz no corpo do documento (quando Produto ausente)
        bool temAssinaturaVoz = RegexIpSoftswitch.IsMatch(text) ||
                                RegexIpSipSoftswitch.IsMatch(text) ||
                                RegexIpSipAcesso.IsMatch(text) ||
                                RegexIpPabx.IsMatch(text) ||
                                RegexNomeSbc.IsMatch(text) ||
                                RegexSoftswitch.IsMatch(text) ||
                                RegexFaixaRamais.IsMatch(text);

        if (temAssinaturaVoz)
        {
            if (text.Contains("R2D", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("ISDN", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("TDM", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("TRONCO DIGITAL", StringComparison.OrdinalIgnoreCase))
            {
                return SaipServiceType.VozDigitalE1;
            }
            return SaipServiceType.VozSipIp;
        }

        // 3. Verificação por palavras-chave no corpo do texto (somente quando Produto ausente)
        if (text.Contains("IP VPN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("IPVPN", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("IP-VPN", StringComparison.OrdinalIgnoreCase))
        {
            return SaipServiceType.IpVpn;
        }

        if (text.Contains("TRONCO DIGITAL", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("PABX TDM", StringComparison.OrdinalIgnoreCase))
        {
            return SaipServiceType.VozDigitalE1;
        }

        if (text.Contains("TRONCO SIP", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("VIP SIP", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("PABX IP", StringComparison.OrdinalIgnoreCase))
        {
            return SaipServiceType.VozSipIp;
        }

        return SaipServiceType.BusinessLinkDirect;
    }

    /// <summary>Valida se a ficha contém os IPs obrigatórios para provisionamento.</summary>
    public static (bool ok, string motivo) Validar(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (false, "Arquivo vazio ou ilegível.");

        var tipoServico = DetectarTipoServico(text);
        var isIpVpn = tipoServico == SaipServiceType.IpVpn;
        var isVozDigital = tipoServico == SaipServiceType.VozDigitalE1;
        var isVozSip = tipoServico == SaipServiceType.VozSipIp;

        var hasWan = RegexWanIp.IsMatch(text);
        var hasLan = RegexLanBlock.IsMatch(text) || RegexIpPabx.IsMatch(text);

        // Quando for IP VPN (MPLS) ou Tronco Digital E1 (R2D/ISDN), não temos o IP de LAN na ficha SAIP (arbitraremos LAN genérica 192.168.1.1/24)
        if (isIpVpn || isVozDigital)
        {
            if (!hasWan) return (false, $"Ficha {(isIpVpn ? "IP VPN (MPLS)" : "Tronco Digital (R2D/ISDN)")} não contém IP Serial (WAN) - campo 'IP Serial Usuário (IPv4) X.X.X.X/YY' não encontrado.");
            return (true, string.Empty);
        }

        // Para Tronco SIP / PABX IP ou BLD, WAN é obrigatório
        if (!hasWan) return (false, "Ficha não contém IP Serial (WAN) - campo 'IP Serial Usuário (IPv4) X.X.X.X/YY' não encontrado.");
        if (!hasLan && !isVozSip) return (false, "Ficha não contém Bloco IPv4 (LAN) - campo 'Blocos IPv4: X.X.X.X/YY' não encontrado.");
        return (true, string.Empty);
    }

    /// <summary>
    /// Interpreta o texto bruto de uma Ficha SAIP e retorna o modelo estruturado.
    /// </summary>
    public static SaipCircuitData ParseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new SaipCircuitData();

        // 0. Detecção de Produto / Sinalização / Tipo de Serviço
        var prodMatch = RegexProduto.Match(text);
        string? produto = prodMatch.Success ? CleanProduto(prodMatch.Groups[1].Value) : null;
        var sinalizacaoMatch = RegexSinalizacao.Match(text);
        string? sinalizacao = sinalizacaoMatch.Success ? CleanField(sinalizacaoMatch.Groups[1].Value) : null;

        var tipoServico = DetectarTipoServico(text, produto, sinalizacao);

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

        // 2. LAN (Blocos IPv4 ou fallback arbitrado)
        var lanMatch = RegexLanBlock.Match(text);
        var ipPabxMatch = RegexIpPabx.Match(text);
        string? ipPabx = ipPabxMatch.Success ? IpCalculator.NormalizeIp(ipPabxMatch.Groups[1].Value) : null;

        string lanBlock = string.Empty;
        int lanCidr = 29;
        string lanIp = string.Empty;
        string lanMask = "255.255.255.248";
        string hostLanIp;
        bool isLanArbitrada = false;

        if (lanMatch.Success)
        {
            lanBlock = IpCalculator.NormalizeIp(lanMatch.Groups[1].Value);
            lanCidr = int.Parse(lanMatch.Groups[2].Value);
            lanMask = IpCalculator.CidrToSubnetMask(lanCidr);
            lanIp = IpCalculator.CalculateFirstUsableIp(lanBlock, lanCidr);
            hostLanIp = IpCalculator.CalculateHostLanIp(lanBlock, lanCidr);
        }
        else if (tipoServico == SaipServiceType.VozSipIp && !string.IsNullOrWhiteSpace(ipPabx))
        {
            // Tronco SIP sem bloco LAN explícito, mas com IP PABX informado
            lanIp = ipPabx;
            lanCidr = 30;
            lanMask = "255.255.255.252";
            lanBlock = ipPabx;
            hostLanIp = IpCalculator.CalculateWanGateway(ipPabx, 30);
            if (hostLanIp == ipPabx) hostLanIp = "192.168.1.2";
        }
        else
        {
            // IP VPN (MPLS) ou Tronco Digital E1 (R2D/ISDN) sem LAN na ficha SAIP (ou fallback):
            // Arbitra rede LAN temporária (192.168.1.1/24) para viabilizar conexão e atualização do equipamento.
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

        // 3. Metadados do Cliente, Circuito e Voz
        var razaoSocial = CleanRazaoSocial(RegexRazaoSocial.Match(text).Groups[1].Value);
        var designacaoIp = CleanField(RegexDesignacaoIp.Match(text).Groups[1].Value);
        var numeroOts = CleanField(RegexNumeroOts.Match(text).Groups[1].Value);
        var descRoteador = CleanField(RegexDescriptionRoteador.Match(text).Groups[1].Value);
        var peRouter = CleanField(RegexPeRouter.Match(text).Groups[1].Value);
        var tipoAcesso = CleanField(RegexTipoAcesso.Match(text).Groups[1].Value);
        var nomeSbc = CleanField(RegexNomeSbc.Match(text).Groups[1].Value);
        var softswitch = CleanField(RegexSoftswitch.Match(text).Groups[1].Value);
        var faixaRamais = CleanField(RegexFaixaRamais.Match(text).Groups[1].Value);
        var numeroChave = CleanField(RegexNumeroChave.Match(text).Groups[1].Value);
        var cnl = CleanField(RegexCnl.Match(text).Groups[1].Value);

        string? ipSipAcesso = null;
        var ipSipMatch = RegexIpSipAcesso.Match(text);
        if (ipSipMatch.Success) ipSipAcesso = IpCalculator.NormalizeIp(ipSipMatch.Groups[1].Value);

        string? ipSoftswitch = null;
        var ipSoftMatch = RegexIpSoftswitch.Match(text);
        if (ipSoftMatch.Success) ipSoftswitch = IpCalculator.NormalizeIp(ipSoftMatch.Groups[1].Value);

        string? ipSipSoftswitch = null;
        var ipSipSoftMatch = RegexIpSipSoftswitch.Match(text);
        if (ipSipSoftMatch.Success) ipSipSoftswitch = IpCalculator.NormalizeIp(ipSipSoftMatch.Groups[1].Value);

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

        string? vlanCliente = null;
        var vlanMatch = RegexVlanCliente.Match(text);
        if (vlanMatch.Success)
        {
            vlanCliente = vlanMatch.Groups[1].Value.Trim();
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
            VlanCliente = vlanCliente,
            TipoAcesso = tipoAcesso,
            Sinalizacao = sinalizacao,
            IpPabx = ipPabx,
            NomeSbc = nomeSbc,
            Softswitch = softswitch,
            IpSipAcesso = ipSipAcesso,
            IpSoftswitch = ipSoftswitch,
            IpSipSoftswitch = ipSipSoftswitch,
            FaixaRamais = faixaRamais,
            NumeroChave = numeroChave,
            Cnl = cnl,
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

    public static string? CleanProduto(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = CleanField(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        var cutPatterns = new[]
        {
            @"(?i)\s*RAZ[ÃA]O\s*SOCIAL.*",
            @"(?i)\s*CONTA[\s\-_]*CORRENTE.*",
            @"(?i)\s*CNPJ.*"
        };

        foreach (var pattern in cutPatterns)
        {
            cleaned = Regex.Replace(cleaned, pattern, string.Empty);
        }

        cleaned = cleaned.Trim().TrimEnd('-', ',', ';', ':', '/', '\\', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
