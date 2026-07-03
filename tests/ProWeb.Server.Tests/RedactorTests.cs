using FluentAssertions;
using ProWeb.Server.Observability;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>Query-string redaction before logging/persistence (UT-C-R1-006).</summary>
public class RedactorTests
{
    [Fact]
    public void RedactUrl_MasksQueryValues_KeepsKeysAndPath()
    {
        var redacted = SensitiveDataRedactor.RedactUrl("https://api.example.com/search?token=secret123&q=hello");
        redacted.Should().StartWith("https://api.example.com/search?");
        redacted.Should().Contain("token=" + SensitiveDataRedactor.Mask);
        redacted.Should().Contain("q=" + SensitiveDataRedactor.Mask);
        redacted.Should().NotContain("secret123");
        redacted.Should().NotContain("hello");
    }

    [Fact]
    public void RedactUrl_NoQuery_ReturnsPathUnchanged()
    {
        SensitiveDataRedactor.RedactUrl("https://example.com/path/page")
            .Should().Be("https://example.com/path/page");
    }

    [Fact]
    public void RedactUrl_EmptyOrNull_ReturnsEmpty()
    {
        SensitiveDataRedactor.RedactUrl(null).Should().BeEmpty();
        SensitiveDataRedactor.RedactUrl("   ").Should().BeEmpty();
    }

    [Fact]
    public void RedactHeaders_MasksSensitiveHeaders()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer abc",
            ["Cookie"] = "sid=1",
            ["Accept"] = "text/html",
        };
        var redacted = SensitiveDataRedactor.RedactHeaders(headers);
        redacted["Authorization"].Should().Be(SensitiveDataRedactor.Mask);
        redacted["Cookie"].Should().Be(SensitiveDataRedactor.Mask);
        redacted["Accept"].Should().Be("text/html");
    }
}
