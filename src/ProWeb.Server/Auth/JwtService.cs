using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ProWeb.Server.Config;

namespace ProWeb.Server.Auth;

/// <summary>Issues and validates HMAC-SHA256 JWTs binding a token to a session and device.</summary>
public sealed class JwtService
{
    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtService(ProWebOptions options)
    {
        _options = options.Jwt;
        _key = new SymmetricSecurityKey(DeriveKey(_options.SigningKey));
    }

    public (string Token, long ExpiresAtUnixMs) Issue(string sessionId, string deviceId)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddSeconds(_options.TtlSeconds);
        var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, sessionId),
            new Claim("did", deviceId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);
        return (_handler.WriteToken(token), expires.ToUnixTimeMilliseconds());
    }

    /// <summary>Validates a token and returns the session id (sub claim), or null if invalid.</summary>
    public string? ValidateAndGetSessionId(string token) => ValidateAndGetClaims(token)?.SessionId;

    /// <summary>Validates a token and returns its (SessionId, DeviceId) claims, or null if invalid.</summary>
    public (string SessionId, string DeviceId)? ValidateAndGetClaims(string token)
    {
        try
        {
            var principal = _handler.ValidateToken(token, ValidationParameters, out _);
            var sessionId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (sessionId is null)
                return null;
            var deviceId = principal.FindFirst("did")?.Value ?? string.Empty;
            return (sessionId, deviceId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _key,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(5),
    };

    private static byte[] DeriveKey(string signingKey)
    {
        var bytes = Encoding.UTF8.GetBytes(signingKey);
        // HMAC-SHA256 requires >= 256-bit key material; pad short keys deterministically.
        if (bytes.Length >= 32) return bytes;
        return System.Security.Cryptography.SHA256.HashData(bytes);
    }
}
