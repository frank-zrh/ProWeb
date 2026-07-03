namespace ProWeb.Server.Config;

/// <summary>Strongly-typed configuration bound from the "ProWeb" section of appsettings.</summary>
public sealed class ProWebOptions
{
    public const string SectionName = "ProWeb";

    public ServerOptions Server { get; set; } = new();

    public JwtOptions Jwt { get; set; } = new();

    public SessionOptions Session { get; set; } = new();

    public FetchOptions Fetch { get; set; } = new();

    public StorageOptions Storage { get; set; } = new();

    public MaintenanceOptions Maintenance { get; set; } = new();

    public RateLimitOptions RateLimit { get; set; } = new();
}

public sealed class ServerOptions
{
    public int HttpsPort { get; set; } = 8443;

    public bool UseHttps { get; set; } = true;

    public string? CertPath { get; set; }

    public string? CertPassword { get; set; }

    /// <summary>Emit an HSTS (Strict-Transport-Security) response header.</summary>
    public bool EnableHsts { get; set; } = true;

    /// <summary>HSTS max-age in seconds (default 1 year).</summary>
    public long HstsMaxAgeSeconds { get; set; } = 31_536_000;

    /// <summary>Require a client certificate (mutual TLS). When true the client cert is validated.</summary>
    public bool RequireClientCertificate { get; set; }

    /// <summary>Advertise and accept HTTP/3 (QUIC) in addition to HTTP/1.1 and HTTP/2.</summary>
    public bool EnableHttp3 { get; set; }
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "proweb";

    public string Audience { get; set; } = "proweb-client";

    public string SigningKey { get; set; } = "dev-signing-key-change-me-please-32b!!";

    public int TtlSeconds { get; set; } = 3600;
}

public sealed class SessionOptions
{
    public int TtlSeconds { get; set; } = 3600;

    public long ReplayWindowMs { get; set; } = 300_000;

    /// <summary>Master key (base64) used to protect stored session keys when DPAPI is unavailable.</summary>
    public string MasterKey { get; set; } = "5rC6r0m7t2Y0m0oQ0m9nZ0aX0bC0dE0fG0hI0jK0lM=";

    /// <summary>Windows DPAPI protection scope for stored session keys: "CurrentUser" (default) or "LocalMachine".</summary>
    public string DpapiScope { get; set; } = "CurrentUser";

    /// <summary>Time-to-live for cached proxy responses in seconds. Set to 0 to disable caching.</summary>
    public int CacheTtlSeconds { get; set; } = 300;
}

public sealed class FetchOptions
{
    public int TimeoutSeconds { get; set; } = 30;

    public int RetryCount { get; set; } = 3;

    public string UserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ProWeb/1.0 Safari/537.36";

    public string AcceptLanguage { get; set; } = "en-US,en;q=0.9";

    public int MaxHeadlessConcurrency { get; set; } = 2;

    public string[] HeadlessDomains { get; set; } = Array.Empty<string>();

    /// <summary>Maximum decompressed upstream response size in bytes (guards against decompression bombs).</summary>
    public long MaxResponseBytes { get; set; } = 52_428_800; // 50 MiB

    /// <summary>Enable the HTML-feature (SPA) heuristic that escalates an HTTP fetch to headless rendering.</summary>
    public bool EnableSpaHeuristic { get; set; } = true;
}

public sealed class StorageOptions
{
    public string ConnectionString { get; set; } = "Data Source=proweb.db";

    /// <summary>Per-connection SQLite busy_timeout in milliseconds (waits instead of failing with SQLITE_BUSY).</summary>
    public int BusyTimeoutMs { get; set; } = 5000;
}

public sealed class MaintenanceOptions
{
    /// <summary>Interval between TTL purge sweeps of expired sessions and cache entries, in seconds.</summary>
    public int PurgeIntervalSeconds { get; set; } = 300;

    /// <summary>Enable the periodic maintenance background service.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class RateLimitOptions
{
    /// <summary>Enable request rate limiting on public endpoints.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Max unauthenticated handshake requests per source IP per window.</summary>
    public int HandshakePermitPerWindow { get; set; } = 30;

    /// <summary>Max proxy/close requests per session (or IP) per window.</summary>
    public int ProxyPermitPerWindow { get; set; } = 240;

    /// <summary>Sliding/fixed window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
}
