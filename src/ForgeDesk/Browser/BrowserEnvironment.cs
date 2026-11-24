using System.IO;
using ForgeDesk.Infrastructure;
using Microsoft.Web.WebView2.Core;

namespace ForgeDesk.Browser;

/// <summary>
/// One WebView2 environment (one browser process group, one persistent profile) shared by
/// every pane and popup. The profile folder holds the provider sign-in cookies; it is managed
/// by WebView2 and is never read or exported by ForgeDesk.
/// </summary>
internal static class BrowserEnvironment
{
    private static Task<CoreWebView2Environment>? _environment;

    public static string UserDataFolder { get; set; } = string.Empty;

    /// <summary>Raised on the UI thread after the browser process died and the environment was reset.</summary>
    public static event EventHandler? Reset;

    public static Task<CoreWebView2Environment> GetAsync()
    {
        if (_environment is null || _environment.IsFaulted || _environment.IsCanceled)
        {
            _environment = CreateAsync();
        }

        return _environment;
    }

    public static void Invalidate()
    {
        if (_environment is null)
        {
            return;
        }

        _environment = null;
        Log.Warn("Browser process exited; the browser environment will be recreated on next use.");
        Reset?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The installed WebView2 Runtime version, or null when it is missing.</summary>
    public static string? TryGetRuntimeVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error("Could not query the WebView2 Runtime version", ex);
            return null;
        }
    }

    private static async Task<CoreWebView2Environment> CreateAsync()
    {
        Directory.CreateDirectory(UserDataFolder);
        var options = new CoreWebView2EnvironmentOptions();
        var environment = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: UserDataFolder, options: options);
        Log.Info($"WebView2 environment ready (runtime {environment.BrowserVersionString}).");
        return environment;
    }
}
