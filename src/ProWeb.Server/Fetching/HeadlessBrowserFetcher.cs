using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProWeb.Server.Config;
using ProWeb.Shared.Content;
using PuppeteerSharp;

namespace ProWeb.Server.Fetching;

/// <summary>
/// Fetches fully-rendered HTML using PuppeteerSharp. A single browser is shared; each session
/// gets an isolated incognito context. Concurrency is bounded by a semaphore. If Chromium is not
/// present it attempts a one-time download; if that fails it throws
/// <see cref="HeadlessUnavailableException"/> so callers can degrade to the HTTP fetcher.
/// </summary>
public sealed class HeadlessBrowserFetcher : IHeadlessFetcher, IAsyncDisposable
{
    private readonly FetchOptions _options;
    private readonly ILogger<HeadlessBrowserFetcher> _logger;
    private readonly SemaphoreSlim _concurrency;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IBrowser? _browser;
    private volatile bool _unavailable;

    public HeadlessBrowserFetcher(IOptions<ProWebOptions> options, ILogger<HeadlessBrowserFetcher> logger)
    {
        _options = options.Value.Fetch;
        _logger = logger;
        _concurrency = new SemaphoreSlim(Math.Max(1, _options.MaxHeadlessConcurrency));
    }

    public string FetcherType => "headless";

    public bool IsUnavailable => _unavailable;

    public async Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        if (!UrlGuard.IsAllowed(request.Url, out var reason))
            throw new FetchBlockedException(reason);

        // SSRF parity with the HTTP path: resolve the target host and reject if it maps to a
        // loopback/private/metadata range BEFORE Chromium is allowed to connect. A per-fetch guard
        // resolves each host once and caches the verdict so the sub-request interception below is
        // consistent with this initial check (UT-C-R3-001).
        var guard = new HostResolutionGuard();
        var targetHost = new Uri(request.Url).DnsSafeHost;
        var (allowed, resolveReason) = await guard.CheckAsync(targetHost, cancellationToken).ConfigureAwait(false);
        if (!allowed)
            throw new FetchBlockedException(resolveReason);

        var browser = await EnsureBrowserAsync(cancellationToken).ConfigureAwait(false);

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        IBrowserContext? context = null;
        try
        {
            context = await browser.CreateBrowserContextAsync().ConfigureAwait(false);
            await using var page = await context.NewPageAsync().ConfigureAwait(false);
            await page.SetUserAgentAsync(_options.UserAgent).ConfigureAwait(false);

            // Intercept every navigation/subresource request so redirects and sub-requests to
            // internal addresses are blocked, not just the initial URL. The same per-fetch guard is
            // captured so repeated sub-requests to one host reuse a single resolved verdict.
            await page.SetRequestInterceptionAsync(true).ConfigureAwait(false);
            page.Request += (_, e) => _ = OnRequestInterceptedAsync(e, guard);

            var navResponse = await page.GoToAsync(request.Url, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                Timeout = _options.TimeoutSeconds * 1000,
            }).ConfigureAwait(false);

            var content = await page.GetContentAsync().ConfigureAwait(false);
            var finalUrl = page.Url;

            return new FetchResult
            {
                StatusCode = navResponse is not null ? (int)navResponse.Status : 200,
                Body = System.Text.Encoding.UTF8.GetBytes(content),
                ContentType = "text/html; charset=utf-8",
                FinalUrl = finalUrl,
                FetcherType = FetcherType,
            };
        }
        finally
        {
            if (context is not null)
                await context.CloseAsync().ConfigureAwait(false);
            _concurrency.Release();
        }
    }

    private async Task OnRequestInterceptedAsync(PuppeteerSharp.RequestEventArgs e, HostResolutionGuard guard)
    {
        try
        {
            var url = e.Request.Url;
            if (!UrlGuard.IsAllowed(url, out _))
            {
                await e.Request.AbortAsync().ConfigureAwait(false);
                return;
            }

            var host = new Uri(url).DnsSafeHost;
            var (allowed, _) = await guard.CheckAsync(host).ConfigureAwait(false);
            if (!allowed)
            {
                _logger.LogWarning("Headless request to {Url} blocked by SSRF guard.", url);
                await e.Request.AbortAsync().ConfigureAwait(false);
                return;
            }

            await e.Request.ContinueAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Headless request interception failed; aborting request.");
            try
            {
                await e.Request.AbortAsync().ConfigureAwait(false);
            }
            catch
            {
                // Request may already be resolved; ignore.
            }
        }
    }

    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        if (_unavailable)
            throw new HeadlessUnavailableException("Headless browser previously marked unavailable.");
        if (_browser is { IsConnected: true })
            return _browser;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_browser is { IsConnected: true })
                return _browser;

            try
            {
                var fetcher = new BrowserFetcher();
                await fetcher.DownloadAsync().ConfigureAwait(false);
                _browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    // '--no-sandbox' removed: rendering untrusted sites without the Chromium sandbox
                    // would let a compromised renderer escalate. The sandbox is kept enabled.
                    Args = new[] { "--disable-dev-shm-usage" },
                }).ConfigureAwait(false);
                return _browser;
            }
            catch (Exception ex)
            {
                _unavailable = true;
                _logger.LogWarning(ex, "Headless browser unavailable; degrading to HTTP fetcher.");
                throw new HeadlessUnavailableException("Failed to initialize headless browser.", ex);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync().ConfigureAwait(false);
        _initLock.Dispose();
        _concurrency.Dispose();
    }
}

/// <summary>Thrown when the headless browser cannot be initialized or used.</summary>
public sealed class HeadlessUnavailableException : Exception
{
    public HeadlessUnavailableException(string message)
        : base(message)
    {
    }

    public HeadlessUnavailableException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
