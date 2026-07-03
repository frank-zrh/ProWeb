namespace ProWeb.Client.Core;

/// <summary>
/// Classifies request URLs so the interception adapter can let local/pseudo schemes
/// (data:, blob:, about:, javascript:, chrome:, devtools:, mailto:, tel:) load natively instead of
/// routing them through the encrypted proxy channel.
/// </summary>
public static class RequestSchemeClassifier
{
    private static readonly string[] LocalSchemes =
    {
        "data:", "blob:", "about:", "javascript:", "chrome:", "chrome-extension:",
        "devtools:", "mailto:", "tel:", "file:", "view-source:",
    };

    /// <summary>True when the URL uses a scheme that must not be proxied.</summary>
    public static bool IsLocalScheme(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        var trimmed = url.TrimStart();
        foreach (var scheme in LocalSchemes)
        {
            if (trimmed.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>True when the URL is an http(s) URL eligible for proxying.</summary>
    public static bool IsProxyable(string? url) =>
        !IsLocalScheme(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
