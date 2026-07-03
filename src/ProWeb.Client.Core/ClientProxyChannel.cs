using System.Net.Http.Json;
using System.Text;
using ProWeb.Shared.Crypto;
using ProWeb.Shared.Protocol;
using ProWeb.Shared.Serialization;

namespace ProWeb.Client.Core;

/// <summary>Request DTO for the handshake call.</summary>
public sealed class ClientHandshakeRequest
{
    public string ClientPublicKey { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public string ClientVersion { get; set; } = "1.0.0";
}

/// <summary>Response DTO for the handshake call.</summary>
public sealed class ClientHandshakeResponse
{
    public string ServerPublicKey { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public long ExpiresAtUnixMs { get; set; }
}

/// <summary>
/// Client side of the encrypted channel. Performs the X25519/HKDF handshake, then sends proxied
/// requests as AES-256-GCM sealed <see cref="RequestEnvelope"/> payloads and decodes the sealed
/// responses. Pure networking + crypto so it can be unit-tested against a stub handler.
/// </summary>
public sealed class ClientProxyChannel : IProxyChannel
{
    public const string HkdfInfo = "proweb-v1";

    private readonly HttpClient _http;
    private readonly CryptoService _crypto;
    private readonly EnvelopeCodec _codec;
    private readonly EnvelopeBuilder _builder;
    private readonly string _deviceId;

    private byte[]? _sessionKey;
    private string? _sessionId;
    private string? _token;

    public ClientProxyChannel(HttpClient http, string deviceId, EnvelopeBuilder? builder = null)
    {
        _http = http;
        _crypto = new CryptoService();
        _codec = new EnvelopeCodec(_crypto);
        _builder = builder ?? new EnvelopeBuilder();
        _deviceId = deviceId;
    }

    public bool IsConnected => _sessionKey is not null && _sessionId is not null && _token is not null;

    public string? SessionId => _sessionId;

    /// <summary>Performs the handshake and stores the derived session key and token.</summary>
    public async Task HandshakeAsync(CancellationToken cancellationToken = default)
    {
        var keyPair = _crypto.GenerateKeyPair();
        var request = new ClientHandshakeRequest
        {
            ClientPublicKey = Convert.ToBase64String(keyPair.PublicKey),
            DeviceId = _deviceId,
            ClientVersion = "1.0.0",
        };

        using var response = await _http.PostAsJsonAsync("/v1/handshake", request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ClientHandshakeResponse>(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("Empty handshake response.");

        var serverPublicKey = Convert.FromBase64String(body.ServerPublicKey);
        var sharedSecret = _crypto.DeriveSharedSecret(keyPair.PrivateKey, serverPublicKey);
        _sessionKey = _crypto.DeriveSessionKey(
            sharedSecret, Encoding.UTF8.GetBytes(body.SessionId), HkdfInfo);
        _sessionId = body.SessionId;
        _token = body.Token;
    }

    /// <summary>Fetches a URL through the encrypted proxy channel.</summary>
    public async Task<ResponseEnvelope> FetchAsync(
        string url,
        string method = "GET",
        IReadOnlyDictionary<string, string>? headers = null,
        byte[]? body = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Channel is not connected; call HandshakeAsync first.");

        var envelope = _builder.Build(_sessionId!, method, url, headers, body);
        var aad = Encoding.UTF8.GetBytes(envelope.RequestId);
        var sealedRequest = _codec.Encode(envelope, _sessionKey!, aad);

        using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/proxy")
        {
            Content = new ByteArrayContent(sealedRequest),
        };
        message.Headers.Authorization = new("Bearer", _token);
        message.Headers.Add("X-Request-Id", envelope.RequestId);

        using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new ProxyRequestException((int)response.StatusCode, envelope.RequestId);

        var sealedResponse = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return _codec.Decode<ResponseEnvelope>(sealedResponse, _sessionKey!, aad);
    }

    /// <summary>Closes the session on the server and clears local state.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_token is not null)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/v1/session/close");
            message.Headers.Authorization = new("Bearer", _token);
            try
            {
                using var response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Best-effort close.
            }
        }

        _sessionKey = null;
        _sessionId = null;
        _token = null;
    }
}

/// <summary>Thrown when the proxy returns a non-success status code.</summary>
public sealed class ProxyRequestException : Exception
{
    public ProxyRequestException(int statusCode, string requestId)
        : base($"Proxy request {requestId} failed with status {statusCode}.")
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    public int StatusCode { get; }

    public string RequestId { get; }
}
