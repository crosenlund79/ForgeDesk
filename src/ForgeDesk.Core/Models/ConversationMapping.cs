namespace ForgeDesk.Core.Models;

/// <summary>
/// A user-defined pointer to an existing conversation on a provider's website.
/// Removing a mapping never touches the conversation itself.
/// </summary>
public sealed class ConversationMapping : ObservableObject
{
    private string _id = Ids.New();
    private Provider _provider;
    private string _name = string.Empty;
    private string _url = string.Empty;
    private int _order;

    /// <summary>Stable local identifier.</summary>
    public string Id
    {
        get => _id;
        set => Set(ref _id, value);
    }

    public Provider Provider
    {
        get => _provider;
        set => Set(ref _provider, value);
    }

    /// <summary>User-defined display name.</summary>
    public string Name
    {
        get => _name;
        set => Set(ref _name, value ?? string.Empty);
    }

    /// <summary>The existing conversation URL on the provider's website.</summary>
    public string Url
    {
        get => _url;
        set => Set(ref _url, value ?? string.Empty);
    }

    /// <summary>Display order within its project and provider (0-based).</summary>
    public int Order
    {
        get => _order;
        set => Set(ref _order, value);
    }

    public ConversationMapping Clone() => new()
    {
        Id = Id,
        Provider = Provider,
        Name = Name,
        Url = Url,
        Order = Order,
    };

    public override string ToString() => Name;
}
