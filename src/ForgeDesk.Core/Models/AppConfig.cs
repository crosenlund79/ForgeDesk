using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ForgeDesk.Core.Models;

/// <summary>
/// Everything ForgeDesk persists locally. Contains no credentials, cookies or
/// conversation contents: browser sessions live in the separate WebView2 profile.
/// </summary>
public sealed class AppConfig : ObservableObject
{
    public const int CurrentSchemaVersion = 1;

    private string? _activeProjectId;
    private WindowPlacement? _window;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ObservableCollection<Project> Projects { get; set; } = [];

    public string? ActiveProjectId
    {
        get => _activeProjectId;
        set => Set(ref _activeProjectId, value);
    }

    public WindowPlacement? Window
    {
        get => _window;
        set => Set(ref _window, value);
    }

    public Preferences Preferences { get; set; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
public enum ThemePreference
{
    [JsonStringEnumMemberName("system")]
    System,

    [JsonStringEnumMemberName("light")]
    Light,

    [JsonStringEnumMemberName("dark")]
    Dark,
}

public sealed class Preferences : ObservableObject
{
    public const int DefaultMaxBrowsersPerProvider = 6;
    public const int MinBrowsersPerProvider = 1;
    public const int MaxBrowsersPerProviderLimit = 30;
    public const int DefaultKeepHiddenMinutes = 10;
    public const int MaxKeepHiddenMinutes = 24 * 60;
    public const double MinZoom = 0.25;
    public const double MaxZoom = 5.0;

    private ThemePreference _theme = ThemePreference.System;
    private int _maxBrowsersPerProvider = DefaultMaxBrowsersPerProvider;
    private int _keepHiddenBrowsersMinutes = DefaultKeepHiddenMinutes;
    private double _chatGptZoom = 1.0;
    private double _claudeZoom = 1.0;

    public ThemePreference Theme
    {
        get => _theme;
        set => Set(ref _theme, value);
    }

    /// <summary>
    /// Soft cap on live browser instances per side. Hidden conversations beyond this
    /// are released only after they have been hidden for <see cref="KeepHiddenBrowsersMinutes"/>.
    /// </summary>
    public int MaxBrowsersPerProvider
    {
        get => _maxBrowsersPerProvider;
        set => Set(ref _maxBrowsersPerProvider, Math.Clamp(value, MinBrowsersPerProvider, MaxBrowsersPerProviderLimit));
    }

    public int KeepHiddenBrowsersMinutes
    {
        get => _keepHiddenBrowsersMinutes;
        set => Set(ref _keepHiddenBrowsersMinutes, Math.Clamp(value, 0, MaxKeepHiddenMinutes));
    }

    public double ChatGptZoom
    {
        get => _chatGptZoom;
        set => Set(ref _chatGptZoom, ClampZoom(value));
    }

    public double ClaudeZoom
    {
        get => _claudeZoom;
        set => Set(ref _claudeZoom, ClampZoom(value));
    }

    public double GetZoom(Provider provider) => provider == Provider.ChatGpt ? ChatGptZoom : ClaudeZoom;

    public void SetZoom(Provider provider, double zoom)
    {
        if (provider == Provider.ChatGpt)
        {
            ChatGptZoom = zoom;
        }
        else
        {
            ClaudeZoom = zoom;
        }
    }

    public Preferences Clone() => new()
    {
        Theme = Theme,
        MaxBrowsersPerProvider = MaxBrowsersPerProvider,
        KeepHiddenBrowsersMinutes = KeepHiddenBrowsersMinutes,
        ChatGptZoom = ChatGptZoom,
        ClaudeZoom = ClaudeZoom,
    };

    private static double ClampZoom(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 1.0 : Math.Clamp(Math.Round(value, 2), MinZoom, MaxZoom);
}

/// <summary>Main window size and position in device-independent units.</summary>
public sealed class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }

    public bool IsUsable =>
        IsFinite(Left) && IsFinite(Top) && IsFinite(Width) && IsFinite(Height) && Width >= 200 && Height >= 150;

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}
