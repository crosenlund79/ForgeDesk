using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ForgeDesk.Browser;
using ForgeDesk.Core;
using ForgeDesk.Core.Configuration;
using ForgeDesk.Core.Models;
using ForgeDesk.Infrastructure;
using ForgeDesk.Views;

namespace ForgeDesk;

public partial class MainWindow : Window
{
    private const double PaneMinWidth = 280;
    private const double SplitterWidth = 5;
    private const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    private readonly Workspace _workspace;
    private readonly ConfigStore _store;
    private readonly AppPaths _paths;
    private readonly ChangeTracker _tracker;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _trimTimer;
    private readonly IReadOnlyList<string> _startupWarnings;
    private readonly bool _quiet;
    private PaneView _activePane;
    private Project? _shownProject;
    private bool _suppressTabSelection;
    private bool _runtimeAvailable = true;
    private bool _saveErrorShown;
    private Action? _welcomePrimary;
    private Action? _welcomeSecondary;

    public MainWindow(Workspace workspace, ConfigStore store, AppPaths paths, IReadOnlyList<string> startupWarnings, bool quiet = false)
    {
        InitializeComponent();
        _workspace = workspace;
        _store = store;
        _paths = paths;
        _startupWarnings = startupWarnings;
        _quiet = quiet;

        WindowPlacementHelper.Restore(this, workspace.Config.Window);
        ThemeManager.ApplyToWindow(this);

        var tabs = new ListCollectionView(workspace.Projects)
        {
            Filter = item => item is Project { IsArchived: false },
            IsLiveFiltering = true,
        };
        tabs.LiveFilteringProperties.Add(nameof(Project.IsArchived));
        ProjectTabs.ItemsSource = tabs;

        LeftPane.Initialize(this, workspace, Provider.ChatGpt);
        RightPane.Initialize(this, workspace, Provider.Claude);
        LeftPane.Activated += (_, _) => _activePane = LeftPane;
        RightPane.Activated += (_, _) => _activePane = RightPane;
        _activePane = LeftPane;

        workspace.PropertyChanged += OnWorkspacePropertyChanged;
        workspace.Projects.CollectionChanged += (_, _) => UpdateWelcome();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
        _tracker = new ChangeTracker(workspace.Config);
        _tracker.Changed += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };

        // Periodically release hidden browser instances beyond the per-side limit.
        _trimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _trimTimer.Tick += (_, _) =>
        {
            LeftPane.TrimPool();
            RightPane.TrimPool();
        };

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    internal Workspace Workspace => _workspace;

    internal AppPaths Paths => _paths;

    internal bool RuntimeAvailable => _runtimeAvailable;

    internal PaneView ActivePane => _activePane;

    // ================================================================ lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var version = BrowserEnvironment.TryGetRuntimeVersion();
        _runtimeAvailable = version is not null;
        Log.Info(version is null ? "WebView2 Runtime not found." : $"WebView2 Runtime {version} found.");

        _workspace.EnsureActiveProject();
        ApplyActiveProject();
        _trimTimer.Start();

