using Microsoft.Data.Sqlite;

namespace ProWeb.Server.Storage;

/// <summary>CRUD + lifecycle operations for sessions.</summary>
public sealed class SessionRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SessionRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public void Insert(SessionRecord s)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Sessions
                (SessionId, DeviceId, SessionKeyProtected, ClientPublicKey, CreatedAtUnixMs, ExpiresAtUnixMs, Revoked)
            VALUES (@id, @dev, @key, @pub, @created, @expires, @revoked);
            """;
        cmd.Parameters.AddWithValue("@id", s.SessionId);
        cmd.Parameters.AddWithValue("@dev", s.DeviceId);
        cmd.Parameters.AddWithValue("@key", s.SessionKeyProtected);
        cmd.Parameters.AddWithValue("@pub", s.ClientPublicKey);
        cmd.Parameters.AddWithValue("@created", s.CreatedAtUnixMs);
        cmd.Parameters.AddWithValue("@expires", s.ExpiresAtUnixMs);
        cmd.Parameters.AddWithValue("@revoked", s.Revoked ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns the session only if it exists, is not revoked, and is not expired.</summary>
    public SessionRecord? GetActive(string sessionId, long nowUnixMs)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT SessionId, DeviceId, SessionKeyProtected, ClientPublicKey, CreatedAtUnixMs, ExpiresAtUnixMs, Revoked
            FROM Sessions
            WHERE SessionId = @id AND Revoked = 0 AND ExpiresAtUnixMs > @now;
            """;
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.Parameters.AddWithValue("@now", nowUnixMs);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public bool Revoke(string sessionId)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Sessions SET Revoked = 1 WHERE SessionId = @id;";
        cmd.Parameters.AddWithValue("@id", sessionId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public int PurgeExpired(long nowUnixMs)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Sessions WHERE ExpiresAtUnixMs < @now;";
        cmd.Parameters.AddWithValue("@now", nowUnixMs);
        return cmd.ExecuteNonQuery();
    }

    private static SessionRecord Map(SqliteDataReader r) => new()
    {
        SessionId = r.GetString(0),
        DeviceId = r.GetString(1),
        SessionKeyProtected = (byte[])r[2],
        ClientPublicKey = (byte[])r[3],
        CreatedAtUnixMs = r.GetInt64(4),
        ExpiresAtUnixMs = r.GetInt64(5),
        Revoked = r.GetInt64(6) != 0,
    };
}
