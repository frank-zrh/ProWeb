using Microsoft.Extensions.Options;
using ProWeb.Server.Auth;
using ProWeb.Server.Config;
using ProWeb.Server.Storage;
using ProWeb.Shared.Crypto;

namespace ProWeb.Server.Endpoints;

/// <summary>Request body for POST /v1/handshake.</summary>
public sealed class HandshakeRequest
{
    public string ClientPublicKey { get; set; } = string.Empty;

    public string DeviceId { get; set; } = string.Empty;

    public string ClientVersion { get; set; } = "1.0.0";
}

/// <summary>Response body for POST /v1/handshake.</summary>
public sealed class HandshakeResponse
{
    public string ServerPublicKey { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public long ExpiresAtUnixMs { get; set; }
}

/// <summary>
/// Establishes a session: performs X25519 ECDH, derives the AES-256 session key via HKDF,
/// persists the (protected) session, and issues a JWT. Pure orchestration, independently testable.
/// </summary>
public sealed class HandshakeService
{
    public const string HkdfInfo = "proweb-v1";

    private readonly CryptoService _crypto;
    private readonly JwtService _jwt;
    private readonly SessionRepository _sessions;
    private readonly SessionKeyProtector _protector;
    private readonly Config.SessionOptions _sessionOptions;

    public HandshakeService(
        CryptoService crypto,
        JwtService jwt,
        SessionRepository sessions,
        SessionKeyProtector protector,
        IOptions<ProWebOptions> options)
    {
        _crypto = crypto;
        _jwt = jwt;
        _sessions = sessions;
        _protector = protector;
        _sessionOptions = options.Value.Session;
    }

    public HandshakeResponse Establish(HandshakeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientPublicKey))
            throw new ArgumentException("clientPublicKey is required.");

        var clientPublicKey = Convert.FromBase64String(request.ClientPublicKey);
        if (clientPublicKey.Length != CryptoService.X25519KeySize)
            throw new ArgumentException("clientPublicKey must be 32 bytes.");

        var serverKeyPair = _crypto.GenerateKeyPair();
        var sharedSecret = _crypto.DeriveSharedSecret(serverKeyPair.PrivateKey, clientPublicKey);

        var sessionId = Guid.NewGuid().ToString("N");
        var salt = System.Text.Encoding.UTF8.GetBytes(sessionId);
        var sessionKey = _crypto.DeriveSessionKey(sharedSecret, salt, HkdfInfo);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var expires = now + (_sessionOptions.TtlSeconds * 1000L);

        // Normalize DeviceId ONCE so the value stored on the session and the value bound into the
        // JWT 'did' claim are identical; the auth middleware relies on this to enforce device binding.
        var deviceId = NormalizeDeviceId(request.DeviceId);

        _sessions.Insert(new SessionRecord
        {
            SessionId = sessionId,
            DeviceId = deviceId,
            SessionKeyProtected = _protector.Protect(sessionKey),
            ClientPublicKey = clientPublicKey,
            CreatedAtUnixMs = now,
            ExpiresAtUnixMs = expires,
            Revoked = false,
        });

        var (token, tokenExpires) = _jwt.Issue(sessionId, deviceId);

        return new HandshakeResponse
        {
            ServerPublicKey = Convert.ToBase64String(serverKeyPair.PublicKey),
            SessionId = sessionId,
            Token = token,
            ExpiresAtUnixMs = Math.Min(expires, tokenExpires),
        };
    }

    /// <summary>Canonical device-id normalization shared by session storage and JWT issuance.</summary>
    public static string NormalizeDeviceId(string? deviceId) =>
        string.IsNullOrWhiteSpace(deviceId) ? "unknown" : deviceId.Trim();
}
