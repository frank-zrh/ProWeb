namespace ProWeb.Client.Core;

/// <summary>
/// Assembles a request body (<see cref="byte"/>[]) from the byte segments of an intercepted CEF
/// request's POST data. Kept as pure logic — independent of CefSharp's <c>IPostData</c> — so the
/// body-extraction rule (UT-F-R3-001: POST/PUT/PATCH must carry their body through the proxy) can be
/// unit-tested without a real browser. The adapter passes each byte element's payload in order.
/// </summary>
public static class PostDataReader
{
    /// <summary>HTTP methods that carry a request body and must have it proxied.</summary>
    public static bool MethodCanHaveBody(string? method) =>
        method is not null &&
        (method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
         method.Equals("PUT", StringComparison.OrdinalIgnoreCase) ||
         method.Equals("PATCH", StringComparison.OrdinalIgnoreCase) ||
         method.Equals("DELETE", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Concatenates the byte segments (each CEF <c>PostDataElement</c>'s bytes, in order) into a
    /// single body. Returns null when there are no non-empty segments, so empty-body requests keep
    /// a null body (GET/HEAD/OPTIONS behavior is unchanged).
    /// </summary>
    public static byte[]? Combine(IEnumerable<byte[]?>? elementByteSegments)
    {
        if (elementByteSegments is null)
            return null;

        using var buffer = new MemoryStream();
        var any = false;
        foreach (var segment in elementByteSegments)
        {
            if (segment is { Length: > 0 })
            {
                buffer.Write(segment, 0, segment.Length);
                any = true;
            }
        }

        return any ? buffer.ToArray() : null;
    }
}
