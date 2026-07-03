using System.Runtime.Versioning;
using System.Security.Cryptography;
using ProWeb.Server.Config;

namespace ProWeb.Server.Auth;

/// <summary>
/// Protects session keys at rest. On Windows the DPAPI (scope configurable, default per-user) is
/// used; on other platforms (or when DPAPI is unavailable) it falls back to AES-256-GCM using a
/// configured master key. The output is self-describing via a one-byte scheme prefix.
/// </summary>
public sealed class SessionKeyProtector
{
    private const byte SchemeDpapi = 0x01;
    private const byte SchemeAesGcm = 0x02;

    private readonly byte[] _masterKey;
    private readonly DataProtectionScope _dpapiScope;

    public SessionKeyProtector(ProWebOptions options)
    {
        _masterKey = DeriveMasterKey(options.Session.MasterKey);
        _dpapiScope = string.Equals(options.Session.DpapiScope, "LocalMachine", StringComparison.OrdinalIgnoreCase)
            ? DataProtectionScope.LocalMachine
            : DataProtectionScope.CurrentUser;
    }

    public byte[] Protect(byte[] plaintextKey)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return Prefix(SchemeDpapi, ProtectDpapi(plaintextKey));
            }
            catch (PlatformNotSupportedException)
            {
                // Fall through to AES.
            }
            catch (CryptographicException)
            {
                // Fall through to AES.
            }
        }

        return Prefix(SchemeAesGcm, ProtectAes(plaintextKey));
    }

    public byte[] Unprotect(byte[] protectedKey)
    {
        if (protectedKey.Length < 1) throw new CryptographicException("Empty protected key.");
        var scheme = protectedKey[0];
        var body = protectedKey[1..];
        return scheme switch
        {
            SchemeDpapi when OperatingSystem.IsWindows() => UnprotectDpapi(body),
            SchemeAesGcm => UnprotectAes(body),
            _ => throw new CryptographicException($"Unsupported protection scheme {scheme}."),
        };
    }

    [SupportedOSPlatform("windows")]
    private byte[] ProtectDpapi(byte[] data) =>
        ProtectedData.Protect(data, null, _dpapiScope);

    [SupportedOSPlatform("windows")]
    private byte[] UnprotectDpapi(byte[] data) =>
        ProtectedData.Unprotect(data, null, _dpapiScope);

    private byte[] ProtectAes(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(_masterKey, 16);
        gcm.Encrypt(nonce, data, cipher, tag);
        return Concat(nonce, cipher, tag);
    }

    private byte[] UnprotectAes(byte[] data)
    {
        if (data.Length < 12 + 16) throw new CryptographicException("Protected key too short.");
        var nonce = data.AsSpan(0, 12);
        var cipherLen = data.Length - 12 - 16;
        var cipher = data.AsSpan(12, cipherLen);
        var tag = data.AsSpan(12 + cipherLen, 16);
        var plain = new byte[cipherLen];
        using var gcm = new AesGcm(_masterKey, 16);
        gcm.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static byte[] DeriveMasterKey(string configured)
    {
        // Accept a base64 32-byte key, otherwise derive a stable 32-byte key via SHA-256.
        try
        {
            var raw = Convert.FromBase64String(configured);
            if (raw.Length == 32) return raw;
        }
        catch (FormatException)
        {
            // Not base64 — derive below.
        }

        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(configured));
    }

    private static byte[] Prefix(byte scheme, byte[] body)
    {
        var result = new byte[body.Length + 1];
        result[0] = scheme;
        Buffer.BlockCopy(body, 0, result, 1, body.Length);
        return result;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }

        return result;
    }
}
