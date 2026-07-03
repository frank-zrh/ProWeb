using FluentAssertions;
using ProWeb.Shared.Crypto;
using Xunit;

namespace ProWeb.Shared.Tests;

public class ReplayGuardTests
{
    [Fact]
    public void Accepts_FreshUniqueRequest()
    {
        long now = 1_000_000;
        var guard = new ReplayGuard(300_000, () => now);
        guard.TryAccept("r1", now).Should().BeTrue();
    }

    [Fact]
    public void Rejects_DuplicateRequestId()
    {
        long now = 1_000_000;
        var guard = new ReplayGuard(300_000, () => now);
        guard.TryAccept("r1", now).Should().BeTrue();
        guard.TryAccept("r1", now).Should().BeFalse();
    }

    [Fact]
    public void Rejects_ExpiredTimestamp()
    {
        long now = 1_000_000;
        var guard = new ReplayGuard(300_000, () => now);
        guard.TryAccept("old", now - 400_000).Should().BeFalse();
        guard.TryAccept("future", now + 400_000).Should().BeFalse();
    }

    [Fact]
    public void Rejects_EmptyRequestId()
    {
        var guard = new ReplayGuard(300_000, () => 0);
        guard.TryAccept("", 0).Should().BeFalse();
    }

    [Fact]
    public void EvictsOldEntries_AsTimeAdvances()
    {
        long now = 1_000_000;
        var guard = new ReplayGuard(300_000, () => now);
        guard.TryAccept("r1", now).Should().BeTrue();
        guard.TrackedCount.Should().Be(1);

        now += 400_000; // advance beyond window
        guard.TryAccept("r2", now).Should().BeTrue();
        guard.TrackedCount.Should().Be(1); // r1 evicted
    }
}
