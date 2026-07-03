using System.Net;
using System.Net.Sockets;

namespace ProWeb.Shared.Content;

/// <summary>
/// Guards against SSRF: only http/https absolute URLs are allowed, and hosts that
/// resolve to loopback, link-local, private, or cloud-metadata ranges are rejected.
/// </summary>
public static class UrlGuard
{
    public static bool IsAllowed(string url, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            reason = "empty url";
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "not an absolute uri";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = $"scheme '{uri.Scheme}' not allowed";
            return false;
        }

        var host = uri.DnsSafeHost;
        // Cloud metadata endpoint.
        if (host == "169.254.169.254" || host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            reason = "metadata endpoint blocked";
            return false;
        }

        if (IPAddress.TryParse(host, out var ip) && IsBlockedAddress(ip))
        {
            reason = "host resolves to a blocked address range";
            return false;
        }

        return true;
    }

    public static bool IsBlockedAddress(IPAddress ip)
    {
        // Unwrap IPv4-mapped IPv6 (e.g. ::ffff:169.254.169.254) so the IPv4 rules below apply;
        // otherwise a mapped private/metadata address would slip past on a dual-stack socket.
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip)) return true;
        // Unspecified / any address.
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (b[0] == 10) return true;
            // 172.16.0.0/12
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            // 192.168.0.0/16
            if (b[0] == 192 && b[1] == 168) return true;
            // 169.254.0.0/16 link-local
            if (b[0] == 169 && b[1] == 254) return true;
            // 0.0.0.0/8
            if (b[0] == 0) return true;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            if (IPAddress.IPv6Loopback.Equals(ip)) return true;
            // Unique local addresses fc00::/7
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the first address in <paramref name="addresses"/> that falls in a blocked range, or
    /// null when every address is allowed. Shared by every fetch path so the SSRF policy has a
    /// single source of truth.
    /// </summary>
    public static IPAddress? FirstBlockedAddress(IEnumerable<IPAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        foreach (var addr in addresses)
        {
            if (IsBlockedAddress(addr))
                return addr;
        }

        return null;
    }

    /// <summary>
    /// Resolves <paramref name="host"/> and validates every resolved address against the SSRF
    /// policy. This is the pre-navigation guard used by fetch paths (e.g. headless) that cannot hook
    /// the socket connect the way <see cref="HttpClientFetcher"/> does. Returns false with a reason
    /// when the host is missing, unresolvable, or resolves to any blocked range.
    /// </summary>
    public static async Task<(bool Allowed, string Reason)> IsResolvedHostAllowedAsync(
        string host, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return (false, "empty host");

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? new[] { literal }
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return (false, $"host '{host}' did not resolve");
        }

        if (addresses.Length == 0)
            return (false, $"host '{host}' did not resolve");

        if (FirstBlockedAddress(addresses) is { } blocked)
            return (false, $"host '{host}' resolves to blocked address {blocked}");

        return (true, string.Empty);
    }
}
