using Microsoft.Data.Sqlite;

namespace ProWeb.Server.Storage;

/// <summary>Append-only request log for tracing and diagnostics.</summary>
public sealed class RequestLogRepository
{
    private readonly SqliteConnectionFactory _factory;

    public RequestLogRepository(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public void Add(RequestLogRecord log)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO RequestLogs
                (RequestId, SessionId, Method, TargetUrl, StatusCode, FetcherType, ServerElapsedMs, CreatedAtUnixMs)
            VALUES (@rid, @sid, @method, @url, @status, @fetcher, @elapsed, @created);
            """;
        cmd.Parameters.AddWithValue("@rid", log.RequestId);
        cmd.Parameters.AddWithValue("@sid", (object?)log.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@method", log.Method);
        cmd.Parameters.AddWithValue("@url", log.TargetUrl);
        cmd.Parameters.AddWithValue("@status", log.StatusCode);
        cmd.Parameters.AddWithValue("@fetcher", log.FetcherType);
        cmd.Parameters.AddWithValue("@elapsed", log.ServerElapsedMs);
        cmd.Parameters.AddWithValue("@created", log.CreatedAtUnixMs);
        cmd.ExecuteNonQuery();
    }

    public RequestLogRecord? GetByRequestId(string requestId)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, RequestId, SessionId, Method, TargetUrl, StatusCode, FetcherType, ServerElapsedMs, CreatedAtUnixMs
            FROM RequestLogs WHERE RequestId = @rid ORDER BY Id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@rid", requestId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new RequestLogRecord
        {
            Id = r.GetInt64(0),
            RequestId = r.GetString(1),
            SessionId = r.IsDBNull(2) ? null : r.GetString(2),
            Method = r.GetString(3),
            TargetUrl = r.GetString(4),
            StatusCode = r.GetInt32(5),
            FetcherType = r.GetString(6),
            ServerElapsedMs = r.GetInt64(7),
            CreatedAtUnixMs = r.GetInt64(8),
        };
    }

    public int CountForSession(string sessionId)
    {
        using var conn = _factory.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM RequestLogs WHERE SessionId = @sid;";
        cmd.Parameters.AddWithValue("@sid", sessionId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
