using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ForgeDesk.Browser;
using ForgeDesk.Core;
using ForgeDesk.Core.Browsers;
using ForgeDesk.Core.Models;
using ForgeDesk.Core.Urls;
using ForgeDesk.Infrastructure;

namespace ForgeDesk.Views;

/// <summary>
/// One side of the desk (ChatGPT on the left, Claude on the right): a compact header with the
/// project's mapped conversations, and the provider's real website below it.
/// Each conversation gets its own live browser instance, kept alive while hidden.
/// </summary>
public partial class PaneView : UserControl
{
    private readonly DispatcherTimer _commitTimer;
    private readonly HashSet<string> _browsingProjects = new(StringComparer.Ordinal);
    private MainWindow _host = null!;
    private Workspace _workspace = null!;
    private BrowserPool<BrowserView> _pool = null!;
    private Project? _project;
    private ObservableCollection<ConversationMapping>? _list;
    private ConversationMapping? _mapping;
    private ConversationMapping? _subscribedMapping;
    private BrowserView? _view;
    private bool _browsing;
    private bool _suppressCombo;
    private bool _hasPending;
    private ConversationMapping? _pending;
    private Action? _noticePrimary;
    private Action? _noticeSecondary;
    private Action? _emptyPrimary;
    private Action? _emptySecondary;

    public PaneView()
    {
        InitializeComponent();
        _commitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _commitTimer.Tick += (_, _) =>
        {
            _commitTimer.Stop();
            if (!ConversationCombo.IsDropDownOpen)
            {
                CommitPending();
            }
        };
    }

    /// <summary>Raised when the user interacts with this side (used for "active side" shortcuts).</summary>
    public event EventHandler? Activated;

    public Provider Provider { get; private set; }

    internal BrowserPool<BrowserView> Pool => _pool;

    internal BrowserView? CurrentView => _view;

    public ConversationMapping? CurrentMapping => _mapping;

    public bool IsBrowsing => _browsing;

    /// <summary>The notice bar text, if it is showing (diagnostics).</summary>
    internal string? NoticeMessage => NoticeBar.Visibility == Visibility.Visible ? NoticeText.Text : null;

    private string BrowseKey => $"{Provider}:browse";

    // ================================================================ setup

