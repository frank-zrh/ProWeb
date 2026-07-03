using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ProWeb.Server.Fetching;
using ProWeb.Server.Middleware;
using ProWeb.Server.Storage;
using ProWeb.Shared.Protocol;
using ProWeb.Shared.Serialization;

namespace ProWeb.Server.Endpoints;

/// <summary>Named rate-limiting policies applied to ProWeb endpoints.</summary>
public static class RateLimitPolicies
{
    public const string Handshake = "handshake";
    public const string Proxy = "proxy";
}

/// <summary>Maps all ProWeb HTTP endpoints.</summary>
public static class EndpointsMapper
{
    public static void MapProWebEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/health", HandleHealth);
        app.MapPost("/v1/handshake", HandleHandshake).RequireRateLimiting(RateLimitPolicies.Handshake);
        app.MapPost("/v1/proxy", HandleProxy).RequireRateLimiting(RateLimitPolicies.Proxy);
        app.MapPost("/v1/session/close", HandleSessionClose).RequireRateLimiting(RateLimitPolicies.Proxy);
    }

    private static IResult HandleHealth(SqliteConnectionFactory dbFactory)
    {
        var dbOk = true;
        try
        {
            using var conn = dbFactory.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            cmd.ExecuteScalar();
        }
        catch (Exception)
        {
            dbOk = false;
        }

        return Results.Ok(new
        {
            status = dbOk ? "ok" : "degraded",
            version = "1.0.0",
            timeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            checks = new { db = dbOk ? "ok" : "error" },
        });
    }

    private static IResult HandleHandshake([FromBody] HandshakeRequest request, HandshakeService service)
    {
        try
        {
            var response = service.Establish(request);
            return Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorEnvelope
            {
                StatusCode = 400,
                Error = "invalid_request",
                Message = ex.Message,
            });
        }
        catch (FormatException)
        {
            return Results.BadRequest(new ErrorEnvelope
            {
                StatusCode = 400,
                Error = "invalid_request",
                Message = "clientPublicKey must be valid base64.",
            });
        }
    }

    private static async Task<IResult> HandleProxy(
        HttpContext context,
        EnvelopeCodec codec,
        ProxyService proxy,
        CancellationToken cancellationToken)
    {
        var requestId = context.GetRequestId();
        var session = context.GetSessionContext()!; // Guaranteed by SessionAuthMiddleware.
        var aad = Encoding.UTF8.GetBytes(requestId);

        byte[] sealedRequest;
        using (var ms = new MemoryStream())
        {
            await context.Request.Body.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            sealedRequest = ms.ToArray();
        }

        RequestEnvelope envelope;
        try
        {
            envelope = codec.Decode<RequestEnvelope>(sealedRequest, session.SessionKey, aad);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidDataException or FormatException)
        {
            return Error(context, 400, "decrypt_failed", "Request payload could not be decrypted.");
        }

        if (!string.Equals(envelope.RequestId, requestId, StringComparison.Ordinal) ||
            !string.Equals(envelope.SessionId, session.SessionId, StringComparison.Ordinal))
        {
            return Error(context, 400, "envelope_mismatch", "Envelope identifiers do not match the request.");
        }

        try
        {
            var response = await proxy.ProcessAsync(envelope, session, cancellationToken).ConfigureAwait(false);
            var sealedResponse = codec.Encode(response, session.SessionKey, aad);
            return Results.Bytes(sealedResponse, "application/octet-stream");
        }
        catch (InvalidEnvelopeException ex)
        {
            return Error(context, 400, "invalid_request", ex.Message);
        }
        catch (ReplayRejectedException)
        {
            return Error(context, 409, "replay_rejected", "Request was rejected as a replay.");
        }
        catch (FetchBlockedException ex)
        {
            return Error(context, 502, "target_blocked", ex.Message);
        }
        catch (ContentTooLargeException)
        {
            return Error(context, 502, "response_too_large", "The target response exceeded the size limit.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Error(context, 502, "upstream_failed", "The target could not be fetched.");
        }
    }

    private static IResult HandleSessionClose(
        HttpContext context,
        SessionRepository sessions,
        CookieRepository cookies,
        SessionCookieStore cookieStore)
    {
        var session = context.GetSessionContext()!;
        sessions.Revoke(session.SessionId);
        cookies.DeleteForSession(session.SessionId);
        cookieStore.Clear(session.SessionId);
        return Results.Ok(new { sessionId = session.SessionId, revoked = true });
    }

    private static IResult Error(HttpContext context, int status, string error, string message)
    {
        var envelope = new ErrorEnvelope
        {
            RequestId = context.GetRequestId(),
            StatusCode = status,
            Error = error,
            Message = message,
        };
        return Results.Json(envelope, statusCode: status);
    }
}
