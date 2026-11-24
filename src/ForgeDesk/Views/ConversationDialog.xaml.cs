using System.Windows;
using System.Windows.Media;
using ForgeDesk.Core;
using ForgeDesk.Core.Models;
using ForgeDesk.Core.Urls;

namespace ForgeDesk.Views;

/// <summary>
/// Add a conversation mapping by pasting its link, or edit an existing mapping's name and link.
/// The provider is detected from the link, so a Claude link always lands on the Claude side.
/// </summary>
public partial class ConversationDialog : Window
{
    private readonly Workspace _workspace;
    private readonly Project _project;
    private readonly Provider? _side;
    private readonly ConversationMapping? _existing;

    private ConversationDialog(Window? owner, Workspace workspace, Project project, Provider? side, ConversationMapping? existing, string? url, string? name)
    {
        InitializeComponent();
        DialogSupport.Prepare(this, owner);
        _workspace = workspace;
        _project = project;
        _side = existing?.Provider ?? side;
        _existing = existing;

        if (existing is not null)
        {
            Title = HeadingText.Text = "Edit conversation";
            OkButton.Content = "Save";
            SubheadingText.Text = $"{existing.Provider.DisplayName()} · {project.Name}";
            UrlBox.Text = existing.Url;
            SetName(existing.Name);
        }
        else
        {
            SubheadingText.Text = side is { } s
                ? $"Map an existing {s.DisplayName()} conversation to {project.Name}. Paste its link from the address bar or the share menu."
                : $"Map an existing ChatGPT or Claude conversation to {project.Name}.";
            UrlBox.Text = url ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                SetName(name);
            }
        }

        Validate();
        Loaded += (_, _) =>
        {
            var focus = string.IsNullOrEmpty(UrlBox.Text) ? UrlBox : NameBox;
            focus.Focus();
            focus.SelectAll();
        };
    }

    public ConversationMapping? Result { get; private set; }

    public static ConversationMapping? Add(Window? owner, Workspace workspace, Project project, Provider? side, string? url = null, string? name = null)
    {
        var dialog = new ConversationDialog(owner, workspace, project, side, null, url, name);
        dialog.ShowDialog();
        return dialog.Result;
    }

    internal static ConversationDialog ForPreview(Window? owner, Workspace workspace, Project project, Provider side, string url, string name) =>
        new(owner, workspace, project, side, null, url, name);

    public static bool Edit(Window? owner, Workspace workspace, Project project, ConversationMapping mapping)
    {
        var dialog = new ConversationDialog(owner, workspace, project, mapping.Provider, mapping, null, null);
        dialog.ShowDialog();
        return dialog.Result is not null;
    }

    private void Input_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Validate();
    }

    private void SetName(string name)
    {
        NameBox.Text = name;
    }

    private bool Validate()
    {
        var check = ConversationUrl.Check(UrlBox.Text);
        if (string.IsNullOrWhiteSpace(UrlBox.Text))
        {
            SetStatus(StatusKind.None, string.Empty);
            OkButton.IsEnabled = false;
            return false;
        }

        if (!check.IsValid)
        {
            SetStatus(StatusKind.Error, check.Error!);
            OkButton.IsEnabled = false;
            return false;
        }

        var provider = check.Provider!.Value;
        if (_existing is not null && provider != _existing.Provider)
        {
            SetStatus(StatusKind.Error, $"This is a {provider.DisplayName()} link, but this mapping is on the {_existing.Provider.DisplayName()} side.");
            OkButton.IsEnabled = false;
            return false;
        }

        var duplicate = _workspace.FindMapping(_project, provider, check.NormalizedUrl!);
        if (duplicate is not null && duplicate != _existing)
        {
            SetStatus(StatusKind.Error, $"Already mapped in {_project.Name} as “{duplicate.Name}”.");
            OkButton.IsEnabled = false;
            return false;
        }

        if (_existing is not null && NameBox.Text.Trim().Length == 0)
        {
            SetStatus(StatusKind.Error, "Give the conversation a name.");
            OkButton.IsEnabled = false;
            return false;
        }

        var message = $"{provider.DisplayName()} {(check.Kind == ConversationUrlKind.Conversation ? "conversation" : "page")}";
        if (_side is { } side && side != provider)
        {
            message += $" — it will be added to the {provider.DisplayName()} side.";
        }

        var others = _workspace.ProjectsMapping(provider, check.NormalizedUrl!, except: _project);
        if (others.Count > 0)
        {
            message += $" Also mapped in {string.Join(", ", others.Select(p => p.Name))}.";
        }

        if (check.Warning is { } warning)
        {
            SetStatus(StatusKind.Warning, $"{message}. {warning}");
        }
        else
        {
            SetStatus(StatusKind.Ok, message);
        }

        OkButton.IsEnabled = true;
        return true;
    }

    private enum StatusKind { None, Ok, Warning, Error }

    private void SetStatus(StatusKind kind, string text)
    {
        StatusText.Text = text;
        var (glyph, brushKey) = kind switch
        {
            StatusKind.Ok => ("", "SuccessForegroundBrush"),
            StatusKind.Warning => ("", "WarningForegroundBrush"),
            StatusKind.Error => ("", "ErrorForegroundBrush"),
            _ => (string.Empty, "TextSecondaryBrush"),
        };
        StatusIcon.Text = glyph;
        var brush = (Brush)FindResource(brushKey);
        StatusIcon.Foreground = brush;
        StatusText.Foreground = brush;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate())
        {
            return;
        }

        try
        {
            if (_existing is null)
            {
                Result = _workspace.AddConversation(_project, NameBox.Text, UrlBox.Text);
            }
            else
            {
                _workspace.UpdateConversation(_project, _existing, NameBox.Text, UrlBox.Text);
                Result = _existing;
            }

            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            SetStatus(StatusKind.Error, ex.Message.Split(" (Parameter")[0]);
        }
    }
}
