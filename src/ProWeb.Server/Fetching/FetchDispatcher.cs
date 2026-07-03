using Microsoft.Extensions.Logging;

namespace ProWeb.Server.Fetching;

/// <summary>
/// Chooses a fetcher for a request and applies the headless→HTTP degrade path. Extracted behind
/// an interface so the proxy pipeline can be tested without real network access.
/// </summary>
public interface IFetchDispatcher
{
    Task<FetchResult> DispatchAsync(FetchRequest request, CancellationToken cancellationToken);
}

/// <summary>Default dispatcher backed by <see cref="FetcherSelector"/> and the concrete fetchers.</summary>
public sealed class FetchDispatcher : IFetchDispatcher
{
    private readonly FetcherSelector _selector;
    private readonly IContentFetcher _httpFetcher;
    private readonly IHeadlessFetcher _headlessFetcher;
    private readonly ILogger<FetchDispatcher> _logger;

    public FetchDispatcher(
        FetcherSelector selector,
        IContentFetcher httpFetcher,
        IHeadlessFetcher headlessFetcher,
        ILogger<FetchDispatcher> logger)
    {
        _selector = selector;
        _httpFetcher = httpFetcher;
        _headlessFetcher = headlessFetcher;
        _logger = logger;
    }

    public async Task<FetchResult> DispatchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        var strategy = _selector.SelectForUrl(request.Url);
        if (strategy == "headless" && !_headlessFetcher.IsUnavailable)
        {
            try
            {
                return await _headlessFetcher.FetchAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HeadlessUnavailableException ex)
            {
                _logger.LogWarning(ex, "Degrading to HTTP fetcher for {Url}.", request.Url);
            }
        }

        var httpResult = await _httpFetcher.FetchAsync(request, cancellationToken).ConfigureAwait(false);

        // HTML-feature escalation: if the HTTP response looks like a client-rendered SPA, re-fetch
        // once through the headless browser so dynamic content renders. Bounded by the config toggle
        // and the headless availability/degrade path.
        if (_selector.SpaHeuristicEnabled &&
            !_headlessFetcher.IsUnavailable &&
            IsHtml(httpResult.ContentType) &&
            _selector.LooksLikeSpa(DecodeHtml(httpResult.Body)))
        {
            try
            {
                _logger.LogInformation("SPA heuristic matched for {Url}; escalating to headless.", request.Url);
                return await _headlessFetcher.FetchAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HeadlessUnavailableException ex)
            {
                _logger.LogWarning(ex, "Headless unavailable for SPA escalation of {Url}; keeping HTTP result.", request.Url);
            }
        }

        return httpResult;
    }

    private static bool IsHtml(string contentType) =>
        contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);

    private static string DecodeHtml(byte[] body)
    {
        if (body.Length == 0) return string.Empty;
        // Only need the markup for heuristic sniffing; a bounded prefix keeps this cheap.
        var length = Math.Min(body.Length, 65536);
        return System.Text.Encoding.UTF8.GetString(body, 0, length);
    }
}
