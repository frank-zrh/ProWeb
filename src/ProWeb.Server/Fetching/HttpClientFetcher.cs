using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using ProWeb.Server.Config;
using ProWeb.Shared.Content;

namespace ProWeb.Server.Fetching;

/// <summary>
/// Fetches content over HTTP(S) using a pooled <see cref="SocketsHttpHandler"/>. Cookies are
/// isolated per session, responses are transparently decompressed, and transient failures are
/// retried via a Polly pipeline. SSRF is prevented via <see cref="UrlGuard"/>.
/// </summary>
public sealed class HttpClientFetcher : IContentFetcher, IDisposable
{
    private readonly HttpClient _client;
    private readonly SessionCookieStore _cookies;
    private readonly FetchOptions _options;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public HttpClientFetcher(IOptions<ProWebOptions> options, SessionCookieStore cookies)
    {
        _options = options.Value.Fetch;
        _cookies = cookies;

        var handler = new SocketsHttpHandler
        {
            UseCookies = false, // Cookies are managed per session by SessionCookieStore.
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
        };
        // SSRF defense-in-depth: validate the actual connected IP on EVERY socket connection,
        // including redirect hops and DNS-resolved hostnames (the initial UrlGuard.IsAllowed only
        // sees the client-supplied literal URL). This closes the redirect-bypass and
        // DNS-resolution / rebinding gaps in one place.
        handler.ConnectCallback = GuardedConnectAsync;
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
        };

        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = _options.RetryCount,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>(ex => ex.InnerException is not FetchBlockedException)
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
            })
            .Build();
    }

    public string FetcherType => "http";

    public async Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        if (!UrlGuard.IsAllowed(request.Url, out var reason))
            throw new FetchBlockedException(reason);

        var uri = new Uri(request.Url);

        var response = await _pipeline.ExecuteAsync(
            async ct => await SendOnceAsync(request, uri, ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        using (response)
        {
            var finalUri = response.RequestMessage?.RequestUri ?? uri;
            CaptureCookies(request.SessionId, finalUri, response);

            var body = await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
            var result = new FetchResult
            {
                StatusCode = (int)response.StatusCode,
                Body = body,
                FinalUrl = finalUri.ToString(),
                ContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
                FetcherType = FetcherType,
            };

            foreach (var header in response.Headers)
                result.Headers[header.Key] = string.Join(", ", header.Value);
            foreach (var header in response.Content.Headers)
                result.Headers[header.Key] = string.Join(", ", header.Value);

            return result;
        }
    }

    private static async ValueTask<Stream> GuardedConnectAsync(
        SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        IPAddress[] addresses = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        if (addresses.Length == 0)
            throw new FetchBlockedException($"host '{host}' did not resolve");

        // Reject if ANY resolved address is in a blocked range. Validating the full resolved
        // set (and connecting only to that set below) mitigates DNS-rebinding between the
        // resolution and the connect. Shared with the headless path via UrlGuard.
        if (UrlGuard.FirstBlockedAddress(addresses) is { } blocked)
            throw new FetchBlockedException($"host '{host}' resolves to blocked address {blocked}");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(FetchRequest request, Uri uri, CancellationToken ct)
    {
        var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
        if (request.Body is { Length: > 0 })
            message.Content = new ByteArrayContent(request.Body);

        // Default identity headers unless the caller supplied overrides.
        message.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        message.Headers.TryAddWithoutValidation("Accept-Language", _options.AcceptLanguage);
        message.Headers.TryAddWithoutValidation(
            "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");

        foreach (var (key, value) in request.Headers)
        {
            if (IsRestrictedHeader(key)) continue;
            message.Headers.TryAddWithoutValidation(key, value);
        }

        var cookieHeader = _cookies.GetCookieHeader(request.SessionId, uri);
        if (cookieHeader is not null)
            message.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

        return await _client.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var max = _options.MaxResponseBytes;

        // Fail fast when the upstream advertises an oversized body.
        if (response.Content.Headers.ContentLength is long declared && declared > max)
            throw new ContentTooLargeException(declared, max);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > max)
                throw new ContentTooLargeException(total, max);
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private void CaptureCookies(string sessionId, Uri finalUri, HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            _cookies.Capture(sessionId, finalUri, setCookies);
    }

    private static bool IsRestrictedHeader(string key) =>
        key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Connection", StringComparison.OrdinalIgnoreCase);

    public void Dispose() => _client.Dispose();
}

/// <summary>Thrown when a target URL is blocked by the SSRF guard.</summary>
public sealed class FetchBlockedException : Exception
{
    public FetchBlockedException(string reason)
        : base($"Target URL blocked: {reason}")
    {
    }
}

/// <summary>Thrown when an upstream response body exceeds the configured maximum size.</summary>
public sealed class ContentTooLargeException : Exception
{
    public ContentTooLargeException(long observedBytes, long maxBytes)
        : base($"Upstream response of {observedBytes} bytes exceeds the {maxBytes}-byte limit.")
    {
        ObservedBytes = observedBytes;
        MaxBytes = maxBytes;
    }

    public long ObservedBytes { get; }

    public long MaxBytes { get; }
}
