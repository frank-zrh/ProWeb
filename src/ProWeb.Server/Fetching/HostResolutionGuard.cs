using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ProWeb.Shared.Content;

namespace ProWeb.Server.Fetching;

/// <summary>
/// Per-fetch SSRF host guard. Resolves each host exactly once and caches the verdict for the
/// lifetime of a single fetch, so the many sub-resource interception callbacks a headless page
/// fires cannot each re-resolve the same host and observe a different answer (the DNS-rebinding /
/// TOCTOU window that <see cref="UrlGuard.IsResolvedHostAllowedAsync"/> leaves open when called
/// repeatedly). The resolver is injectable so the caching/verdict logic is unit-testable without
/// real DNS.
/// </summary>
/// <remarks>
/// Residual limitation (documented, tracked as UT-C-R3-001): this pins the *verdict* per fetch but
/// does not pin the *address* Chromium ultimately connects to — full closure requires launching
/// Chromium with <c>--host-resolver-rules</c> to force it to reuse our resolved IP, which is not
/// safe to do on a browser shared across sessions. The single-resolution cache narrows the window
/// from "every sub-request" to "once per host per fetch".
/// </remarks>
public sealed class HostResolutionGuard
{
    /// <summary>Resolves a host name to its candidate addresses.</summary>
    public delegate Task<IPAddress[]> HostResolver(string host, CancellationToken cancellationToken);

    private readonly HostResolver _resolver;
    private readonly ConcurrentDictionary<string, (bool Allowed, string Reason)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public HostResolutionGuard(HostResolver? resolver = null) => _resolver = resolver ?? DefaultResolveAsync;

    /// <summary>Number of distinct hosts actually resolved (cache misses). For tests/observability.</summary>
    public int ResolvedHostCount => _cache.Count;

    /// <summary>
    /// Returns the (cached) SSRF verdict for <paramref name="host"/>. The first call resolves and
    /// validates; subsequent calls for the same host reuse the stored verdict.
    /// </summary>
    public async Task<(bool Allowed, string Reason)> CheckAsync(string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return (false, "empty host");

        if (_cache.TryGetValue(host, out var cached))
            return cached;

        var verdict = await ResolveAndValidateAsync(host, cancellationToken).ConfigureAwait(false);
        return _cache.GetOrAdd(host, verdict);
    }

    private async Task<(bool Allowed, string Reason)> ResolveAndValidateAsync(string host, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? new[] { literal }
                : await _resolver(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return (false, $"host '{host}' did not resolve");
        }

        if (addresses.Length == 0)
            return (false, $"host '{host}' did not resolve");

        if (UrlGuard.FirstBlockedAddress(addresses) is { } blocked)
            return (false, $"host '{host}' resolves to blocked address {blocked}");

        return (true, string.Empty);
    }

    private static async Task<IPAddress[]> DefaultResolveAsync(string host, CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
}
