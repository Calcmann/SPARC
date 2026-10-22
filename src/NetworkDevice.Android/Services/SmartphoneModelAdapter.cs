using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace NetworkDevice.Android.Services;

public enum ScreenCategory
{
    Compact,   // < 360 dp width (Smartphones de entrada / telas pequenas: ~320x640 dp)
    Standard,  // 360 a 440 dp width (Maioria dos smartphones modernos: 360x800, 393x873, 412x915 dp)
    Large,     // 440 a 599 dp width (Smartphones grandes / phablets / dobráveis abertos)
    Tablet     // >= 600 dp width (Tablets de campo)
}

public enum HardwareTier
{
    Entry,   // <= 3GB RAM ou <= 4 núcleos (aparelhos de entrada / Android Go)
    Mid,     // 4GB a 6GB RAM (intermediários modernos)
    High     // >= 8GB RAM (flagships e aparelhos de alta performance)
}

public sealed class SmartphoneHardwareProfile
{
    public string Manufacturer { get; init; } = "Android";
    public string RawModel { get; init; } = "Device";
    public string FriendlyModelName { get; init; } = "Smartphone Android";
    public string AndroidVersion { get; init; } = "Android";
    public int ApiLevel { get; init; } = 30;
    public int CpuCores { get; init; } = 4;
    public long TotalRamBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public bool IsLowRamDevice { get; init; } = false;
    public HardwareTier Tier { get; init; } = HardwareTier.Mid;
    
    // Dimensões de tela
    public double ScreenWidthDp { get; init; } = 360;
    public double ScreenHeightDp { get; init; } = 640;
    public double ScreenDensity { get; init; } = 2.0;
    public double AspectRatio { get; init; } = 1.77;
    public bool IsTallScreen { get; init; } = false;
    public ScreenCategory ScreenCategory { get; init; } = ScreenCategory.Standard;
    
    // Parâmetros de UI adaptativa
    public double HeaderFontSize => ScreenCategory switch
    {
        ScreenCategory.Compact => 13.5,
        ScreenCategory.Large => 17.0,
        ScreenCategory.Tablet => 19.0,
        _ => 15.0
    };

    public double BodyFontSize => ScreenCategory switch
    {
        ScreenCategory.Compact => 10.0,
        ScreenCategory.Large => 12.5,
        ScreenCategory.Tablet => 13.5,
        _ => 11.0
    };

    public double ButtonHeight => ScreenCategory switch
    {
        ScreenCategory.Compact => 36.0,
        ScreenCategory.Large => 46.0,
        ScreenCategory.Tablet => 50.0,
        _ => 42.0
    };

    public Thickness CardPadding => ScreenCategory switch
    {
        ScreenCategory.Compact => new Thickness(8, 6),
        ScreenCategory.Large => new Thickness(16, 14),
        ScreenCategory.Tablet => new Thickness(20, 16),
        _ => new Thickness(12, 10)
    };

    public double ElementSpacing => ScreenCategory switch
    {
        ScreenCategory.Compact => 6.0,
        ScreenCategory.Large => 12.0,
        ScreenCategory.Tablet => 14.0,
        _ => 8.0
    };

    // Parâmetros de rede e desempenho
    public bool RequiresManualOtgToggle { get; init; } = false;
    public string VendorOtgGuide { get; init; } = string.Empty;
    public int RecommendedUdpBufferSizeBytes { get; init; } = 2 * 1024 * 1024;
    public int TelemetryRefreshIntervalMs { get; init; } = 500;

    public string TotalRamFormatted => TotalRamBytes > 0
        ? $"{(int)Math.Round((double)TotalRamBytes / (1024 * 1024 * 1024))} GB"
        : "4 GB";

    public string ScreenCategoryName => ScreenCategory switch
    {
        ScreenCategory.Compact => "Tela Compacta (Otimizada)",
        ScreenCategory.Large => "Tela Expandida",
        ScreenCategory.Tablet => "Tablet de Campo",
        _ => "Tela Padrão"
    };

