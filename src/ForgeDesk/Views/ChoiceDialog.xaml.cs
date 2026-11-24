using System.Windows;
using System.Windows.Controls;

namespace ForgeDesk.Views;

/// <summary>A themed message box with custom buttons.</summary>
public partial class ChoiceDialog : Window
{
    private ChoiceDialog(Window? owner, string title, string heading, string message, string? details)
    {
        InitializeComponent();
        DialogSupport.Prepare(this, owner);
        Title = title;
        HeadingText.Text = heading;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrEmpty(details))
        {
            DetailsBox.Text = details;
            DetailsBox.Visibility = Visibility.Visible;
        }
    }

    public string? Result { get; private set; }

    public sealed record Choice(string Id, string Text, bool IsPrimary = false, bool IsCancel = false);

    public static string? Ask(Window? owner, string heading, string message, IReadOnlyList<Choice> choices, string? details = null, string title = "ForgeDesk")
    {
        var dialog = Build(owner, heading, message, choices, details, title);
        dialog.ShowDialog();
        return dialog.Result;
    }

    internal static ChoiceDialog Build(Window? owner, string heading, string message, IReadOnlyList<Choice> choices, string? details = null, string title = "ForgeDesk")
    {
        var dialog = new ChoiceDialog(owner, title, heading, message, details);
        foreach (var choice in choices)
        {
            var button = new Button
            {
                Content = choice.Text,
                Style = (Style)dialog.FindResource(choice.IsPrimary ? "AccentButton" : "TextButton"),
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 84,
                IsDefault = choice.IsPrimary,
                IsCancel = choice.IsCancel,
            };
            button.Click += (_, _) =>
            {
                dialog.Result = choice.Id;
                dialog.Close();
            };
            dialog.ButtonRow.Children.Add(button);
        }

        return dialog;
    }

    public static bool Confirm(Window? owner, string heading, string message, string confirmText) =>
        Ask(owner, heading, message,
        [
            new Choice("cancel", "Cancel", IsCancel: true),
            new Choice("ok", confirmText, IsPrimary: true),
        ]) == "ok";

    public static void Info(Window? owner, string heading, string message, string? details = null) =>
        Ask(owner, heading, message, [new Choice("ok", "OK", IsPrimary: true, IsCancel: true)], details);
}
