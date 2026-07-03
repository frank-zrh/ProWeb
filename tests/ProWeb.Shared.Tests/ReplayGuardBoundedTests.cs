using FluentAssertions;
using ProWeb.Shared.Crypto;
using Xunit;

namespace ProWeb.Shared.Tests;

/// <summary>Covers the bounded/LRU behavior added to ReplayGuard (UT-C-R1-008).</summary>
public class ReplayGuardBoundedTests
{
    [Fact]
    public void TrackedSet_IsCappedAtMaxEntries()
    {
        long now = 1_000_000;
        var guard = new ReplayGuard(300_000, () => now, maxEntries: 100);

        for (var i = 0; i < 1_000; i++)
            guard.TryAccept($"r{i}", now).Should().BeTrue();

        // Memory stays bounded near the cap despite 1000 unique ids in one window.
        guard.TrackedCount.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void OldestEntriesAreEvictedFirst_WhenOverCapacity()
    {
        long now = 1_000_000;
        var guard = new ReplayGuard(300_000, () => now, maxEntries: 50);

        for (var i = 0; i < 500; i++)
            guard.TryAccept($"r{i}", now + i).Should().BeTrue();

        // The very first (oldest) id should have been evicted, so re-accepting it succeeds.
        guard.TryAccept("r0", now).Should().BeTrue();
    }

    [Fact]
    public void ReplaySemanticsPreserved_UnderBounding()
    {
        long now = 5_000_000;
        var guard = new ReplayGuard(300_000, () => now, maxEntries: 1000);
        guard.TryAccept("dup", now).Should().BeTrue();
        guard.TryAccept("dup", now).Should().BeFalse();
    }
}
