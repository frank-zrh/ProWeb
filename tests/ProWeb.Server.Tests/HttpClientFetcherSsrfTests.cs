using FluentAssertions;
using Microsoft.Extensions.Options;
using ProWeb.Server.Config;
using ProWeb.Server.Fetching;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>
/// SSRF regression tests: the fetcher must reject not only literal private/loopback IPs
/// (caught by <c>UrlGuard.IsAllowed</c>) but also hostnames that RESOLVE to blocked ranges —
/// the latter is enforced at socket-connect time so it also covers redirect hops.
/// </summary>
public class HttpClientFetcherSsrfTests
{
    private static HttpClientFetcher NewFetcher()
    {
        var opts = new ProWebOptions();
        return new HttpClientFetcher(Options.Create(opts), new SessionCookieStore());
    }

    [Fact]
    public async Task FetchAsync_LiteralLoopbackIp_IsBlockedByGuard()
    {
        using var fetcher = NewFetcher();
        var req = new FetchRequest { SessionId = "s1", Url = "http://127.0.0.1/" };

        var act = async () => await fetcher.FetchAsync(req, CancellationToken.None);

        await act.Should().ThrowAsync<FetchBlockedException>();
    }

    [Fact]
    public async Task FetchAsync_HostnameResolvingToLoopback_IsBlockedAtConnect()
    {
        using var fetcher = NewFetcher();
        // "localhost" passes the literal-URL guard but resolves to 127.0.0.1/::1,
        // which the ConnectCallback must reject before any socket is used.
        var req = new FetchRequest { SessionId = "s1", Url = "http://localhost:1/" };

        Exception? caught = null;
        try
        {
            await fetcher.FetchAsync(req, CancellationToken.None);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        // The block surfaces either directly or wrapped by HttpClient.
        var isBlocked = caught is FetchBlockedException
            || caught!.InnerException is FetchBlockedException;
        isBlocked.Should().BeTrue($"expected an SSRF block but got: {caught}");
    }
}
