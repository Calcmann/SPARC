using System;
using System.Collections.Generic;
using NetworkDevice.Core.Domain;

namespace NetworkDevice.Core.Firmware;

/// <summary>
/// Mapeamento canônico entre séries de equipamentos do SPARC,
/// os nomes das pastas no repositório remoto/local e descrições amigáveis.
/// </summary>
public sealed record FirmwareModelDefinition(
    DeviceSeries Series,
    string FolderName,
    string DisplayName,
    string DefaultExtension,
    string AllowedExtensionsPattern);

public static class FirmwareModelMap
{
    private static readonly Dictionary<DeviceSeries, FirmwareModelDefinition> BySeries = new()
    {
        [DeviceSeries.FortiGate40F] = new(
            DeviceSeries.FortiGate40F,
            "fortigate-40f",
            "Fortinet FortiGate 40F",
            ".out",
            "*.out"),

        [DeviceSeries.Isr921] = new(
            DeviceSeries.Isr921,
            "cisco-c921",
            "Cisco Série 900 / C921-4P",
            ".bin",
            "*.bin"),

        [DeviceSeries.Isr841] = new(
            DeviceSeries.Isr841,
            "cisco-c841",
            "Cisco Série 800 / C841M",
            ".bin",
            "*.bin"),

        [DeviceSeries.Series1900] = new(
            DeviceSeries.Series1900,
            "cisco-c1900",
            "Cisco Série 1900 / 1941 / G2",
            ".bin",
            "*.bin"),

        [DeviceSeries.Series2900] = new(
            DeviceSeries.Series2900,
            "cisco-c2900",
            "Cisco Série 2900 / 2911 / G2",
            ".bin",
            "*.bin"),

        [DeviceSeries.Msr954] = new(
            DeviceSeries.Msr954,
            "hpe-msr954",
            "HPE MSR 954 / 958",
            ".ipe",
            "*.ipe;*.bin"),

        [DeviceSeries.Msr930] = new(
            DeviceSeries.Msr930,
            "hpe-msr930",
            "HPE MSR 930 / 931 / 935",
            ".ipe",
            "*.ipe;*.bin"),

        [DeviceSeries.Msr1002] = new(
            DeviceSeries.Msr1002,
            "hpe-msr1002",
            "HPE MSR 1002 / 1003",
            ".ipe",
            "*.ipe;*.bin")
    };

    public static IReadOnlyCollection<FirmwareModelDefinition> AllDefinitions => BySeries.Values;

    public static FirmwareModelDefinition? GetDefinition(DeviceSeries series)
    {
        return BySeries.TryGetValue(series, out var def) ? def : null;
    }

    public static FirmwareModelDefinition? FindByFolderName(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;

        var clean = folderName.Trim().ToLowerInvariant();
        foreach (var def in BySeries.Values)
        {
            if (def.FolderName.Equals(clean, StringComparison.OrdinalIgnoreCase))
                return def;
        }
        return null;
    }

    public static DeviceSeries ResolveFromTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return DeviceSeries.Unknown;

        var t = tag.ToLowerInvariant();
        if (t.Contains("fgt") || t.Contains("forti") || t.Contains("40f")) return DeviceSeries.FortiGate40F;
        if (t.Contains("c900") || t.Contains("921")) return DeviceSeries.Isr921;
        if (t.Contains("c841") || t.Contains("c800") || t.Contains("841")) return DeviceSeries.Isr841;
        if (t.Contains("c1900") || t.Contains("1921") || t.Contains("1941") || t.Contains("1900")) return DeviceSeries.Series1900;
        if (t.Contains("c2900") || t.Contains("2901") || t.Contains("2911") || t.Contains("2921") || t.Contains("2951") || t.Contains("2900")) return DeviceSeries.Series2900;
        if (t.Contains("954") || t.Contains("958") || t.Contains("95x")) return DeviceSeries.Msr954;
        if (t.Contains("930") || t.Contains("931") || t.Contains("935") || t.Contains("93x")) return DeviceSeries.Msr930;
        if (t.Contains("1002") || t.Contains("1003") || t.Contains("1000") || t.Contains("100x") || t.Contains("msr100")) return DeviceSeries.Msr1002;

        return DeviceSeries.Unknown;
    }

    /// <summary>
    /// Identifica com alta precisão a qual série de equipamento pertence um arquivo de firmware.
    /// Utilizado especialmente para mapear assets de GitHub Releases para os modelos correspondentes.
    /// </summary>
    public static DeviceSeries MatchSeriesFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return DeviceSeries.Unknown;

        // 1. Tenta correspondência direta por tag/nome
        var fromTag = ResolveFromTag(fileName);
        if (fromTag != DeviceSeries.Unknown)
        {
            return fromTag;
        }

        // 2. Se não identificar por tag, tenta correspondência pelas pastas canônicas
        foreach (var def in BySeries.Values)
        {
            if (fileName.Contains(def.FolderName, StringComparison.OrdinalIgnoreCase))
            {
                return def.Series;
            }
        }

        return DeviceSeries.Unknown;
    }
}
