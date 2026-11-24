using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ForgeDesk.Infrastructure;

/// <summary>"#RRGGBB" → brush; null/invalid → transparent (so a fallback layer shows through).</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ToBrush(value as string) ?? Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;

    public static SolidColorBrush? ToBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>Item index → "Alt+N" hint for the first nine conversations.</summary>
public sealed class ShortcutHintConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && index is >= 0 and < 9 ? $"Alt+{index + 1}" : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DependencyProperty.UnsetValue;
}
