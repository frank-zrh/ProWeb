namespace ProWeb.Client.Core;

/// <summary>A single browser tab with its own navigation history and title.</summary>
public sealed class BrowserTab
{
    public BrowserTab(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public string Title { get; set; } = "New Tab";

    public NavigationState Navigation { get; } = new();

    public string? CurrentUrl => Navigation.CurrentUrl;
}

/// <summary>Manages the set of open tabs and the active-tab selection.</summary>
public sealed class TabCollection
{
    private readonly List<BrowserTab> _tabs = new();
    private int _activeIndex = -1;

    public IReadOnlyList<BrowserTab> Tabs => _tabs;

    public int Count => _tabs.Count;

    public BrowserTab? Active =>
        _activeIndex >= 0 && _activeIndex < _tabs.Count ? _tabs[_activeIndex] : null;

    public BrowserTab AddTab()
    {
        var tab = new BrowserTab(Guid.NewGuid().ToString("N"));
        _tabs.Add(tab);
        _activeIndex = _tabs.Count - 1;
        return tab;
    }

    public bool Activate(string tabId)
    {
        var idx = _tabs.FindIndex(t => t.Id == tabId);
        if (idx < 0)
            return false;
        _activeIndex = idx;
        return true;
    }

    /// <summary>Closes a tab; the last tab cannot be closed so a window always has content.</summary>
    public bool CloseTab(string tabId)
    {
        if (_tabs.Count <= 1)
            return false;

        var idx = _tabs.FindIndex(t => t.Id == tabId);
        if (idx < 0)
            return false;

        _tabs.RemoveAt(idx);
        if (_activeIndex >= _tabs.Count)
            _activeIndex = _tabs.Count - 1;
        else if (idx < _activeIndex)
            _activeIndex--;

        return true;
    }
}
