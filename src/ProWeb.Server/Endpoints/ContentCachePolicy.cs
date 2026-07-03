using System.Security.Cryptography;
using System.Text;

namespace ProWeb.Server.Endpoints;

/// <summary>
/// Decides whether a fetched response may be cached and derives the per-session partition key.
/// Pure/static so it is unit-testable. Caching is deliberately session-scoped to preserve the
/// isolation guarantee (a cached authenticated page must never leak across sessions).
/// </summary>
public static class ContentCachePolicy
{
    private static readonly string[] CacheableTypePrefixes =
    {
        "text/html",
        "text/css",
        "text/plain",
        "application/javascript",
        "text/javascript",
        "application/json",
        "image/",
        "font/",
        "application/font",
    };

    /// <summary>Derives a stable, session-scoped partition key from method + URL.</summary>
    public static string PartitionKey(string sessionId, string method, string url)
    {
        var material = $"{sessionId}\n{method?.ToUpperInvariant()}\n{url}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash);
    }

    /// <summary>True when the request/response pair is safe to cache.</summary>
    public static bool IsCacheable(string method, int statusCode, string contentType, IReadOnlyDictionary<string, string> responseHeaders)
    {
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)) return false;
        if (statusCode is < 200 or >= 300) return false;

        if (responseHeaders is not null &&
            responseHeaders.TryGetValue("Cache-Control", out var cc) &&
            (cc.Contains("no-store", StringComparison.OrdinalIgnoreCase) ||
             cc.Contains("private", StringComparison.OrdinalIgnoreCase) ||
             cc.Contains("no-cache", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Never cache a response that sets cookies — it is inherently per-user/dynamic.
        if (responseHeaders is not null && responseHeaders.ContainsKey("Set-Cookie")) return false;

        var type = (contentType ?? string.Empty).ToLowerInvariant();
        foreach (var prefix in CacheableTypePrefixes)
        {
            if (type.StartsWith(prefix, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
