using Microsoft.Extensions.Options;
using ProWeb.Server.Config;

namespace ProWeb.Server.Middleware;

/// <summary>
/// Emits a Strict-Transport-Security (HSTS) header on responses when enabled via configuration.
/// The header instructs conforming clients to only use HTTPS for the configured max-age.
/// </summary>
public sealed class HstsMiddleware
{
    public const string HeaderName = "Strict-Transport-Security";

    private readonly RequestDelegate _next;
    private readonly string? _headerValue;

    public HstsMiddleware(RequestDelegate next, IOptions<ProWebOptions> options)
    {
        _next = next;
        _headerValue = TransportConfigurator.BuildHstsHeaderValue(options.Value.Server);
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (_headerValue is not null)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[HeaderName] = _headerValue;
                return Task.CompletedTask;
            });
        }

        return _next(context);
    }
}
