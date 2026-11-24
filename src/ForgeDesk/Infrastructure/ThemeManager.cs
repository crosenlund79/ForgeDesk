using System.Windows;
using System.Windows.Media;
using ForgeDesk.Core.Models;
using Microsoft.Win32;

namespace ForgeDesk.Infrastructure;

/// <summary>
/// Applies the light or dark palette: ForgeDesk's own colour tokens, WPF's Fluent control
/// theme (where available) and the DWM title bar. "System" follows the Windows app theme.
/// </summary>
internal static class ThemeManager
{
    private const string LightSource = "Themes/Light.xaml";
    private const string DarkSource = "Themes/Dark.xaml";
    private static ThemePreference _preference = ThemePreference.System;
    private static bool _listening;

    public static bool IsDark { get; private set; } = true;

    public static event EventHandler? Changed;

    public static void Apply(ThemePreference preference)
    {
        _preference = preference;
        var dark = preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => SystemPrefersDark(),
        };

        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        SwapPalette(app, dark);
        try
        {
            // Fluent styles for standard controls (menus, tooltips, scrollbars).
            app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch (Exception ex)
        {
            Log.Warn("Fluent theme unavailable; using classic control styles", ex);
        }

        // The Fluent theme can reset merged dictionaries; make sure our palette wins.
        SwapPalette(app, dark);
        IsDark = dark;

        foreach (Window window in app.Windows)
        {
            ApplyToWindow(window);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyToWindow(Window window)
    {
        if (window.IsLoaded || new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            NativeMethods.ApplyTitleBarTheme(window, IsDark);
        }
        else
        {
            window.SourceInitialized += (_, _) => NativeMethods.ApplyTitleBarTheme(window, IsDark);
        }
    }

    /// <summary>The background colour the embedded browser paints before a page renders.</summary>
    public static System.Drawing.Color ContentBackground
    {
        get
        {
            var color = Application.Current?.TryFindResource("ContentBackgroundColor") is Color c
                ? c
                : Color.FromRgb(0x21, 0x21, 0x21);
            return System.Drawing.Color.FromArgb(255, color.R, color.G, color.B);
        }
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value ? value == 0 : false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void SwapPalette(Application app, bool dark)
    {
        var dictionaries = app.Resources.MergedDictionaries;
        var wanted = dark ? DarkSource : LightSource;
        for (var i = 0; i < dictionaries.Count; i++)
        {
            var source = dictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.EndsWith(LightSource, StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith(DarkSource, StringComparison.OrdinalIgnoreCase))
            {
                if (!source.EndsWith(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    dictionaries[i] = new ResourceDictionary { Source = new Uri($"pack://application:,,,/ForgeDesk;component/{wanted}") };
                }

                // Move the palette to the end so it takes precedence over any theme dictionaries.
                if (i != dictionaries.Count - 1)
                {
                    var palette = dictionaries[i];
                    dictionaries.RemoveAt(i);
                    dictionaries.Add(palette);
                }

                return;
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/ForgeDesk;component/{wanted}") });
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_preference != ThemePreference.System || e.Category != UserPreferenceCategory.General)
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (SystemPrefersDark() != IsDark)
            {
                Apply(_preference);
            }
        });
    }
}
