using System.Text.RegularExpressions;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Core.Urls;

public enum ConversationUrlKind
{
    /// <summary>A specific conversation, e.g. chatgpt.com/c/{id} or claude.ai/chat/{id}.</summary>
    Conversation,

    /// <summary>A ChatGPT or Claude project page.</summary>
    ProjectPage,

    /// <summary>A read-only shared link.</summary>
    SharedLink,

    /// <summary>Any other page on the provider's site.</summary>
    Other,
}

/// <summary>Result of validating a pasted conversation link.</summary>
public sealed record ConversationUrlCheck(
    bool IsValid,
    string? Error,
    Provider? Provider,
    string? NormalizedUrl,
    ConversationUrlKind Kind)
{
    /// <summary>Non-blocking advice about a valid link.</summary>
    public string? Warning => !IsValid
        ? null
        : Kind switch
        {
            ConversationUrlKind.SharedLink => "This is a read-only shared link rather than one of your own conversations.",
            ConversationUrlKind.ProjectPage => "This is a project page rather than a single conversation. It can still be mapped.",
            ConversationUrlKind.Other => "This doesn't look like a conversation link. It can still be mapped and will open as-is.",
            _ => null,
        };
}

/// <summary>
/// Validation, normalisation and identity for provider conversation URLs.
/// Pure string logic: nothing here contacts the network.
/// </summary>
public static partial class ConversationUrl
{
    private static readonly string[] ChatGptHosts = ["chatgpt.com", "www.chatgpt.com", "chat.openai.com"];
    private static readonly string[] ClaudeHosts = ["claude.ai", "www.claude.ai"];

