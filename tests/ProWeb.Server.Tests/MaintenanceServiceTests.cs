using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ProWeb.Server;
using ProWeb.Server.Config;
using ProWeb.Server.Storage;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>TTL purge background service sweep logic (UT-F-R2-001).</summary>
public class MaintenanceServiceTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory;

    public MaintenanceServiceTests()
    {
        _factory = new SqliteConnectionFactory("Data Source=:memory:");
        new SqliteBootstrapper(_factory).Initialize();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void PurgeOnce_RemovesExpiredSessionsAndCacheEntries()
    {
        var sessions = new SessionRepository(_factory);
        var cache = new CacheRepository(_factory);

        sessions.Insert(new SessionRecord
        {
            SessionId = "expired",
            DeviceId = "d",
            SessionKeyProtected = new byte[] { 1 },
            ClientPublicKey = new byte[] { 1 },
            CreatedAtUnixMs = 0,
            ExpiresAtUnixMs = 1_000,
        });
        sessions.Insert(new SessionRecord
        {
            SessionId = "live",
            DeviceId = "d",
            SessionKeyProtected = new byte[] { 1 },
            ClientPublicKey = new byte[] { 1 },
            CreatedAtUnixMs = 0,
            ExpiresAtUnixMs = long.MaxValue,
        });
        cache.Put(new CacheRecord
        {
            PartitionKey = "pk",
            Url = "https://x/a",
            Mime = "text/html",
            Body = Encoding.UTF8.GetBytes("x"),
            StoredAtUnixMs = 0,
            ExpiresAtUnixMs = 1_000,
        });

        long now = 10_000;
        var svc = new MaintenanceService(sessions, cache, new ProWebOptions(), NullLogger<MaintenanceService>.Instance, () => now);

        var (sessionsPurged, cachePurged) = svc.PurgeOnce();

        sessionsPurged.Should().Be(1);
        cachePurged.Should().Be(1);
        sessions.GetActive("live", now).Should().NotBeNull("non-expired session survives the sweep");
    }

    [Fact]
    public void PurgeOnce_NothingExpired_PurgesZero()
    {
        var sessions = new SessionRepository(_factory);
        var cache = new CacheRepository(_factory);
        var svc = new MaintenanceService(sessions, cache, new ProWebOptions(), NullLogger<MaintenanceService>.Instance, () => 1);

        svc.PurgeOnce().Should().Be((0, 0));
    }
}
