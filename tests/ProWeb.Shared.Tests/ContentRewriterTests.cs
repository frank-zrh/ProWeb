using System.Text;
using FluentAssertions;
using ProWeb.Shared.Content;
using Xunit;

namespace ProWeb.Shared.Tests;

public class ContentRewriterTests
{
    private readonly ContentRewriter _rw = new("/p?u=");
    private readonly Uri _base = new("https://example.com/dir/page.html");

    // The server registers ContentRewriter with the DEFAULT (empty) prefix so URLs are rewritten
    // to their absolute REAL form and the client's real-URL interceptor proxies them (UT-X-R3-901/902).
    private readonly ContentRewriter _abs = new();

    [Theory]
    [InlineData("/a.js", "https://example.com/a.js")]
    [InlineData("b.js", "https://example.com/dir/b.js")]
    [InlineData("../c.js", "https://example.com/c.js")]
    [InlineData("//cdn.com/x.js", "https://cdn.com/x.js")]
    [InlineData("https://other.com/y.js", "https://other.com/y.js")]
    public void RewriteSingleUrl_DefaultMode_EmitsAbsoluteRealUrl(string input, string expectedAbsolute)
    {
        _abs.RewriteSingleUrl(input, _base).Should().Be(expectedAbsolute);
    }

    [Fact]
    public void RewriteHtml_DefaultMode_ProducesAbsoluteUrls_NoProxyPrefix()
    {
        var html = "<html><body><a href=\"/x\">l</a><img src='img/p.png'></body></html>";
        var outStr = _abs.RewriteHtmlString(html, "https://example.com/dir/");
        outStr.Should().Contain("href=\"https://example.com/x\"");
        outStr.Should().Contain("src='https://example.com/dir/img/p.png'");
        outStr.Should().NotContain("/p?u=");
    }

    [Theory]
    [InlineData("/a.js", "https://example.com/a.js")]
    [InlineData("b.js", "https://example.com/dir/b.js")]
    [InlineData("../c.js", "https://example.com/c.js")]
    [InlineData("//cdn.com/x.js", "https://cdn.com/x.js")]
    [InlineData("https://other.com/y.js", "https://other.com/y.js")]
    public void RewriteSingleUrl_ResolvesRelativeForms(string input, string expectedAbsolute)
    {
        var result = _rw.RewriteSingleUrl(input, _base);
        result.Should().Be("/p?u=" + Uri.EscapeDataString(expectedAbsolute));
    }

    [Theory]
    [InlineData("#anchor")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("javascript:void(0)")]
    [InlineData("mailto:a@b.com")]
    [InlineData("about:blank")]
    public void RewriteSingleUrl_LeavesNonHttpSchemesUntouched(string input)
    {
        _rw.RewriteSingleUrl(input, _base).Should().BeNull();
    }

    [Fact]
    public void RewriteHtml_RewritesHrefAndSrc()
    {
        var html = "<html><body><a href=\"/x\">l</a><img src='img/p.png'><script src=app.js></script></body></html>";
        var outStr = _rw.RewriteHtmlString(html, "https://example.com/dir/");
        outStr.Should().Contain("/p?u=" + Uri.EscapeDataString("https://example.com/x"));
        outStr.Should().Contain("/p?u=" + Uri.EscapeDataString("https://example.com/dir/img/p.png"));
        outStr.Should().Contain("/p?u=" + Uri.EscapeDataString("https://example.com/dir/app.js"));
    }

    [Fact]
    public void RewriteCss_RewritesUrlAndImport()
    {
        var css = "body{background:url('bg.png')} @import \"base.css\";";
        var outStr = _rw.RewriteCssString(css, "https://example.com/styles/main.css");
        outStr.Should().Contain("/p?u=" + Uri.EscapeDataString("https://example.com/styles/bg.png"));
        outStr.Should().Contain("/p?u=" + Uri.EscapeDataString("https://example.com/styles/base.css"));
    }

    [Fact]
    public void RewriteHtml_MalformedInput_ReturnsOriginalBytes()
    {
        var bytes = new byte[] { 0xFF, 0xFE, 0x00 };
        _rw.RewriteHtml(bytes, "not-a-url", null).Should().Equal(bytes);
    }

    [Fact]
    public void RewriteHtml_EmptyInput_ReturnsEmpty()
    {
        _rw.RewriteHtml(Array.Empty<byte>(), "https://x", null).Should().BeEmpty();
    }

    [Fact]
    public void RewriteHtmlString_InjectsBaseIntoHead_WhenMissing()
    {
        var html = "<html><head><title>t</title></head><body>hi</body></html>";
        var outStr = _rw.RewriteHtmlString(html, "https://example.com/dir/page.html");
        outStr.Should().Contain("<base href=\"https://example.com/dir/page.html\">");
        // Base must sit inside the head, before the title.
        outStr.IndexOf("<base", StringComparison.Ordinal)
            .Should().BeLessThan(outStr.IndexOf("<title>", StringComparison.Ordinal));
    }

    [Fact]
    public void RewriteHtmlString_NormalizesExistingBase()
    {
        var html = "<html><head><base href=\"https://old.example/\"></head><body></body></html>";
        var outStr = _rw.RewriteHtmlString(html, "https://example.com/dir/");
        outStr.Should().Contain("<base href=\"https://example.com/dir/\">");
        outStr.Should().NotContain("old.example");
        System.Text.RegularExpressions.Regex.Matches(outStr, "<base").Count.Should().Be(1);
    }

    [Fact]
    public void RewriteHtmlString_InjectsHeadAndBase_WhenNoHead()
    {
        var html = "<html><body>hi</body></html>";
        var outStr = _rw.RewriteHtmlString(html, "https://example.com/");
        outStr.Should().Contain("<base href=\"https://example.com/\">");
    }
}
