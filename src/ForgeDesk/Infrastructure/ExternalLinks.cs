using System.Diagnostics;

namespace ForgeDesk.Infrastructure;

/// <summary>Opens web links in the user's default browser (http, https and mailto only).</summary>
internal static class ExternalLinks
{
    public static bool Open(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto"))
        {
            Log.Warn($"Refused to open a non-web link externally: {Log.Safe(url)}");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {Log.Safe(url)} in the default browser", ex);
            return false;
        }
    }

    public static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Could not open folder", ex);
        }
    }
}
