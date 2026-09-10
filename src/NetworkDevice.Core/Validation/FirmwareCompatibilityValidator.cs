using System;
using System.IO;
using System.Text.RegularExpressions;
using NetworkDevice.Core.Domain;

namespace NetworkDevice.Core.Validation;

public sealed record FirmwareValidationResult(
    bool IsCompatible,
    string ErrorMessage,
    string ExpectedFormatDescription);

/// <summary>
/// Validador estrito de integridade e compatibilidade entre o firmware selecionado e o hardware detectado/selecionado.
/// Previne envio de imagens de outros modelos (ex.: c1900 em c921 ou msr930 em msr954).
/// </summary>
public static class FirmwareCompatibilityValidator
{
    private static readonly Regex Cisco900FirmwareRegex = new(
        @"(?i)^c900[-_]|^c92[0-9][-_]",
        RegexOptions.Compiled);

    private static readonly Regex Cisco841FirmwareRegex = new(
        @"(?i)^c841[-_]|^c800[-_]|^c800m[-_]|^c841m[-_]",
        RegexOptions.Compiled);

    private static readonly Regex Cisco1900FirmwareRegex = new(
        @"(?i)^c19[0-9]{2}[-_]",
        RegexOptions.Compiled);

    private static readonly Regex Hpe954FirmwareRegex = new(
        @"(?i)msr95[0-9][-_]|msr954",
        RegexOptions.Compiled);

    private static readonly Regex Hpe930FirmwareRegex = new(
        @"(?i)msr93[0-9][-_]|msr930",
        RegexOptions.Compiled);

    private static readonly Regex Hpe1002FirmwareRegex = new(
        @"(?i)msr100[0-9][-_]|msr1002|msr1003|msr1000",
        RegexOptions.Compiled);

    public static FirmwareValidationResult Validate(DeviceSeries series, string? filePathOrName)
    {
        if (string.IsNullOrWhiteSpace(filePathOrName))
        {
            return new FirmwareValidationResult(true, string.Empty, string.Empty);
        }

        var fileName = Path.GetFileName(filePathOrName);
        var ext = Path.GetExtension(filePathOrName).ToLowerInvariant();

        switch (series)
        {
            case DeviceSeries.Isr841:
                if (ext != ".bin")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O arquivo '{fileName}' possui extensão '{ext}'. Roteadores Cisco Série 800 / C841 aceitam exclusivamente imagens executáveis no formato .BIN.",
                        "c841-universalk9-mz.*.bin / c800-universalk9-mz.*.bin");
                }

