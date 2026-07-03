using System.Text;
using FluentAssertions;
using ProWeb.Client.Core;
using ProWeb.Shared.Content;
using ProWeb.Shared.Protocol;
using Xunit;

namespace ProWeb.Client.Core.Tests;

/// <summary>
/// UT-X-R3-902: proves the in-page link chain works end to end at the logic layer (no CefSharp GUI):
/// server-side rewriting resolves a relative link to its absolute REAL URL, and that URL — when
/// intercepted — is proxied through the encrypted channel (not direct-connected, not turned into a
/// dead /p?u= URL that 404s at the origin).
/// </summary>
public class NavigationProxyTests
{
    private readonly ContentRewriter _rewriter = new(); // server default (empty prefix → absolute URLs)

    [Theory]
    [InlineData("/path", "https://example.com/path")]
    [InlineData("../a", "https://example.com/a")]
    [InlineData("?q=1", "https://example.com/dir/page?q=1")]
    [InlineData("sub/page.html", "https://example.com/dir/sub/page.html")]
    public void RelativeLink_RewritesToAbsoluteRealUrl(string href, string expected)
    {
        var abs = _rewriter.RewriteSingleUrl(href, new Uri("https://example.com/dir/page"));
        abs.Should().Be(expected);
        abs.Should().NotContain("/p?u=");
    }

    [Fact]
    public void Fragment_Link_IsLeftUntouched()
    {
        _rewriter.RewriteSingleUrl("#frag", new Uri("https://example.com/dir/page")).Should().BeNull();
    }

    [Fact]
    public async Task RewrittenLink_IsFetchedThroughEncryptedChannel_NotDirectConnected()
    {
        // 1) A page HTML with a relative in-page link, rewritten by the server.
        const string html = "<html><body><a href=\"/next\">go</a></body></html>";
        var rewritten = _rewriter.RewriteHtmlString(html, "https://example.com/");
        rewritten.Should().Contain("href=\"https://example.com/next\"");

        // 2) Clicking that link navigates to the absolute real URL, which the interceptor proxies.
        var channel = new StubProxyChannel(connected: true);
        var coordinator = new ProxyInterceptionCoordinator(channel);

        coordinator.Decide("https://example.com/next").Should().Be(InterceptionDecision.Proxy);
        var result = await coordinator.HandleAsync("https://example.com/next");

        result.ServesContent.Should().BeTrue();
        channel.FetchCalls.Should().Be(1, "the navigation must go through the encrypted channel");
        Encoding.UTF8.GetString(result.Body).Should().Be("proxied:https://example.com/next");
    }
}
