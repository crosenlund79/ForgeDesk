using System.Windows;
using System.Windows.Input;
using ForgeDesk.Core;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Views;

/// <summary>Create, rename, recolour, reorder, archive and remove projects and their mappings.</summary>
public partial class ProjectsWindow : Window
{
    private readonly Workspace _workspace;
    private bool _loadingSelection;
    private Project? _detailsProject;

    public ProjectsWindow(Window? owner, Workspace workspace, Project? select = null)
    {
        InitializeComponent();
        DialogSupport.Prepare(this, owner);
        _workspace = workspace;
        ProjectList.ItemsSource = workspace.Projects;
        ProjectList.SelectedItem = select ?? workspace.ActiveProject ?? workspace.Projects.FirstOrDefault();
        workspace.Projects.CollectionChanged += OnProjectsChanged;
        Closed += (_, _) => workspace.Projects.CollectionChanged -= OnProjectsChanged;
        ShowSelected();
    }

    /// <summary>Set when the user asks to open a project on the desk.</summary>
    public Project? ProjectToOpen { get; private set; }

    public static Project? Open(Window? owner, Workspace workspace, Project? select = null)
    {
        var window = new ProjectsWindow(owner, workspace, select);
        window.ShowDialog();
        return window.ProjectToOpen;
    }

    private Project? Selected => ProjectList.SelectedItem as Project;

    private void OnProjectsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (Selected is null && _workspace.Projects.Count > 0)
        {
            ProjectList.SelectedItem = _workspace.Projects[0];
        }

        UpdateButtons();
    }

    private void ProjectList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CommitName();
        ShowSelected();
    }

    private void ShowSelected()
    {
        var project = Selected;
        _detailsProject = project;
        _loadingSelection = true;
        DetailsPanel.Visibility = project is null ? Visibility.Collapsed : Visibility.Visible;
        NoSelectionText.Visibility = project is null ? Visibility.Visible : Visibility.Collapsed;
        NameBox.Text = project?.Name ?? string.Empty;
        DialogSupport.BuildSwatches(SwatchPanel, project?.Color, color =>
        {
            if (!_loadingSelection && _detailsProject is { } p && _workspace.Projects.Contains(p))
            {
                _workspace.SetProjectColor(p, color);
            }
        });
        ChatGptEditor.Bind(_workspace, project, Provider.ChatGpt);
        ClaudeEditor.Bind(_workspace, project, Provider.Claude);
        _loadingSelection = false;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var project = Selected;
        var peers = project is null ? [] : _workspace.Projects.Where(p => p.IsArchived == project.IsArchived).ToList();
        var index = project is null ? -1 : peers.IndexOf(project);
        UpButton.IsEnabled = index > 0;
        DownButton.IsEnabled = index >= 0 && index < peers.Count - 1;
        RemoveButton.IsEnabled = project is not null;
        ArchiveButton.IsEnabled = project is not null;
        ArchiveButton.Content = project?.IsArchived == true ? "" : "";
        ArchiveButton.ToolTip = project?.IsArchived == true ? "Restore to the tab bar" : "Archive (hide from the tab bar, keep mappings)";
        OpenButton.IsEnabled = project is { IsArchived: false };
    }

    /// <summary>Applies the name box to the project whose details are shown (not the newly selected one).</summary>
    private void CommitName()
    {
        if (_loadingSelection || _detailsProject is not { } project || !_workspace.Projects.Contains(project))
        {
            return;
        }

        var text = NameBox.Text.Trim();
        if (text.Length == 0)
        {
            NameBox.Text = project.Name;
            return;
        }

        if (text != project.Name)
        {
            _workspace.RenameProject(project, text);
        }
    }

    private void NameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitName();

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitName();
            e.Handled = true;
        }
    }

    private void ProjectList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            RemoveButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        CommitName();
        var created = ProjectDialog.Create(this, _workspace);
        if (created is not null)
        {
            ProjectList.SelectedItem = created;
            ProjectList.ScrollIntoView(created);
        }
    }

    private void UpButton_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void DownButton_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (Selected is not { } project)
        {
            return;
        }

        CommitName();
        _workspace.MoveProject(project, delta);
        ProjectList.SelectedItem = project;
        ProjectList.ScrollIntoView(project);
        UpdateButtons();
    }

    private void ArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } project)
        {
            return;
        }

        CommitName();
        _workspace.SetArchived(project, !project.IsArchived);
        UpdateButtons();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } project)
        {
            return;
        }

        var count = project.ChatGptConversations.Count + project.ClaudeConversations.Count;
        var confirmed = ChoiceDialog.Confirm(this,
            $"Remove “{project.Name}”?",
            $"The project and its {count} conversation mapping{(count == 1 ? string.Empty : "s")} will be removed from ForgeDesk. " +
            "Nothing is deleted on ChatGPT or Claude. Consider archiving instead if you might need it again.",
            "Remove project");
        if (!confirmed)
        {
            return;
        }

        var index = ProjectList.SelectedIndex;
        _workspace.RemoveProject(project);
        if (_workspace.Projects.Count > 0)
        {
            ProjectList.SelectedIndex = Math.Min(index, _workspace.Projects.Count - 1);
        }

        ShowSelected();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        CommitName();
        if (Selected is { IsArchived: false } project)
        {
            ProjectToOpen = project;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CommitName();
        Close();
    }
}
