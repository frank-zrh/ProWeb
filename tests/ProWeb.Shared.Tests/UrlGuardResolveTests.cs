using System.Net;
using FluentAssertions;
using ProWeb.Shared.Content;
using Xunit;

namespace ProWeb.Shared.Tests;

/// <summary>DNS-resolution SSRF guard shared by all fetch paths (UT-C-R1-001/009).</summary>
public class UrlGuardResolveTests
{
    [Fact]
    public void FirstBlockedAddress_FindsPrivateAddress()
    {
        var addrs = new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.5") };
        UrlGuard.FirstBlockedAddress(addrs).Should().Be(IPAddress.Parse("10.0.0.5"));
    }

    [Fact]
    public void FirstBlockedAddress_AllPublic_ReturnsNull()
    {
        var addrs = new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Parse("1.1.1.1") };
        UrlGuard.FirstBlockedAddress(addrs).Should().BeNull();
    }

    [Fact]
    public async Task IsResolvedHostAllowedAsync_Loopback_IsBlocked()
    {
        var (allowed, reason) = await UrlGuard.IsResolvedHostAllowedAsync("localhost");
        allowed.Should().BeFalse();
        reason.Should().Contain("blocked");
    }

    [Fact]
    public async Task IsResolvedHostAllowedAsync_MetadataLiteral_IsBlocked()
    {
        var (allowed, _) = await UrlGuard.IsResolvedHostAllowedAsync("169.254.169.254");
        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task IsResolvedHostAllowedAsync_EmptyHost_IsBlocked()
    {
        var (allowed, _) = await UrlGuard.IsResolvedHostAllowedAsync("   ");
        allowed.Should().BeFalse();
    }

    [Fact]
    public async Task IsResolvedHostAllowedAsync_PublicLiteral_IsAllowed()
    {
        var (allowed, _) = await UrlGuard.IsResolvedHostAllowedAsync("93.184.216.34");
        allowed.Should().BeTrue();
    }
}
