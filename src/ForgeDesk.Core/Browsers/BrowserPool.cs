namespace ForgeDesk.Core.Browsers;

/// <summary>
/// Lifecycle bookkeeping for the browser instances of one side of the desk.
///
/// One instance exists per conversation (keyed by conversation identity, so a conversation
/// mapped into several projects shares one instance). At most one instance is shown at a
/// time; hidden instances stay alive so streaming responses can continue and scroll/draft
/// state is preserved.
///
/// The pool is a soft cap: when it holds more than <see cref="MaxLive"/> instances it releases
/// the least recently shown hidden ones, but only after they have been hidden for at least
/// <see cref="MinHiddenBeforeRelease"/> and only if <see cref="IsBusy"/> doesn't object.
/// A conversation that was just switched away from is therefore never closed immediately,
/// even if the cap is exceeded for a while.
/// </summary>
public sealed class BrowserPool<T> where T : class
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private int _maxLive;

    public BrowserPool(int maxLive, TimeSpan minHiddenBeforeRelease, Func<DateTimeOffset>? clock = null)
    {
        MaxLive = maxLive;
        MinHiddenBeforeRelease = minHiddenBeforeRelease;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int MaxLive
    {
        get => _maxLive;
        set => _maxLive = Math.Max(1, value);
    }

    public TimeSpan MinHiddenBeforeRelease { get; set; }

    /// <summary>Optional veto, e.g. "this page is playing audio".</summary>
    public Func<T, bool>? IsBusy { get; set; }

    public int Count => _entries.Count;

    public string? ShownKey { get; private set; }

    public T? Shown => ShownKey is not null && _entries.TryGetValue(ShownKey, out var e) ? e.Instance : null;

    public IEnumerable<string> Keys => _entries.Keys;

    public IEnumerable<T> Instances => _entries.Values.Select(e => e.Instance);

    public bool Contains(string key) => _entries.ContainsKey(key);

    public bool TryGet(string key, out T instance)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            instance = entry.Instance;
            return true;
        }

        instance = null!;
        return false;
    }

    public string? KeyOf(T instance) =>
        _entries.FirstOrDefault(kv => ReferenceEquals(kv.Value.Instance, instance)).Key;

    public void Add(string key, T instance)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(instance);
        if (_entries.ContainsKey(key))
        {
            throw new InvalidOperationException($"An instance for '{key}' already exists.");
        }

        _entries[key] = new Entry(instance, _clock());
    }

    /// <summary>Marks an instance as the one on screen; the previously shown one becomes hidden.</summary>
    public void Show(string key)
    {
        if (!_entries.ContainsKey(key))
        {
            throw new KeyNotFoundException(key);
        }

        if (ShownKey == key)
        {
            return;
        }

        HideShown();
        ShownKey = key;
        _entries[key].HiddenSince = null;
    }

    /// <summary>Marks whatever is on screen as hidden (e.g. the empty state is showing).</summary>
    public void HideShown()
    {
        if (ShownKey is not null && _entries.TryGetValue(ShownKey, out var previous))
        {
            previous.HiddenSince = _clock();
        }

        ShownKey = null;
    }

    /// <summary>Changes the key of an instance (e.g. the page it shows was just mapped).</summary>
    public bool Rekey(string oldKey, string newKey)
    {
        if (oldKey == newKey || !_entries.TryGetValue(oldKey, out var entry) || _entries.ContainsKey(newKey))
        {
            return false;
        }

        _entries.Remove(oldKey);
        _entries[newKey] = entry;
        if (ShownKey == oldKey)
        {
            ShownKey = newKey;
        }

        return true;
    }

    public T? Remove(string key)
    {
        if (!_entries.Remove(key, out var entry))
        {
            return null;
        }

        if (ShownKey == key)
        {
            ShownKey = null;
        }

        return entry.Instance;
    }

    /// <summary>
    /// Removes and returns hidden instances that should be released to respect the cap.
    /// The caller disposes them.
    /// </summary>
    public IReadOnlyList<T> TrimExcess()
    {
        var released = new List<T>();
        if (_entries.Count <= MaxLive)
        {
            return released;
        }

        var now = _clock();
        var candidates = _entries
            .Where(kv => kv.Key != ShownKey && kv.Value.HiddenSince is not null)
            .Where(kv => now - kv.Value.HiddenSince!.Value >= MinHiddenBeforeRelease)
            .Where(kv => IsBusy is null || !IsBusy(kv.Value.Instance))
            .OrderBy(kv => kv.Value.HiddenSince)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in candidates)
        {
            if (_entries.Count <= MaxLive)
            {
                break;
            }

            released.Add(Remove(key)!);
        }

        return released;
    }

    /// <summary>Removes everything (on shutdown). The caller disposes the returned instances.</summary>
    public IReadOnlyList<T> Clear()
    {
        var all = _entries.Values.Select(e => e.Instance).ToList();
        _entries.Clear();
        ShownKey = null;
        return all;
    }

    /// <summary>When an instance was hidden, or null if it is shown or never was.</summary>
    public DateTimeOffset? HiddenSince(string key) =>
        _entries.TryGetValue(key, out var e) ? e.HiddenSince : null;

    private sealed class Entry(T instance, DateTimeOffset createdAt)
    {
        public T Instance { get; } = instance;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        // New instances count as hidden from creation until shown, so an instance that was
        // created but never displayed can still be released.
        public DateTimeOffset? HiddenSince { get; set; } = createdAt;
    }
}
