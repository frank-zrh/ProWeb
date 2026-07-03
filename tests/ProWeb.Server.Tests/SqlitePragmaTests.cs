using System.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using ProWeb.Server.Storage;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>Verifies busy_timeout PRAGMA is applied on every opened connection (UT-C-R1-009).</summary>
public class SqlitePragmaTests
{
    private static long QueryBusyTimeout(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout;";
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public void Open_AppliesConfiguredBusyTimeout()
    {
        using var factory = new SqliteConnectionFactory("Data Source=:memory:", busyTimeoutMs: 7500);
        using var conn = factory.Open();
        conn.State.Should().Be(ConnectionState.Open);
        QueryBusyTimeout(conn).Should().Be(7500);
    }

    [Fact]
    public void Open_FileDatabase_AppliesBusyTimeout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"proweb-pragma-{Guid.NewGuid():N}.db");
        try
        {
            using var factory = new SqliteConnectionFactory($"Data Source={path}", busyTimeoutMs: 3000);
            using var conn = factory.Open();
            QueryBusyTimeout(conn).Should().Be(3000);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
