using System;

namespace NetworkDevice.Core.UI;

public enum MobileScreenCategory
{
    Compact,   // < 360 dp width (Smartphones de entrada / telas pequenas: ~320x640 dp)
    Standard,  // 360 a 440 dp width (Maioria dos smartphones modernos: 360x800, 393x873, 412x915 dp)
    Large,     // 440 a 599 dp width (Smartphones grandes / phablets / dobráveis abertos)
    Tablet     // >= 600 dp width (Tablets de campo)
}

public enum MobileHardwareTier
{
    Entry,   // <= 3.2GB RAM ou <= 4 núcleos (aparelhos de entrada / Android Go)
    Mid,     // 4GB a 6GB RAM (intermediários modernos)
    High     // >= 7.2GB RAM e >= 8 núcleos (flagships e alta performance)
}

/// <summary>
/// Utilitário e classificador de modelos e perfis de hardware de smartphones para o SPARC Mobile.
/// Garante calibração responsiva para a mais variada gama de modelos (Motorola, Samsung, Xiaomi, Realme, etc.).
/// </summary>
public static class MobileDeviceHelper
{
    public static MobileScreenCategory ClassifyScreenCategory(double widthDp)
    {
        if (widthDp < 360) return MobileScreenCategory.Compact;
        if (widthDp >= 600) return MobileScreenCategory.Tablet;
        if (widthDp >= 440) return MobileScreenCategory.Large;
        return MobileScreenCategory.Standard;
    }

    public static MobileHardwareTier ClassifyHardwareTier(double ramGb, int cores, bool isLowRamDevice = false)
    {
        if (isLowRamDevice || ramGb <= 3.2 || cores <= 4)
        {
            return MobileHardwareTier.Entry;
        }
        if (ramGb >= 7.2 && cores >= 8)
        {
            return MobileHardwareTier.High;
        }
        return MobileHardwareTier.Mid;
    }

    public static int DetermineOptimalUdpBufferSizeBytes(MobileHardwareTier tier)
    {
        return tier switch
        {
            MobileHardwareTier.Entry => 1024 * 1024,      // 1 MB: alívio de memória contra OOM
            MobileHardwareTier.High  => 4 * 1024 * 1024,  // 4 MB: vazão plena gigabit sem drops
            _                        => 2 * 1024 * 1024   // 2 MB: padrão estável
        };
    }

    public static int DetermineTelemetryRefreshIntervalMs(MobileHardwareTier tier)
    {
        return tier switch
        {
            MobileHardwareTier.Entry => 1000, // 1000ms: evita engasgos na UI thread em CPUs de entrada
            _                        => 500   // 500ms: fluidez contínua
        };
    }

    public static bool DetermineVendorOtgRequirement(string manufacturer, out string guide)
    {
        var mfrLower = (manufacturer ?? string.Empty).ToLowerInvariant();

        if (mfrLower.Contains("xiaomi") || mfrLower.Contains("redmi") || mfrLower.Contains("poco"))
        {
            guide = "Xiaomi/Redmi/POCO (MIUI/HyperOS): Caso o cabo OTG não acenda, vá em Configurações > Configurações Adicionais > Conexão OTG e ative o interruptor.";
            return true;
        }
        if (mfrLower.Contains("oppo") || mfrLower.Contains("realme"))
        {
            guide = "Realme/Oppo (ColorOS): O sistema desliga a conexão OTG após 10 minutos de inatividade. Acesse Configurações > Configurações do Sistema > Conexão OTG e ligue antes do teste.";
            return true;
        }
        if (mfrLower.Contains("vivo"))
        {
            guide = "Vivo (Funtouch OS): Acesse Configurações > Bluetooth e Dispositivos > OTG e ative.";
            return true;
        }
        if (mfrLower.Contains("samsung"))
        {
            guide = "Samsung (OneUI): OTG plug-and-play nativo. Para o teste de 15 minutos, certifique-se de que o SPARC não seja colocado em suspensão de bateria.";
            return false;
        }
        if (mfrLower.Contains("motorola"))
        {
            guide = "Motorola: Suporte nativo pleno a adaptadores USB OTG e Ethernet RJ45.";
            return false;
        }

        guide = "Dispositivo Android: Conectores OTG e Ethernet operando com calibração automática.";
        return false;
    }

