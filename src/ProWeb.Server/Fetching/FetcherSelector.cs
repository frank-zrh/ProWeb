using Microsoft.Extensions.Options;
using ProWeb.Server.Config;

namespace ProWeb.Server.Fetching;

/// <summary>
/// Decides whether a request should be served by the plain HTTP fetcher or the headless browser.
/// Selection is by configured domain allow-list first, then by heuristic SPA detection on an
/// already-fetched HTML body.
/// </summary>
public sealed class FetcherSelector
{
    private readonly FetchOptions _options;

    public FetcherSelector(IOptions<ProWebOptions> options)
    {
        _options = options.Value.Fetch;
    }

    /// <summary>Whether HTML-feature (SPA) escalation to headless is enabled by configuration.</summary>
    public bool SpaHeuristicEnabled => _options.EnableSpaHeuristic;

    /// <summary>Returns "headless" when the URL host matches a configured headless domain.</summary>
    public string SelectForUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "http";

        var host = uri.Host;
        foreach (var domain in _options.HeadlessDomains)
        {
            if (string.IsNullOrWhiteSpace(domain)) continue;
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                return "headless";
        }

        return "http";
    }

    /// <summary>
    /// Heuristic: an HTML document with an app-root mount point but little static body text is
    /// likely a single-page application that needs client-side rendering.
    /// </summary>
    public bool LooksLikeSpa(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return false;

        var lower = html.ToLowerInvariant();
        var hasAppRoot =
            lower.Contains("id=\"root\"") || lower.Contains("id='root'") ||
            lower.Contains("id=\"app\"") || lower.Contains("id='app'") ||
            lower.Contains("ng-app") || lower.Contains("data-reactroot");

        var bodyStart = lower.IndexOf("<body", StringComparison.Ordinal);
        var visibleTextLength = bodyStart >= 0 ? html.Length - bodyStart : html.Length;
        var scriptCount = CountOccurrences(lower, "<script");

        return hasAppRoot && scriptCount >= 1 && visibleTextLength < 4000;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