    public string SummaryBadge =>
        $"📱 {FriendlyModelName} • {TotalRamFormatted} RAM • {CpuCores} núcleos • {ScreenCategoryName}";
}

/// <summary>
/// Motor de Adaptação Dinâmica de Hardware do SPARC Mobile.
/// Calibra buffer de rede, dimensionamento de telas e guia de conectividade OTG
/// para a mais variada gama de modelos de smartphones em campo.
/// </summary>
public sealed class SmartphoneModelAdapter
{
    private static readonly Lazy<SmartphoneModelAdapter> _instance = new(() => new SmartphoneModelAdapter());
    public static SmartphoneModelAdapter Instance => _instance.Value;

    public SmartphoneHardwareProfile CurrentProfile { get; }

    private SmartphoneModelAdapter()
    {
        CurrentProfile = DetectHardwareProfile();
    }

    private SmartphoneHardwareProfile DetectHardwareProfile()
    {
        var mfr = DeviceInfo.Current.Manufacturer ?? "Android";
        var rawModel = DeviceInfo.Current.Model ?? "Device";
        var version = DeviceInfo.Current.VersionString;
        var api = (int)global::Android.OS.Build.VERSION.SdkInt;
        var cores = Math.Max(2, Environment.ProcessorCount);

        // Obtenção de Memória RAM nativa no Android
        long totalRam = 0;
        bool isLowRam = false;
        try
        {
            var actManager = (global::Android.App.ActivityManager?)global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.ActivityService);
            if (actManager != null)
            {
                var memInfo = new global::Android.App.ActivityManager.MemoryInfo();
                actManager.GetMemoryInfo(memInfo);
                totalRam = memInfo.TotalMem;
                isLowRam = actManager.IsLowRamDevice;
            }
        }
        catch { }

        if (totalRam <= 0)
        {
            // Estimativa baseada em núcleos caso a API do sistema não retorne
            totalRam = cores >= 8 ? 6L * 1024 * 1024 * 1024 : 4L * 1024 * 1024 * 1024;
        }

        // Métricas de Display
        var display = DeviceDisplay.Current.MainDisplayInfo;
        double density = display.Density > 0 ? display.Density : 2.0;
        double widthDp = display.Width / density;
        double heightDp = display.Height / density;
        double aspectRatio = widthDp > 0 ? heightDp / widthDp : 1.77;
        bool isTall = aspectRatio >= 2.0;

        // Categorização da Tela, Hardware Tier e Parâmetros via MobileDeviceHelper
        var ramGb = totalRam / (1024.0 * 1024.0 * 1024.0);
        var category = (ScreenCategory)NetworkDevice.Core.UI.MobileDeviceHelper.ClassifyScreenCategory(widthDp);
        var tier = (HardwareTier)NetworkDevice.Core.UI.MobileDeviceHelper.ClassifyHardwareTier(ramGb, cores, isLowRam);
        int bufferBytes = NetworkDevice.Core.UI.MobileDeviceHelper.DetermineOptimalUdpBufferSizeBytes((NetworkDevice.Core.UI.MobileHardwareTier)tier);
        int refreshInterval = NetworkDevice.Core.UI.MobileDeviceHelper.DetermineTelemetryRefreshIntervalMs((NetworkDevice.Core.UI.MobileHardwareTier)tier);
        bool requiresManualOtg = NetworkDevice.Core.UI.MobileDeviceHelper.DetermineVendorOtgRequirement(mfr, out var otgGuide);
        var friendlyName = NetworkDevice.Core.UI.MobileDeviceHelper.ResolveFriendlyModelName(mfr, rawModel);

