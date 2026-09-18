using System.IO;
using System.Text.RegularExpressions;

namespace NetworkDevice.Fortinet;

/// <summary>
/// Informações extraídas de versão e build do FortiOS.
/// </summary>
public sealed record FortiOsVersionInfo(
    string? Version,
    string? Build,
    string RawText)
{
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);
    public bool HasBuild => !string.IsNullOrWhiteSpace(Build);

    public string DisplayString
    {
        get
        {
            if (HasVersion && HasBuild)
                return $"v{Version} (build {Build})";
            if (HasVersion)
                return $"v{Version}";
            if (HasBuild)
                return $"build {Build}";
            return string.IsNullOrWhiteSpace(RawText) ? "Desconhecida" : RawText;
        }
    }
}

/// <summary>
/// Inspeciona e compara a versão de firmware do FortiGate 40F (FortiOS)
/// entre o equipamento em execução ('get system status') e a imagem selecionada (.out).
/// Evita transferências TFTP redundantes quando o equipamento já possui a mesma versão.
/// </summary>
public static class FortiOsFirmwareVersionInspector
{
    private static readonly Regex VersionRegex = new(
        @"(?i)v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex BuildRegex = new(
        @"(?i)(?:build|[-_]b)(?<build>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex StatusVersionLineRegex = new(
        @"(?im)^\s*Version\s*:\s*(?<line>[^\r\n]+)",
        RegexOptions.Compiled);

    private static readonly Regex BranchPointRegex = new(
        @"(?im)^\s*Branch\s+point\s*:\s*(?<build>\d+)",
        RegexOptions.Compiled);

    /// <summary>
    /// Extrai a versão e o build do FortiOS a partir da saída do comando 'get system status'.
    /// Exemplo de linha: 'Version: FortiGate-40F v7.2.6,build1575,230815 (GA.F)'
    /// </summary>
    public static FortiOsVersionInfo ExtractFromStatus(string? statusOutput)
    {
        if (string.IsNullOrWhiteSpace(statusOutput))
            return new FortiOsVersionInfo(null, null, string.Empty);

        string? version = null;
        string? build = null;
        string rawLine = string.Empty;

        var lineMatch = StatusVersionLineRegex.Match(statusOutput);
        if (lineMatch.Success)
        {
            rawLine = lineMatch.Groups["line"].Value.Trim();
            var verMatch = VersionRegex.Match(rawLine);
            if (verMatch.Success)
            {
                version = $"{verMatch.Groups["major"].Value}.{verMatch.Groups["minor"].Value}.{verMatch.Groups["patch"].Value}";
            }

            var bldMatch = BuildRegex.Match(rawLine);
            if (bldMatch.Success)
            {
                build = bldMatch.Groups["build"].Value;
            }
        }

        // Se o build não foi encontrado na linha 'Version:', tenta 'Branch point: 1575'
        if (string.IsNullOrEmpty(build))
        {
            var branchMatch = BranchPointRegex.Match(statusOutput);
            if (branchMatch.Success)
            {
                build = branchMatch.Groups["build"].Value;
            }
        }

        return new FortiOsVersionInfo(version, build, rawLine);
    }

    /// <summary>
    /// Extrai versão e build a partir do nome ou caminho do arquivo de firmware .out.
    /// Ex.: 'FGT_40F-v7.2.6.F-build1575-FORTINET.out' -> Version: 7.2.6, Build: 1575
    /// </summary>
    public static FortiOsVersionInfo ExtractFromFileName(string? filePathOrName)
    {
        if (string.IsNullOrWhiteSpace(filePathOrName))
            return new FortiOsVersionInfo(null, null, string.Empty);

        var fileName = Path.GetFileName(filePathOrName);
        string? version = null;
        string? build = null;

        var verMatch = VersionRegex.Match(fileName);
        if (verMatch.Success)
        {
            version = $"{verMatch.Groups["major"].Value}.{verMatch.Groups["minor"].Value}.{verMatch.Groups["patch"].Value}";
        }

        var bldMatch = BuildRegex.Match(fileName);
        if (bldMatch.Success)
        {
            build = bldMatch.Groups["build"].Value;
        }

        return new FortiOsVersionInfo(version, build, fileName);
    }

    /// <summary>
    /// Compara as versões do equipamento e da imagem alvo.
    /// Retorna true se a imagem for comprovadamente a mesma versão e build instalados no equipamento.
    /// </summary>
    public static bool IsSameVersion(FortiOsVersionInfo deviceVer, FortiOsVersionInfo targetVer)
    {
        ArgumentNullException.ThrowIfNull(deviceVer);
        ArgumentNullException.ThrowIfNull(targetVer);

        // Se não foi possível identificar a versão base de qualquer um dos lados, não assume igualdade
        if (!deviceVer.HasVersion || !targetVer.HasVersion)
            return false;

        // Versões base precisam coincidir (ex.: 7.2.6 == 7.2.6)
        if (!string.Equals(deviceVer.Version, targetVer.Version, StringComparison.OrdinalIgnoreCase))
            return false;

        // Se ambos informam build, compara numericamente (ex.: build 0523 == 523)
        if (deviceVer.HasBuild && targetVer.HasBuild)
        {
            if (int.TryParse(deviceVer.Build, out var dBld) && int.TryParse(targetVer.Build, out var tBld))
            {
                return dBld == tBld;
            }

            return string.Equals(deviceVer.Build, targetVer.Build, StringComparison.OrdinalIgnoreCase);
        }

        // Se a versão base é idêntica e apenas um dos lados não especificou o build no nome do arquivo,
        // consideramos a mesma versão compatível.
        return true;
    }

    /// <summary>
    /// Avalia se a versão do equipamento obtida via 'get system status' é idêntica à do arquivo de firmware selecionado.
    /// </summary>
    public static bool IsSameVersion(
        string? statusOutput,
        string? filePathOrName,
        out string currentVersionDisplay,
        out string targetVersionDisplay)
    {
        var deviceVer = ExtractFromStatus(statusOutput);
        var targetVer = ExtractFromFileName(filePathOrName);

        currentVersionDisplay = deviceVer.DisplayString;
        targetVersionDisplay = targetVer.DisplayString;

        return IsSameVersion(deviceVer, targetVer);
    }
}
