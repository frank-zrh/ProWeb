namespace ProWeb.Server.Storage;

/// <summary>Persisted session row.</summary>
public sealed class SessionRecord
{
    public string SessionId { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public byte[] SessionKeyProtected { get; set; } = Array.Empty<byte>();

    public byte[] ClientPublicKey { get; set; } = Array.Empty<byte>();

    public long CreatedAtUnixMs { get; set; }

    public long ExpiresAtUnixMs { get; set; }

    public bool Revoked { get; set; }
}

/// <summary>Persisted per-session cookie row.</summary>
public sealed class CookieRecord
{
    public long Id { get; set; }

    public string SessionId { get; set; } = string.Empty;

    public string Domain { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string Path { get; set; } = "/";

    public long? ExpiresAtUnixMs { get; set; }

    public bool Secure { get; set; }

    public bool HttpOnly { get; set; }
}

/// <summary>Persisted cache entry.</summary>
public sealed class CacheRecord
{
    public string PartitionKey { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Mime { get; set; }

    public byte[]? Body { get; set; }

    public long StoredAtUnixMs { get; set; }

    public long ExpiresAtUnixMs { get; set; }
}

/// <summary>Persisted request log row for full-chain tracing.</summary>
public sealed class RequestLogRecord
{
    public long Id { get; set; }

    public string RequestId { get; set; } = string.Empty;

    public string? SessionId { get; set; }

    public string Method { get; set; } = "GET";

    public string TargetUrl { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string FetcherType { get; set; } = "http";

    public long ServerElapsedMs { get; set; }

    public long CreatedAtUnixMs { get; set; }
}
