using System;
using System.IO;
using System.Text.RegularExpressions;

namespace NetworkDevice.Cisco;

/// <summary>
/// Informações extraídas de versão e imagem do Cisco IOS.
/// </summary>
public sealed record CiscoIOSVersionInfo(
    string? CanonicalVersion,
    string? RunningImageFileName,
    string RawText)
{
    public bool HasVersion => !string.IsNullOrWhiteSpace(CanonicalVersion);
    public bool HasImageFile => !string.IsNullOrWhiteSpace(RunningImageFileName);

    public string DisplayString
    {
        get
        {
            if (HasVersion && HasImageFile)
                return $"{CanonicalVersion} ({RunningImageFileName})";
            if (HasVersion)
                return CanonicalVersion!;
            if (HasImageFile)
                return RunningImageFileName!;
            return string.IsNullOrWhiteSpace(RawText) ? "Desconhecida" : RawText;
        }
    }
}

/// <summary>
/// Inspetor e validador estrito de versão de firmware para Cisco IOS (ISR 921, 841, 1900, etc.).
/// Previne falsos positivos causados por substrings parciais (ex.: '15.9' coincidente entre 15.9(3)M2 e 15.9(3)M4).
/// </summary>
public static class CiscoIOSFirmwareVersionInspector
{
    // Ex.: System image file is "flash:c900-universalk9-mz.SPA.159-3.M4.bin" ou "flash:/c900-universalk9-mz.SPA.15.9-3.M4.bin"
    private static readonly Regex SystemImageFileRegex = new(
        @"(?im)System\s+image\s+file\s+is\s+""(?:[^:\""]+:)?(?:\/)?(?<fileName>[^\""\r\n]+)""",
        RegexOptions.Compiled);

    // Ex.: Cisco IOS Software, C900 Software (C900-UNIVERSALK9-M), Version 15.9(3)M4, RELEASE SOFTWARE (fc2)
    // ou: Cisco IOS Software, C800 Software (C800-UNIVERSALK9-M), Version 15.6(3)M2, RELEASE SOFTWARE (fc2)
    private static readonly Regex ShowVersionLineRegex = new(
        @"(?im)Cisco\s+IOS\s+Software.*?(?:Version\s+|,\s*Version\s*)(?<ver>[0-9]+\.[0-9]+\([0-9]+\)[A-Za-z0-9]+)",
        RegexOptions.Compiled);

    // Fallback para linha genérica 'Version 15.9(3)M4'
    private static readonly Regex GenericVersionRegex = new(
        @"(?im)\bVersion\s+(?<ver>[0-9]+\.[0-9]+\([0-9]+\)[A-Za-z0-9]+)",
        RegexOptions.Compiled);

    // Padrão 1 de arquivo binário: c900-universalk9-mz.SPA.159-3.M4.bin -> major=159, minor=3, train=M4
    private static readonly Regex BinPatternWithoutDotRegex = new(
        @"(?i)(?<major>\d{2,3})-(?<minor>\d+)\.(?<train>[A-Za-z0-9]+)",
        RegexOptions.Compiled);

    // Padrão 2 de arquivo binário: c900-universalk9-mz.SPA.15.9-3.M4.bin -> major=15, submajor=9, minor=3, train=M4
    private static readonly Regex BinPatternWithDotRegex = new(
        @"(?i)(?<major>\d+)\.(?<submajor>\d+)-(?<minor>\d+)\.(?<train>[A-Za-z0-9]+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Extrai o nome da imagem e a versão canônica do Cisco IOS a partir da saída do comando 'show version'.
    /// </summary>
    public static CiscoIOSVersionInfo ExtractFromShowVersion(string? showVerOutput)
    {
        if (string.IsNullOrWhiteSpace(showVerOutput))
            return new CiscoIOSVersionInfo(null, null, string.Empty);

        string? runningImage = null;
        string? canonicalVer = null;

        var imgMatch = SystemImageFileRegex.Match(showVerOutput);
        if (imgMatch.Success)
        {
            runningImage = Path.GetFileName(imgMatch.Groups["fileName"].Value.Trim());
        }

        var verMatch = ShowVersionLineRegex.Match(showVerOutput);
        if (verMatch.Success)
        {
            canonicalVer = NormalizeCanonicalVersion(verMatch.Groups["ver"].Value);
        }
        else
        {
            var genMatch = GenericVersionRegex.Match(showVerOutput);
            if (genMatch.Success)
            {
                canonicalVer = NormalizeCanonicalVersion(genMatch.Groups["ver"].Value);
            }
        }

        return new CiscoIOSVersionInfo(canonicalVer, runningImage, showVerOutput);
    }

    /// <summary>
    /// Extrai a versão canônica e o nome limpo do arquivo binário (.bin).
    /// Suporta formatos como:
    /// - c900-universalk9-mz.SPA.159-3.M4.bin -> 15.9(3)M4
    /// - c900-universalk9-mz.SPA.15.9-3.M4.bin -> 15.9(3)M4
    /// - c841-universalk9-mz.SPA.157-3.M9.bin -> 15.7(3)M9
    /// </summary>
    public static CiscoIOSVersionInfo ExtractFromFileName(string? fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath))
            return new CiscoIOSVersionInfo(null, null, string.Empty);

        var cleanBin = Path.GetFileName(fileNameOrPath).Trim();

        // 1. Tenta formato com ponto (15.9-3.M4)
        var dotMatch = BinPatternWithDotRegex.Match(cleanBin);
        if (dotMatch.Success)
        {
            var major = dotMatch.Groups["major"].Value;
            var submajor = dotMatch.Groups["submajor"].Value;
            var minor = dotMatch.Groups["minor"].Value;
            var train = dotMatch.Groups["train"].Value;
            var canonical = $"{major}.{submajor}({minor}){train}";
            return new CiscoIOSVersionInfo(NormalizeCanonicalVersion(canonical), cleanBin, cleanBin);
        }

        // 2. Tenta formato sem ponto (159-3.M4 -> 15.9(3)M4)
        var noDotMatch = BinPatternWithoutDotRegex.Match(cleanBin);
        if (noDotMatch.Success)
        {
            var majorStr = noDotMatch.Groups["major"].Value;
            var minor = noDotMatch.Groups["minor"].Value;
            var train = noDotMatch.Groups["train"].Value;

            if (majorStr.Length == 3)
            {
                var canonical = $"{majorStr[0]}{majorStr[1]}.{majorStr[2]}({minor}){train}";
                return new CiscoIOSVersionInfo(NormalizeCanonicalVersion(canonical), cleanBin, cleanBin);
            }
        }

        return new CiscoIOSVersionInfo(null, cleanBin, cleanBin);
    }

    /// <summary>
    /// Normaliza strings de versão canônica removendo espaços e padronizando maiúsculas.
    /// Ex.: "15.9(3) M4" -> "15.9(3)M4"
    /// </summary>
    public static string NormalizeCanonicalVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return string.Empty;

        return Regex.Replace(version, @"\s+", "").Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Avalia se a versão do Cisco IOS em execução corresponde exatamente à imagem proposta.
    /// Retorna true APENAS se:
    /// 1. O nome do arquivo executado for idêntico ao binário proposto; OU
    /// 2. As versões canônicas completas (Major.Minor(Maintenance)Train) forem idênticas.
    /// Não realiza match parcial de prefixo (ex: 15.9(3)M2 != 15.9(3)M4).
    /// </summary>
    public static bool IsSameVersion(
        string? showVerOutput,
        string? targetBinFileNameOrPath,
        out CiscoIOSVersionInfo currentVersion,
        out CiscoIOSVersionInfo targetVersion)
    {
        currentVersion = ExtractFromShowVersion(showVerOutput);
        targetVersion = ExtractFromFileName(targetBinFileNameOrPath);

        if (string.IsNullOrWhiteSpace(targetBinFileNameOrPath))
            return false;

        var targetCleanBin = Path.GetFileName(targetBinFileNameOrPath).Trim();

        // 1. Comparação direta pelo nome do arquivo de imagem do sistema em execução
        if (!string.IsNullOrWhiteSpace(currentVersion.RunningImageFileName))
        {
            if (currentVersion.RunningImageFileName.Equals(targetCleanBin, StringComparison.OrdinalIgnoreCase) ||
                currentVersion.RunningImageFileName.Equals(Path.GetFileNameWithoutExtension(targetCleanBin), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. Comparação estrita de versão canônica (ex.: "15.9(3)M4" == "15.9(3)M4")
        if (currentVersion.HasVersion && targetVersion.HasVersion)
        {
            return currentVersion.CanonicalVersion!.Equals(targetVersion.CanonicalVersion, StringComparison.OrdinalIgnoreCase);
        }

        // 3. Verificação de nome exato presente no show version
        if (showVerOutput != null && (
            showVerOutput.Contains($":{targetCleanBin}", StringComparison.OrdinalIgnoreCase) ||
            showVerOutput.Contains($"/{targetCleanBin}", StringComparison.OrdinalIgnoreCase) ||
            showVerOutput.Contains($"\"{targetCleanBin}\"", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}
