using ForgeDesk.Core.Browsers;

namespace ForgeDesk.Core.Tests;

public class BrowserPoolTests
{
    private sealed class FakeBrowser(string name)
    {
        public string Name { get; } = name;

        public bool PlayingAudio { get; set; }

        public override string ToString() => Name;
    }

    private DateTimeOffset _now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    private BrowserPool<FakeBrowser> NewPool(int max = 2, int minHiddenMinutes = 10) =>
        new(max, TimeSpan.FromMinutes(minHiddenMinutes), () => _now) { IsBusy = b => b.PlayingAudio };

    private static FakeBrowser AddAndShow(BrowserPool<FakeBrowser> pool, string key)
    {
        var browser = new FakeBrowser(key);
        pool.Add(key, browser);
        pool.Show(key);
        return browser;
    }

    [Fact]
    public void Showing_a_conversation_hides_the_previous_one_but_keeps_it_alive()
    {
        var pool = NewPool();
        var a = AddAndShow(pool, "a");
        var b = AddAndShow(pool, "b");

        Assert.Equal("b", pool.ShownKey);
        Assert.Same(b, pool.Shown);
        Assert.True(pool.TryGet("a", out var stillThere));
        Assert.Same(a, stillThere);
        Assert.NotNull(pool.HiddenSince("a"));
        Assert.Null(pool.HiddenSince("b"));
    }

    [Fact]
    public void Returning_to_a_conversation_reuses_the_same_instance()
    {
        var pool = NewPool(max: 5);
        var a = AddAndShow(pool, "a");
        AddAndShow(pool, "b");

        Assert.True(pool.TryGet("a", out var again));
        pool.Show("a");
        Assert.Same(a, again);
        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void Recently_hidden_conversations_are_never_released_even_over_the_cap()
    {
        var pool = NewPool(max: 2, minHiddenMinutes: 10);
        AddAndShow(pool, "a");
        AddAndShow(pool, "b");
        AddAndShow(pool, "c");
        AddAndShow(pool, "d");

        _now = _now.AddMinutes(5);
        Assert.Empty(pool.TrimExcess());
        Assert.Equal(4, pool.Count);
    }

    [Fact]
    public void Least_recently_shown_hidden_instances_are_released_after_the_grace_period()
    {
        var pool = NewPool(max: 2, minHiddenMinutes: 10);
        AddAndShow(pool, "a");
        _now = _now.AddMinutes(1);
        AddAndShow(pool, "b");
        _now = _now.AddMinutes(1);
        AddAndShow(pool, "c");
        _now = _now.AddMinutes(1);
        AddAndShow(pool, "d");

        _now = _now.AddMinutes(11);
        var released = pool.TrimExcess();

        Assert.Equal(["a", "b"], released.Select(r => r.Name));
        Assert.Equal(2, pool.Count);
        Assert.True(pool.Contains("c"));
        Assert.Equal("d", pool.ShownKey);
    }

    [Fact]
    public void The_shown_instance_is_never_released()
    {
        var pool = NewPool(max: 1, minHiddenMinutes: 0);
        AddAndShow(pool, "a");
        AddAndShow(pool, "b");

        var released = pool.TrimExcess();

        Assert.Equal(["a"], released.Select(r => r.Name));
        Assert.Equal("b", pool.ShownKey);
        Assert.Empty(pool.TrimExcess());
    }

    [Fact]
    public void Busy_instances_are_protected()
    {
        var pool = NewPool(max: 1, minHiddenMinutes: 0);
        var a = AddAndShow(pool, "a");
        AddAndShow(pool, "b");
        a.PlayingAudio = true;

        Assert.Empty(pool.TrimExcess());
        a.PlayingAudio = false;
        Assert.Single(pool.TrimExcess());
    }

    [Fact]
    public void Lowering_the_cap_takes_effect_on_the_next_trim()
    {
        var pool = NewPool(max: 10, minHiddenMinutes: 0);
        foreach (var key in new[] { "a", "b", "c", "d" })
        {
            AddAndShow(pool, key);
        }

        Assert.Empty(pool.TrimExcess());
        pool.MaxLive = 2;
        Assert.Equal(2, pool.TrimExcess().Count);
        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void Hiding_everything_makes_the_last_shown_instance_eligible_later()
    {
        var pool = NewPool(max: 1, minHiddenMinutes: 10);
        AddAndShow(pool, "a");
        AddAndShow(pool, "b");
        pool.HideShown();
        Assert.Null(pool.ShownKey);

        _now = _now.AddMinutes(30);
        Assert.Single(pool.TrimExcess());
        Assert.Equal(1, pool.Count);
    }

    [Fact]
    public void Rekeying_keeps_the_instance_and_its_shown_state()
    {
        var pool = NewPool();
        var browse = AddAndShow(pool, "home");

        Assert.True(pool.Rekey("home", "chatgpt:c:1"));
        Assert.Equal("chatgpt:c:1", pool.ShownKey);
        Assert.True(pool.TryGet("chatgpt:c:1", out var same));
        Assert.Same(browse, same);
        Assert.False(pool.Contains("home"));

        pool.Add("other", new FakeBrowser("other"));
        Assert.False(pool.Rekey("other", "chatgpt:c:1"));
    }

    [Fact]
    public void Duplicate_keys_are_rejected_and_clear_returns_everything()
    {
        var pool = NewPool();
        AddAndShow(pool, "a");
        Assert.Throws<InvalidOperationException>(() => pool.Add("a", new FakeBrowser("a2")));
        AddAndShow(pool, "b");

        var all = pool.Clear();
        Assert.Equal(2, all.Count);
        Assert.Equal(0, pool.Count);
        Assert.Null(pool.ShownKey);
    }
}
