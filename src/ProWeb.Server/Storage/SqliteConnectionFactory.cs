using Microsoft.Data.Sqlite;

namespace ProWeb.Server.Storage;

/// <summary>
/// Opens SQLite connections. For a shared in-memory database (used by tests) a single
/// connection is kept alive for the lifetime of the factory so the schema persists.
/// </summary>
public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly string _connectionString;
    private readonly bool _isSharedInMemory;
    private readonly int _busyTimeoutMs;
    private readonly SqliteConnection? _keepAlive;

    public SqliteConnectionFactory(string connectionString, int busyTimeoutMs = 5000)
    {
        _busyTimeoutMs = busyTimeoutMs < 0 ? 0 : busyTimeoutMs;
        _connectionString = NormalizeInMemory(connectionString, out _isSharedInMemory);
        if (_isSharedInMemory)
        {
            _keepAlive = new SqliteConnection(_connectionString);
            _keepAlive.Open();
            ApplyPragmas(_keepAlive);
        }
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        ApplyPragmas(conn);
        return conn;
    }

    /// <summary>
    /// Applies per-connection PRAGMAs. <c>busy_timeout</c> makes concurrent writers wait for the
    /// lock instead of failing immediately with SQLITE_BUSY; WAL + synchronous=NORMAL keep read/write
    /// concurrency healthy for file-backed databases.
    /// </summary>
    private void ApplyPragmas(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = _isSharedInMemory
            ? $"PRAGMA busy_timeout={_busyTimeoutMs};"
            : $"PRAGMA busy_timeout={_busyTimeoutMs}; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        cmd.ExecuteNonQuery();
    }

    private static string NormalizeInMemory(string connectionString, out bool isSharedInMemory)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            builder.Mode == SqliteOpenMode.Memory)
        {
            // Give the in-memory database a shared name so multiple connections see the same data.
            builder.DataSource = "proweb-shared";
            builder.Mode = SqliteOpenMode.Memory;
            builder.Cache = SqliteCacheMode.Shared;
            isSharedInMemory = true;
            return builder.ToString();
        }

        isSharedInMemory = false;
        return builder.ToString();
    }

    public void Dispose() => _keepAlive?.Dispose();
}
