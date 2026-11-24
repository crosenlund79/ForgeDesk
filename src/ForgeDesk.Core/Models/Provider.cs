using System.Text.Json.Serialization;

namespace ForgeDesk.Core.Models;

/// <summary>The two assistant websites ForgeDesk hosts.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Provider>))]
public enum Provider
{
    [JsonStringEnumMemberName("chatgpt")]
    ChatGpt,

    [JsonStringEnumMemberName("claude")]
    Claude,
}

public static class ProviderInfo
{
    public static IReadOnlyList<Provider> All { get; } = [Provider.ChatGpt, Provider.Claude];

    public static string DisplayName(this Provider provider) => provider switch
    {
        Provider.ChatGpt => "ChatGPT",
        Provider.Claude => "Claude",
        _ => provider.ToString(),
    };

    /// <summary>The provider's own start page, used for signing in and browsing.</summary>
    public static string HomeUrl(this Provider provider) => provider switch
    {
        Provider.ChatGpt => "https://chatgpt.com/",
        Provider.Claude => "https://claude.ai/",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    public static string Domain(this Provider provider) => provider switch
    {
        Provider.ChatGpt => "chatgpt.com",
        Provider.Claude => "claude.ai",
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    public static Provider Other(this Provider provider) =>
        provider == Provider.ChatGpt ? Provider.Claude : Provider.ChatGpt;
}