    public void Initialize(MainWindow host, Workspace workspace, Provider provider)
    {
        _host = host;
        _workspace = workspace;
        Provider = provider;
        ProviderLabel.Text = provider.DisplayName();
        ProviderBadge.SetResourceReference(Border.BackgroundProperty, provider == Provider.ChatGpt ? "ChatGptBrush" : "ClaudeBrush");

        var preferences = workspace.Preferences;
        _pool = new BrowserPool<BrowserView>(
            preferences.MaxBrowsersPerProvider,
            TimeSpan.FromMinutes(preferences.KeepHiddenBrowsersMinutes))
        {
            // Never release a page that is playing audio (e.g. voice mode or read-aloud).
            IsBusy = view => view.IsPlayingAudio,
        };
        preferences.PropertyChanged += OnPreferencesChanged;
        ThemeManager.Changed += (_, _) =>
        {
            foreach (var view in _pool.Instances)
            {
                view.ApplyTheme();
            }
        };

        AddHandler(GotFocusEvent, new RoutedEventHandler((_, _) => Activated?.Invoke(this, EventArgs.Empty)), handledEventsToo: true);
        AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => Activated?.Invoke(this, EventArgs.Empty)), handledEventsToo: true);
        UpdateChrome();
    }

    /// <summary>Shows a project's desk on this side; browser instances of other projects stay alive.</summary>
    public void ShowProject(Project? project)
    {
        if (_list is not null)
        {
            _list.CollectionChanged -= OnListChanged;
        }

        _project = project;
        _list = project?.Conversations(Provider);
        if (_list is not null)
        {
            _list.CollectionChanged += OnListChanged;
        }

        _commitTimer.Stop();
        _hasPending = false;
        var selection = project is null ? null : _workspace.ResolveSelection(project, Provider);
        if (project is not null && selection is not null && project.Desk.GetSelectedId(Provider) != selection.Id)
        {
            // Remember what is shown so the desk comes back exactly the same, even after reordering.
            _workspace.SelectConversation(project, Provider, selection);
        }

        SetComboSelection(_list, selection);

        if (project is null)
        {
            _browsing = false;
            SetMapping(null);
            SetCurrent(null);
        }
        else if (_browsingProjects.Contains(project.Id))
        {
            ShowBrowse();
        }
        else
        {
            ShowMapping(selection);
        }
    }

    // ================================================================ navigation between conversations

    public bool SelectIndex(int index)
    {
        if (_list is null || index < 0 || index >= _list.Count)
        {
            return false;
        }

        SelectMapping(_list[index]);
        return true;
    }

    public bool SelectRelative(int delta)
    {
        if (_list is null || _list.Count == 0)
        {
            return false;
        }

        var index = _mapping is null ? (delta > 0 ? -1 : 0) : _list.IndexOf(_mapping);
        var next = ((index + delta) % _list.Count + _list.Count) % _list.Count;
        SelectMapping(_list[next]);
        return true;
    }

    public void SelectMapping(ConversationMapping mapping)
    {
        if (_project is null || _list is null || !_list.Contains(mapping))
        {
            return;
        }

        SetComboSelection(_list, mapping);
        _workspace.SelectConversation(_project, Provider, mapping);
        ShowMapping(mapping);
        FocusBrowser();
    }

    public void FocusBrowser()
    {
        if (_view is { } view)
        {
            view.WebView.Focus();
        }
    }

    public void ReloadCurrent()
    {
        if (_view is null)
        {
            if (_mapping is not null)
            {
                ShowMapping(_mapping);
            }

            return;
        }

        if (!_view.Reload())
        {
            Recreate(_view);
        }
    }

    public void SetMaximised(bool maximised)
    {
        FocusButton.Content = maximised ? "" : "";
        FocusButton.ToolTip = maximised ? "Restore the split view (F11)" : "Maximise this side (F11)";
    }

    /// <summary>Releases hidden instances beyond the configured limit (see <see cref="BrowserPool{T}"/>).</summary>
    public void TrimPool()
    {
        foreach (var released in _pool.TrimExcess())
        {
            Log.Info($"Released a hidden {Provider.DisplayName()} conversation to save memory ({_pool.Count} still open).");
            DisposeView(released);
        }
    }

    public void ReleaseAll()
    {
        foreach (var view in _pool.Clear())
        {
            DisposeView(view);
        }

        _view = null;
    }

    public void AddConversation()
    {
        if (_project is null)
        {
            return;
        }

        var mapping = ConversationDialog.Add(_host, _workspace, _project, Provider);
        if (mapping is not null && mapping.Provider == Provider)
        {
            SelectMapping(mapping);
        }
    }

    public void ShowBrowse()
    {
        if (_project is null)
        {
            return;
        }

        _browsing = true;
        _browsingProjects.Add(_project.Id);
        SetMapping(null);
        SetComboSelection(_list, null);
        ShowKey(BrowseKey, Provider.HomeUrl());
    }

    private void ExitBrowse()
    {
        if (_project is null)
        {
            return;
        }

        _browsing = false;
        _browsingProjects.Remove(_project.Id);
        var selection = _workspace.ResolveSelection(_project, Provider);
        SetComboSelection(_list, selection);
        ShowMapping(selection);
    }

    private void ShowMapping(ConversationMapping? mapping)
    {
        _browsing = false;
        if (_project is not null)
        {
            _browsingProjects.Remove(_project.Id);
        }

        SetMapping(mapping);
        if (mapping is null)
        {
            SetCurrent(null);
            return;
        }

        var check = ConversationUrl.Check(mapping.Url);
        if (!check.IsValid || check.Provider != Provider)
        {
            SetCurrent(null);
            return;
        }

        ShowKey(ConversationUrl.GetKey(Provider, mapping.Url), mapping.Url);
    }

    private void ShowKey(string key, string url)
    {
        if (!_pool.TryGet(key, out var view))
        {
            view = CreateView(key, url);
            _pool.Add(key, view);
        }
        else if (view.IsCrashed)
        {
            view = Recreate(view, show: false);
        }

        SetCurrent(view);
        TrimPool();
    }

    private void SetCurrent(BrowserView? view)
    {
        _view = view;
        if (view is null)
        {
            _pool.HideShown();
        }
        else
        {
            _pool.Show(view.Key);
        }

        foreach (var instance in _pool.Instances)
        {
            instance.WebView.Visibility = ReferenceEquals(instance, view) ? Visibility.Visible : Visibility.Hidden;
        }

        UpdateChrome();
    }

    private BrowserView CreateView(string key, string url)
    {
        var view = new BrowserView(Provider, key, url, _workspace.Preferences, _host);
        view.StateChanged += OnViewStateChanged;
        BrowserHost.Children.Insert(0, view.WebView);
        _ = view.InitializeAsync();
        Log.Info($"Opened a {Provider.DisplayName()} browser instance ({_pool.Count + 1} open on this side).");
        return view;
    }

    private BrowserView Recreate(BrowserView old, bool show = true)
    {
        var key = old.Key;
        var home = old.HomeUrl;
        _pool.Remove(key);
        DisposeView(old);
        var fresh = CreateView(key, home);
        _pool.Add(key, fresh);
        if (show)
        {
            SetCurrent(fresh);
        }

        return fresh;
    }

    private void DisposeView(BrowserView view)
    {
        view.StateChanged -= OnViewStateChanged;
        BrowserHost.Children.Remove(view.WebView);
        view.Dispose();
        if (ReferenceEquals(view, _view))
        {
            _view = null;
        }
    }

    private void SetMapping(ConversationMapping? mapping)
    {
        if (_subscribedMapping is not null)
        {
            _subscribedMapping.PropertyChanged -= OnMappingPropertyChanged;
        }

        _mapping = mapping;
        _subscribedMapping = mapping;
        if (mapping is not null)
        {
            mapping.PropertyChanged += OnMappingPropertyChanged;
        }
    }

    private void SetComboSelection(ObservableCollection<ConversationMapping>? list, ConversationMapping? selection)
    {
        _suppressCombo = true;
        try
        {
            if (!ReferenceEquals(ConversationCombo.ItemsSource, list))
            {
                ConversationCombo.ItemsSource = list;
            }

            ConversationCombo.SelectedItem = selection;
        }
        finally
        {
            _suppressCombo = false;
        }
    }

    // ================================================================ events

    private void OnViewStateChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _view))
        {
            UpdateChrome();
        }
    }

    private void OnMappingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _mapping))
        {
            return;
        }

        if (e.PropertyName == nameof(ConversationMapping.Url) && !_browsing)
        {
            ShowMapping(_mapping);
        }
        else
        {
            UpdateChrome();
        }
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_project is null || _list is null)
        {
            return;
        }

        if (_mapping is not null && !_list.Contains(_mapping))
        {
            // The shown mapping was removed: fall back to a neighbour (its browser instance is
            // simply released later; nothing happens to the conversation itself).
            var next = _workspace.ResolveSelection(_project, Provider);
            SetComboSelection(_list, next);
            ShowMapping(next);
        }
        else if (_mapping is null && !_browsing && _list.Count > 0)
        {
            var next = _workspace.ResolveSelection(_project, Provider);
            SetComboSelection(_list, next);
            ShowMapping(next);
        }
        else
        {
            UpdateChrome();
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        var preferences = _workspace.Preferences;
        switch (e.PropertyName)
        {
            case nameof(Preferences.MaxBrowsersPerProvider):
                _pool.MaxLive = preferences.MaxBrowsersPerProvider;
                TrimPool();
                break;
            case nameof(Preferences.KeepHiddenBrowsersMinutes):
                _pool.MinHiddenBeforeRelease = TimeSpan.FromMinutes(preferences.KeepHiddenBrowsersMinutes);
                TrimPool();
                break;
            case nameof(Preferences.ChatGptZoom) when Provider == Provider.ChatGpt:
            case nameof(Preferences.ClaudeZoom) when Provider == Provider.Claude:
                foreach (var view in _pool.Instances)
                {
                    view.ApplyZoom(preferences.GetZoom(Provider));
                }

                break;
        }
    }

    private void ConversationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCombo)
        {
            return;
        }

        _pending = ConversationCombo.SelectedItem as ConversationMapping;
        _hasPending = true;
        if (!ConversationCombo.IsDropDownOpen)
        {
            // Keyboard browsing of a closed list: wait briefly so we don't open every item.
            _commitTimer.Stop();
            _commitTimer.Start();
        }
    }

    private void ConversationCombo_DropDownClosed(object? sender, EventArgs e)
    {
        if (_hasPending)
        {
            _commitTimer.Stop();
            CommitPending();
        }
    }

    private void CommitPending()
    {
        if (!_hasPending)
        {
            return;
        }

        _hasPending = false;
        var mapping = _pending;
        _pending = null;
        if (mapping is null || _project is null || _list is null || !_list.Contains(mapping))
        {
            UpdateChrome();
            return;
        }

        if (ReferenceEquals(mapping, _mapping) && !_browsing)
        {
            return;
        }

        _workspace.SelectConversation(_project, Provider, mapping);
        ShowMapping(mapping);
        FocusBrowser();
    }

    // ================================================================ chrome

    private string? CurrentLink()
    {
        var url = _view?.CurrentUrl;
        if (url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return url;
        }

        return _mapping?.Url;
    }

    private void UpdateChrome()
    {
        var hasProject = _project is not null;
        var count = _list?.Count ?? 0;

        if (ConversationCombo.SelectedItem is null)
        {
            ComboPlaceholder.Text = !hasProject ? string.Empty
                : _browsing ? $"Browsing {Provider.DisplayName()}"
                : count == 0 ? "No conversations mapped"
                : "Choose a conversation";
            ComboPlaceholder.Visibility = Visibility.Visible;
        }
        else
        {
            ComboPlaceholder.Visibility = Visibility.Collapsed;
        }

        ConversationCombo.IsEnabled = hasProject && count > 0;
        var view = _view;
        ReloadButton.IsEnabled = view is not null || _mapping is not null;
        var link = CurrentLink();
        CopyButton.IsEnabled = link is not null;
        ExternalButton.IsEnabled = link is not null;
        MoreButton.IsEnabled = hasProject;
        FocusButton.IsEnabled = hasProject;

        var loading = view is { IsLoading: true, IsCrashed: false };
        LoadingBar.IsIndeterminate = loading;
        LoadingBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;

        if (view is null && hasProject)
        {
            ConfigureEmptyState();
            EmptyState.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
        }

        ConfigureNotice(view);
    }

    private void ConfigureEmptyState()
    {
        var name = Provider.DisplayName();
        if (_mapping is not null)
        {
            var check = ConversationUrl.Check(_mapping.Url);
            EmptyGlyph.Text = "";
            EmptyTitle.Text = "This link can't be opened";
            EmptyBody.Text = $"“{_mapping.Name}” doesn't have a valid {name} link. {check.Error ?? $"It isn't a {Provider.Domain()} address."}";
            SetEmptyButtons("Edit conversation…", EditCurrentMapping, null, null);
            return;
        }

        if ((_list?.Count ?? 0) > 0)
        {
            EmptyGlyph.Text = "";
            EmptyTitle.Text = "Choose a conversation";
            EmptyBody.Text = "Pick one of this project's conversations from the list above.";
            SetEmptyButtons(null, null, null, null);
            return;
        }

        EmptyGlyph.Text = "";
        EmptyTitle.Text = $"No {name} conversations in {_project?.Name}";
        EmptyBody.Text = $"Map an existing conversation by pasting its link. Or open {name}, sign in if needed, find the conversation and map the page you're on.";
        SetEmptyButtons("Add conversation…", AddConversation, $"Open {name}", ShowBrowse);
    }

    private void SetEmptyButtons(string? primary, Action? primaryAction, string? secondary, Action? secondaryAction)
    {
        _emptyPrimary = primaryAction;
        _emptySecondary = secondaryAction;
        EmptyPrimary.Content = primary;
        EmptyPrimary.Visibility = primary is null ? Visibility.Collapsed : Visibility.Visible;
        EmptySecondary.Content = secondary;
        EmptySecondary.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private enum NoticeStyle { Info, Warning, Error }

    private void ConfigureNotice(BrowserView? view)
    {
        if (view is null)
        {
            HideNotice();
            return;
        }

        if (view.IsCrashed || view.ErrorText is not null)
        {
            ShowNotice(NoticeStyle.Error, view.ErrorText ?? "This page stopped unexpectedly.",
                "Reload", ReloadCurrent,
                _mapping is not null && view.IsDrifted ? "Return" : null, ReturnToMapping);
            return;
        }

        if (_browsing)
        {
            var mappable = IsMappable(view.CurrentUrl, out var alreadyMapped);
            if (alreadyMapped is not null)
            {
                ShowNotice(NoticeStyle.Info, $"This page is mapped as “{alreadyMapped.Name}”.",
                    "Switch to it", () => SelectMapping(alreadyMapped), "Close", ExitBrowse);
            }
            else
            {
                ShowNotice(NoticeStyle.Info, $"Browsing {Provider.DisplayName()} — open a conversation, then map it to {_project?.Name}.",
                    mappable ? "Map this page…" : null, MapCurrentPage, "Close", ExitBrowse);
            }

            return;
        }

        if (_mapping is not null && view.IsDrifted && view.IsOnSignInPage)
        {
            ShowNotice(NoticeStyle.Info, $"Sign in to {Provider.DisplayName()} to open “{_mapping.Name}”. You'll be taken back to it afterwards.",
                null, null, "Return", ReturnToMapping);
            return;
        }

        if (_mapping is not null && view.IsDrifted && !view.HasShownHome && !view.IsLoading)
        {
            ShowNotice(NoticeStyle.Info, $"“{_mapping.Name}” didn't open. If you're signed out, sign in to {Provider.DisplayName()} first; then choose Return.",
                "Return", ReturnToMapping, null, null);
            return;
        }

        if (_mapping is not null && view.IsDrifted)
        {
            var mappable = IsMappable(view.CurrentUrl, out var alreadyMapped);
            ShowNotice(NoticeStyle.Warning, $"You've moved away from “{_mapping.Name}”.",
                "Return", ReturnToMapping,
                alreadyMapped is not null ? $"Switch to “{alreadyMapped.Name}”" : mappable ? "Map this page…" : null,
                alreadyMapped is not null ? () => SelectMapping(alreadyMapped) : MapCurrentPage);
            return;
        }

        HideNotice();
    }

    private bool IsMappable(string? url, out ConversationMapping? alreadyMapped)
    {
        alreadyMapped = null;
        var check = ConversationUrl.Check(url);
        if (!check.IsValid || check.Provider != Provider || _project is null)
        {
            return false;
        }

        alreadyMapped = _workspace.FindMapping(_project, Provider, check.NormalizedUrl!);
        if (alreadyMapped is not null && ReferenceEquals(alreadyMapped, _mapping))
        {
            alreadyMapped = null;
        }

        return check.Kind is ConversationUrlKind.Conversation or ConversationUrlKind.ProjectPage;
    }

    private void ShowNotice(NoticeStyle style, string text, string? primary, Action? primaryAction, string? secondary, Action? secondaryAction)
    {
        var (background, foreground, glyph) = style switch
        {
            NoticeStyle.Error => ("ErrorBackgroundBrush", "ErrorForegroundBrush", ""),
            NoticeStyle.Warning => ("NoticeBackgroundBrush", "NoticeForegroundBrush", ""),
            _ => ("InfoBackgroundBrush", "InfoForegroundBrush", ""),
        };
        NoticeBar.SetResourceReference(Border.BackgroundProperty, background);
        NoticeIcon.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        NoticeText.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        NoticePrimary.SetResourceReference(ForegroundProperty, foreground);
        NoticeSecondary.SetResourceReference(ForegroundProperty, foreground);
        NoticeIcon.Text = glyph;
        NoticeText.Text = text;
        NoticeText.ToolTip = text;
        _noticePrimary = primaryAction;
        _noticeSecondary = secondaryAction;
        NoticePrimary.Content = primary;
        NoticePrimary.Visibility = primary is null ? Visibility.Collapsed : Visibility.Visible;
        NoticeSecondary.Content = secondary;
        NoticeSecondary.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
        NoticeBar.Visibility = Visibility.Visible;
    }

    private void HideNotice()
    {
        NoticeBar.Visibility = Visibility.Collapsed;
        _noticePrimary = _noticeSecondary = null;
    }

    // ================================================================ actions

    private void ReturnToMapping()
    {
        if (_view is null)
        {
            return;
        }

        if (_view.IsCrashed)
        {
            Recreate(_view);
        }
        else
        {
            _view.NavigateHome();
        }

        FocusBrowser();
    }

    private void MapCurrentPage()
    {
        if (_project is null || _view is null)
        {
            return;
        }

        var check = ConversationUrl.Check(_view.CurrentUrl);
        if (!check.IsValid || check.Provider != Provider)
        {
            ChoiceDialog.Info(_host, "This page can't be mapped",
                $"Only {Provider.Domain()} pages can be mapped on the {Provider.DisplayName()} side.");
            return;
        }

        var existing = _workspace.FindMapping(_project, Provider, check.NormalizedUrl!);
        if (existing is not null)
        {
            SelectMapping(existing);
            return;
        }

        var mapping = ConversationDialog.Add(_host, _workspace, _project, Provider,
            check.NormalizedUrl, ConversationUrl.SuggestName(Provider, _view.Title));
        if (mapping is null || mapping.Provider != Provider)
        {
            return;
        }

        // Re-label this live instance as the new mapping so nothing reloads.
        var newKey = ConversationUrl.GetKey(Provider, mapping.Url);
        if (_pool.Rekey(_view.Key, newKey))
        {
            _view.Key = newKey;
            _view.HomeUrl = mapping.Url;
        }

        SelectMapping(mapping);
    }

    private void EditCurrentMapping()
    {
        if (_project is not null && _mapping is not null)
        {
            ConversationDialog.Edit(_host, _workspace, _project, _mapping);
        }
    }

    private void CopyLink()
    {
        var link = CurrentLink();
        if (link is null)
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetText(link);
                FlashCopied();
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(40); // clipboard briefly locked by another app
            }
        }

        Log.Warn("Could not copy the link: the clipboard is busy.");
    }

    private void FlashCopied()
    {
        CopyButton.Content = "";
        CopyButton.ToolTip = "Copied";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CopyButton.Content = "";
            CopyButton.ToolTip = "Copy link to this conversation";
        };
        timer.Start();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => ReloadCurrent();

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyLink();

    private void ExternalButton_Click(object sender, RoutedEventArgs e) => ExternalLinks.Open(CurrentLink());

    private void FocusButton_Click(object sender, RoutedEventArgs e) => _host.ToggleFocus(Provider);

    private void NoticePrimary_Click(object sender, RoutedEventArgs e) => _noticePrimary?.Invoke();

    private void NoticeSecondary_Click(object sender, RoutedEventArgs e) => _noticeSecondary?.Invoke();

    private void EmptyPrimary_Click(object sender, RoutedEventArgs e) => _emptyPrimary?.Invoke();

    private void EmptySecondary_Click(object sender, RoutedEventArgs e) => _emptySecondary?.Invoke();

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var name = Provider.DisplayName();
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        menu.Items.Add(MenuFactory.Item("Add conversation…", AddConversation, ""));
        var mappable = _view is not null && IsMappable(_view.CurrentUrl, out var already) && already is null
                       && (_browsing || _view.IsDrifted);
        menu.Items.Add(MenuFactory.Item("Map this page…", MapCurrentPage, "", enabled: mappable));
        if (_mapping is not null)
        {
            menu.Items.Add(MenuFactory.Item($"Edit “{Shorten(_mapping.Name)}”…", EditCurrentMapping, ""));
        }

        menu.Items.Add(MenuFactory.Item("Manage project…", () => _host.OpenProjects(_project), ""));
        menu.Items.Add(new Separator());
        if (_mapping is not null && _view is { IsDrifted: true })
        {
            menu.Items.Add(MenuFactory.Item($"Return to “{Shorten(_mapping.Name)}”", ReturnToMapping, ""));
        }

        menu.Items.Add(MenuFactory.Item(_browsing ? $"Back to mapped conversations" : $"Browse {name}…", _browsing ? ExitBrowse : ShowBrowse, ""));
        menu.Items.Add(MenuFactory.Item("Reload", ReloadCurrent, "", gesture: "F5"));
        menu.Items.Add(MenuFactory.Item("Copy link", CopyLink, "", enabled: CurrentLink() is not null));
        menu.Items.Add(MenuFactory.Item("Open in default browser", () => ExternalLinks.Open(CurrentLink()), "", enabled: CurrentLink() is not null));
        var zoom = _workspace.Preferences.GetZoom(Provider);
        if (Math.Abs(zoom - 1.0) > 0.001)
        {
            menu.Items.Add(MenuFactory.Item($"Reset zoom ({zoom:P0})", () => _workspace.Preferences.SetZoom(Provider, 1.0), ""));
        }

        menu.IsOpen = true;
    }

    private static string Shorten(string text) => text.Length > 32 ? text[..31] + "…" : text;
}
