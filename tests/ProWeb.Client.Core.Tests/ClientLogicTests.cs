using FluentAssertions;
using ProWeb.Client.Core;
using Xunit;

namespace ProWeb.Client.Core.Tests;

public class UrlNormalizerTests
{
    [Theory]
    [InlineData("https://example.com/a", "https://example.com/a")]
    [InlineData("http://example.com", "http://example.com/")]
    [InlineData("example.com", "https://example.com")]
    [InlineData("example.com/path", "https://example.com/path")]
    [InlineData("localhost:8443", "https://localhost:8443")]
    public void Normalize_ProducesExpectedUrl(string input, string expected)
    {
        UrlNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("what is proweb")]
    [InlineData("singleword")]
    public void Normalize_NonHost_BecomesSearch(string input)
    {
        UrlNormalizer.Normalize(input).Should().StartWith("https://duckduckgo.com/?q=");
    }

    [Fact]
    public void Normalize_Empty_ReturnsAboutBlank()
    {
        UrlNormalizer.Normalize("   ").Should().Be("about:blank");
    }
}

public class NavigationStateTests
{
    [Fact]
    public void Navigate_Back_Forward_TraversesHistory()
    {
        var nav = new NavigationState();
        nav.Navigate("https://a.com");
        nav.Navigate("https://b.com");
        nav.Navigate("https://c.com");

        nav.CurrentUrl.Should().Be("https://c.com");
        nav.CanGoForward.Should().BeFalse();

        nav.GoBack().Should().Be("https://b.com");
        nav.GoBack().Should().Be("https://a.com");
        nav.CanGoBack.Should().BeFalse();
        nav.GoForward().Should().Be("https://b.com");
    }

    [Fact]
    public void Navigate_AfterBack_TruncatesForwardHistory()
    {
        var nav = new NavigationState();
        nav.Navigate("https://a.com");
        nav.Navigate("https://b.com");
        nav.GoBack();
        nav.Navigate("https://d.com");

        nav.CanGoForward.Should().BeFalse();
        nav.History.Should().Equal("https://a.com", "https://d.com");
    }

    [Fact]
    public void Navigate_SameUrl_IsNoOp()
    {
        var nav = new NavigationState();
        nav.Navigate("https://a.com");
        nav.Navigate("https://a.com");
        nav.History.Should().HaveCount(1);
    }
}

public class TabCollectionTests
{
    [Fact]
    public void AddTab_SetsActive()
    {
        var tabs = new TabCollection();
        var t1 = tabs.AddTab();
        tabs.Active.Should().Be(t1);
        tabs.Count.Should().Be(1);
    }

    [Fact]
    public void CloseTab_LastTab_IsRejected()
    {
        var tabs = new TabCollection();
        var t1 = tabs.AddTab();
        tabs.CloseTab(t1.Id).Should().BeFalse();
    }

    [Fact]
    public void CloseTab_UpdatesActiveSelection()
    {
        var tabs = new TabCollection();
        var t1 = tabs.AddTab();
        var t2 = tabs.AddTab();
        tabs.Activate(t1.Id);
        tabs.CloseTab(t2.Id).Should().BeTrue();
        tabs.Active.Should().Be(t1);
    }
}
