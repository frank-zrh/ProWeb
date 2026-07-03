using System.Text;
using System.Text.RegularExpressions;

namespace ProWeb.Shared.Content;

/// <summary>
/// Detects the character encoding of an HTML/CSS byte payload using (in order):
/// the HTTP Content-Type charset, a &lt;meta&gt; charset declaration, then a UTF-8 default.
/// Supports GBK/GB2312 via the code-pages encoding provider (registered on first use).
/// </summary>
public static class EncodingDetector
{
    private static readonly Regex MetaCharset = new(
        "charset\\s*=\\s*[\"']?\\s*([a-zA-Z0-9_\\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool _providerRegistered;

    public static Encoding Detect(byte[] content, string? contentTypeHeader)
    {
        EnsureProvider();

        var fromHeader = FromCharsetName(ExtractCharset(contentTypeHeader));
        if (fromHeader != null) return fromHeader;

        // Sniff a prefix as ASCII to find a meta charset.
        var prefixLen = Math.Min(content.Length, 4096);
        var prefix = Encoding.ASCII.GetString(content, 0, prefixLen);
        var m = MetaCharset.Match(prefix);
        if (m.Success)
        {
            var fromMeta = FromCharsetName(m.Groups[1].Value);
            if (fromMeta != null) return fromMeta;
        }

        return new UTF8Encoding(false);
    }

    public static string DecodeToString(byte[] content, string? contentTypeHeader)
        => Detect(content, contentTypeHeader).GetString(content);

    private static string? ExtractCharset(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return null;
        var m = MetaCharset.Match(contentType);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static Encoding? FromCharsetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            var normalized = name.Trim().ToLowerInvariant();
            if (normalized is "gbk" or "gb2312") normalized = "GBK";
            return Encoding.GetEncoding(normalized);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void EnsureProvider()
    {
        if (_providerRegistered) return;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _providerRegistered = true;
    }
}
