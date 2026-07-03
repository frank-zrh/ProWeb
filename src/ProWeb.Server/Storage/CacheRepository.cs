using Microsoft.Data.Sqlite;

namespace ProWeb.Server.Storage;

/// <summary>Content cache keyed by a URL-derived partition key.</summary>
public sealed class CacheRepository
{
    private readonly SqliteConnectionFactory _factory;

    public CacheRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public void Put(CacheRecord c)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CacheEntries (PartitionKey, Url, Mime, Body, StoredAtUnixMs, ExpiresAtUnixMs)
            VALUES (@pk, @url, @mime, @body, @stored, @expires)
            ON CONFLICT(PartitionKey) DO UPDATE SET
                Url = excluded.Url,
                Mime = excluded.Mime,
                Body = excluded.Body,
                StoredAtUnixMs = excluded.StoredAtUnixMs,
                ExpiresAtUnixMs = excluded.ExpiresAtUnixMs;
            """;
        cmd.Parameters.AddWithValue("@pk", c.PartitionKey);
        cmd.Parameters.AddWithValue("@url", c.Url);
        cmd.Parameters.AddWithValue("@mime", (object?)c.Mime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@body", (object?)c.Body ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@stored", c.StoredAtUnixMs);
        cmd.Parameters.AddWithValue("@expires", c.ExpiresAtUnixMs);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns the cache entry only if present and not expired.</summary>
    public CacheRecord? Get(string partitionKey, long nowUnixMs)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT PartitionKey, Url, Mime, Body, StoredAtUnixMs, ExpiresAtUnixMs
            FROM CacheEntries WHERE PartitionKey = @pk AND ExpiresAtUnixMs > @now;
            """;
        cmd.Parameters.AddWithValue("@pk", partitionKey);
        cmd.Parameters.AddWithValue("@now", nowUnixMs);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new CacheRecord
        {
            PartitionKey = r.GetString(0),
            Url = r.GetString(1),
            Mime = r.IsDBNull(2) ? null : r.GetString(2),
            Body = r.IsDBNull(3) ? null : (byte[])r[3],
            StoredAtUnixMs = r.GetInt64(4),
            ExpiresAtUnixMs = r.GetInt64(5),
        };
    }

    public int PurgeExpired(long nowUnixMs)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM CacheEntries WHERE ExpiresAtUnixMs < @now;";
        cmd.Parameters.AddWithValue("@now", nowUnixMs);
        return cmd.ExecuteNonQuery();
    }
}