        if (_startupWarnings.Count > 0 && !_quiet)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => ChoiceDialog.Info(this,
                "Some settings needed attention",
                "ForgeDesk repaired its settings while starting. Your projects and mappings were kept where possible.",
                string.Join(Environment.NewLine, _startupWarnings)));
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _workspace.Config.Window = WindowPlacementHelper.Capture(this);
        _saveTimer.Stop();
        _trimTimer.Stop();
        SaveNow();
        LeftPane.ReleaseAll();
        RightPane.ReleaseAll();
        _tracker.Dispose();
    }

    internal bool SaveNow()
    {
        try
        {
            _store.Save(_workspace.Config);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed", ex);
            if (!_saveErrorShown && !_quiet)
            {
                _saveErrorShown = true;
                ChoiceDialog.Info(this, "Couldn't save your settings",
                    $"ForgeDesk will keep trying. The settings file is {_store.ConfigPath}.", ex.Message);
            }

            return false;
        }
    }

    internal void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    // ================================================================ projects

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Workspace.ActiveProject))
        {
            ApplyActiveProject();
        }
    }

    internal void ApplyActiveProject()
    {
        var project = _workspace.ActiveProject;
        if (_shownProject is not null)
        {
            _shownProject.PropertyChanged -= OnShownProjectChanged;
        }

        _shownProject = project;
        if (project is not null)
        {
            project.PropertyChanged += OnShownProjectChanged;
        }

        _suppressTabSelection = true;
        ProjectTabs.SelectedItem = project;
        _suppressTabSelection = false;
        BringTabIntoView(project);
        UpdateTitle();
        UpdateWelcome();

        if (!_runtimeAvailable)
        {
            return;
        }

        ApplyDeskLayout();
        LeftPane.ShowProject(project);
        RightPane.ShowProject(project);
    }

    private void OnShownProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Project.Name))
        {
            UpdateTitle();
        }
        else if (e.PropertyName == nameof(Project.IsArchived))
        {
            ApplyActiveProject();
        }
    }

    private void UpdateTitle() =>
        Title = _workspace.ActiveProject is { } project ? $"{project.Name} — ForgeDesk" : "ForgeDesk";

    private void BringTabIntoView(Project? project)
    {
        if (project is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ProjectTabs.ItemContainerGenerator.ContainerFromItem(project) is FrameworkElement container)
            {
                container.BringIntoView();
            }
        });
    }

    private void ProjectTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabSelection || ProjectTabs.SelectedItem is not Project project || project == _workspace.ActiveProject)
        {
            return;
        }

        _workspace.ActivateProject(project);
    }

    private void TabScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        TabScroller.ScrollToHorizontalOffset(TabScroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void NewProjectButton_Click(object sender, RoutedEventArgs e) => NewProject();

    internal void NewProject()
    {
        var project = ProjectDialog.Create(this, _workspace);
        if (project is not null)
        {
            _workspace.ActivateProject(project);
        }
    }

    internal void OpenProjects(Project? select = null)
    {
        var open = ProjectsWindow.Open(this, _workspace, select ?? _workspace.ActiveProject);
        _workspace.EnsureActiveProject();
        if (open is not null && _workspace.Projects.Contains(open) && !open.IsArchived)
        {
            _workspace.ActivateProject(open);
        }
    }

    private static Project? TabProject(object sender) => (sender as FrameworkElement)?.DataContext as Project;

    private void ProjectTab_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TabProject(sender) is { } project)
        {
            ProjectDialog.Edit(this, _workspace, project);
            e.Handled = true;
        }
    }

    private void ProjectTab_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TabProject(sender) is not { } project)
        {
            return;
        }

        var visible = _workspace.VisibleProjects;
        var index = visible.ToList().IndexOf(project);
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        menu.Items.Add(MenuFactory.Item("Edit name and colour…", () => ProjectDialog.Edit(this, _workspace, project), ""));
        menu.Items.Add(MenuFactory.Item("Manage conversations…", () => OpenProjects(project), ""));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuFactory.Item("Move left", () => _workspace.MoveProject(project, -1), "", enabled: index > 0));
        menu.Items.Add(MenuFactory.Item("Move right", () => _workspace.MoveProject(project, +1), "", enabled: index >= 0 && index < visible.Count - 1));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuFactory.Item("Archive", () => _workspace.SetArchived(project, true), ""));
        menu.Items.Add(MenuFactory.Item("Remove…", () => RemoveProject(project), ""));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void RemoveProject(Project project)
    {
        var count = project.ChatGptConversations.Count + project.ClaudeConversations.Count;
        var choice = ChoiceDialog.Ask(this, $"Remove “{project.Name}”?",
            $"The project and its {count} conversation mapping{(count == 1 ? string.Empty : "s")} will be removed from ForgeDesk. " +
            "Nothing is deleted on ChatGPT or Claude. Archiving hides the project but keeps its mappings.",
            [
                new ChoiceDialog.Choice("cancel", "Cancel", IsCancel: true),
                new ChoiceDialog.Choice("archive", "Archive instead"),
                new ChoiceDialog.Choice("remove", "Remove", IsPrimary: true),
            ]);
        if (choice == "remove")
        {
            _workspace.RemoveProject(project);
        }
        else if (choice == "archive")
        {
            _workspace.SetArchived(project, true);
        }
    }

    // ================================================================ desk layout

    internal void ApplyDeskLayout()
    {
        var desk = _workspace.ActiveProject?.Desk;
        var focus = desk?.Focus ?? DeskFocus.None;
        var ratio = desk?.SplitRatio ?? ProjectDesk.DefaultSplitRatio;

        switch (focus)
        {
            case DeskFocus.ChatGpt:
                SetColumns(new GridLength(1, GridUnitType.Star), 0, new GridLength(0), 0, 0);
                LeftPane.Visibility = Visibility.Visible;
                RightPane.Visibility = Visibility.Collapsed;
                Splitter.Visibility = Visibility.Collapsed;
                break;
            case DeskFocus.Claude:
                SetColumns(new GridLength(0), 0, new GridLength(1, GridUnitType.Star), 0, 0);
                LeftPane.Visibility = Visibility.Collapsed;
                RightPane.Visibility = Visibility.Visible;
                Splitter.Visibility = Visibility.Collapsed;
                break;
            default:
                SetColumns(new GridLength(ratio, GridUnitType.Star), SplitterWidth,
                           new GridLength(1 - ratio, GridUnitType.Star), PaneMinWidth, PaneMinWidth);
                LeftPane.Visibility = Visibility.Visible;
                RightPane.Visibility = Visibility.Visible;
                Splitter.Visibility = Visibility.Visible;
                break;
        }

        LeftPane.SetMaximised(focus == DeskFocus.ChatGpt);
        RightPane.SetMaximised(focus == DeskFocus.Claude);
    }

    private void SetColumns(GridLength left, double splitter, GridLength right, double leftMin, double rightMin)
    {
        LeftColumn.MinWidth = leftMin;
        RightColumn.MinWidth = rightMin;
        LeftColumn.Width = left;
        SplitterColumn.Width = new GridLength(splitter);
        RightColumn.Width = right;
    }

    /// <summary>Current share of the desk width shown on the ChatGPT side.</summary>
    internal double MeasuredSplitRatio
    {
        get
        {
            var total = LeftColumn.ActualWidth + RightColumn.ActualWidth;
            return total <= 0 ? double.NaN : LeftColumn.ActualWidth / total;
        }
    }

    internal void ToggleFocus(Provider provider)
    {
        if (_workspace.ActiveProject is not { } project)
        {
            return;
        }

        _workspace.ToggleFocus(project, provider);
        ApplyDeskLayout();
        _activePane = provider == Provider.ChatGpt ? LeftPane : RightPane;
        _activePane.FocusBrowser();
    }

    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_workspace.ActiveProject is { } project && !double.IsNaN(MeasuredSplitRatio))
        {
            _workspace.SetSplitRatio(project, MeasuredSplitRatio);
            ApplyDeskLayout();
        }
    }

    private void Splitter_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_workspace.ActiveProject is { } project)
        {
            _workspace.SetSplitRatio(project, ProjectDesk.DefaultSplitRatio);
            ApplyDeskLayout();
        }
    }

    // ================================================================ welcome / prerequisites

    private void UpdateWelcome()
    {
        if (!_runtimeAvailable)
        {
            ShowWelcome("WebView2 Runtime required",
                "ForgeDesk shows the real ChatGPT and Claude websites using the Microsoft Edge WebView2 Runtime, which isn't installed on this PC. " +
                "It's a free Microsoft component that is already included in Windows 11 and most Windows 10 installations.",
                "Get WebView2 Runtime", () => ExternalLinks.Open(RuntimeDownloadUrl),
                "Check again", () =>
                {
                    _runtimeAvailable = BrowserEnvironment.TryGetRuntimeVersion() is not null;
                    ApplyActiveProject();
                });
            return;
        }

        if (_workspace.VisibleProjects.Count == 0)
        {
            var archived = _workspace.Projects.Count;
            ShowWelcome("ForgeDesk",
                archived > 0
                    ? "All your projects are archived. Create a new project, or restore one from Manage projects."
                    : "Create a project, then map your existing ChatGPT and Claude conversations to it. " +
                      "ChatGPT opens on the left, Claude on the right — and every project remembers where you left off.",
                "Create a project", NewProject,
                archived > 0 ? "Manage projects…" : "Import configuration…",
                archived > 0 ? () => OpenProjects() : ImportConfiguration);
            return;
        }

        _workspace.EnsureActiveProject();
        WelcomePanel.Visibility = Visibility.Collapsed;
        DeskGrid.Visibility = Visibility.Visible;
    }

    private void ShowWelcome(string title, string body, string primary, Action primaryAction, string secondary, Action secondaryAction)
    {
        WelcomeTitle.Text = title;
        WelcomeTagline.Visibility = title == "ForgeDesk" ? Visibility.Visible : Visibility.Collapsed;
        WelcomeBody.Text = body;
        WelcomePrimary.Content = primary;
        WelcomeSecondary.Content = secondary;
        _welcomePrimary = primaryAction;
        _welcomeSecondary = secondaryAction;
        // Browser views are native windows drawn above WPF content, so the desk must be hidden.
        DeskGrid.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Visible;
    }

    private void WelcomePrimary_Click(object sender, RoutedEventArgs e) => _welcomePrimary?.Invoke();

    private void WelcomeSecondary_Click(object sender, RoutedEventArgs e) => _welcomeSecondary?.Invoke();

    // ================================================================ keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleShortcut(key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Desk-level shortcuts. Modifier sets are matched exactly so AltGr (= Ctrl+Alt) combinations
    /// used to type characters on many keyboard layouts are never intercepted.
    /// </summary>
    internal bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        var digit = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => 0,
        };

        switch (modifiers)
        {
            case ModifierKeys.Control when digit > 0:
                _workspace.ActivateProjectAt(digit - 1);
                return true;
            case ModifierKeys.Control when key is Key.Tab or Key.PageDown:
                _workspace.ActivateRelative(+1);
                return true;
            case ModifierKeys.Control when key == Key.PageUp:
            case ModifierKeys.Control | ModifierKeys.Shift when key == Key.Tab:
                _workspace.ActivateRelative(-1);
                return true;
            case ModifierKeys.Alt when digit > 0 && _runtimeAvailable:
                _activePane.SelectIndex(digit - 1);
                return true;
            case ModifierKeys.Alt when key == Key.Down && _runtimeAvailable:
                _activePane.SelectRelative(+1);
                return true;
            case ModifierKeys.Alt when key == Key.Up && _runtimeAvailable:
                _activePane.SelectRelative(-1);
                return true;
            case ModifierKeys.None when key == Key.F6 && _runtimeAvailable:
                SwitchActivePane();
                return true;
            case ModifierKeys.None when key == Key.F11 && _runtimeAvailable:
                ToggleFocus(_activePane.Provider);
                return true;
            case ModifierKeys.None when key == Key.F5 && _runtimeAvailable:
                _activePane.ReloadCurrent();
                return true;
            default:
                return false;
        }
    }

    private void SwitchActivePane()
    {
        var next = _activePane == LeftPane ? RightPane : LeftPane;
        if (_workspace.ActiveProject is { } project && project.Desk.Focus != DeskFocus.None)
        {
            // In focus mode F6 flips which side is maximised.
            _workspace.SetFocus(project, ProjectDesk.FocusFor(next.Provider));
            ApplyDeskLayout();
        }

        _activePane = next;
        next.FocusBrowser();
    }

    // ================================================================ menus

    private void AllProjectsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AllProjectsButton, Placement = PlacementMode.Bottom };
        var visible = _workspace.VisibleProjects;
        for (var i = 0; i < visible.Count; i++)
        {
            var project = visible[i];
            menu.Items.Add(MenuFactory.Item(project.Name, () => _workspace.ActivateProject(project),
                gesture: i < 9 ? $"Ctrl+{i + 1}" : null, isChecked: project == _workspace.ActiveProject));
        }

        var archived = _workspace.Projects.Where(p => p.IsArchived).ToList();
        if (archived.Count > 0)
        {
            var sub = MenuFactory.Submenu("Archived", "");
            foreach (var project in archived)
            {
                sub.Items.Add(MenuFactory.Item($"Restore {project.Name}", () =>
                {
                    _workspace.SetArchived(project, false);
                    _workspace.ActivateProject(project);
                }));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(sub);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(MenuFactory.Item("New project…", NewProject, ""));
        menu.Items.Add(MenuFactory.Item("Manage projects…", () => OpenProjects(), ""));
        menu.IsOpen = true;
    }

    private void AppMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var preferences = _workspace.Preferences;
        var menu = new ContextMenu { PlacementTarget = AppMenuButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(MenuFactory.Item("New project…", NewProject, ""));
        menu.Items.Add(MenuFactory.Item("Manage projects…", () => OpenProjects(), ""));
        menu.Items.Add(new Separator());

        menu.Items.Add(MenuFactory.Submenu("Theme", "",
            MenuFactory.Item("Match Windows", () => SetTheme(ThemePreference.System), isChecked: preferences.Theme == ThemePreference.System),
            MenuFactory.Item("Light", () => SetTheme(ThemePreference.Light), isChecked: preferences.Theme == ThemePreference.Light),
            MenuFactory.Item("Dark", () => SetTheme(ThemePreference.Dark), isChecked: preferences.Theme == ThemePreference.Dark)));

        var background = MenuFactory.Submenu("Background conversations", "");
        background.Items.Add(MenuFactory.Caption("Keep open per side (at least)"));
        foreach (var n in new[] { 3, 6, 10, 15 })
        {
            background.Items.Add(MenuFactory.Item($"{n} conversations", () => preferences.MaxBrowsersPerProvider = n, isChecked: preferences.MaxBrowsersPerProvider == n));
        }

        background.Items.Add(new Separator());
        background.Items.Add(MenuFactory.Caption("Close extra hidden ones after"));
        foreach (var minutes in new[] { 5, 10, 30, 60 })
        {
            background.Items.Add(MenuFactory.Item($"{minutes} minutes", () => preferences.KeepHiddenBrowsersMinutes = minutes, isChecked: preferences.KeepHiddenBrowsersMinutes == minutes));
        }

        menu.Items.Add(background);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuFactory.Item("Export configuration…", ExportConfiguration, ""));
        menu.Items.Add(MenuFactory.Item("Import configuration…", ImportConfiguration, ""));
        menu.Items.Add(MenuFactory.Item("Open data folder", () => ExternalLinks.OpenFolder(_paths.Root), ""));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuFactory.Item("Keyboard shortcuts", ShowShortcuts, ""));
        menu.Items.Add(MenuFactory.Item("About ForgeDesk", ShowAbout, ""));
        menu.IsOpen = true;
    }

    private void SetTheme(ThemePreference theme)
    {
        _workspace.Preferences.Theme = theme;
        ThemeManager.Apply(theme);
    }

    internal static string ShortcutsText =>
        """
        Ctrl+1 … Ctrl+9          Switch to project 1–9
        Ctrl+Tab / Ctrl+PgDn     Next project
        Ctrl+Shift+Tab / Ctrl+PgUp  Previous project
        Alt+1 … Alt+9            Conversation 1–9 on the active side
        Alt+↓ / Alt+↑            Next / previous conversation on the active side
        F6                       Switch between the ChatGPT and Claude sides
        F11                      Maximise / restore the active side
        F5                       Reload the active side

        Inside each side the usual browser keys work:
        Ctrl+C / Ctrl+V, Ctrl+F (find), Ctrl+P (print),
        Ctrl + / Ctrl − / Ctrl+0 (zoom, remembered per side).
        """;

    private void ShowShortcuts() =>
        ChoiceDialog.Info(this, "Keyboard shortcuts", "The active side is the one you last clicked or typed in.", ShortcutsText);

    private void ShowAbout()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        var runtime = BrowserEnvironment.TryGetRuntimeVersion() ?? "not installed";
        ChoiceDialog.Info(this, $"ForgeDesk {version}",
            "One desk. Two assistants. Your projects.\n\n" +
            "ForgeDesk shows the official ChatGPT and Claude websites side by side and keeps your project's conversations one click away. " +
            "It does not use AI APIs, read your conversations or store your passwords.",
            $"WebView2 Runtime: {runtime}\nData folder:      {_paths.Root}\nSettings file:    {_store.ConfigPath}\nBrowser profile:  {_paths.BrowserDataDirectory}\nLog:              {Log.FilePath}");
    }

    // ================================================================ export / import

    private void ExportConfiguration()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export ForgeDesk configuration",
            FileName = $"ForgeDesk-{DateTime.Now:yyyy-MM-dd}.json",
            Filter = "ForgeDesk configuration (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
            File.WriteAllText(dialog.FileName, ConfigTransfer.Export(_workspace.Config, $"ForgeDesk {version}"));
            var conversations = _workspace.Projects.Sum(p => p.ChatGptConversations.Count + p.ClaudeConversations.Count);
            ChoiceDialog.Info(this, "Configuration exported",
                $"{_workspace.Projects.Count} projects and {conversations} conversation mappings were saved to {Path.GetFileName(dialog.FileName)}. " +
                "Sign-ins, cookies and conversation contents are never included.");
        }
        catch (Exception ex)
        {
            Log.Error("Export failed", ex);
            ChoiceDialog.Info(this, "Export failed", ex.Message);
        }
    }

    private void ImportConfiguration()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import ForgeDesk configuration",
            Filter = "ForgeDesk configuration (*.json)|*.json|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ImportPreview preview;
        try
        {
            preview = ConfigTransfer.Parse(File.ReadAllText(dialog.FileName));
        }
        catch (ConfigImportException ex)
        {
            ChoiceDialog.Info(this, "This file can't be imported", ex.Message);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ChoiceDialog.Info(this, "This file can't be read", ex.Message);
            return;
        }

        var choice = _workspace.Projects.Count == 0 ? "replace" : ChoiceDialog.Ask(this,
            $"Import {preview.ProjectCount} projects ({preview.ConversationCount} conversations)?",
            "Merge keeps everything you have and adds new projects and conversations. " +
            "Replace swaps all current projects for the imported ones (a backup of the current setup is saved first).",
            [
                new ChoiceDialog.Choice("cancel", "Cancel", IsCancel: true),
                new ChoiceDialog.Choice("replace", "Replace all"),
                new ChoiceDialog.Choice("merge", "Merge", IsPrimary: true),
            ]);
        if (choice is not ("merge" or "replace"))
        {
            return;
        }

        if (choice == "replace" && _workspace.Projects.Count > 0)
        {
            TryWriteBackup();
        }

        var summary = _workspace.Import(preview, choice == "merge" ? ImportMode.Merge : ImportMode.Replace);
        ThemeManager.Apply(_workspace.Preferences.Theme);
        ApplyActiveProject();
        var message = choice == "merge"
            ? $"Added {summary.ProjectsAdded} projects and {summary.ConversationsAdded} conversations" +
              (summary.ConversationsSkipped > 0 ? $"; skipped {summary.ConversationsSkipped} already mapped." : ".")
            : $"Imported {summary.ProjectsAdded} projects with {summary.ConversationsAdded} conversations.";
        ChoiceDialog.Info(this, "Import complete", message,
            summary.Warnings.Count > 0 ? string.Join(Environment.NewLine, summary.Warnings) : null);
    }

    private void TryWriteBackup()
    {
        try
        {
            var folder = Path.Combine(_paths.Root, "backups");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, $"before-import-{DateTime.Now:yyyyMMdd-HHmmss}.json"),
                ConfigTransfer.Export(_workspace.Config, "ForgeDesk automatic backup"));
        }
        catch (Exception ex)
        {
            Log.Warn("Could not write a backup before importing", ex);
        }
    }
}
