using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProWeb.Server.Config;
using ProWeb.Server.Fetching;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>SPA HTML-feature escalation from HTTP to headless (UT-C-R1-007).</summary>
public class FetchDispatcherSpaTests
{
    private sealed class StubFetcher : IContentFetcher
    {
        private readonly FetchResult _result;

        public StubFetcher(FetchResult result) => _result = result;

        public int Calls { get; private set; }

        public string FetcherType => "http";

        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class StubHeadless : IHeadlessFetcher
    {
        private readonly FetchResult _result;

        public StubHeadless(FetchResult result, bool unavailable = false)
        {
            _result = result;
            IsUnavailable = unavailable;
        }

        public int Calls { get; private set; }

        public bool IsUnavailable { get; }

        public string FetcherType => "headless";

        public Task<FetchResult> FetchAsync(FetchRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private static FetcherSelector Selector(bool spaEnabled)
    {
        var opts = new ProWebOptions();
        opts.Fetch.EnableSpaHeuristic = spaEnabled;
        return new FetcherSelector(Microsoft.Extensions.Options.Options.Create(opts));
    }

    private const string SpaHtml =
        "<html><head><script src=\"/app.js\"></script></head><body><div id=\"root\"></div></body></html>";

    private static FetchResult Html(string body) => new()
    {
        StatusCode = 200,
        ContentType = "text/html; charset=utf-8",
        Body = System.Text.Encoding.UTF8.GetBytes(body),
        FetcherType = "http",
    };

    [Fact]
    public async Task SpaResponse_EscalatesToHeadless()
    {
        var http = new StubFetcher(Html(SpaHtml));
        var headless = new StubHeadless(new FetchResult { StatusCode = 200, FetcherType = "headless", Body = System.Text.Encoding.UTF8.GetBytes("rendered") });
        var dispatcher = new FetchDispatcher(Selector(spaEnabled: true), http, headless, NullLogger<FetchDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync(new FetchRequest { Url = "https://spa.example/" }, CancellationToken.None);

        headless.Calls.Should().Be(1, "SPA heuristic should trigger a headless re-fetch");
        result.FetcherType.Should().Be("headless");
    }

    [Fact]
    public async Task SpaResponse_WithHeuristicDisabled_StaysHttp()
    {
        var http = new StubFetcher(Html(SpaHtml));
        var headless = new StubHeadless(new FetchResult { StatusCode = 200, FetcherType = "headless" });
        var dispatcher = new FetchDispatcher(Selector(spaEnabled: false), http, headless, NullLogger<FetchDispatcher>.Instance);

        var result = await dispatcher.DispatchAsync(new FetchRequest { Url = "https://spa.example/" }, CancellationToken.None);

        headless.Calls.Should().Be(0);
        result.FetcherType.Should().Be("http");
    }

    [Fact]
    public async Task StaticHtml_DoesNotEscalate()
    {
        var staticHtml = "<html><body>" + new string('x', 5000) + "</body></html>";
        var http = new StubFetcher(Html(staticHtml));
        var headless = new StubHeadless(new FetchResult { StatusCode = 200, FetcherType = "headless" });
        var dispatcher = new FetchDispatcher(Selector(spaEnabled: true), http, headless, NullLogger<FetchDispatcher>.Instance);

        await dispatcher.DispatchAsync(new FetchRequest { Url = "https://static.example/" }, CancellationToken.None);

        headless.Calls.Should().Be(0);
    }
}
