using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ForgeDesk.Core;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Views;

/// <summary>Editable list of one provider's mappings within a project.</summary>
public partial class MappingListEditor : UserControl
{
    private Workspace? _workspace;
    private Project? _project;
    private Provider _provider;
    private NotifyCollectionChangedEventHandler? _listHandler;

    public MappingListEditor()
    {
        InitializeComponent();
    }

    public void Bind(Workspace workspace, Project? project, Provider provider)
    {
        if (_project is not null && _listHandler is not null)
        {
            _project.Conversations(_provider).CollectionChanged -= _listHandler;
        }

        _workspace = workspace;
        _project = project;
        _provider = provider;
        HeaderText.Text = $"{provider.DisplayName()} conversations";
        Badge.SetResourceReference(Border.BackgroundProperty, provider == Provider.ChatGpt ? "ChatGptBrush" : "ClaudeBrush");
        List.ItemsSource = project?.Conversations(provider);
        if (project is not null)
        {
            _listHandler = (_, _) => UpdateState();
            project.Conversations(provider).CollectionChanged += _listHandler;
        }

        UpdateState();
    }

    public ConversationMapping? Selected => List.SelectedItem as ConversationMapping;

    private Window? OwnerWindow => Window.GetWindow(this);

    private void UpdateState()
    {
        var list = _project?.Conversations(_provider);
        var count = list?.Count ?? 0;
        CountText.Text = count == 0 ? string.Empty : count.ToString();
        EmptyText.Text = $"No {_provider.DisplayName()} conversations yet.\nAdd one by pasting its link.";
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.IsEnabled = _project is not null;
        var selected = Selected;
        var index = selected is null || list is null ? -1 : list.IndexOf(selected);
        EditButton.IsEnabled = RemoveButton.IsEnabled = selected is not null;
        UpButton.IsEnabled = index > 0;
        DownButton.IsEnabled = index >= 0 && index < count - 1;
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateState();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace is null || _project is null)
        {
            return;
        }

        var added = ConversationDialog.Add(OwnerWindow, _workspace, _project, _provider);
        if (added is not null && added.Provider == _provider)
        {
            List.SelectedItem = added;
            List.ScrollIntoView(added);
        }
    }

    private void EditButton_Click(object sender, RoutedEventArgs e) => EditSelected();

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelected();

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.F2 || e.Key == Key.Enter)
        {
            EditSelected();
            e.Handled = true;
        }
    }

    private void EditSelected()
    {
        if (_workspace is null || _project is null || Selected is not { } mapping)
        {
            return;
        }

        ConversationDialog.Edit(OwnerWindow, _workspace, _project, mapping);
    }

    private void UpButton_Click(object sender, RoutedEventArgs e) => Move(-1);

    private void DownButton_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Move(int delta)
    {
        if (_workspace is null || _project is null || Selected is not { } mapping)
        {
            return;
        }

        _workspace.MoveConversation(_project, mapping, delta);
        List.SelectedItem = mapping;
        List.ScrollIntoView(mapping);
        UpdateState();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e) => RemoveSelected();

    private void RemoveSelected()
    {
        if (_workspace is null || _project is null || Selected is not { } mapping)
        {
            return;
        }

        var confirmed = ChoiceDialog.Confirm(OwnerWindow,
            $"Remove “{mapping.Name}”?",
            $"It will be removed from {_project.Name} in ForgeDesk only. The conversation stays on {_provider.DisplayName()} and in any other project that maps it.",
            "Remove");
        if (confirmed)
        {
            var index = List.SelectedIndex;
            _workspace.RemoveConversation(_project, mapping);
            var list = _project.Conversations(_provider);
            if (list.Count > 0)
            {
                List.SelectedIndex = Math.Min(index, list.Count - 1);
            }
        }
    }
}
