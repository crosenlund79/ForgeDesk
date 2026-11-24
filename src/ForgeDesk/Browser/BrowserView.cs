using System.Windows;
using ForgeDesk.Core.Models;
using ForgeDesk.Core.Urls;
using ForgeDesk.Infrastructure;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ForgeDesk.Browser;

/// <summary>
/// One live browser instance showing one conversation on the provider's real website.
/// Instances are kept alive while hidden so a response can keep streaming in the background
/// and scroll position / unsent drafts survive switching.
/// </summary>
internal sealed class BrowserView : IDisposable
{
    private readonly Provider _provider;
    private readonly Preferences _preferences;
    private readonly Window _owner;
    private CoreWebView2? _core;
    private bool _disposed;
    private bool _applyingZoom;

    public BrowserView(Provider provider, string key, string homeUrl, Preferences preferences, Window owner)
    {
        _provider = provider;
        _preferences = preferences;
        _owner = owner;
        Key = key;
        HomeUrl = homeUrl;
        WebView = new WebView2
        {
            DefaultBackgroundColor = ThemeManager.ContentBackground,
            Visibility = Visibility.Hidden,
        };
        CreatedAt = DateTimeOffset.Now;
    }

    public WebView2 WebView { get; }

    /// <summary>Conversation identity used by the pool (changes if the page gets mapped).</summary>
    public string Key { get; set; }

    /// <summary>The mapped URL this instance belongs to; used for drift detection and "Return".</summary>
    public string HomeUrl { get; set; }

    public DateTimeOffset CreatedAt { get; }

    public string? CurrentUrl { get; private set; }

    public string? Title { get; private set; }

    public bool IsLoading { get; private set; } = true;

    public bool IsReady => _core is not null && !IsCrashed && !_disposed;

    /// <summary>The embedded browser for this view is gone; it must be recreated.</summary>
    public bool IsCrashed { get; private set; }

    /// <summary>A user-facing description of the last load problem, if any.</summary>
    public string? ErrorText { get; private set; }

    public bool IsPlayingAudio => _core is not null && !_disposed && SafeGet(() => _core.IsDocumentPlayingAudio);

    /// <summary>True when the page shown is no longer the mapped conversation.</summary>
    public bool IsDrifted =>
        CurrentUrl is not null &&
        !string.Equals(CurrentUrl, "about:blank", StringComparison.OrdinalIgnoreCase) &&
        !ConversationUrl.IsSameConversation(_provider, CurrentUrl, HomeUrl);

    // Diagnostics (smoke test / log).
    public int DocumentNavigations { get; private set; }

    public bool? LastNavigationSucceeded { get; private set; }

    public int LastHttpStatus { get; private set; }

    public string? LastWebErrorStatus { get; private set; }

    public TaskCompletionSource FirstNavigationCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler? StateChanged;

    public async Task InitializeAsync()
    {
        try
        {
            var environment = await BrowserEnvironment.GetAsync();
            if (_disposed)
            {
                return;
            }

            await WebView.EnsureCoreWebView2Async(environment);
            if (_disposed)
            {
                return;
            }

            _core = WebView.CoreWebView2;
            Configure(_core);
            ApplyTheme();
            ApplyZoom(_preferences.GetZoom(_provider));
            _core.Navigate(HomeUrl);
        }
        catch (Exception ex) when (!_disposed)
        {
            Log.Error($"{_provider.DisplayName()} view failed to start ({Log.Safe(HomeUrl)})", ex);
            IsCrashed = true;
            IsLoading = false;
            ErrorText = ex is WebView2RuntimeNotFoundException
                ? "The Microsoft Edge WebView2 Runtime isn't installed."
                : "The embedded browser couldn't start.";
            FirstNavigationCompleted.TrySetResult();
            Raise();
        }
    }

    public void NavigateHome()
    {
        if (!IsReady)
        {
            return;
        }

        ErrorText = null;
        _core!.Navigate(HomeUrl);
        Raise();
    }

    /// <returns>false when the view is crashed and must be recreated instead.</returns>
    public bool Reload()
    {
        if (!IsReady)
        {
            return false;
        }

        ErrorText = null;
        if (string.IsNullOrEmpty(CurrentUrl) || CurrentUrl.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            _core!.Navigate(HomeUrl);
        }
        else
        {
            _core!.Reload();
        }

        Raise();
        return true;
    }

    public void ApplyTheme()
    {
        if (!IsReady)
        {
            return;
        }

        try
        {
            WebView.DefaultBackgroundColor = ThemeManager.ContentBackground;
            _core!.Profile.PreferredColorScheme = ThemeManager.IsDark
                ? CoreWebView2PreferredColorScheme.Dark
                : CoreWebView2PreferredColorScheme.Light;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not apply the colour scheme to a browser view", ex);
        }
    }

