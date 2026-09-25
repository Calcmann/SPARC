using System;
using System.IO;
using System.Text.Json;

namespace NetworkDevice.Core.UI;

/// <summary>
/// Modelo de configuração de escala e preferências de exibição do SPARC.
/// </summary>
public class UiDisplaySettings
{
    public double Scale { get; set; } = 1.0;
    public bool IsAuto { get; set; } = true;
}

/// <summary>
/// Utilitário e calculadora para dimensionamento dinâmico da interface (UI Scale),
/// projetado para adequar a experiência em telas de 14" Full HD (1080p) com escalas DPI do Windows
/// em 125% e 150%, onde o espaço útil em DIPs é reduzido (~672px a ~816px de altura útil).
/// </summary>
public static class UiScaleCalculator
{
    public const double MinScale = 0.65;
    public const double MaxScale = 1.30;
    public const double ScaleStep = 0.05;
    public const double DefaultScale = 1.00;

    /// <summary>
    /// Calcula a escala recomendada com base nas dimensões da área de trabalho do monitor (WorkArea em DIPs).
    /// Em monitores de 14" 1080p:
    /// - Com DPI em 150%: altura útil de ~672 DIPs -> escala de 82% (0.82).
    /// - Com DPI em 125%: altura útil de ~816 DIPs -> escala de 90% (0.90).
    /// - Com DPI em 100%: altura útil de ~1032 DIPs -> escala de 100% (1.00).
    /// </summary>
    public static double CalculateRecommendedScale(double workAreaWidth, double workAreaHeight)
    {
        if (workAreaHeight <= 0 || workAreaWidth <= 0)
            return DefaultScale;

        // Telas compactas: 14" 1080p @ 150% (WorkArea ~1280x672) ou telas antigas 1366x768 com barra grande
        if (workAreaHeight <= 710 || workAreaWidth <= 1200)
        {
            return 0.82;
        }

        // Telas intermediárias: 14" 1080p @ 125% (WorkArea ~1536x816) ou 1366x768 standard
        if (workAreaHeight <= 840)
        {
            return 0.90;
        }

        // Telas convencionais (1080p @ 100% com altura útil ~1032, 1440p, 4K)
        return DefaultScale;
    }

    /// <summary>
    /// Restringe a escala aos limites operacionais seguros [0.65, 1.30], arredondando para 2 casas.
    /// </summary>
    public static double ClampScale(double scale)
    {
        double clamped = Math.Clamp(scale, MinScale, MaxScale);
        return Math.Round(clamped, 2);
    }

    /// <summary>
    /// Calcula dimensões ideais de largura e altura da janela para caber com folga na área de trabalho.
    /// </summary>
    public static (double Width, double Height) CalculateWindowBounds(double workAreaWidth, double workAreaHeight, double idealWidth = 1060, double idealHeight = 820)
    {
        if (workAreaWidth <= 0 || workAreaHeight <= 0)
            return (idealWidth, idealHeight);

        double maxAllowedWidth = Math.Max(780, workAreaWidth - 24);
        double maxAllowedHeight = Math.Max(520, workAreaHeight - 24);

        double targetWidth = Math.Min(idealWidth, maxAllowedWidth);
        double targetHeight = Math.Min(idealHeight, maxAllowedHeight);

        return (Math.Round(targetWidth), Math.Round(targetHeight));
    }

    /// <summary>
    /// Obtém o caminho do arquivo de persistência de configuração de escala do SPARC.
    /// </summary>
    public static string GetSettingsFilePath()
    {
        string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SPARC");
        return Path.Combine(baseDir, "ui_scale.json");
    }

    /// <summary>
    /// Carrega as preferências salvas pelo usuário ou retorna padrão se não existir.
    /// </summary>
    public static UiDisplaySettings LoadSettings()
    {
        try
        {
            string path = GetSettingsFilePath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<UiDisplaySettings>(json);
                if (settings != null)
                {
                    settings.Scale = ClampScale(settings.Scale);
                    return settings;
                }
            }
        }
        catch
        {
            // Fallback silencioso para padrões
        }

        return new UiDisplaySettings { Scale = DefaultScale, IsAuto = true };
    }

    /// <summary>
    /// Salva as preferências de escala no disco.
    /// </summary>
    public static void SaveSettings(UiDisplaySettings settings)
    {
        try
        {
            string path = GetSettingsFilePath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignora falhas de escrita (ex: permissões restritas)
        }
    }
}
