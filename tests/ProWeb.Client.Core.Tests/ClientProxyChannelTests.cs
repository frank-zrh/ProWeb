using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using ProWeb.Client.Core;
using ProWeb.Shared.Crypto;
using ProWeb.Shared.Protocol;
using ProWeb.Shared.Serialization;
using Xunit;

namespace ProWeb.Client.Core.Tests;

/// <summary>
/// A stub HTTP handler that mimics the server's handshake and proxy crypto so the client channel
/// can be exercised end-to-end (handshake → sealed request → sealed response) without a server.
/// </summary>
internal sealed class StubServerHandler : HttpMessageHandler
{
    private readonly CryptoService _crypto = new();
    private readonly EnvelopeCodec _codec;
    private byte[]? _sessionKey;
    private string _sessionId = string.Empty;

    public StubServerHandler()
    {
        _codec = new EnvelopeCodec(_crypto);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/v1/handshake")
            return await HandleHandshakeAsync(request).ConfigureAwait(false);
        if (path == "/v1/proxy")
            return await HandleProxyAsync(request).ConfigureAwait(false);
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> HandleHandshakeAsync(HttpRequestMessage request)
    {
        var body = await request.Content!.ReadFromJsonAsync<ClientHandshakeRequest>().ConfigureAwait(false);
        var clientPub = Convert.FromBase64String(body!.ClientPublicKey);
        var keyPair = _crypto.GenerateKeyPair();
        var shared = _crypto.DeriveSharedSecret(keyPair.PrivateKey, clientPub);

        _sessionId = Guid.NewGuid().ToString("N");
        _sessionKey = _crypto.DeriveSessionKey(
            shared, Encoding.UTF8.GetBytes(_sessionId), ClientProxyChannel.HkdfInfo);

        var response = new ClientHandshakeResponse
        {
            ServerPublicKey = Convert.ToBase64String(keyPair.PublicKey),
            SessionId = _sessionId,
            Token = "stub-token",
            ExpiresAtUnixMs = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
        };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) };
    }

    private async Task<HttpResponseMessage> HandleProxyAsync(HttpRequestMessage request)
    {
        var requestId = request.Headers.GetValues("X-Request-Id").First();
        var aad = Encoding.UTF8.GetBytes(requestId);
        var sealedBytes = await request.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
        var envelope = _codec.Decode<RequestEnvelope>(sealedBytes, _sessionKey!, aad);

        var response = new ResponseEnvelope
        {
            RequestId = envelope.RequestId,
            StatusCode = 200,
            Body = Encoding.UTF8.GetBytes($"fetched:{envelope.TargetUrl}"),
            ContentType = "text/plain",
            FinalUrl = envelope.TargetUrl,
            ServerElapsedMs = 5,
        };
        var sealedResponse = _codec.Encode(response, _sessionKey!, aad);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(sealedResponse),
        };
    }
}

public class ClientProxyChannelTests
{
    private static HttpClient StubClient() =>
        new(new StubServerHandler()) { BaseAddress = new Uri("https://server.test") };

    [Fact]
    public async Task Handshake_Then_Fetch_RoundTripsThroughEncryptedChannel()
    {
        var channel = new ClientProxyChannel(StubClient(), "device-1");
        channel.IsConnected.Should().BeFalse();

        await channel.HandshakeAsync();
        channel.IsConnected.Should().BeTrue();

        var response = await channel.FetchAsync("https://example.com/page");
        response.StatusCode.Should().Be(200);
        Encoding.UTF8.GetString(response.Body!).Should().Be("fetched:https://example.com/page");
    }

    [Fact]
    public async Task Fetch_BeforeHandshake_Throws()
    {
        var channel = new ClientProxyChannel(StubClient(), "device-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => channel.FetchAsync("https://example.com"));
    }
}

public class EnvelopeBuilderTests
{
    [Fact]
    public void Build_PopulatesFieldsAndGeneratesIds()
    {
        var builder = new EnvelopeBuilder(nowProvider: () => 42, idFactory: () => "req-1");
        var envelope = builder.Build("sid", "get", "https://x.com",
            new Dictionary<string, string> { ["X-A"] = "1" });

        envelope.SessionId.Should().Be("sid");
        envelope.RequestId.Should().Be("req-1");
        envelope.TimestampUnixMs.Should().Be(42);
        envelope.Method.Should().Be("GET");
        envelope.Headers["X-A"].Should().Be("1");
    }
}

public class ErrorPageModelTests
{
    [Fact]
    public void Render_IncludesRequestIdAndTitle()
    {
        var html = ErrorPageModel.Render(502, "req-xyz", "boom");
        html.Should().Contain("req-xyz");
        html.Should().Contain("Upstream Fetch Failed");
        html.Should().Contain("boom");
    }

    [Fact]
    public void Render_EncodesHtmlInDetail()
    {
        var html = ErrorPageModel.Render(400, "r", "<script>alert(1)</script>");
        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;");
    }
}

public class DpapiSessionKeyStoreTests
{
    [Fact]
    public void Protect_Unprotect_RoundTrips()
    {
        var store = new DpapiSessionKeyStore();
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var protectedKey = store.Protect(key);
        store.Unprotect(protectedKey).Should().Equal(key);
    }
}
