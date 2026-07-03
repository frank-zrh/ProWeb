using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace ProWeb.Shared.Crypto;

/// <summary>
/// End-to-end application-layer cryptography for ProWeb:
/// X25519 ECDH key agreement (RFC 7748), HKDF-SHA256 key derivation (RFC 5869),
/// and AES-256-GCM AEAD. Sealed payload layout: nonce(12) || ciphertext || tag(16).
/// </summary>
public sealed class CryptoService
{
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int KeySize = 32; // AES-256
    public const int X25519KeySize = 32;

    /// <summary>Generates an X25519 key pair (raw 32-byte public and private keys).</summary>
    public X25519KeyPair GenerateKeyPair()
    {
        var priv = new X25519PrivateKeyParameters(new SecureRandom());
        var pub = priv.GeneratePublicKey();
        return new X25519KeyPair(pub.GetEncoded(), priv.GetEncoded());
    }

    /// <summary>Computes the raw X25519 shared secret from our private key and the peer public key.</summary>
    public byte[] DeriveSharedSecret(byte[] myPrivateKey, byte[] peerPublicKey)
    {
        ArgumentNullException.ThrowIfNull(myPrivateKey);
        ArgumentNullException.ThrowIfNull(peerPublicKey);
        if (myPrivateKey.Length != X25519KeySize) throw new ArgumentException("Invalid private key size.", nameof(myPrivateKey));
        if (peerPublicKey.Length != X25519KeySize) throw new ArgumentException("Invalid public key size.", nameof(peerPublicKey));

        var priv = new X25519PrivateKeyParameters(myPrivateKey, 0);
        var pub = new X25519PublicKeyParameters(peerPublicKey, 0);
        var agreement = new X25519Agreement();
        agreement.Init(priv);
        var secret = new byte[agreement.AgreementSize];
        agreement.CalculateAgreement(pub, secret, 0);
        return secret;
    }

    /// <summary>Derives a 32-byte AES-256 session key from the shared secret using HKDF-SHA256.</summary>
    public byte[] DeriveSessionKey(byte[] sharedSecret, byte[] salt, string info)
    {
        ArgumentNullException.ThrowIfNull(sharedSecret);
        var infoBytes = System.Text.Encoding.UTF8.GetBytes(info ?? string.Empty);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, KeySize, salt, infoBytes);
    }

    /// <summary>Seals plaintext with AES-256-GCM. Output layout: nonce || ciphertext || tag.</summary>
    public byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        ValidateKey(key);
        var nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plaintext, cipher, tag, associatedData);

        var output = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, output, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, output, NonceSize + cipher.Length, TagSize);
        return output;
    }

    /// <summary>Opens a sealed payload (nonce || ciphertext || tag). Throws <see cref="CryptographicException"/> on tamper.</summary>
    public byte[] Open(byte[] key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associatedData)
    {
        ValidateKey(key);
        if (sealedData.Length < NonceSize + TagSize)
            throw new CryptographicException("Sealed payload too short.");
        var nonce = sealedData.Slice(0, NonceSize);
        var cipherLen = sealedData.Length - NonceSize - TagSize;
        var cipher = sealedData.Slice(NonceSize, cipherLen);
        var tag = sealedData.Slice(NonceSize + cipherLen, TagSize);
        var plain = new byte[cipherLen];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Decrypt(nonce, cipher, tag, plain, associatedData);
        return plain;
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize) throw new ArgumentException($"Key must be {KeySize} bytes.", nameof(key));
    }
}

/// <summary>An X25519 key pair with raw 32-byte public key and private scalar.</summary>
public sealed record X25519KeyPair(byte[] PublicKey, byte[] PrivateKey);
