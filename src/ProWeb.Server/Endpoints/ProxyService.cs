using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ProWeb.Server.Auth;
using ProWeb.Server.Config;
using ProWeb.Server.Fetching;
using ProWeb.Server.Observability;
using ProWeb.Server.Storage;
using ProWeb.Shared.Content;
using ProWeb.Shared.Crypto;
using ProWeb.Shared.Protocol;

namespace ProWeb.Server.Endpoints;

/// <summary>
/// Orchestrates a proxied request: anti-replay check → cache lookup → fetcher selection (with
/// headless degrade) → content rewriting → response envelope assembly → request logging. Returns a
/// <see cref="ResponseEnvelope"/>; transport-level encryption is handled by the endpoint.
/// </summary>
public sealed class ProxyService
{
    private readonly ReplayGuard _replayGuard;
    private readonly IFetchDispatcher _dispatcher;
    private readonly ContentRewriter _rewriter;
    private readonly RequestLogRepository _logs;
    private readonly CacheRepository _cache;
    private readonly int _cacheTtlSeconds;
    private readonly ILogger<ProxyService> _logger;

    public ProxyService(
        ReplayGuard replayGuard,
        IFetchDispatcher dispatcher,
        ContentRewriter rewriter,
        RequestLogRepository logs,
        CacheRepository cache,
        ProWebOptions options,
        ILogger<ProxyService> logger)
    {
        _replayGuard = replayGuard;
        _dispatcher = dispatcher;
        _rewriter = rewriter;
        _logs = logs;
        _cache = cache;
        _cacheTtlSeconds = options.Session.CacheTtlSeconds;
        _logger = logger;
    }

