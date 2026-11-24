using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace ForgeDesk.Core.Models;

/// <summary>A named group of explicitly mapped ChatGPT and Claude conversations.</summary>
public sealed class Project : ObservableObject
{
    private string _id = Ids.New();
    private string _name = string.Empty;
    private string? _color;
    private int _order;
    private bool _isArchived;

    public string Id
    {
        get => _id;
        set => Set(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    /// <summary>Optional colour as #RRGGBB.</summary>
    public string? Color
    {
        get => _color;
        set => Set(ref _color, value);
    }

    public int Order
    {
        get => _order;
        set => Set(ref _order, value);
    }

    [JsonPropertyName("archived")]
    public bool IsArchived
    {
        get => _isArchived;
        set => Set(ref _isArchived, value);
    }

    public ObservableCollection<ConversationMapping> ChatGptConversations { get; set; } = [];

    public ObservableCollection<ConversationMapping> ClaudeConversations { get; set; } = [];

    /// <summary>How the desk looked the last time this project was active.</summary>
    public ProjectDesk Desk { get; set; } = new();

    public ObservableCollection<ConversationMapping> Conversations(Provider provider) =>
        provider == Provider.ChatGpt ? ChatGptConversations : ClaudeConversations;

    public override string ToString() => Name;
}

/// <summary>Which side, if any, is temporarily maximised.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeskFocus>))]
public enum DeskFocus
{
    [JsonStringEnumMemberName("none")]
    None,

    [JsonStringEnumMemberName("chatgpt")]
    ChatGpt,

    [JsonStringEnumMemberName("claude")]
    Claude,
}

/// <summary>Per-project desk state restored when switching back to a project.</summary>
public sealed class ProjectDesk : ObservableObject
{
    public const double DefaultSplitRatio = 0.5;
    public const double MinSplitRatio = 0.1;
    public const double MaxSplitRatio = 0.9;

    private string? _selectedChatGptId;
    private string? _selectedClaudeId;
    private double _splitRatio = DefaultSplitRatio;
    private DeskFocus _focus;

    public string? SelectedChatGptId
    {
        get => _selectedChatGptId;
        set => Set(ref _selectedChatGptId, value);
    }

    public string? SelectedClaudeId
    {
        get => _selectedClaudeId;
        set => Set(ref _selectedClaudeId, value);
    }

    /// <summary>Fraction of the desk width given to the ChatGPT (left) side.</summary>
    public double SplitRatio
    {
        get => _splitRatio;
        set => Set(ref _splitRatio, ClampRatio(value));
    }

    public DeskFocus Focus
    {
        get => _focus;
        set => Set(ref _focus, value);
    }

    public string? GetSelectedId(Provider provider) =>
        provider == Provider.ChatGpt ? SelectedChatGptId : SelectedClaudeId;

    public void SetSelectedId(Provider provider, string? id)
    {
        if (provider == Provider.ChatGpt)
        {
            SelectedChatGptId = id;
        }
        else
        {
            SelectedClaudeId = id;
        }
    }

    public static double ClampRatio(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultSplitRatio;
        }

        return Math.Clamp(value, MinSplitRatio, MaxSplitRatio);
    }

    public static DeskFocus FocusFor(Provider provider) =>
        provider == Provider.ChatGpt ? DeskFocus.ChatGpt : DeskFocus.Claude;
}
