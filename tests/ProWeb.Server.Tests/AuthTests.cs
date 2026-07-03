using FluentAssertions;
using Microsoft.Extensions.Options;
using ProWeb.Server.Auth;
using ProWeb.Server.Config;
using Xunit;

namespace ProWeb.Server.Tests;

public class AuthTests
{
    private static ProWebOptions Options() => new();

    [Fact]
    public void Jwt_Issue_Then_Validate_ReturnsSessionId()
    {
        var jwt = new JwtService(Options());
        var (token, expires) = jwt.Issue("session-123", "device-a");

        expires.Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        jwt.ValidateAndGetSessionId(token).Should().Be("session-123");
    }

    [Fact]
    public void Jwt_TamperedToken_IsRejected()
    {
        var jwt = new JwtService(Options());
        var (token, _) = jwt.Issue("s", "d");
        var tampered = token[..^2] + (token[^1] == 'a' ? "bb" : "aa");
        jwt.ValidateAndGetSessionId(tampered).Should().BeNull();
    }

    [Fact]
    public void Jwt_DifferentSigningKey_IsRejected()
    {
        var jwt1 = new JwtService(Options());
        var opts2 = new ProWebOptions();
        opts2.Jwt.SigningKey = "a-completely-different-signing-key-256bits!!";
        var jwt2 = new JwtService(opts2);

        var (token, _) = jwt1.Issue("s", "d");
        jwt2.ValidateAndGetSessionId(token).Should().BeNull();
    }

    [Fact]
    public void SessionKeyProtector_RoundTrips()
    {
        var protector = new SessionKeyProtector(new ProWebOptions());
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

        var protectedKey = protector.Protect(key);
        protectedKey.Should().NotBeEquivalentTo(key);
        protector.Unprotect(protectedKey).Should().Equal(key);
    }
}
