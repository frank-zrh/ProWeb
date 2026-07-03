namespace ProWeb.Client.Core;

/// <summary>A tab that was closed and can be reopened (UT-X-R1-005).</summary>
public sealed record ClosedTab(string Url, string Title);

/// <summary>
/// LIFO stack of recently-closed tabs so an accidental close can be undone (Ctrl+Shift+T). Bounded
/// so it cannot grow without limit.
/// </summary>
public sealed class ClosedTabStack
{
    private readonly LinkedList<ClosedTab> _items = new();
    private readonly int _capacity;

    public ClosedTabStack(int capacity = 25)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count => _items.Count;

    public bool CanReopen => _items.Count > 0;

    public void Push(ClosedTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        _items.AddFirst(tab);
        while (_items.Count > _capacity)
            _items.RemoveLast();
    }

    /// <summary>Pops the most recently closed tab, or null when there is nothing to reopen.</summary>
    public ClosedTab? Reopen()
    {
        if (_items.First is null)
            return null;
        var tab = _items.First.Value;
        _items.RemoveFirst();
        return tab;
    }
}
