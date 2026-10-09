using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NetworkDevice.Cisco;

/// <summary>
/// Parser especializado para saídas de comandos de sistema de arquivos e memória Flash do Cisco IOS (ex.: 'dir flash:', 'show flash:', 'dir usbflash0:').
/// </summary>
public static class CiscoIOSFlashParser
{
    private static readonly Regex BinFileRegex = new(
        @"(?i)\b([a-zA-Z0-9_\-\.]+\.(?:bin|ipe|pkg))\b",
        RegexOptions.Compiled);

    private static readonly Regex AvailableBytesRegex = new(
        @"(?i)(\d+)\s+bytes\s+(?:available|free)",
        RegexOptions.Compiled);

    private static readonly Regex AvailableKBytesRegex = new(
        @"(?i)(\d+)\s*K\s+bytes\s+(?:available|free)",
        RegexOptions.Compiled);

    /// <summary>
    /// Extrai todos os arquivos de imagem de firmware (.bin, .ipe, .pkg) presentes na listagem da Flash.
    /// </summary>
    public static List<string> ExtractFirmwareFiles(string dirOutput)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(dirOutput)) return files.ToList();

        var lines = dirOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var match = BinFileRegex.Match(line);
            if (match.Success)
            {
                var f = match.Groups[1].Value.Trim();
                if (!f.StartsWith(".") &&
                    (f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                     f.EndsWith(".ipe", StringComparison.OrdinalIgnoreCase) ||
                     f.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)))
                {
                    files.Add(f);
                }
            }
        }
        return files.ToList();
    }

    /// <summary>
    /// Retorna a lista de arquivos de firmware presentes na flash que diferem do arquivo alvo especificado.
    /// Esses arquivos concorrentes podem ser excluídos para liberar espaço e garantir que o boot ocorra exclusivamente na versão alvo.
    /// </summary>
    public static List<string> GetConflictingFirmwareFiles(string dirOutput, string targetFileName)
    {
        if (string.IsNullOrWhiteSpace(targetFileName)) return new List<string>();

        var cleanTarget = Path.GetFileName(targetFileName).Trim();
        var allFirmwares = ExtractFirmwareFiles(dirOutput);

        return allFirmwares
            .Where(f => !f.Equals(cleanTarget, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Extrai o total de bytes disponíveis/livres na memória Flash informada.
    /// Retorna -1 se não for possível identificar.
    /// </summary>
    public static long ParseAvailableBytes(string dirOutput)
    {
        if (string.IsNullOrWhiteSpace(dirOutput)) return -1;

        var mBytes = AvailableBytesRegex.Match(dirOutput);
        if (mBytes.Success && long.TryParse(mBytes.Groups[1].Value, out var bytes))
        {
            return bytes;
        }

        var mKBytes = AvailableKBytesRegex.Match(dirOutput);
        if (mKBytes.Success && long.TryParse(mKBytes.Groups[1].Value, out var kbytes))
        {
            return kbytes * 1024L;
        }

        return -1;
    }

    /// <summary>
    /// Detecta o prefixo canônico do filesystem da Flash (ex.: 'flash:', 'usbflash0:', 'flash0:') a partir do 'show version' ou 'dir'.
    /// </summary>
    public static string DetectFlashFilesystemPrefix(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return "flash:";

        if (output.Contains("usbflash0:", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("USB Flash usbflash0", StringComparison.OrdinalIgnoreCase))
        {
            return "usbflash0:";
        }

        if (output.Contains("flash0:", StringComparison.OrdinalIgnoreCase))
        {
            return "flash0:";
        }

        return "flash:";
    }
}
