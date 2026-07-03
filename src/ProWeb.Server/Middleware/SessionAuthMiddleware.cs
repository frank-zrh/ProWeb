using System.Text.Json;
using ProWeb.Server.Auth;
using ProWeb.Server.Storage;
using ProWeb.Shared.Protocol;

namespace ProWeb.Server.Middleware;

/// <summary>
/// Authenticates requests to protected endpoints. It validates the bearer JWT, loads the active
/// session, unprotects the session key, and stores a <see cref="SessionContext"/> on the request.
/// Requests that fail authentication receive a 401 error envelope.
/// </summary>
public sealed class SessionAuthMiddleware
{
    public const string ContextKey = "SessionContext";

    private static readonly string[] ProtectedPrefixes = { "/v1/proxy", "/v1/session/close", "/v1/stream" };

    private readonly RequestDelegate _next;

    public SessionAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        JwtService jwt,
        SessionRepository sessions,
        SessionKeyProtector protector)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!ProtectedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var token = ExtractBearer(context);
        var claims = token is null ? null : jwt.ValidateAndGetClaims(token);
        if (claims is null)
        {
            await WriteUnauthorizedAsync(context, "invalid_or_missing_token").ConfigureAwait(false);
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var session = sessions.GetActive(claims.Value.SessionId, now);
        if (session is null)
        {
            await WriteUnauthorizedAsync(context, "session_revoked_or_expired").ConfigureAwait(false);
            return;
        }

        // Device binding: the JWT 'did' claim must match the device the session was bound to at
        // handshake. This stops a leaked token from being replayed from a different device context.
        if (!string.Equals(claims.Value.DeviceId, session.DeviceId, StringComparison.Ordinal))
        {
            await WriteUnauthorizedAsync(context, "device_binding_mismatch").ConfigureAwait(false);
            return;
        }

        byte[] sessionKey;
        try
        {
            sessionKey = protector.Unprotect(session.SessionKeyProtected);
        }
        catch (Exception)
        {
            await WriteUnauthorizedAsync(context, "session_key_unavailable").ConfigureAwait(false);
            return;
        }

        context.Items[ContextKey] = new SessionContext(session, sessionKey);
        await _next(context).ConfigureAwait(false);
    }

    private static string? ExtractBearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return header[prefix.Length..].Trim();
        return null;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string error)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        var envelope = new ErrorEnvelope
        {
            RequestId = context.GetRequestId(),
            StatusCode = StatusCodes.Status401Unauthorized,
            Error = error,
            Message = "Authentication required.",
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope)).ConfigureAwait(false);
    }
}

/// <summary>Extension to retrieve the authenticated <see cref="SessionContext"/> from a request.</summary>
public static class SessionContextAccessor
{
    public static SessionContext? GetSessionContext(this HttpContext context) =>
        context.Items.TryGetValue(SessionAuthMiddleware.ContextKey, out var value)
            ? value as SessionContext
            : null;
}