    public async Task<ResponseEnvelope> ProcessAsync(
        RequestEnvelope request,
        SessionContext session,
        CancellationToken cancellationToken)
    {
        ValidateEnvelope(request);

        if (!_replayGuard.TryAccept(request.RequestId, request.TimestampUnixMs))
            throw new ReplayRejectedException(request.RequestId);

        if (!UrlGuard.IsAllowed(request.TargetUrl, out var reason))
            throw new FetchBlockedException(reason);

        var stopwatch = Stopwatch.StartNew();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cacheEnabled = _cacheTtlSeconds > 0;
        var partitionKey = cacheEnabled
            ? ContentCachePolicy.PartitionKey(session.SessionId, request.Method, request.TargetUrl)
            : null;

        if (cacheEnabled && string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var cached = _cache.Get(partitionKey!, nowMs);
            if (cached is not null)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "Cache hit for {RequestId} ({Url}).", request.RequestId, request.TargetUrl);
                LogRequest(request, session, cached.Body?.Length ?? 0, 200, "cache", stopwatch.ElapsedMilliseconds, nowMs);
                return new ResponseEnvelope
                {
                    RequestId = request.RequestId,
                    StatusCode = 200,
                    Body = cached.Body ?? Array.Empty<byte>(),
                    ContentType = cached.Mime ?? string.Empty,
                    FinalUrl = cached.Url,
                    ServerElapsedMs = stopwatch.ElapsedMilliseconds,
                };
            }
        }

        var fetchRequest = new FetchRequest
        {
            SessionId = session.SessionId,
            Method = request.Method,
            Url = request.TargetUrl,
            Body = request.Body,
        };
        foreach (var (key, value) in request.Headers)
            fetchRequest.Headers[key] = value;

        FetchResult result = await _dispatcher.DispatchAsync(fetchRequest, cancellationToken)
            .ConfigureAwait(false);
        var rewrittenBody = RewriteIfNeeded(result);
        stopwatch.Stop();

        if (cacheEnabled &&
            ContentCachePolicy.IsCacheable(request.Method, result.StatusCode, result.ContentType, result.Headers))
        {
            _cache.Put(new CacheRecord
            {
                PartitionKey = partitionKey!,
                Url = result.FinalUrl,
                Mime = result.ContentType,
                Body = rewrittenBody,
                StoredAtUnixMs = nowMs,
                ExpiresAtUnixMs = nowMs + (_cacheTtlSeconds * 1000L),
            });
        }

        var response = new ResponseEnvelope
        {
            RequestId = request.RequestId,
            StatusCode = result.StatusCode,
            Body = rewrittenBody,
            ContentType = result.ContentType,
            FinalUrl = result.FinalUrl,
            ServerElapsedMs = stopwatch.ElapsedMilliseconds,
        };
        foreach (var (key, value) in result.Headers)
            response.Headers[key] = value;

        LogRequest(request, session, rewrittenBody.Length, result.StatusCode, result.FetcherType, stopwatch.ElapsedMilliseconds, nowMs);

        return response;
    }

    /// <summary>Allowed HTTP methods for proxied requests (per SECURITY_DESIGN input validation).</summary>
    public static readonly IReadOnlySet<string> AllowedMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "DELETE", "HEAD", "PATCH", "OPTIONS",
    };

    private const int MaxHeaderCount = 100;
    private const int MaxHeaderValueLength = 8192;

    private static void ValidateEnvelope(RequestEnvelope request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
            throw new InvalidEnvelopeException("requestId is required.");
        if (string.IsNullOrWhiteSpace(request.TargetUrl))
            throw new InvalidEnvelopeException("targetUrl is required.");
        if (string.IsNullOrWhiteSpace(request.Method) || !AllowedMethods.Contains(request.Method))
            throw new InvalidEnvelopeException("method is not in the allowed set.");
        if (request.Headers.Count > MaxHeaderCount)
            throw new InvalidEnvelopeException("too many headers.");
        foreach (var (key, value) in request.Headers)
        {
            if (string.IsNullOrEmpty(key) || (value?.Length ?? 0) > MaxHeaderValueLength)
                throw new InvalidEnvelopeException("header field is invalid or exceeds the size limit.");
        }
    }

    private void LogRequest(
        RequestEnvelope request, SessionContext session, int bodyLength, int statusCode,
        string fetcherType, long elapsedMs, long nowMs)
    {
        // Redact any query-string tokens/PII before the URL is persisted or logged.
        var redactedUrl = SensitiveDataRedactor.RedactUrl(request.TargetUrl);
        _logs.Add(new RequestLogRecord
        {
            RequestId = request.RequestId,
            SessionId = session.SessionId,
            Method = request.Method,
            TargetUrl = redactedUrl,
            StatusCode = statusCode,
            FetcherType = fetcherType,
            ServerElapsedMs = elapsedMs,
            CreatedAtUnixMs = nowMs,
        });
        _logger.LogInformation(
            "Proxied {Method} {Url} → {Status} via {Fetcher} in {Elapsed}ms ({Bytes}B) [{RequestId}]",
            request.Method, redactedUrl, statusCode, fetcherType, elapsedMs, bodyLength, request.RequestId);
    }

    private byte[] RewriteIfNeeded(FetchResult result)
    {
        if (result.Body.Length == 0) return result.Body;
        var contentType = result.ContentType.ToLowerInvariant();
        if (contentType.Contains("text/html"))
            return _rewriter.RewriteHtml(result.Body, result.FinalUrl, result.ContentType);
        if (contentType.Contains("text/css"))
            return _rewriter.RewriteCss(result.Body, result.FinalUrl, result.ContentType);
        return result.Body;
    }
}

/// <summary>Thrown when a request is rejected by the anti-replay guard.</summary>
public sealed class ReplayRejectedException : Exception
{
    public ReplayRejectedException(string requestId)
        : base($"Request {requestId} rejected as a replay or outside the freshness window.")
    {
        RequestId = requestId;
    }

    public string RequestId { get; }
}

/// <summary>Thrown when a request envelope fails field validation (method whitelist, size limits).</summary>
public sealed class InvalidEnvelopeException : Exception
{
    public InvalidEnvelopeException(string message)
        : base(message)
    {
    }
}
