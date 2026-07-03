using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProWeb.Server.Fetching;

namespace ProWeb.Server.Tests;

/// <summary>A deterministic fetch dispatcher used to test the proxy pipeline without network I/O.</summary>
public sealed class FakeFetchDispatcher : IFetchDispatcher
{
    public FetchResult Result { get; set; } = new()
    {
        StatusCode = 200,
        Body = System.Text.Encoding.UTF8.GetBytes(
            "<html><body><a href=\"/next\">n</a><img src=\"img/a.png\"></body></html>"),
        ContentType = "text/html; charset=utf-8",
        FinalUrl = "https://example.com/",
        FetcherType = "http",
    };

    public FetchRequest? LastRequest { get; private set; }

    public int CallCount { get; private set; }

    public Task<FetchResult> DispatchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        CallCount++;
        return Task.FromResult(Result);
    }
}

/// <summary>
/// Boots the server in-memory with a shared SQLite database and a fake fetch dispatcher so
/// integration tests exercise the real HTTP/crypto/session pipeline deterministically.
/// </summary>
public sealed class ProWebTestFactory : WebApplicationFactory<Program>
{
    public FakeFetchDispatcher Dispatcher { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProWeb:Server:UseHttps"] = "false",
                ["ProWeb:Storage:ConnectionString"] = "Data Source=:memory:",
                ["ProWeb:Session:TtlSeconds"] = "3600",
                ["ProWeb:Jwt:SigningKey"] = "integration-test-signing-key-256bits-long!!",
            });
        });

        builder.ConfigureServices(services =>
        {
            var existing = services.Single(d => d.ServiceType == typeof(IFetchDispatcher));
            services.Remove(existing);
            services.AddSingleton<IFetchDispatcher>(Dispatcher);
        });
    }
}
