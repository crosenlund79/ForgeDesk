using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ForgeDesk.Infrastructure;

/// <summary>Builds menu items in code so menus always reflect current state when opened.</summary>
internal static class MenuFactory
{
    public static MenuItem Item(string header, Action? action, string? glyph = null, bool enabled = true, string? gesture = null, bool? isChecked = null)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = enabled,
            InputGestureText = gesture ?? string.Empty,
        };

        if (isChecked is { } check)
        {
            item.IsCheckable = false;
            item.IsChecked = check;
        }
        else if (glyph is not null)
        {
            item.Icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)Application.Current.FindResource("IconFont"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        if (action is not null)
        {
            item.Click += (_, _) => action();
        }

        return item;
    }

    public static MenuItem Submenu(string header, string? glyph, params object[] children)
    {
        var item = Item(header, null, glyph);
        foreach (var child in children)
        {
            item.Items.Add(child);
        }

        return item;
    }

    public static MenuItem Caption(string text) => new()
    {
        Header = text,
        IsEnabled = false,
        FontSize = 11,
    };
}
