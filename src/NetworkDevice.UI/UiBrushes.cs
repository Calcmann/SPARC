using System;
using System.Collections.Concurrent;
using System.Windows.Media;

namespace NetworkDevice.UI;

/// <summary>
/// Provedor e cache global de instâncias congeladas (Frozen) de SolidColorBrush.
/// 
/// Motivação arquitetural:
/// No WPF, SolidColorBrush herda de Freezable. Quando instanciado diretamente via code-behind
/// sem chamar .Freeze(), o WPF tenta rastrear a hierarquia de contexto (InheritanceContext).
/// Se o mesmo brush for atribuído a múltiplos controles ou propriedades dependentes (como Background,
/// BorderBrush e Foreground nos cards de checklist ou passos superiores) e posteriormente reatribuído,
/// o WPF lança a exceção:
/// "O DependencyObject fornecido não é um contexto para este Freezable. (Parameter 'context')"
/// (Freezable.RemoveContextInformation).
/// 
/// Ao chamar .Freeze(), a propriedade IsFrozen torna-se true:
/// - O WPF desativa completamente o rastreamento de contexto e notificações de alteração;
/// - O brush pode ser compartilhado com segurança e eficiência entre ilimitados DependencyObjects;
/// - Desempenho máximo sem alocações desnecessárias na memória (GC).
/// </summary>
public static class UiBrushes
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> HexCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Color, SolidColorBrush> ColorCache = new();

    /// <summary>
    /// Retorna um SolidColorBrush congelado a partir de uma string hexadecimal (ex.: "#15803D", "#F0FDF4").
    /// </summary>
    public static SolidColorBrush Get(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Brushes.Transparent;

        return HexCache.GetOrAdd(hex, h =>
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(h);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                return Brushes.Transparent;
            }
        });
    }

    /// <summary>
    /// Retorna um SolidColorBrush congelado a partir de um valor Color.
    /// </summary>
    public static SolidColorBrush Get(Color color)
    {
        return ColorCache.GetOrAdd(color, c =>
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        });
    }
}
