using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ProWeb.Shared.Crypto;
using Xunit;

namespace ProWeb.Shared.Tests;

public class CryptoServiceTests
{
    private readonly CryptoService _crypto = new();

    [Fact]
    public void Ecdh_BothParties_DeriveSameSharedSecret()
    {
        var alice = _crypto.GenerateKeyPair();
        var bob = _crypto.GenerateKeyPair();

        var aliceSecret = _crypto.DeriveSharedSecret(alice.PrivateKey, bob.PublicKey);
        var bobSecret = _crypto.DeriveSharedSecret(bob.PrivateKey, alice.PublicKey);

        aliceSecret.Should().Equal(bobSecret);
        aliceSecret.Length.Should().Be(32);
    }

    [Fact]
    public void Hkdf_IsDeterministic_ForSameInputs()
    {
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var salt = Encoding.UTF8.GetBytes("session-123");

        var k1 = _crypto.DeriveSessionKey(secret, salt, "proweb-v1");
        var k2 = _crypto.DeriveSessionKey(secret, salt, "proweb-v1");

        k1.Should().Equal(k2);
        k1.Length.Should().Be(32);
    }

    [Fact]
    public void Hkdf_DifferentInfo_ProducesDifferentKey()
    {
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var salt = Encoding.UTF8.GetBytes("s");

        _crypto.DeriveSessionKey(secret, salt, "a")
            .Should().NotEqual(_crypto.DeriveSessionKey(secret, salt, "b"));
    }

    [Fact]
    public void SealOpen_RoundTrips_WithCorrectKeyAndAad()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("hello secure world");
        var aad = Encoding.UTF8.GetBytes("req-1");

        var sealedData = _crypto.Seal(key, plaintext, aad);
        var opened = _crypto.Open(key, sealedData, aad);

        opened.Should().Equal(plaintext);
        sealedData.Length.Should().Be(CryptoService.NonceSize + plaintext.Length + CryptoService.TagSize);
    }

    [Fact]
    public void Open_WithWrongKey_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var wrong = RandomNumberGenerator.GetBytes(32);
        var sealedData = _crypto.Seal(key, "data"u8, "aad"u8);

        var act = () => _crypto.Open(wrong, sealedData, "aad"u8);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Open_WithTamperedTag_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var sealedData = _crypto.Seal(key, "data"u8, "aad"u8);
        sealedData[^1] ^= 0xFF; // flip a tag bit

        var act = () => _crypto.Open(key, sealedData, "aad"u8);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Open_WithWrongAad_Throws()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var sealedData = _crypto.Seal(key, "data"u8, "aad-1"u8);

        var act = () => _crypto.Open(key, sealedData, "aad-2"u8);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Seal_WithBadKeySize_Throws()
    {
        var act = () => _crypto.Seal(new byte[16], "x"u8, default);
        act.Should().Throw<ArgumentException>();
    }
}
