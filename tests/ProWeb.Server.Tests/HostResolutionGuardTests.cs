using System.Net;
using FluentAssertions;
using ProWeb.Server.Fetching;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>
/// Verifies the per-fetch <see cref="HostResolutionGuard"/> resolves each host exactly once and
/// reuses that verdict, closing the repeated-re-resolution TOCTOU window (UT-C-R3-001), while still
/// applying the shared SSRF address policy.
/// </summary>
public class HostResolutionGuardTests
{
    [Fact]
    public async Task AllowsPublicHost_AndCachesVerdict_ResolvingOnlyOnce()
    {
        var calls = 0;
        var guard = new HostResolutionGuard((_, _) =>
        {
            calls++;
            return Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") });
        });

        var first = await guard.CheckAsync("example.com");
        var second = await guard.CheckAsync("example.com");

        first.Allowed.Should().BeTrue();
        second.Allowed.Should().BeTrue();
        calls.Should().Be(1, "the verdict is cached for the fetch lifetime");
        guard.ResolvedHostCount.Should().Be(1);
    }

    [Fact]
    public async Task BlocksHostResolvingToPrivateRange()
    {
        var guard = new HostResolutionGuard((_, _) =>
            Task.FromResult(new[] { IPAddress.Parse("169.254.169.254") }));

        var result = await guard.CheckAsync("rebind.evil.example");

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Contain("blocked address");
    }

    [Fact]
    public async Task CachedVerdictIsStable_EvenIfResolverWouldRebind()
    {
        var sequence = new Queue<IPAddress[]>(new[]
        {
            new[] { IPAddress.Parse("93.184.216.34") }, // first (checked) answer: public → allowed
            new[] { IPAddress.Parse("127.0.0.1") },     // a later rebind attempt would be private
        });
        var guard = new HostResolutionGuard((_, _) => Task.FromResult(sequence.Dequeue()));

        var first = await guard.CheckAsync("rebind.example");
        var second = await guard.CheckAsync("rebind.example");

        first.Allowed.Should().BeTrue();
        second.Allowed.Should().BeTrue("the first verdict is pinned; the resolver is not consulted again");
        sequence.Should().ContainSingle("only one resolution should have been dequeued");
    }

    [Fact]
    public async Task LiteralPrivateIpHost_IsBlockedWithoutResolver()
    {
        var resolverCalled = false;
        var guard = new HostResolutionGuard((_, _) =>
        {
            resolverCalled = true;
            return Task.FromResult(Array.Empty<IPAddress>());
        });

        var result = await guard.CheckAsync("10.0.0.5");

        result.Allowed.Should().BeFalse();
        resolverCalled.Should().BeFalse("literal IPs are validated directly, not via DNS");
    }

    [Fact]
    public async Task EmptyHost_IsRejected()
    {
        var guard = new HostResolutionGuard((_, _) => Task.FromResult(Array.Empty<IPAddress>()));
        var result = await guard.CheckAsync("   ");
        result.Allowed.Should().BeFalse();
    }
}
