using Microsoft.Data.Sqlite;

namespace ProWeb.Server.Storage;

/// <summary>Creates the SQLite schema idempotently and enables WAL journaling.</summary>
public sealed class SqliteBootstrapper
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteBootstrapper(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public void Initialize()
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;

            CREATE TABLE IF NOT EXISTS Sessions (
                SessionId TEXT PRIMARY KEY,
                DeviceId TEXT NOT NULL,
                SessionKeyProtected BLOB NOT NULL,
                ClientPublicKey BLOB NOT NULL,
                CreatedAtUnixMs INTEGER NOT NULL,
                ExpiresAtUnixMs INTEGER NOT NULL,
                Revoked INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS IX_Sessions_Device ON Sessions(DeviceId);
            CREATE INDEX IF NOT EXISTS IX_Sessions_Expires ON Sessions(ExpiresAtUnixMs);

            CREATE TABLE IF NOT EXISTS Cookies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL,
                Domain TEXT NOT NULL,
                Name TEXT NOT NULL,
                Value TEXT,
                Path TEXT NOT NULL DEFAULT '/',
                ExpiresAtUnixMs INTEGER,
                Secure INTEGER NOT NULL DEFAULT 0,
                HttpOnly INTEGER NOT NULL DEFAULT 0,
                UNIQUE(SessionId, Domain, Path, Name),
                FOREIGN KEY(SessionId) REFERENCES Sessions(SessionId) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_Cookies_Session ON Cookies(SessionId);

            CREATE TABLE IF NOT EXISTS CacheEntries (
                PartitionKey TEXT PRIMARY KEY,
                Url TEXT NOT NULL,
                Mime TEXT,
                Body BLOB,
                StoredAtUnixMs INTEGER NOT NULL,
                ExpiresAtUnixMs INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Cache_Expires ON CacheEntries(ExpiresAtUnixMs);

            CREATE TABLE IF NOT EXISTS RequestLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RequestId TEXT NOT NULL,
                SessionId TEXT,
                Method TEXT NOT NULL,
                TargetUrl TEXT NOT NULL,
                StatusCode INTEGER NOT NULL,
                FetcherType TEXT NOT NULL,
                ServerElapsedMs INTEGER NOT NULL,
                CreatedAtUnixMs INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ReqLog_RequestId ON RequestLogs(RequestId);
            CREATE INDEX IF NOT EXISTS IX_ReqLog_Session ON RequestLogs(SessionId);
            """;
        cmd.ExecuteNonQuery();
    }
}
