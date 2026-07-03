using FluentAssertions;
using Microsoft.Extensions.Options;
using ProWeb.Server.Config;
using ProWeb.Server.Fetching;
using Xunit;

namespace ProWeb.Server.Tests;

public class FetcherSelectorTests
{
    private static FetcherSelector Selector(params string[] headlessDomains)
    {
        var opts = new ProWebOptions();
        opts.Fetch.HeadlessDomains = headlessDomains;
        return new FetcherSelector(Options.Create(opts));
    }

    [Fact]
    public void SelectForUrl_DefaultsToHttp()
    {
        Selector().SelectForUrl("https://example.com/page").Should().Be("http");
    }

    [Theory]
    [InlineData("https://app.spa.com/x")]
    [InlineData("https://spa.com/x")]
    public void SelectForUrl_MatchesConfiguredDomain_UsesHeadless(string url)
    {
        Selector("spa.com").SelectForUrl(url).Should().Be("headless");
    }

    [Fact]
    public void SelectForUrl_InvalidUrl_DefaultsToHttp()
    {
        Selector("spa.com").SelectForUrl("not-a-url").Should().Be("http");
    }

    [Fact]
    public void LooksLikeSpa_DetectsAppRootWithLittleContent()
    {
        var spa = "<html><head><script src=\"bundle.js\"></script></head><body><div id=\"root\"></div></body></html>";
        Selector().LooksLikeSpa(spa).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeSpa_ReturnsFalseForContentfulPage()
    {
        var html = "<html><body>" + new string('x', 5000) + "</body></html>";
        Selector().LooksLikeSpa(html).Should().BeFalse();
    }
}