                if (Cisco1900FirmwareRegex.IsMatch(fileName) ||
                    Cisco900FirmwareRegex.IsMatch(fileName) ||
                    fileName.StartsWith("c2900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c3900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("msr", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("cmw", StringComparison.OrdinalIgnoreCase) ||
                    ext == ".ipe")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O firmware '{fileName}' é incompatível com o Cisco Série 800 / C841 (arquivo destinado a outro modelo como Série 1900, Série 900 ou HPE).",
                        "c841-universalk9-mz.*.bin / c800-universalk9-mz.*.bin");
                }
                return new FirmwareValidationResult(true, string.Empty, "c841-universalk9-mz.*.bin / c800-universalk9-mz.*.bin");

            case DeviceSeries.Isr921:
                if (ext != ".bin")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O arquivo '{fileName}' possui extensão '{ext}'. Roteadores Cisco Série 900 / C921 aceitam exclusivamente imagens executáveis no formato .BIN.",
                        "c900-universalk9-mz.*.bin");
                }

                if (Cisco1900FirmwareRegex.IsMatch(fileName) ||
                    Cisco841FirmwareRegex.IsMatch(fileName) ||
                    fileName.StartsWith("c2900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c3900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("msr", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("cmw", StringComparison.OrdinalIgnoreCase) ||
                    ext == ".ipe")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O firmware '{fileName}' é incompatível com o Cisco Série 900 / C921 (arquivo destinado a outro modelo como Série 1900, Série 800 ou HPE).",
                        "c900-universalk9-mz.*.bin");
                }
                return new FirmwareValidationResult(true, string.Empty, "c900-universalk9-mz.*.bin");

            case DeviceSeries.Series1900:
                if (ext != ".bin")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O arquivo '{fileName}' possui extensão '{ext}'. Roteadores Cisco Série 1900 aceitam exclusivamente imagens executáveis no formato .BIN.",
                        "c1900-universalk9-mz.*.bin");
                }

                if (Cisco900FirmwareRegex.IsMatch(fileName) ||
                    Cisco841FirmwareRegex.IsMatch(fileName) ||
                    fileName.StartsWith("c2900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c3900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("msr", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("cmw", StringComparison.OrdinalIgnoreCase) ||
                    ext == ".ipe")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O firmware '{fileName}' é incompatível com o Cisco Série 1900 (arquivo destinado a outro modelo como Série 900, Série 800 ou HPE).",
                        "c1900-universalk9-mz.*.bin");
                }
                return new FirmwareValidationResult(true, string.Empty, "c1900-universalk9-mz.*.bin");

            case DeviceSeries.Msr954:
                if (ext != ".ipe" && ext != ".bin")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O arquivo '{fileName}' possui extensão '{ext}'. Equipamentos HPE MSR aceitam pacotes .IPE ou .BIN.",
                        "msr954-cmw710-*.ipe");
                }

                if (Hpe930FirmwareRegex.IsMatch(fileName) ||
                    Hpe1002FirmwareRegex.IsMatch(fileName) ||
                    fileName.StartsWith("c900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c800", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c841", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c1900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("cisco", StringComparison.OrdinalIgnoreCase))
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O firmware '{fileName}' é incompatível com o HPE MSR 954 (arquivo destinado ao MSR 930/1002 ou Cisco).",
                        "msr954-cmw710-*.ipe");
                }
                return new FirmwareValidationResult(true, string.Empty, "msr954-cmw710-*.ipe");

            case DeviceSeries.Msr930:
                if (ext != ".ipe" && ext != ".bin")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O arquivo '{fileName}' possui extensão '{ext}'. Equipamentos HPE MSR aceitam pacotes .IPE ou .BIN.",
                        "msr930-cmw710-*.ipe");
                }

                if (Hpe954FirmwareRegex.IsMatch(fileName) ||
                    Hpe1002FirmwareRegex.IsMatch(fileName) ||
                    fileName.StartsWith("c900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c800", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c841", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c1900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("cisco", StringComparison.OrdinalIgnoreCase))
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O firmware '{fileName}' é incompatível com o HPE MSR 930 (arquivo destinado ao MSR 954/1002 ou Cisco).",
                        "msr930-cmw710-*.ipe");
                }
                return new FirmwareValidationResult(true, string.Empty, "msr930-cmw710-*.ipe");

            case DeviceSeries.Msr1002:
                if (ext != ".ipe" && ext != ".bin")
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O arquivo '{fileName}' possui extensão '{ext}'. Equipamentos HPE MSR aceitam pacotes .IPE ou .BIN.",
                        "msr1000-cmw710-*.ipe");
                }

                if (Hpe954FirmwareRegex.IsMatch(fileName) ||
                    Hpe930FirmwareRegex.IsMatch(fileName) ||
                    fileName.StartsWith("c900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c800", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c841", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("c1900", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("cisco", StringComparison.OrdinalIgnoreCase))
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O firmware '{fileName}' é incompatível com o HPE MSR 1002 (arquivo destinado ao MSR 954/930 ou Cisco).",
                        "msr1000-cmw710-*.ipe");
                }
                return new FirmwareValidationResult(true, string.Empty, "msr1000-cmw710-*.ipe");

            default:
                if (ext == ".ipe" && (fileName.Contains("cisco", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("c9", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("c19", StringComparison.OrdinalIgnoreCase)))
                {
                    return new FirmwareValidationResult(
                        false,
                        $"O arquivo '{fileName}' é um pacote HPE (.ipe) e não pode ser instalado em roteadores Cisco.",
                        "*.bin");
                }
                return new FirmwareValidationResult(true, string.Empty, string.Empty);
        }
    }
}
