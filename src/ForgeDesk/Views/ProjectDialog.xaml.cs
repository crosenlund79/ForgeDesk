using System.Windows;
using ForgeDesk.Core;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Views;

/// <summary>Create a project, or rename / recolour an existing one.</summary>
public partial class ProjectDialog : Window
{
    private readonly Workspace _workspace;
    private readonly Project? _existing;
    private readonly Func<string?> _color;

    private ProjectDialog(Window? owner, Workspace workspace, Project? existing)
    {
        InitializeComponent();
        DialogSupport.Prepare(this, owner);
        _workspace = workspace;
        _existing = existing;
        if (existing is not null)
        {
            Title = HeadingText.Text = "Edit project";
            OkButton.Content = "Save";
            NameBox.Text = existing.Name;
        }

        _color = DialogSupport.BuildSwatches(SwatchPanel, existing?.Color);
        UpdateState();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public Project? Result { get; private set; }

    /// <summary>Shows the dialog; returns the created project or null if cancelled.</summary>
    public static Project? Create(Window? owner, Workspace workspace)
    {
        var dialog = new ProjectDialog(owner, workspace, null);
        dialog.ShowDialog();
        return dialog.Result;
    }

    internal static ProjectDialog ForPreview(Window? owner, Workspace workspace) => new(owner, workspace, null);

    public static bool Edit(Window? owner, Workspace workspace, Project project)
    {
        var dialog = new ProjectDialog(owner, workspace, project);
        dialog.ShowDialog();
        return dialog.Result is not null;
    }

    private void NameBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateState();

    private void UpdateState() => OkButton.IsEnabled = NameBox.Text.Trim().Length > 0;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_existing is null)
            {
                Result = _workspace.CreateProject(NameBox.Text, _color());
            }
            else
            {
                _workspace.RenameProject(_existing, NameBox.Text);
                _workspace.SetProjectColor(_existing, _color());
                Result = _existing;
            }

            Close();
        }
        catch (ArgumentException ex)
        {
            ErrorText.Text = ex.Message.Split(" (Parameter")[0];
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
