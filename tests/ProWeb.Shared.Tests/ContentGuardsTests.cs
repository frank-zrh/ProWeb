using System.Net;
using System.Text;
using FluentAssertions;
using ProWeb.Shared.Content;
using Xunit;

namespace ProWeb.Shared.Tests;

public class UrlGuardTests
{
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://1.2.3.4/path")]
    public void Allows_PublicHttpUrls(string url)
    {
        UrlGuard.IsAllowed(url, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("ftp://example.com/")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void Blocks_UnsafeOrInvalidUrls(string url)
    {
        UrlGuard.IsAllowed(url, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void IsBlockedAddress_DetectsLoopbackAndPrivate()
    {
        UrlGuard.IsBlockedAddress(IPAddress.Loopback).Should().BeTrue();
        UrlGuard.IsBlockedAddress(IPAddress.Parse("8.8.8.8")).Should().BeFalse();
    }

    [Theory]
    [InlineData("::ffff:169.254.169.254")] // IPv4-mapped metadata endpoint
    [InlineData("::ffff:10.0.0.1")]         // IPv4-mapped private
    [InlineData("::ffff:127.0.0.1")]        // IPv4-mapped loopback
    [InlineData("::1")]                      // IPv6 loopback
    [InlineData("::")]                       // unspecified
    [InlineData("fc00::1")]                  // unique-local
    [InlineData("fe80::1")]                  // link-local
    public void IsBlockedAddress_BlocksIpv6AndMappedForms(string address)
    {
        UrlGuard.IsBlockedAddress(IPAddress.Parse(address)).Should().BeTrue();
    }
}

public class EncodingDetectorTests
{
    [Fact]
    public void Detects_Utf8_ByDefault()
    {
        var bytes = Encoding.UTF8.GetBytes("<html><body>你好</body></html>");
        EncodingDetector.DecodeToString(bytes, "text/html").Should().Contain("你好");
    }

    [Fact]
    public void Decodes_Gbk_FromHeaderCharset()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding("GBK");
        var bytes = gbk.GetBytes("中文页面");
        EncodingDetector.DecodeToString(bytes, "text/html; charset=gbk").Should().Be("中文页面");
    }

    [Fact]
    public void Decodes_Gbk_FromMetaTag()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gbk = Encoding.GetEncoding("GBK");
        var html = "<html><head><meta charset=\"gb2312\"></head><body>汉字</body></html>";
        var bytes = gbk.GetBytes(html);
        EncodingDetector.DecodeToString(bytes, null).Should().Contain("汉字");
    }
}
