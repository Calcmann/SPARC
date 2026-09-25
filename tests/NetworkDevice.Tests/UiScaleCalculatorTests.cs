using System;
using System.IO;
using NetworkDevice.Core.UI;
using Xunit;

namespace NetworkDevice.Tests;

public class UiScaleCalculatorTests
{
    [Fact]
    public void CalculateRecommendedScale_14Inch1080p_150PercentDpi_Returns82Percent()
    {
        // 14" 1080p com escala do Windows em 150%:
        // Resolução lógica: 1920/1.5 = 1280 DIP, 1080/1.5 = 720 DIP.
        // Área útil (WorkArea): ~1280 x 672 DIP.
        double scale = UiScaleCalculator.CalculateRecommendedScale(1280, 672);

        Assert.Equal(0.82, scale);
    }

    [Fact]
    public void CalculateRecommendedScale_14Inch1080p_125PercentDpi_Returns90Percent()
    {
        // 14" 1080p com escala do Windows em 125%:
        // Resolução lógica: 1920/1.25 = 1536 DIP, 1080/1.25 = 864 DIP.
        // Área útil (WorkArea): ~1536 x 816 DIP.
        double scale = UiScaleCalculator.CalculateRecommendedScale(1536, 816);

        Assert.Equal(0.90, scale);
    }

    [Fact]
    public void CalculateRecommendedScale_StandardDesktop1080p_100PercentDpi_Returns100Percent()
    {
        // Monitor padrão 1080p com DPI 100%:
        // Área útil: 1920 x 1032 DIP.
        double scale = UiScaleCalculator.CalculateRecommendedScale(1920, 1032);

        Assert.Equal(1.00, scale);
    }

    [Fact]
    public void ClampScale_LimitsWithinExpectedRange()
    {
        Assert.Equal(UiScaleCalculator.MinScale, UiScaleCalculator.ClampScale(0.40));
        Assert.Equal(UiScaleCalculator.MaxScale, UiScaleCalculator.ClampScale(1.80));
        Assert.Equal(0.85, UiScaleCalculator.ClampScale(0.854));
    }

    [Fact]
    public void CalculateWindowBounds_CompactScreen_FitsWithinWorkArea()
    {
        // Tela compacta (altura útil 672)
        var (width, height) = UiScaleCalculator.CalculateWindowBounds(1280, 672);

        Assert.True(height <= 672 - 24);
        Assert.True(width <= 1280 - 24);
        Assert.True(height >= 520);
        Assert.True(width >= 780);
    }

    [Fact]
    public void CalculateWindowBounds_LargeScreen_KeepsIdealDimensions()
    {
        var (width, height) = UiScaleCalculator.CalculateWindowBounds(1920, 1032);

        Assert.Equal(1060, width);
        Assert.Equal(820, height);
    }

    [Fact]
    public void SaveAndLoadSettings_RoundtripsSuccessfully()
    {
        var original = new UiDisplaySettings { Scale = 0.88, IsAuto = false };
        UiScaleCalculator.SaveSettings(original);

        var loaded = UiScaleCalculator.LoadSettings();
        Assert.Equal(0.88, loaded.Scale);
        Assert.False(loaded.IsAuto);

        // Limpa e restaura para Auto
        UiScaleCalculator.SaveSettings(new UiDisplaySettings { Scale = 1.0, IsAuto = true });
        var restored = UiScaleCalculator.LoadSettings();
        Assert.True(restored.IsAuto);
    }
}
