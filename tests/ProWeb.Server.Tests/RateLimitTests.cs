using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using ProWeb.Server.Endpoints;
using ProWeb.Server.Fetching;
using ProWeb.Shared.Crypto;
using Xunit;

namespace ProWeb.Server.Tests;

/// <summary>Rate limiting returns 429 with Retry-After once the window is exhausted (UT-C-R1-005).</summary>
public class RateLimitTests
{
    private sealed class LowLimitFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ProWeb:Server:UseHttps"] = "false",
                    ["ProWeb:Storage:ConnectionString"] = "Data Source=:memory:",
                    ["ProWeb:Jwt:SigningKey"] = "integration-test-signing-key-256bits-long!!",
                    ["ProWeb:RateLimit:Enabled"] = "true",
                    ["ProWeb:RateLimit:HandshakePermitPerWindow"] = "3",
                    ["ProWeb:RateLimit:WindowSeconds"] = "60",
                });
            });

            builder.ConfigureServices(services =>
            {
                var existing = services.Single(d => d.ServiceType == typeof(IFetchDispatcher));
                services.Remove(existing);
                services.AddSingleton<IFetchDispatcher>(new FakeFetchDispatcher());
            });
        }
    }

    [Fact]
    public async Task Handshake_ExceedingWindow_Returns429WithRetryAfter()
    {
        using var factory = new LowLimitFactory();
        var client = factory.CreateClient();

        var crypto = new CryptoService();
        var keys = crypto.GenerateKeyPair();
        var request = new HandshakeRequest
        {
            ClientPublicKey = Convert.ToBase64String(keys.PublicKey),
            DeviceId = "rl-device",
            ClientVersion = "1.0.0",
        };

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? limited = null;
        for (var i = 0; i < 8; i++)
        {
            var resp = await client.PostAsJsonAsync("/v1/handshake", request);
            statuses.Add(resp.StatusCode);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                limited ??= resp;
        }

        statuses.Should().Contain(HttpStatusCode.OK, "requests within the window succeed");
        statuses.Should().Contain(HttpStatusCode.TooManyRequests, "requests beyond the window are throttled");
        limited!.Headers.RetryAfter.Should().NotBeNull("throttled responses advertise Retry-After");
    }
}
