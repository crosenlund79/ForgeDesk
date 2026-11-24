using System.Windows;
using ForgeDesk.Infrastructure;
using Microsoft.Web.WebView2.Core;

namespace ForgeDesk.Browser;

/// <summary>
/// Decides where links that ask for a new window go:
///  • sign-in popups and provider pages open in a small in-app window that shares the
///    browser profile (so "Sign in with Google/Apple/Microsoft" popups can talk back to the page);
///  • everything else (links in answers, citations, docs) opens in the default browser.
/// </summary>
internal static class NewWindowHandler
{
    private static readonly string[] InAppDomains =
    [
        "chatgpt.com", "openai.com", "claude.ai", "anthropic.com",
        "accounts.google.com", "appleid.apple.com",
        "login.microsoftonline.com", "login.live.com", "account.live.com", "login.microsoft.com",
    ];

    public static bool ShouldOpenInApp(string? url)
    {
        if (string.IsNullOrEmpty(url) || url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return true; // script-opened popups often start blank and then navigate
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return InAppDomains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal));
    }

    public static async void Handle(CoreWebView2NewWindowRequestedEventArgs e, Window owner)
    {
        var url = e.Uri;
        if (!ShouldOpenInApp(url))
        {
            e.Handled = true;
            ExternalLinks.Open(url);
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var popup = new PopupWindow(owner, e.WindowFeatures);
            popup.Show();
            var environment = await BrowserEnvironment.GetAsync();
            await popup.WebView.EnsureCoreWebView2Async(environment);
            popup.Attach();
            e.NewWindow = popup.WebView.CoreWebView2;
            e.Handled = true;
            Log.Info($"Opened in-app window for {Log.Safe(url)}");
        }
        catch (Exception ex)
        {
            // Fall back to WebView2's own popup window, which still shares the session.
            Log.Warn($"In-app window failed for {Log.Safe(url)}; using the default popup", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
