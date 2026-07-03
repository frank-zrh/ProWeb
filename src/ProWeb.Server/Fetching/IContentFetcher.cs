namespace ProWeb.Server.Fetching;

/// <summary>A normalized fetch request independent of transport.</summary>
public sealed class FetchRequest
{
    public string SessionId { get; set; } = string.Empty;

    public string Method { get; set; } = "GET";

    public string Url { get; set; } = string.Empty;

    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[]? Body { get; set; }
}

/// <summary>The outcome of a fetch, including the final (post-redirect) URL.</summary>
public sealed class FetchResult
{
    public int StatusCode { get; set; }

    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public byte[] Body { get; set; } = Array.Empty<byte>();

    public string ContentType { get; set; } = string.Empty;

    public string FinalUrl { get; set; } = string.Empty;

    public string FetcherType { get; set; } = "http";
}

/// <summary>Abstraction over the strategy used to retrieve remote content.</summary>
public interface IContentFetcher
{
    string FetcherType { get; }

    Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken);
}

/// <summary>A content fetcher that renders via a headless browser and can report unavailability.</summary>
public interface IHeadlessFetcher : IContentFetcher
{
    /// <summary>True once the headless browser has been determined unavailable (degrade to HTTP).</summary>
    bool IsUnavailable { get; }
}
