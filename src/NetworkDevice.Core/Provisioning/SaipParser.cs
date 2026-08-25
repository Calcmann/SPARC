using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace NetworkDevice.Core.Provisioning;

public static class SaipParser
{
    private static readonly Regex RegexWanIp = new(
        @"(?i)IP\s*Serial\s*(?:Usu[áa]rio)?\s*(?:\(\s*IPv4\s*\))?\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})\s*/\s*(\d{1,2})",
        RegexOptions.Compiled);

    private static readonly Regex RegexLanBlock = new(
        @"(?i)Blocos?\s*IPv4\s*[:\t]?\s*([0-9]{1,3}(?:\.[0-9]{1,3}){3})\s*/\s*(\d{1,2})",
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

    /// <summary>
    /// Carrega e extrai os dados de uma Ficha SAIP a partir de um arquivo .txt ou .pdf.
    /// </summary>
    public static async Task<SaipCircuitData> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException($"Arquivo de Ficha SAIP não encontrado: {filePath}");

        string text;
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (ext == ".pdf")
        {
            text = await Task.Run(() => ExtractTextFromPdf(filePath), cancellationToken);
        }
        else
        {
            text = await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken);
        }

        return ParseText(text);
    }

    /// <summary>Valida se a ficha contém os IPs obrigatórios para provisionamento.</summary>
    public static (bool ok, string motivo) Validar(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (false, "Arquivo vazio ou ilegível.");
        var hasWan = RegexWanIp.IsMatch(text);
        var hasLan = RegexLanBlock.IsMatch(text);
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

        // 1. WAN (IP Serial Usuário)
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

        // 2. LAN (Blocos IPv4)
        var lanMatch = RegexLanBlock.Match(text);
        string lanBlock = string.Empty;
        int lanCidr = 29;
        string lanIp = string.Empty;
        string lanMask = "255.255.255.248";

        if (lanMatch.Success)
        {
            lanBlock = IpCalculator.NormalizeIp(lanMatch.Groups[1].Value);
            lanCidr = int.Parse(lanMatch.Groups[2].Value);
            lanMask = IpCalculator.CidrToSubnetMask(lanCidr);
            lanIp = IpCalculator.CalculateFirstUsableIp(lanBlock, lanCidr);
        }
        else
        {
            // Fallback se não especificado na ficha
            lanIp = "192.168.1.1";
            lanMask = "255.255.255.0";
            lanCidr = 24;
        }

        // 3. Metadados do Cliente e Circuito
        var razaoSocial = CleanRazaoSocial(RegexRazaoSocial.Match(text).Groups[1].Value);
        var designacaoIp = CleanField(RegexDesignacaoIp.Match(text).Groups[1].Value);
        var numeroOts = CleanField(RegexNumeroOts.Match(text).Groups[1].Value);
        var descRoteador = CleanField(RegexDescriptionRoteador.Match(text).Groups[1].Value);
        var peRouter = CleanField(RegexPeRouter.Match(text).Groups[1].Value);
        var hostLanIp = string.IsNullOrEmpty(lanBlock) ? "192.168.1.2" : IpCalculator.CalculateHostLanIp(lanBlock, lanCidr);

        return new SaipCircuitData
        {
            ClienteRazaoSocial = razaoSocial,
            DesignacaoIp = designacaoIp,
            NumeroOts = numeroOts,
            DescriptionRoteador = descRoteador,
            PeRouter = peRouter,
            WanIp = wanIp,
            WanCidr = wanCidr,
            WanSubnetMask = wanMask,
            WanGateway = wanGateway,
            LanBlockNetwork = lanBlock,
            LanCidr = lanCidr,
            LanIp = lanIp,
            LanSubnetMask = lanMask,
            HostLanIp = hostLanIp,
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
