using System.Text;
using FluentAssertions;
using ProWeb.Server.Storage;
using Xunit;

namespace ProWeb.Server.Tests;

public class StorageTests : IDisposable
{
    private readonly SqliteConnectionFactory _factory;

    public StorageTests()
    {
        _factory = new SqliteConnectionFactory("Data Source=:memory:");
        new SqliteBootstrapper(_factory).Initialize();
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Session_Insert_GetActive_Revoke_Purge()
    {
        var repo = new SessionRepository(_factory);
        var now = 1_000_000L;
        repo.Insert(new SessionRecord
        {
            SessionId = "s1",
            DeviceId = "d1",
            SessionKeyProtected = new byte[] { 1, 2, 3 },
            ClientPublicKey = new byte[] { 4, 5, 6 },
            CreatedAtUnixMs = now,
            ExpiresAtUnixMs = now + 10_000,
        });

        repo.GetActive("s1", now).Should().NotBeNull();
        repo.GetActive("s1", now + 20_000).Should().BeNull("session is expired");

        repo.Revoke("s1").Should().BeTrue();
        repo.GetActive("s1", now).Should().BeNull("session is revoked");

        repo.PurgeExpired(now + 20_000).Should().Be(1);
    }

    [Fact]
    public void Cookies_Upsert_And_IsolateBySession()
    {
        var repo = new CookieRepository(_factory);
        var sessions = new SessionRepository(_factory);
        foreach (var sid in new[] { "sa", "sb" })
        {
            sessions.Insert(new SessionRecord
            {
                SessionId = sid,
                DeviceId = "d",
                SessionKeyProtected = new byte[] { 1 },
                ClientPublicKey = new byte[] { 1 },
                CreatedAtUnixMs = 0,
                ExpiresAtUnixMs = long.MaxValue,
            });
        }

        repo.Upsert(new CookieRecord { SessionId = "sa", Domain = "x.com", Path = "/", Name = "sid", Value = "1" });
        repo.Upsert(new CookieRecord { SessionId = "sa", Domain = "x.com", Path = "/", Name = "sid", Value = "2" });
        repo.Upsert(new CookieRecord { SessionId = "sb", Domain = "x.com", Path = "/", Name = "sid", Value = "9" });

        var a = repo.GetForSession("sa");
        a.Should().HaveCount(1);
        a[0].Value.Should().Be("2", "upsert replaces the value for the same key");

        repo.GetForSession("sb").Should().ContainSingle(c => c.Value == "9");
        repo.GetForDomain("sa", "x.com").Should().HaveCount(1);
    }

    [Fact]
    public void Cache_Put_Get_Expire()
    {
        var repo = new CacheRepository(_factory);
        repo.Put(new CacheRecord
        {
            PartitionKey = "pk1",
            Url = "https://x.com/a",
            Mime = "text/html",
            Body = Encoding.UTF8.GetBytes("hello"),
            StoredAtUnixMs = 1000,
            ExpiresAtUnixMs = 5000,
        });

        repo.Get("pk1", 2000).Should().NotBeNull();
        repo.Get("pk1", 6000).Should().BeNull("entry has expired");
        repo.PurgeExpired(6000).Should().Be(1);
    }

    [Fact]
    public void RequestLog_Add_And_Query()
    {
        var repo = new RequestLogRepository(_factory);
        repo.Add(new RequestLogRecord
        {
            RequestId = "r1",
            SessionId = "s1",
            Method = "GET",
            TargetUrl = "https://x.com",
            StatusCode = 200,
            FetcherType = "http",
            ServerElapsedMs = 12,
            CreatedAtUnixMs = 1,
        });

        repo.GetByRequestId("r1")!.StatusCode.Should().Be(200);
        repo.CountForSession("s1").Should().Be(1);
    }
}
