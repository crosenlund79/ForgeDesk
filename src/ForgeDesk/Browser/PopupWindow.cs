using System.Windows;
using System.Windows.Media;
using ForgeDesk.Infrastructure;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ForgeDesk.Browser;

/// <summary>
/// A plain window for sign-in popups. The title always shows the site's host so it is clear
/// which website is asking for credentials.
/// </summary>
internal sealed class PopupWindow : Window
{
    public PopupWindow(Window owner, CoreWebView2WindowFeatures? features)
    {
        if (owner.IsVisible)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Width = features is { HasSize: true } ? Math.Clamp(features.Width, 380, 1400) : 560;
        Height = features is { HasSize: true } ? Math.Clamp(features.Height, 420, 1100) : 720;
        MinWidth = 320;
        MinHeight = 320;
        Title = "Sign in";
        Icon = owner.Icon;
        Background = TryFindResource("ContentBackgroundBrush") as Brush ?? Brushes.White;
        WebView = new WebView2 { DefaultBackgroundColor = ThemeManager.ContentBackground };
        Content = WebView;
        ThemeManager.ApplyToWindow(this);
        Closed += (_, _) =>
        {
            try
            {
                WebView.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("Error closing popup window", ex);
            }
        };
    }

    public WebView2 WebView { get; }

    public void Attach()
    {
        var core = WebView.CoreWebView2;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.WindowCloseRequested += (_, _) => Close();
        core.DocumentTitleChanged += (_, _) => UpdateTitle(core);
        core.SourceChanged += (_, _) => UpdateTitle(core);
        core.NewWindowRequested += (_, e) => NewWindowHandler.Handle(e, Owner ?? this);
    }

    private void UpdateTitle(CoreWebView2 core)
    {
        var host = Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) ? uri.Host : "Sign in";
        var title = core.DocumentTitle;
        Title = string.IsNullOrWhiteSpace(title) || title == core.Source ? host : $"{host} — {title}";
    }
}
