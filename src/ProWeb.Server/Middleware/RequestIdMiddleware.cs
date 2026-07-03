namespace ProWeb.Server.Middleware;

/// <summary>
/// Assigns a stable RequestId to every request (honoring an inbound X-Request-Id when present),
/// exposes it on <see cref="HttpContext.Items"/> and the response header, and pushes it into the
/// Serilog logging scope for full-chain tracing.
/// </summary>
public sealed class RequestIdMiddleware
{
    public const string HeaderName = "X-Request-Id";
    public const string ItemKey = "RequestId";

    private readonly RequestDelegate _next;

    public RequestIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<RequestIdMiddleware> logger)
    {
        var requestId = context.Request.Headers.TryGetValue(HeaderName, out var inbound) &&
                        !string.IsNullOrWhiteSpace(inbound)
            ? inbound.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = requestId;
        context.Response.Headers[HeaderName] = requestId;

        using (logger.BeginScope(new Dictionary<string, object> { ["RequestId"] = requestId }))
        {
            await _next(context).ConfigureAwait(false);
        }
    }
}

/// <summary>Convenience accessors for the ambient RequestId.</summary>
public static class RequestIdAccessor
{
    public static string GetRequestId(this HttpContext context) =>
        context.Items.TryGetValue(RequestIdMiddleware.ItemKey, out var value) && value is string s
            ? s
            : "unknown";
}
