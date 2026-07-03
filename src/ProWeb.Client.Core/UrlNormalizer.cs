namespace ProWeb.Client.Core;

/// <summary>
/// Normalizes user-entered address-bar text into a navigable absolute URL. Bare hosts get an
/// https scheme; text that is not a plausible host becomes a search query.
/// </summary>
public static class UrlNormalizer
{
    private const string SearchTemplate = "https://duckduckgo.com/?q=";

    /// <summary>How a raw address-bar entry should be treated.</summary>
    public enum AddressInputKind
    {
        /// <summary>A navigable URL (explicit scheme or a plausible host).</summary>
        Url,

        /// <summary>Free text to be sent to the search engine.</summary>
        Search,

        /// <summary>Malformed input that must not navigate (e.g. broken scheme, control chars).</summary>
        Invalid,
    }

    /// <summary>Classifies raw address-bar text without navigating, for inline UI feedback.</summary>
    public static AddressInputKind Classify(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return AddressInputKind.Invalid;

        var text = input.Trim();

        if (text.Any(char.IsControl))
            return AddressInputKind.Invalid;

        // An explicit scheme attempt must parse into a real http(s) URL with a host.
        if (text.Contains("://", StringComparison.Ordinal) ||
            text.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(text, UriKind.Absolute, out var scheme) &&
                   (scheme.Scheme == Uri.UriSchemeHttp || scheme.Scheme == Uri.UriSchemeHttps) &&
                   !string.IsNullOrEmpty(scheme.Host)
                ? AddressInputKind.Url
                : AddressInputKind.Invalid;
        }

        if (LooksLikeHost(text))
            return AddressInputKind.Url;

        return AddressInputKind.Search;
    }

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "about:blank";

        var text = input.Trim();

        // Already absolute with a scheme we understand.
        if (Uri.TryCreate(text, UriKind.Absolute, out var abs) &&
            (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
        {
            return abs.ToString();
        }

        // localhost or an explicit host:port.
        if (LooksLikeHost(text))
            return "https://" + text;

        return SearchTemplate + Uri.EscapeDataString(text);
    }

    private static bool LooksLikeHost(string text)
    {
        if (text.Contains(' '))
            return false;

        // host[:port][/path] — require a dot in the host or a well-known local host.
        var authority = text.Split('/', 2)[0];
        var host = authority.Split(':', 2)[0];
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.');
    }
}