    /// <summary>
    /// Converte códigos técnicos de modelo em nomes comerciais conhecidos
    /// </summary>
    public static string ResolveFriendlyModelName(string manufacturer, string rawModel)
    {
        var mfr = string.IsNullOrWhiteSpace(manufacturer) ? "Android" : manufacturer.Trim();
        var model = string.IsNullOrWhiteSpace(rawModel) ? "Dispositivo" : rawModel.Trim();
        var mfrClean = char.ToUpperInvariant(mfr[0]) + (mfr.Length > 1 ? mfr.Substring(1).ToLowerInvariant() : "");

        // Se o modelo já contiver a marca, normaliza
        if (model.StartsWith(mfr, StringComparison.OrdinalIgnoreCase))
        {
            return CapitalizeWords(model);
        }

        // Padrões Samsung Galaxy
        if (model.StartsWith("SM-A", StringComparison.OrdinalIgnoreCase) && model.Length >= 6)
        {
            var sub = model.Substring(4);
            if (sub.StartsWith("03")) return "Samsung Galaxy A03";
            if (sub.StartsWith("04")) return "Samsung Galaxy A04";
            if (sub.StartsWith("05")) return "Samsung Galaxy A05";
            if (sub.StartsWith("13")) return "Samsung Galaxy A13";
            if (sub.StartsWith("14")) return "Samsung Galaxy A14";
            if (sub.StartsWith("15")) return "Samsung Galaxy A15";
            if (sub.StartsWith("23")) return "Samsung Galaxy A23";
            if (sub.StartsWith("24")) return "Samsung Galaxy A24";
            if (sub.StartsWith("25")) return "Samsung Galaxy A25 5G";
            if (sub.StartsWith("33")) return "Samsung Galaxy A33 5G";
            if (sub.StartsWith("34")) return "Samsung Galaxy A34 5G";
            if (sub.StartsWith("35")) return "Samsung Galaxy A35 5G";
            if (sub.StartsWith("52")) return "Samsung Galaxy A52";
            if (sub.StartsWith("53")) return "Samsung Galaxy A53 5G";
            if (sub.StartsWith("54")) return "Samsung Galaxy A54 5G";
            if (sub.StartsWith("55")) return "Samsung Galaxy A55 5G";
            return $"Samsung Galaxy {model.ToUpperInvariant()}";
        }
        if (model.StartsWith("SM-S", StringComparison.OrdinalIgnoreCase) && model.Length >= 6)
        {
            var sub = model.Substring(4);
            if (sub.StartsWith("90")) return "Samsung Galaxy S22 Series";
            if (sub.StartsWith("91")) return "Samsung Galaxy S23 Series";
            if (sub.StartsWith("92")) return "Samsung Galaxy S24 Series";
            return $"Samsung Galaxy {model.ToUpperInvariant()} (Flagship)";
        }
        if (model.StartsWith("SM-M", StringComparison.OrdinalIgnoreCase))
        {
            return $"Samsung Galaxy {model.ToUpperInvariant()}";
        }
        if (model.StartsWith("SM-X", StringComparison.OrdinalIgnoreCase) || model.StartsWith("SM-T", StringComparison.OrdinalIgnoreCase))
        {
            return $"Samsung Galaxy Tab ({model.ToUpperInvariant()})";
        }

        // Padrões Motorola
        if (model.StartsWith("moto", StringComparison.OrdinalIgnoreCase))
        {
            return CapitalizeWords(model);
        }
        if (model.StartsWith("XT", StringComparison.OrdinalIgnoreCase))
        {
            return $"Motorola ({model.ToUpperInvariant()})";
        }

        // Padrões Xiaomi / Redmi / POCO
        if (model.StartsWith("Redmi", StringComparison.OrdinalIgnoreCase) || model.StartsWith("POCO", StringComparison.OrdinalIgnoreCase))
        {
            return CapitalizeWords(model);
        }
        if (model.StartsWith("22") || model.StartsWith("23") || model.StartsWith("24"))
        {
            return $"Xiaomi / Redmi ({model})";
        }

        // Padrões Realme
        if (model.StartsWith("RMX", StringComparison.OrdinalIgnoreCase))
        {
            return $"Realme ({model.ToUpperInvariant()})";
        }

        // Padrões Oppo
        if (model.StartsWith("CPH", StringComparison.OrdinalIgnoreCase))
        {
            return $"Oppo ({model.ToUpperInvariant()})";
        }

        return $"{mfrClean} {model}";
    }

    private static string CapitalizeWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Equals("poco", StringComparison.OrdinalIgnoreCase))
            {
                words[i] = "POCO";
            }
            else if (words[i].Equals("5g", StringComparison.OrdinalIgnoreCase))
            {
                words[i] = "5G";
            }
            else if (words[i].Length > 0)
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1).ToLowerInvariant();
            }
        }
        return string.Join(" ", words);
    }
}
