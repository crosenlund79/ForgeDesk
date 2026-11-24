using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeDesk.Infrastructure;

namespace ForgeDesk.Views;

internal static class DialogSupport
{
    /// <summary>Project colour choices (null = no colour).</summary>
    public static readonly string?[] Palette =
    [
        null, "#E5484D", "#F76B15", "#FFB224", "#46A758", "#12A594",
        "#0090FF", "#3E63DD", "#8E4EC6", "#D6409F", "#8D8D86",
    ];

    public static void Prepare(Window dialog, Window? owner)
    {
        owner ??= Application.Current?.MainWindow;
        if (owner is not null && owner != dialog && owner.IsVisible)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.Icon = owner.Icon;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        ThemeManager.ApplyToWindow(dialog);
    }

    /// <summary>Fills a panel with colour swatches and returns a getter for the chosen colour.</summary>
    public static Func<string?> BuildSwatches(Panel panel, string? selected, Action<string?>? onChanged = null)
    {
        panel.Children.Clear();
        var group = "swatch-" + Guid.NewGuid().ToString("N");
        string? current = selected;
        foreach (var color in Palette)
        {
            var brush = ColorToBrushConverter.ToBrush(color);
            var button = new RadioButton
            {
                GroupName = group,
                Style = (Style)panel.FindResource("SwatchRadioButton"),
                Background = brush ?? Brushes.Transparent,
                BorderBrush = brush is null ? (Brush)panel.FindResource("TextTertiaryBrush") : Brushes.Transparent,
                IsChecked = string.Equals(color, selected, StringComparison.OrdinalIgnoreCase),
                ToolTip = color ?? "No colour",
                Tag = color,
            };
            if (color is null)
            {
                button.Content = new TextBlock
                {
                    Text = "",
                    FontFamily = (FontFamily)panel.FindResource("IconFont"),
                    FontSize = 9,
                    Foreground = (Brush)panel.FindResource("TextTertiaryBrush"),
                };
            }

            button.Checked += (_, _) =>
            {
                current = (string?)button.Tag;
                onChanged?.Invoke(current);
            };
            panel.Children.Add(button);
        }

        return () => current;
    }
}
