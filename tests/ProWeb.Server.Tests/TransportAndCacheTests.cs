using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using ProWeb.Server;
using ProWeb.Server.Auth;
using ProWeb.Server.Config;
using ProWeb.Server.Endpoints;
using ProWeb.Server.Observability;
using Xunit;

namespace ProWeb.Server.Tests;

public class DevCertificateTests
{
    // Regression: the self-signed dev cert used by Kestrel must remain usable AFTER the
    // factory method returns. A previous implementation disposed it (via `using`) inside the
    // Kestrel configuration callback, so the handle was invalid by the time the listener bound,
    // crashing startup with "m_safeCertContext is an invalid handle". Integration tests use
    // TestServer and never bind real HTTPS, so only this unit test guards the behaviour.
    [Fact]
    public void Create_ReturnsUsableCertificate_ForServerAuth()
    {
        var cert = DevCertificate.Create();
        try
        {
            // Accessing members must not throw (i.e. the certificate is not disposed).
            cert.Extensions.Should().NotBeNull();
            cert.HasPrivateKey.Should().BeTrue("Kestrel needs the private key to serve TLS");
            cert.Subject.Should().Contain("proweb-dev");
        }
        finally
        {
            cert.Dispose();
        }
    }
}

public class TransportConfiguratorTests
{
    [Fact]
    public void Http3_Toggle_SelectsProtocols()
    {
        TransportConfigurator.ResolveProtocols(new ServerOptions { EnableHttp3 = false })
            .Should().Be(HttpProtocols.Http1AndHttp2);
        TransportConfigurator.ResolveProtocols(new ServerOptions { EnableHttp3 = true })
            .Should().Be(HttpProtocols.Http1AndHttp2AndHttp3);
    }

    [Fact]
    public void Mtls_Toggle_SelectsClientCertificateMode()
    {
        TransportConfigurator.ResolveClientCertificateMode(new ServerOptions { RequireClientCertificate = false })
            .Should().Be(ClientCertificateMode.NoCertificate);
        TransportConfigurator.ResolveClientCertificateMode(new ServerOptions { RequireClientCertificate = true })
            .Should().Be(ClientCertificateMode.RequireCertificate);
    }

    [Fact]
    public void Hsts_HeaderValue_RespectsToggleAndMaxAge()
    {
        TransportConfigurator.BuildHstsHeaderValue(new ServerOptions { EnableHsts = false }).Should().BeNull();
        TransportConfigurator.BuildHstsHeaderValue(new ServerOptions { EnableHsts = true, HstsMaxAgeSeconds = 100 })
            .Should().Be("max-age=100; includeSubDomains");
    }
}

public class SensitiveDataRedactorTests
{
    [Theory]
    [InlineData("Authorization", true)]
    [InlineData("Cookie", true)]
    [InlineData("Set-Cookie", true)]
    [InlineData("cookie", true)]
    [InlineData("Accept", false)]
    [InlineData("User-Agent", false)]
    public void IsSensitive_ClassifiesHeaders(string name, bool expected)
    {
        SensitiveDataRedactor.IsSensitive(name).Should().Be(expected);
    }

    [Fact]
    public void RedactHeaders_MasksSensitiveValues_KeepsOthers()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer secret-token",
            ["Cookie"] = "sid=abc",
            ["Accept"] = "text/html",
        };

        var redacted = SensitiveDataRedactor.RedactHeaders(headers);

        redacted["Authorization"].Should().Be(SensitiveDataRedactor.Mask);
        redacted["Cookie"].Should().Be(SensitiveDataRedactor.Mask);
        redacted["Accept"].Should().Be("text/html");
        string.Join(";", redacted.Values).Should().NotContain("secret-token");
    }
}

public class ContentCachePolicyTests
{
    private static Dictionary<string, string> NoHeaders() => new();

    [Fact]
    public void PartitionKey_IsSessionScoped_And_Deterministic()
    {
        var a = ContentCachePolicy.PartitionKey("s1", "GET", "https://x/p");
        var b = ContentCachePolicy.PartitionKey("s1", "GET", "https://x/p");
        var c = ContentCachePolicy.PartitionKey("s2", "GET", "https://x/p");

        a.Should().Be(b);
        a.Should().NotBe(c, "different sessions must not share a partition key");
    }

    [Theory]
    [InlineData("GET", 200, "text/html; charset=utf-8", true)]
    [InlineData("GET", 200, "image/png", true)]
    [InlineData("POST", 200, "text/html", false)]
    [InlineData("GET", 302, "text/html", false)]
    [InlineData("GET", 200, "application/octet-stream", false)]
    public void IsCacheable_HonorsMethodStatusAndType(string method, int status, string ctype, bool expected)
    {
        ContentCachePolicy.IsCacheable(method, status, ctype, NoHeaders()).Should().Be(expected);
    }

    [Fact]
    public void IsCacheable_False_WhenNoStoreOrSetCookie()
    {
        ContentCachePolicy.IsCacheable("GET", 200, "text/html",
            new Dictionary<string, string> { ["Cache-Control"] = "no-store" }).Should().BeFalse();
        ContentCachePolicy.IsCacheable("GET", 200, "text/html",
            new Dictionary<string, string> { ["Set-Cookie"] = "sid=1" }).Should().BeFalse();
    }
}

public class SessionKeyProtectorScopeTests
{
    [Fact]
    public void Protect_Unprotect_Roundtrips_WithConfiguredScope()
    {
        var options = new ProWebOptions();
        options.Session.DpapiScope = "CurrentUser";
        var protector = new SessionKeyProtector(options);

        var key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var wrapped = protector.Protect(key);
        wrapped.Should().NotBeEmpty();
        protector.Unprotect(wrapped).Should().Equal(key);
    }
}
