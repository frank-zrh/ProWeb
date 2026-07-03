using System.Text;
using System.Text.RegularExpressions;

namespace ProWeb.Shared.Content;

/// <summary>
/// Rewrites HTML and CSS so that relative and root-relative URLs resolve correctly when the
/// content is rendered on the client. URLs are resolved against the response's final URL.
/// <para>
/// In ProWeb's deployment the client intercepts <b>every</b> proxyable http(s) request at the CEF
/// resource layer by its real URL and serves it over the encrypted channel, so URLs must be
/// rewritten to their absolute <b>real</b> form (the default, when <c>proxyPrefix</c> is empty).
/// Wrapping them in a proxy-origin prefix such as <c>/p?u=</c> would produce URLs like
/// <c>https://site/p?u=…</c> that the client re-proxies verbatim, which 404 at the origin — that
/// was the root cause of broken images (UT-X-R3-901) and dead in-page links (UT-X-R3-902).
/// A non-empty <c>proxyPrefix</c> is retained only for a hypothetical server-hosted rewrite mode.
/// </para>
/// All methods are pure and never throw for malformed input (they return the input unchanged).
/// </summary>
public sealed class ContentRewriter
{
    private readonly string _proxyPrefix;

    // Matches href/src/action attributes with single, double, or unquoted values.
    private static readonly Regex AttrUrl = new(
        "(?<attr>\\b(?:href|src|action|poster|data-src)\\s*=\\s*)(?<q>[\"']?)(?<url>[^\"'>\\s]+)\\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssUrl = new(
        "url\\(\\s*(?<q>[\"']?)(?<url>[^\"')]+)\\k<q>\\s*\\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssImport = new(
        "@import\\s+(?<q>[\"'])(?<url>[^\"']+)\\k<q>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExistingBase = new(
        "<base\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HeadOpen = new(
        "<head\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HtmlOpen = new(
        "<html\\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ContentRewriter(string proxyPrefix = "")
    {
        _proxyPrefix = proxyPrefix ?? string.Empty;
    }

    /// <summary>Rewrites an HTML document (given as bytes) and returns rewritten UTF-8 bytes.</summary>
    public byte[] RewriteHtml(byte[] content, string finalUrl, string? contentTypeHeader)
    {
        if (content == null || content.Length == 0) return content ?? Array.Empty<byte>();
        if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out _)) return content;
        try
        {
            var html = EncodingDetector.DecodeToString(content, contentTypeHeader);
            var rewritten = RewriteHtmlString(html, finalUrl);
            return new UTF8Encoding(false).GetBytes(rewritten);
        }
        catch
        {
            return content;
        }
    }

    public string RewriteHtmlString(string html, string finalUrl)
    {
        if (string.IsNullOrEmpty(html)) return html;
        if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var baseUri)) return html;

        var result = AttrUrl.Replace(html, m =>
        {
            var url = m.Groups["url"].Value;
            var rewritten = RewriteSingleUrl(url, baseUri);
            if (rewritten == null) return m.Value;
            var q = m.Groups["q"].Value;
            var quote = string.IsNullOrEmpty(q) ? "\"" : q;
            return $"{m.Groups["attr"].Value}{quote}{rewritten}{quote}";
        });

        // Inject/normalize <base href=FinalUrl> so relative URLs that escaped attribute rewriting
        // (e.g. those built dynamically) still resolve against the original document URL.
        result = InjectBase(result, baseUri.AbsoluteUri);

        return result;
    }

    /// <summary>
    /// Ensures the document carries a single <c>&lt;base href=finalUrl&gt;</c>. An existing base is
    /// normalized; otherwise one is injected inside &lt;head&gt; (or &lt;html&gt;, else prepended).
    /// The base tag itself is written after URL rewriting so it is never proxied.
    /// </summary>
    private static string InjectBase(string html, string finalUrl)
    {
        var baseTag = $"<base href=\"{finalUrl}\">";

        if (ExistingBase.IsMatch(html))
            return ExistingBase.Replace(html, baseTag, 1);

        var head = HeadOpen.Match(html);
        if (head.Success)
            return html.Insert(head.Index + head.Length, baseTag);

        var htmlOpen = HtmlOpen.Match(html);
        if (htmlOpen.Success)
            return html.Insert(htmlOpen.Index + htmlOpen.Length, "<head>" + baseTag + "</head>");

        return baseTag + html;
    }

    /// <summary>Rewrites a CSS document, handling url(...) and @import.</summary>
    public byte[] RewriteCss(byte[] content, string finalUrl, string? contentTypeHeader)
    {
        if (content == null || content.Length == 0) return content ?? Array.Empty<byte>();
        if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out _)) return content;
        try
        {
            var css = EncodingDetector.DecodeToString(content, contentTypeHeader);
            var rewritten = RewriteCssString(css, finalUrl);
            return new UTF8Encoding(false).GetBytes(rewritten);
        }
        catch
        {
            return content;
        }
    }

    public string RewriteCssString(string css, string finalUrl)
    {
        if (string.IsNullOrEmpty(css)) return css;
        if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var baseUri)) return css;

        css = CssUrl.Replace(css, m =>
        {
            var url = m.Groups["url"].Value;
            var rewritten = RewriteSingleUrl(url, baseUri);
            return rewritten == null ? m.Value : $"url(\"{rewritten}\")";
        });

        css = CssImport.Replace(css, m =>
        {
            var url = m.Groups["url"].Value;
            var rewritten = RewriteSingleUrl(url, baseUri);
            return rewritten == null ? m.Value : $"@import \"{rewritten}\"";
        });

        return css;
    }

    /// <summary>
    /// Resolves a possibly-relative URL against the base and returns the rewritten form.
    /// By default (empty proxy prefix) this is the absolute <b>real</b> URL, which the client
    /// intercepts and proxies by real URL. Returns null for non-http schemes (data:, blob:,
    /// javascript:, mailto:, #fragments) so callers leave them untouched.
    /// </summary>
    public string? RewriteSingleUrl(string url, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var trimmed = url.Trim();

        if (trimmed.StartsWith('#')) return null;
        var lower = trimmed.ToLowerInvariant();
        if (lower.StartsWith("data:") || lower.StartsWith("blob:") ||
            lower.StartsWith("javascript:") || lower.StartsWith("mailto:") ||
            lower.StartsWith("about:") || lower.StartsWith("tel:"))
            return null;

        // Protocol-relative URL ("//host/path"): adopt the base scheme. Done explicitly because
        // Uri.TryCreate treats a leading "//" as a UNC/file path on Windows.
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
            trimmed = baseUri.Scheme + ":" + trimmed;

        Uri? absolute;
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var abs))
        {
            absolute = abs;
        }
        else if (Uri.TryCreate(baseUri, trimmed, out var rel))
        {
            absolute = rel;
        }
        else
        {
            return null;
        }

        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            return null;

        // Default (empty prefix): emit the absolute real URL so the client's real-URL interceptor
        // proxies it correctly. Non-empty prefix wraps it for a server-hosted rewrite mode.
        return _proxyPrefix.Length == 0
            ? absolute.AbsoluteUri
            : _proxyPrefix + Uri.EscapeDataString(absolute.AbsoluteUri);
    }
}
