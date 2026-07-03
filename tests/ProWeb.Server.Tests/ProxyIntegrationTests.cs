using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ProWeb.Server.Auth;
using ProWeb.Server.Config;
using ProWeb.Server.Endpoints;
using ProWeb.Shared.Crypto;
using ProWeb.Shared.Protocol;
using ProWeb.Shared.Serialization;
using Xunit;

namespace ProWeb.Server.Tests;

public class ProxyIntegrationTests : IClassFixture<ProWebTestFactory>
{
    private readonly ProWebTestFactory _factory;
    private readonly HttpClient _client;

    public ProxyIntegrationTests(ProWebTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Response_CarriesHstsHeader()
    {
        var resp = await _client.GetAsync("/v1/health");
        resp.Headers.TryGetValues("Strict-Transport-Security", out var values).Should().BeTrue();
        string.Join(string.Empty, values!).Should().Contain("max-age=");
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var resp = await _client.GetAsync("/v1/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("ok");
        json.GetProperty("checks").GetProperty("db").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Handshake_Then_Proxy_ReturnsRewrittenContent()
    {
        var (session, key) = await HandshakeAsync();

        var requestId = Guid.NewGuid().ToString("N");
        var envelope = new RequestEnvelope
        {
            SessionId = session.SessionId,
            RequestId = requestId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Method = "GET",
            TargetUrl = "https://example.com/",
        };

        var response = await SendProxyAsync(session.Token, requestId, envelope, key);
        response.StatusCode.Should().Be(200);
        var html = Encoding.UTF8.GetString(response.Body!);
        html.Should().NotContain("/p?u=", "the dead proxy prefix must be gone; the client intercepts real URLs");
        html.Should().Contain("href=\"https://example.com/next\"", "relative links are rewritten to absolute real URLs");
        html.Should().Contain("src=\"https://example.com/img/a.png\"", "relative image sources are rewritten to absolute real URLs");
        _factory.Dispatcher.LastRequest!.Url.Should().Be("https://example.com/");
    }

    [Fact]
    public async Task Proxy_WithoutToken_Returns401()
    {
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        var resp = await _client.PostAsync("/v1/proxy", content);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Proxy_ReplayedRequestId_Returns409()
    {
        var (session, key) = await HandshakeAsync();
        var requestId = Guid.NewGuid().ToString("N");
        var envelope = new RequestEnvelope
        {
            SessionId = session.SessionId,
            RequestId = requestId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Method = "GET",
            TargetUrl = "https://example.com/",
        };

        (await SendProxyRawAsync(session.Token, requestId, envelope, key)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await SendProxyRawAsync(session.Token, requestId, envelope, key)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SessionClose_ThenProxy_Returns401()
    {
        var (session, key) = await HandshakeAsync();

        var closeReq = new HttpRequestMessage(HttpMethod.Post, "/v1/session/close");
        closeReq.Headers.Authorization = new("Bearer", session.Token);
        (await _client.SendAsync(closeReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var requestId = Guid.NewGuid().ToString("N");
        var envelope = new RequestEnvelope
        {
            SessionId = session.SessionId,
            RequestId = requestId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TargetUrl = "https://example.com/",
        };
        (await SendProxyRawAsync(session.Token, requestId, envelope, key)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Proxy_SecondIdenticalGet_ServedFromCache()
    {
        var (session, key) = await HandshakeAsync();

        var before = _factory.Dispatcher.CallCount;

        var firstId = Guid.NewGuid().ToString("N");
        var first = await SendProxyAsync(session.Token, firstId, new RequestEnvelope
        {
            SessionId = session.SessionId,
            RequestId = firstId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Method = "GET",
            TargetUrl = "https://cache-me.example/page",
        }, key);
        first.StatusCode.Should().Be(200);
        _factory.Dispatcher.CallCount.Should().Be(before + 1, "first request goes upstream");

        var secondId = Guid.NewGuid().ToString("N");
        var second = await SendProxyAsync(session.Token, secondId, new RequestEnvelope
        {
            SessionId = session.SessionId,
            RequestId = secondId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Method = "GET",
            TargetUrl = "https://cache-me.example/page",
        }, key);

        second.StatusCode.Should().Be(200);
        Encoding.UTF8.GetString(second.Body!).Should().NotContain("/p?u=", "rewritten content must emit absolute real URLs, not the dead proxy prefix");
        _factory.Dispatcher.CallCount.Should().Be(before + 1, "identical GET is served from cache, no second upstream fetch");
    }

    [Fact]
    public async Task Proxy_DisallowedMethod_Returns400()
    {
        var (session, key) = await HandshakeAsync();
        var requestId = Guid.NewGuid().ToString("N");
        var envelope = new RequestEnvelope
        {
            SessionId = session.SessionId,
            RequestId = requestId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Method = "TRACE",
            TargetUrl = "https://example.com/",
        };

        (await SendProxyRawAsync(session.Token, requestId, envelope, key)).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Proxy_TokenWithMismatchedDeviceId_Returns401()
    {
        var (session, key) = await HandshakeAsync();

        // Forge a validly-signed token for the same session but a DIFFERENT device id; the
        // middleware must reject it because the JWT 'did' no longer matches the stored session.
        var opts = new ProWebOptions();
        opts.Jwt.SigningKey = "integration-test-signing-key-256bits-long!!";
        var jwt = new JwtService(opts);
        var (forgedToken, _) = jwt.Issue(session.SessionId, "some-other-device");

        var requestId = Guid.NewGuid().ToString("N");
        var envelope = new RequestEnvelope
        {
            SessionId = session.SessionId,
            RequestId = requestId,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Method = "GET",
            TargetUrl = "https://example.com/",
        };

        (await SendProxyRawAsync(forgedToken, requestId, envelope, key)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(HandshakeResponse Session, byte[] Key)> HandshakeAsync()
    {
        var crypto = new CryptoService();
        var clientKeys = crypto.GenerateKeyPair();

        var request = new HandshakeRequest
        {
            ClientPublicKey = Convert.ToBase64String(clientKeys.PublicKey),
            DeviceId = "test-device",
            ClientVersion = "1.0.0",
        };
        var resp = await _client.PostAsJsonAsync("/v1/handshake", request);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = (await resp.Content.ReadFromJsonAsync<HandshakeResponse>())!;

        var serverPub = Convert.FromBase64String(session.ServerPublicKey);
        var shared = crypto.DeriveSharedSecret(clientKeys.PrivateKey, serverPub);
        var key = crypto.DeriveSessionKey(shared, Encoding.UTF8.GetBytes(session.SessionId), HandshakeService.HkdfInfo);
        return (session, key);
    }

    private async Task<ResponseEnvelope> SendProxyAsync(
        string token, string requestId, RequestEnvelope envelope, byte[] key)
    {
        var resp = await SendProxyRawAsync(token, requestId, envelope, key);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var codec = new EnvelopeCodec(new CryptoService());
        var sealedBytes = await resp.Content.ReadAsByteArrayAsync();
        return codec.Decode<ResponseEnvelope>(sealedBytes, key, Encoding.UTF8.GetBytes(requestId));
    }

    private async Task<HttpResponseMessage> SendProxyRawAsync(
        string token, string requestId, RequestEnvelope envelope, byte[] key)
    {
        var codec = new EnvelopeCodec(new CryptoService());
        var aad = Encoding.UTF8.GetBytes(requestId);
        var sealedBytes = codec.Encode(envelope, key, aad);

        var msg = new HttpRequestMessage(HttpMethod.Post, "/v1/proxy")
        {
            Content = new ByteArrayContent(sealedBytes),
        };
        msg.Headers.Authorization = new("Bearer", token);
        msg.Headers.Add("X-Request-Id", requestId);
        return await _client.SendAsync(msg);
    }
}
