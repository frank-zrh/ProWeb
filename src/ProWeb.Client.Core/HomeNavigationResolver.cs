namespace ProWeb.Client.Core;

/// <summary>
/// Resolves what a new tab / first launch / the "home" button should navigate to, honoring the
/// user's configured <see cref="ClientSettings.HomePage"/> (UT-X-R3-002). Previously HomePage was
/// persisted but never consumed by any navigation, making it a dead setting.
/// </summary>
public static class HomeNavigationResolver
{
    /// <summary>
    /// Returns the absolute, proxyable URL a "go home" / new-tab action should load, or null when
    /// no valid home page is configured (caller should then show the new-tab empty state). An empty
    /// or malformed HomePage safely falls back to null rather than navigating somewhere invalid.
    /// </summary>
    public static string? ResolveHomeTarget(ClientSettings? settings)
    {
        var raw = settings?.HomePage?.Trim();
        if (string.IsNullOrEmpty(raw))
            return null;

        var normalized = UrlNormalizer.Normalize(raw);
        return RequestSchemeClassifier.IsProxyable(normalized) ? normalized : null;
    }

    /// <summary>True when a usable home page is configured.</summary>
    public static bool HasHome(ClientSettings? settings) => ResolveHomeTarget(settings) is not null;
}