    [GeneratedRegex(@"(?:^|/)c/(?<id>[A-Za-z0-9][A-Za-z0-9-]{7,})(?:/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ChatGptConversationRegex();

    [GeneratedRegex(@"^/chat/(?<id>[A-Za-z0-9][A-Za-z0-9-]{7,})(?:/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeConversationRegex();

    [GeneratedRegex(@"^/g/g-p-[^/]+(?:/project)?/?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ChatGptProjectRegex();

    [GeneratedRegex(@"^/project/[A-Za-z0-9-]{8,}/?$", RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeProjectRegex();

    /// <summary>Validates a pasted link and returns its provider and normalised form.</summary>
    public static ConversationUrlCheck Check(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Invalid("Paste a link to a ChatGPT or Claude conversation.");
        }

        var text = input.Trim();
        if (text.Any(char.IsWhiteSpace))
        {
            return Invalid("The link contains spaces. Paste a single conversation link.");
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return Invalid("That isn't a valid web address.");
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return Invalid("Only https links to chatgpt.com or claude.ai can be mapped.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
        {
            return Invalid("That link has an unexpected format.");
        }

        var provider = DetectProvider(uri.Host);
        if (provider is null)
        {
            return Invalid("This isn't a ChatGPT (chatgpt.com) or Claude (claude.ai) link.");
        }

        var path = NormalizePath(uri.AbsolutePath);
        var normalized = $"https://{provider.Value.Domain()}{path}{uri.Query}";
        return new ConversationUrlCheck(true, null, provider, normalized, Classify(provider.Value, path));
    }

    /// <summary>Returns the provider whose website serves the host, if any.</summary>
    public static Provider? DetectProvider(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return null;
        }

        if (ChatGptHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return Provider.ChatGpt;
        }

        if (ClaudeHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return Provider.Claude;
        }

        return null;
    }

    /// <summary>Extracts the conversation id from a conversation URL, if it is one.</summary>
    public static string? TryGetConversationId(Provider provider, string? url)
    {
        if (!TryParseProviderUri(provider, url, out var uri))
        {
            return null;
        }

        var path = NormalizePath(uri.AbsolutePath);
        var match = provider == Provider.ChatGpt
            ? ChatGptConversationRegex().Match(path)
            : ClaudeConversationRegex().Match(path);
        return match.Success ? match.Groups["id"].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// A stable identity for "the same conversation", tolerant of host aliases,
    /// query strings, fragments, trailing slashes and ChatGPT's GPT/project path prefixes.
    /// Used to share one browser instance across projects and to detect navigation drift.
    /// </summary>
    public static string GetKey(Provider provider, string? url)
    {
        var prefix = provider == Provider.ChatGpt ? "chatgpt" : "claude";
        var id = TryGetConversationId(provider, url);
        if (id is not null)
        {
            return $"{prefix}:c:{id}";
        }

        if (TryParseProviderUri(provider, url, out var uri))
        {
            return $"{prefix}:p:{NormalizePath(uri.AbsolutePath).ToLowerInvariant()}";
        }

        return $"{prefix}:x:{url?.Trim()}";
    }

    public static bool IsSameConversation(Provider provider, string? a, string? b) =>
        a is not null && b is not null && GetKey(provider, a) == GetKey(provider, b);

    private static readonly string[] IdentityHosts =
    [
        "accounts.google.com", "appleid.apple.com", "login.microsoftonline.com", "login.live.com",
        "login.microsoft.com", "account.live.com", "auth.openai.com", "auth0.openai.com",
    ];

    private static readonly string[] SignInPaths =
    [
        "/login", "/log-in", "/signin", "/sign-in", "/signup", "/sign-up", "/auth", "/magic-link", "/oauth",
    ];

    /// <summary>
    /// True for sign-in pages of the providers and their identity providers. Used to show
    /// "sign in" guidance instead of a "you moved away" warning when a session has expired.
    /// </summary>
    public static bool IsSignInPage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (IdentityHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
        {
            return true;
        }

        if (DetectProvider(host) is null)
        {
            return false;
        }

        var path = NormalizePath(uri.AbsolutePath).ToLowerInvariant();
        return SignInPaths.Any(p => path == p || path.StartsWith(p + "/", StringComparison.Ordinal));
    }

    /// <summary>True when the URL is served by the given provider's website.</summary>
    public static bool IsProviderUrl(Provider provider, string? url) =>
        TryParseProviderUri(provider, url, out _);

    /// <summary>Turns a provider page title into a suggested mapping name.</summary>
    public static string SuggestName(Provider provider, string? title)
    {
        var text = (title ?? string.Empty).Trim();
        string[] suffixes = provider == Provider.ChatGpt
            ? [" - ChatGPT", " | ChatGPT", " – ChatGPT"]
            : [" - Claude", " | Claude", " – Claude", " \\ Claude", " \\ Anthropic"];
        foreach (var suffix in suffixes)
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^suffix.Length].TrimEnd();
            }
        }

        if (text.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Claude", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("New chat", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return text.Length > 80 ? text[..80].TrimEnd() : text;
    }

    private static bool TryParseProviderUri(Provider provider, string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (DetectProvider(parsed.Host) != provider)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }

        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static ConversationUrlKind Classify(Provider provider, string path)
    {
        if (path.StartsWith("/share/", StringComparison.OrdinalIgnoreCase))
        {
            return ConversationUrlKind.SharedLink;
        }

        if (provider == Provider.ChatGpt)
        {
            if (ChatGptConversationRegex().IsMatch(path))
            {
                return ConversationUrlKind.Conversation;
            }

            if (ChatGptProjectRegex().IsMatch(path))
            {
                return ConversationUrlKind.ProjectPage;
            }
        }
        else
        {
            if (ClaudeConversationRegex().IsMatch(path))
            {
                return ConversationUrlKind.Conversation;
            }

            if (ClaudeProjectRegex().IsMatch(path))
            {
                return ConversationUrlKind.ProjectPage;
            }
        }

        return ConversationUrlKind.Other;
    }

    private static ConversationUrlCheck Invalid(string error) =>
        new(false, error, null, null, ConversationUrlKind.Other);
}