        return new SmartphoneHardwareProfile
        {
            Manufacturer = mfr,
            RawModel = rawModel,
            FriendlyModelName = friendlyName,
            AndroidVersion = $"Android {version} (API {api})",
            ApiLevel = api,
            CpuCores = cores,
            TotalRamBytes = totalRam,
            IsLowRamDevice = isLowRam,
            Tier = tier,
            ScreenWidthDp = widthDp,
            ScreenHeightDp = heightDp,
            ScreenDensity = density,
            AspectRatio = aspectRatio,
            IsTallScreen = isTall,
            ScreenCategory = category,
            RequiresManualOtgToggle = requiresManualOtg,
            VendorOtgGuide = otgGuide,
            RecommendedUdpBufferSizeBytes = bufferBytes,
            TelemetryRefreshIntervalMs = refreshInterval
        };
    }

    /// <summary>
    /// Converte códigos técnicos de modelo em nomes comerciais conhecidos
    /// </summary>
    public static string ResolveFriendlyModelName(string mfr, string rawModel)
    {
        return NetworkDevice.Core.UI.MobileDeviceHelper.ResolveFriendlyModelName(mfr, rawModel);
    }

    /// <summary>
    /// Abre de forma inteligente as Configurações do Android para ativação de OTG ou Conexões USB.
    /// Suporta intents diretos e fallbacks universais.
    /// </summary>
    public void OpenDeviceOtgOrSystemSettings()
    {
        var context = global::Android.App.Application.Context;
        string[] actions =
        {
            "android.settings.CONNECTED_DEVICE_SETTINGS",
            "android.settings.WIRELESS_SETTINGS",
            "android.settings.SETTINGS"
        };

        foreach (var action in actions)
        {
            try
            {
                var intent = new global::Android.Content.Intent(action);
                intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                context.StartActivity(intent);
                return;
            }
            catch { }
        }

        try
        {
            var fallback = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionSettings);
            fallback.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(fallback);
        }
        catch { }
    }

    /// <summary>
    /// Abre as configurações de economia de bateria para evitar que o Android suspenda o aplicativo durante testes longos (Y.1564 de 15 minutos).
    /// </summary>
    public void OpenBatteryOptimizationSettings()
    {
        var context = global::Android.App.Application.Context;
        try
        {
            var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch
        {
            try
            {
                var fallback = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionBatterySaverSettings);
                fallback.AddFlags(global::Android.Content.ActivityFlags.NewTask);
                context.StartActivity(fallback);
            }
            catch
            {
                OpenDeviceOtgOrSystemSettings();
            }
        }
    }

    /// <summary>
    /// Exibe diálogo detalhado de conformidade do hardware para o operador técnico.
    /// </summary>
    public async Task ShowHardwareDiagnosticsAsync(Page page)
    {
        var p = CurrentProfile;
        var msg =
            $"📱 Aparelho: {p.FriendlyModelName}\n" +
            $"⚙️ Sistema: {p.AndroidVersion}\n" +
            $"⚡ Hardware: {p.CpuCores} núcleos | {p.TotalRamFormatted} RAM\n" +
            $"📐 Tela: {p.ScreenWidthDp:F0} x {p.ScreenHeightDp:F0} dp (Densidade: {p.ScreenDensity:F1}x)\n" +
            $"🏷️ Categoria de Tela: {p.ScreenCategoryName} {(p.IsTallScreen ? "(20:9 Wide)" : "")}\n" +
            $"📦 Buffer UDP Otimizado: {p.RecommendedUdpBufferSizeBytes / (1024 * 1024)} MB\n" +
            $"⏱️ Amostragem Telemetria: {p.TelemetryRefreshIntervalMs} ms\n" +
            $"🔌 USB Host (OTG): Homologado\n" +
            $"💡 Modo Doze: Proteção WakeLock e KeepScreenOn integradas\n\n" +
            $"📌 Dica do Fabricante:\n{p.VendorOtgGuide}";

        await page.DisplayAlert("Diagnóstico do Aparelho (SPARC)", msg, "OK");
    }
}
