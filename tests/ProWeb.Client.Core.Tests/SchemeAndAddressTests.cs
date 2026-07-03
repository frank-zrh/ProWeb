using FluentAssertions;
using ProWeb.Client.Core;
using Xunit;

namespace ProWeb.Client.Core.Tests;

public class RequestSchemeClassifierTests
{
    [Theory]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("blob:https://x/abc")]
    [InlineData("about:blank")]
    [InlineData("javascript:void(0)")]
    [InlineData("chrome://settings")]
    [InlineData("mailto:a@b.com")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsLocalScheme_TrueForLocalAndPseudoSchemes(string url)
    {
        RequestSchemeClassifier.IsLocalScheme(url).Should().BeTrue();
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("http://example.com")]
    public void IsLocalScheme_FalseForHttp(string url)
    {
        RequestSchemeClassifier.IsLocalScheme(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://example.com", true)]
    [InlineData("data:text/plain,hi", false)]
    [InlineData("about:blank", false)]
    [InlineData("ftp://example.com", false)]
    public void IsProxyable_OnlyForHttp(string url, bool expected)
    {
        RequestSchemeClassifier.IsProxyable(url).Should().Be(expected);
    }
}

public class UrlNormalizerClassifyTests
{
    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("example.com")]
    [InlineData("localhost:8443")]
    [InlineData("http://example.com")]
    public void Classify_Url(string input)
    {
        UrlNormalizer.Classify(input).Should().Be(UrlNormalizer.AddressInputKind.Url);
    }

    [Theory]
    [InlineData("hello world")]
    [InlineData("what is proweb")]
    public void Classify_Search(string input)
    {
        UrlNormalizer.Classify(input).Should().Be(UrlNormalizer.AddressInputKind.Search);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://")]
    [InlineData("https://")]
    [InlineData("http:// broken host")]
    public void Classify_Invalid(string input)
    {
        UrlNormalizer.Classify(input).Should().Be(UrlNormalizer.AddressInputKind.Invalid);
    }
}