    public void ApplyZoom(double zoom)
    {
        if (!IsReady || Math.Abs(WebView.ZoomFactor - zoom) < 0.001)
        {
            return;
        }

        _applyingZoom = true;
        try
        {
            WebView.ZoomFactor = zoom;
        }
        finally
        {
            _applyingZoom = false;
        }
    }

    public Task<string?> CapturePreviewAsync(string path) => CapturePngAsync(path);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_core is not null)
        {
            _core.SourceChanged -= OnSourceChanged;
            _core.NavigationStarting -= OnNavigationStarting;
            _core.NavigationCompleted -= OnNavigationCompleted;
            _core.ProcessFailed -= OnProcessFailed;
            _core.NewWindowRequested -= OnNewWindowRequested;
            _core.DocumentTitleChanged -= OnDocumentTitleChanged;
        }

        WebView.ZoomFactorChanged -= OnZoomFactorChanged;
        try
        {
            WebView.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warn("Error while closing a browser view", ex);
        }
    }

    private void Configure(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = true;  // copy/paste, spell-check suggestions
        settings.AreBrowserAcceleratorKeysEnabled = true; // Ctrl+F, Ctrl+P, zoom, etc.
        settings.IsZoomControlEnabled = true;
        settings.IsStatusBarEnabled = true;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = true;

        core.SourceChanged += OnSourceChanged;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.ProcessFailed += OnProcessFailed;
        core.NewWindowRequested += OnNewWindowRequested;
        core.DocumentTitleChanged += OnDocumentTitleChanged;
        WebView.ZoomFactorChanged += OnZoomFactorChanged;
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        CurrentUrl = _core?.Source;
        Raise();
    }

    private void OnDocumentTitleChanged(object? sender, object e)
    {
        Title = _core?.DocumentTitle;
        Raise();
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        IsLoading = true;
        ErrorText = null;
        DocumentNavigations++;
        Raise();
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        IsLoading = false;
        LastNavigationSucceeded = e.IsSuccess;
        LastHttpStatus = e.HttpStatusCode;
        LastWebErrorStatus = e.WebErrorStatus.ToString();
        CurrentUrl = _core?.Source ?? CurrentUrl;

        if (!e.IsSuccess)
        {
            var reportable = e.WebErrorStatus switch
            {
                CoreWebView2WebErrorStatus.OperationCanceled => false,
                CoreWebView2WebErrorStatus.Unknown => e.HttpStatusCode >= 500,
                _ => true,
            };
            if (reportable)
            {
                ErrorText = Describe(e.WebErrorStatus, e.HttpStatusCode);
                Log.Warn($"{_provider.DisplayName()} navigation failed: {e.WebErrorStatus} HTTP {e.HttpStatusCode} ({Log.Safe(CurrentUrl)})");
            }
        }

        FirstNavigationCompleted.TrySetResult();
        Raise();
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Warn($"{_provider.DisplayName()} browser process failure: {e.ProcessFailedKind} ({e.Reason})");
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                IsCrashed = true;
                IsLoading = false;
                ErrorText = "The embedded browser closed unexpectedly.";
                BrowserEnvironment.Invalidate();
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                IsLoading = false;
                ErrorText = "This page stopped unexpectedly.";
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                ErrorText = "This page isn't responding.";
                break;
            default:
                return; // GPU/utility/iframe processes recover on their own.
        }

        Raise();
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) =>
        NewWindowHandler.Handle(e, _owner);

    private void OnZoomFactorChanged(object? sender, EventArgs e)
    {
        if (!_applyingZoom && !_disposed)
        {
            _preferences.SetZoom(_provider, WebView.ZoomFactor);
        }
    }

    private async Task<string?> CapturePngAsync(string path)
    {
        if (!IsReady)
        {
            return "not ready";
        }

        try
        {
            await using var stream = System.IO.File.Create(path);
            await _core!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string Describe(CoreWebView2WebErrorStatus status, int httpStatus) => status switch
    {
        CoreWebView2WebErrorStatus.CannotConnect or
        CoreWebView2WebErrorStatus.ServerUnreachable or
        CoreWebView2WebErrorStatus.HostNameNotResolved or
        CoreWebView2WebErrorStatus.Disconnected => "Couldn't connect. Check your internet connection.",
        CoreWebView2WebErrorStatus.Timeout => "The page took too long to respond.",
        CoreWebView2WebErrorStatus.ConnectionAborted or
        CoreWebView2WebErrorStatus.ConnectionReset => "The connection was interrupted.",
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or
        CoreWebView2WebErrorStatus.CertificateExpired or
        CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or
        CoreWebView2WebErrorStatus.CertificateRevoked or
        CoreWebView2WebErrorStatus.CertificateIsInvalid => "There's a problem with the site's security certificate.",
        _ when httpStatus >= 500 => $"The site returned an error (HTTP {httpStatus}).",
        _ => $"The page couldn't be loaded ({status}).",
    };

    private static bool SafeGet(Func<bool> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);
}
