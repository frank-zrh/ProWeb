using FluentAssertions;
using ProWeb.Server.Auth;
using ProWeb.Server.Config;
using ProWeb.Server.Endpoints;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>
/// Device-binding claim consistency (UT-C-R1-011) and request-method whitelist (UT-C-R1-010).
/// </summary>
public class SecurityValidationTests
{
    [Fact]
    public void Jwt_CarriesDeviceIdClaim_AndValidatesBack()
    {
        var jwt = new JwtService(new ProWebOptions());
        var (token, _) = jwt.Issue("session-x", "device-y");

        var claims = jwt.ValidateAndGetClaims(token);
        claims.Should().NotBeNull();
        claims!.Value.SessionId.Should().Be("session-x");
        claims.Value.DeviceId.Should().Be("device-y");
    }

    [Fact]
    public void NormalizeDeviceId_IsStableForStorageAndToken()
    {
        HandshakeService.NormalizeDeviceId("  dev-1  ").Should().Be("dev-1");
        HandshakeService.NormalizeDeviceId(null).Should().Be("unknown");
        HandshakeService.NormalizeDeviceId("   ").Should().Be("unknown");
        // Idempotent: normalizing the already-normalized value yields the same result.
        var once = HandshakeService.NormalizeDeviceId(" Abc ");
        HandshakeService.NormalizeDeviceId(once).Should().Be(once);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("post")]
    [InlineData("PUT")]
    [InlineData("delete")]
    [InlineData("HEAD")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    public void AllowedMethods_AcceptsStandardVerbs_CaseInsensitive(string method)
    {
        ProxyService.AllowedMethods.Contains(method).Should().BeTrue();
    }

    [Theory]
    [InlineData("TRACE")]
    [InlineData("CONNECT")]
    [InlineData("FROBNICATE")]
    public void AllowedMethods_RejectsUnexpectedVerbs(string method)
    {
        ProxyService.AllowedMethods.Contains(method).Should().BeFalse();
    }
}
