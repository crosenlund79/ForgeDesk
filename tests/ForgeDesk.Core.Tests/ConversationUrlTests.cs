using ForgeDesk.Core.Models;
using ForgeDesk.Core.Urls;

namespace ForgeDesk.Core.Tests;

public class ConversationUrlTests
{
    private const string Id1 = "6744a1b2-1c2d-8003-9e4f-0123456789ab";
    private const string Id2 = "8f1e2d3c-4b5a-4697-8877-665544332211";

    [Theory]
    [InlineData("https://chatgpt.com/c/" + Id1, Provider.ChatGpt, "https://chatgpt.com/c/" + Id1)]
    [InlineData("  https://chatgpt.com/c/" + Id1 + "/  ", Provider.ChatGpt, "https://chatgpt.com/c/" + Id1)]
    [InlineData("https://chat.openai.com/c/" + Id1, Provider.ChatGpt, "https://chatgpt.com/c/" + Id1)]
    [InlineData("http://www.chatgpt.com/c/" + Id1 + "#frag", Provider.ChatGpt, "https://chatgpt.com/c/" + Id1)]
    [InlineData("chatgpt.com/c/" + Id1, Provider.ChatGpt, "https://chatgpt.com/c/" + Id1)]
    [InlineData("https://CHATGPT.com/g/g-abc123-my-gpt/c/" + Id1, Provider.ChatGpt, "https://chatgpt.com/g/g-abc123-my-gpt/c/" + Id1)]
    [InlineData("https://claude.ai/chat/" + Id2, Provider.Claude, "https://claude.ai/chat/" + Id2)]
    [InlineData("https://www.claude.ai/chat/" + Id2 + "?ref=x", Provider.Claude, "https://claude.ai/chat/" + Id2 + "?ref=x")]
    public void Valid_links_are_detected_and_normalised(string input, Provider provider, string expected)
    {
        var check = ConversationUrl.Check(input);
        Assert.True(check.IsValid, check.Error);
        Assert.Equal(provider, check.Provider);
        Assert.Equal(expected, check.NormalizedUrl);
        Assert.Equal(ConversationUrlKind.Conversation, check.Kind);
        Assert.Null(check.Warning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://example.com/c/" + Id1)]
    [InlineData("https://chatgpt.com.evil.example/c/" + Id1)]
    [InlineData("https://evilchatgpt.com/c/" + Id1)]
    [InlineData("ftp://chatgpt.com/c/" + Id1)]
    [InlineData("file:///C:/Users/me/chat.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@chatgpt.com/c/" + Id1)]
    [InlineData("https://chatgpt.com:8443/c/" + Id1)]
    [InlineData("https://chatgpt.com/c/" + Id1 + " https://claude.ai/chat/" + Id2)]
    public void Invalid_links_are_rejected_with_a_message(string? input)
    {
        var check = ConversationUrl.Check(input);
        Assert.False(check.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(check.Error));
        Assert.Null(check.Provider);
        Assert.Null(check.NormalizedUrl);
    }

    [Theory]
    [InlineData("https://chatgpt.com/", ConversationUrlKind.Other)]
    [InlineData("https://chatgpt.com/g/g-p-67ab12cd34-hypercars/project", ConversationUrlKind.ProjectPage)]
    [InlineData("https://chatgpt.com/share/" + Id1, ConversationUrlKind.SharedLink)]
    [InlineData("https://claude.ai/project/" + Id2, ConversationUrlKind.ProjectPage)]
    [InlineData("https://claude.ai/share/" + Id2, ConversationUrlKind.SharedLink)]
    [InlineData("https://claude.ai/recents", ConversationUrlKind.Other)]
    public void Non_conversation_pages_are_allowed_with_a_warning(string input, ConversationUrlKind kind)
    {
        var check = ConversationUrl.Check(input);
        Assert.True(check.IsValid);
        Assert.Equal(kind, check.Kind);
        Assert.NotNull(check.Warning);
    }

    [Fact]
    public void Same_conversation_is_recognised_across_aliases_and_redirects()
    {
        // ChatGPT may redirect /c/{id} to /g/{gpt}/c/{id} and append query strings.
        Assert.True(ConversationUrl.IsSameConversation(Provider.ChatGpt,
            "https://chatgpt.com/c/" + Id1,
            "https://chatgpt.com/g/g-abc123-my-gpt/c/" + Id1.ToUpperInvariant() + "?model=gpt-5"));
        Assert.True(ConversationUrl.IsSameConversation(Provider.ChatGpt,
            "https://chat.openai.com/c/" + Id1,
            "https://chatgpt.com/c/" + Id1 + "/"));
        Assert.True(ConversationUrl.IsSameConversation(Provider.Claude,
            "https://claude.ai/chat/" + Id2,
            "https://claude.ai/chat/" + Id2 + "#bottom"));
    }

    [Fact]
    public void Different_conversations_and_other_pages_are_not_the_same()
    {
        Assert.False(ConversationUrl.IsSameConversation(Provider.ChatGpt, "https://chatgpt.com/c/" + Id1, "https://chatgpt.com/c/" + Id2));
        Assert.False(ConversationUrl.IsSameConversation(Provider.Claude, "https://claude.ai/chat/" + Id2, "https://claude.ai/login?returnTo=%2Fchat%2F" + Id2));
        Assert.False(ConversationUrl.IsSameConversation(Provider.Claude, "https://claude.ai/chat/" + Id2, "https://accounts.google.com/o/oauth2"));
        Assert.False(ConversationUrl.IsSameConversation(Provider.ChatGpt, "https://chatgpt.com/c/" + Id1, null));
        Assert.False(ConversationUrl.IsSameConversation(Provider.ChatGpt, "https://chatgpt.com/c/" + Id1, "about:blank"));
    }

    [Fact]
    public void Keys_are_provider_specific()
    {
        var chat = ConversationUrl.GetKey(Provider.ChatGpt, "https://chatgpt.com/c/" + Id1);
        var claude = ConversationUrl.GetKey(Provider.Claude, "https://claude.ai/chat/" + Id1);
        Assert.NotEqual(chat, claude);
        Assert.Equal(Id1, ConversationUrl.TryGetConversationId(Provider.ChatGpt, "https://chatgpt.com/c/" + Id1));
        Assert.Null(ConversationUrl.TryGetConversationId(Provider.Claude, "https://chatgpt.com/c/" + Id1));
    }

    [Theory]
    [InlineData("https://claude.ai/login?returnTo=%2Fchat%2Fabc", true)]
    [InlineData("https://claude.ai/magic-link#token", true)]
    [InlineData("https://chatgpt.com/auth/login", true)]
    [InlineData("https://auth.openai.com/log-in", true)]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=x", true)]
    [InlineData("https://appleid.apple.com/auth/authorize", true)]
    [InlineData("https://login.microsoftonline.com/common/oauth2", true)]
    [InlineData("https://claude.ai/chat/" + Id2, false)]
    [InlineData("https://chatgpt.com/", false)]
    [InlineData("https://chatgpt.com/c/" + Id1, false)]
    [InlineData("https://example.com/login", false)]
    [InlineData("https://claude.ai/loginhelp", false)]
    [InlineData("about:blank", false)]
    [InlineData(null, false)]
    public void Sign_in_pages_are_recognised(string? url, bool expected)
    {
        Assert.Equal(expected, ConversationUrl.IsSignInPage(url));
    }

    [Theory]
    [InlineData(Provider.ChatGpt, "Racing AI tuning - ChatGPT", "Racing AI tuning")]
    [InlineData(Provider.ChatGpt, "ChatGPT", "")]
    [InlineData(Provider.Claude, "Multiplayer netcode - Claude", "Multiplayer netcode")]
    [InlineData(Provider.Claude, "Claude", "")]
    [InlineData(Provider.Claude, null, "")]
    public void Page_titles_become_suggested_names(Provider provider, string? title, string expected)
    {
        Assert.Equal(expected, ConversationUrl.SuggestName(provider, title));
    }
}
