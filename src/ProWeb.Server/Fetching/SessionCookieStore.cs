using System.Collections.Concurrent;
using System.Net;

namespace ProWeb.Server.Fetching;

/// <summary>
/// Holds a per-session <see cref="CookieContainer"/> so that cookies from one session are never
/// visible to another. Cookies are applied to outgoing requests and captured from responses.
/// </summary>
public sealed class SessionCookieStore
{
    private readonly ConcurrentDictionary<string, CookieContainer> _containers = new();

    public CookieContainer GetContainer(string sessionId) =>
        _containers.GetOrAdd(sessionId, _ => new CookieContainer());

    public void Clear(string sessionId) => _containers.TryRemove(sessionId, out _);

    /// <summary>Returns the Cookie header value for the given URL, or null if there are none.</summary>
    public string? GetCookieHeader(string sessionId, Uri uri)
    {
        var header = GetContainer(sessionId).GetCookieHeader(uri);
        return string.IsNullOrEmpty(header) ? null : header;
    }

    /// <summary>Records Set-Cookie header values from a response.</summary>
    public void Capture(string sessionId, Uri uri, IEnumerable<string> setCookieValues)
    {
        var container = GetContainer(sessionId);
        foreach (var value in setCookieValues)
        {
            try
            {
                container.SetCookies(uri, value);
            }
            catch (CookieException)
            {
                // Ignore malformed Set-Cookie values.
            }
        }
    }

    public IReadOnlyCollection<Cookie> Snapshot(string sessionId, Uri uri) =>
        GetContainer(sessionId).GetCookies(uri).Cast<Cookie>().ToList();
}
