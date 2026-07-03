namespace ProWeb.Client.Core;

/// <summary>Tracks navigation history for a single tab, supporting back/forward traversal.</summary>
public sealed class NavigationState
{
    private readonly List<string> _history = new();
    private int _index = -1;

    public string? CurrentUrl => _index >= 0 && _index < _history.Count ? _history[_index] : null;

    public bool CanGoBack => _index > 0;

    public bool CanGoForward => _index >= 0 && _index < _history.Count - 1;

    /// <summary>Navigates to a new URL, truncating any forward history.</summary>
    public void Navigate(string url)
    {
        if (string.IsNullOrEmpty(url))
            return;

        // Re-navigating to the same current URL is a no-op.
        if (CurrentUrl == url)
            return;

        if (_index < _history.Count - 1)
            _history.RemoveRange(_index + 1, _history.Count - _index - 1);

        _history.Add(url);
        _index = _history.Count - 1;
    }

    public string? GoBack()
    {
        if (!CanGoBack)
            return CurrentUrl;
        _index--;
        return CurrentUrl;
    }

    public string? GoForward()
    {
        if (!CanGoForward)
            return CurrentUrl;
        _index++;
        return CurrentUrl;
    }

    public IReadOnlyList<string> History => _history;
}
