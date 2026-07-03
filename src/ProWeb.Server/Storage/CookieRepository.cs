using Microsoft.Data.Sqlite;

namespace ProWeb.Server.Storage;

/// <summary>Per-session cookie storage. Cookies are isolated by SessionId.</summary>
public sealed class CookieRepository
{
    private readonly SqliteConnectionFactory _factory;

    public CookieRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>Inserts or updates a cookie keyed by (SessionId, Domain, Path, Name).</summary>
    public void Upsert(CookieRecord c)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Cookies (SessionId, Domain, Name, Value, Path, ExpiresAtUnixMs, Secure, HttpOnly)
            VALUES (@sid, @domain, @name, @value, @path, @expires, @secure, @httponly)
            ON CONFLICT(SessionId, Domain, Path, Name) DO UPDATE SET
                Value = excluded.Value,
                ExpiresAtUnixMs = excluded.ExpiresAtUnixMs,
                Secure = excluded.Secure,
                HttpOnly = excluded.HttpOnly;
            """;
        cmd.Parameters.AddWithValue("@sid", c.SessionId);
        cmd.Parameters.AddWithValue("@domain", c.Domain);
        cmd.Parameters.AddWithValue("@name", c.Name);
        cmd.Parameters.AddWithValue("@value", (object?)c.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@path", c.Path);
        cmd.Parameters.AddWithValue("@expires", (object?)c.ExpiresAtUnixMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@secure", c.Secure ? 1 : 0);
        cmd.Parameters.AddWithValue("@httponly", c.HttpOnly ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<CookieRecord> GetForSession(string sessionId)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, SessionId, Domain, Name, Value, Path, ExpiresAtUnixMs, Secure, HttpOnly
            FROM Cookies WHERE SessionId = @sid;
            """;
        cmd.Parameters.AddWithValue("@sid", sessionId);
        return Read(cmd);
    }

    public IReadOnlyList<CookieRecord> GetForDomain(string sessionId, string domain)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, SessionId, Domain, Name, Value, Path, ExpiresAtUnixMs, Secure, HttpOnly
            FROM Cookies WHERE SessionId = @sid AND Domain = @domain;
            """;
        cmd.Parameters.AddWithValue("@sid", sessionId);
        cmd.Parameters.AddWithValue("@domain", domain);
        return Read(cmd);
    }

    public int DeleteForSession(string sessionId)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Cookies WHERE SessionId = @sid;";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        return cmd.ExecuteNonQuery();
    }

    private static IReadOnlyList<CookieRecord> Read(SqliteCommand cmd)
    {
        var list = new List<CookieRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new CookieRecord
            {
                Id = r.GetInt64(0),
                SessionId = r.GetString(1),
                Domain = r.GetString(2),
                Name = r.GetString(3),
                Value = r.IsDBNull(4) ? null : r.GetString(4),
                Path = r.GetString(5),
                ExpiresAtUnixMs = r.IsDBNull(6) ? null : r.GetInt64(6),
                Secure = r.GetInt64(7) != 0,
                HttpOnly = r.GetInt64(8) != 0,
            });
        }

        return list;
    }
}
