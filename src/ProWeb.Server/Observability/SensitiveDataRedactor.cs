namespace ProWeb.Server.Observability;

/// <summary>
/// Redacts sensitive values (auth tokens, cookies) from header collections before they reach any
/// log sink, so secrets are never written to disk. Case-insensitive on header names.
/// </summary>
public static class SensitiveDataRedactor
{
    public const string Mask = "***REDACTED***";

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "X-Auth-Token",
    };

    /// <summary>Returns true when the header name carries sensitive material that must be masked.</summary>
    public static bool IsSensitive(string headerName) =>
        !string.IsNullOrEmpty(headerName) && SensitiveHeaders.Contains(headerName);

    /// <summary>Returns a copy of the headers with sensitive values replaced by a mask.</summary>
    public static IReadOnlyDictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers)
            result[key] = IsSensitive(key) ? Mask : value;
        return result;
    }

    /// <summary>
    /// Redacts the query string of a URL (which can carry tokens/PII) before it is logged or
    /// persisted, keeping scheme/host/path for diagnostics. Each query value is replaced with the
    /// mask; keys are preserved so the shape of the request remains debuggable.
    /// </summary>
    public static string RedactUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // Fall back to a coarse split for non-absolute inputs.
            var q = url.IndexOf('?');
            return q < 0 ? url : url[..q] + "?" + RedactRawQuery(url[(q + 1)..]);
        }

        if (string.IsNullOrEmpty(uri.Query))
            return uri.GetLeftPart(UriPartial.Path);

        return uri.GetLeftPart(UriPartial.Path) + "?" + RedactRawQuery(uri.Query.TrimStart('?'));
    }

    private static string RedactRawQuery(string rawQuery)
    {
        if (string.IsNullOrEmpty(rawQuery))
            return string.Empty;

        var parts = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var eq = parts[i].IndexOf('=');
            parts[i] = eq < 0 ? parts[i] : parts[i][..eq] + "=" + Mask;
        }

        return string.Join('&', parts);
    }
}
