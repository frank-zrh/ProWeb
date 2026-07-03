using System.Security.Cryptography;
using FluentAssertions;
using ProWeb.Shared.Crypto;
using ProWeb.Shared.Protocol;
using ProWeb.Shared.Serialization;
using Xunit;

namespace ProWeb.Shared.Tests;

public class EnvelopeCodecTests
{
    private readonly EnvelopeCodec _codec = new(new CryptoService());
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Request_RoundTrips()
    {
        var env = new RequestEnvelope
        {
            SessionId = "s1",
            RequestId = "r1",
            TimestampUnixMs = 123,
            Method = "POST",
            TargetUrl = "https://example.com/api",
            Headers = new() { ["Accept"] = "text/html" },
            Body = new byte[] { 1, 2, 3 },
        };

        var encoded = _codec.Encode(env, _key, "r1"u8);
        var decoded = _codec.Decode<RequestEnvelope>(encoded, _key, "r1"u8);

        decoded.SessionId.Should().Be("s1");
        decoded.Method.Should().Be("POST");
        decoded.TargetUrl.Should().Be("https://example.com/api");
        decoded.Headers["Accept"].Should().Be("text/html");
        decoded.Body.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Response_RoundTrips()
    {
        var env = new ResponseEnvelope
        {
            RequestId = "r1",
            StatusCode = 200,
            ContentType = "text/html",
            FinalUrl = "https://example.com/",
            ServerElapsedMs = 42,
            Body = new byte[] { 9, 8, 7 },
        };

        var decoded = _codec.Decode<ResponseEnvelope>(_codec.Encode(env, _key, "r1"u8), _key, "r1"u8);

        decoded.StatusCode.Should().Be(200);
        decoded.ServerElapsedMs.Should().Be(42);
        decoded.Body.Should().Equal(9, 8, 7);
    }

    [Fact]
    public void LargePayload_IsBrotliCompressed_AndRoundTrips()
    {
        var big = new string('A', 5000);
        var env = new ResponseEnvelope { RequestId = "r", StatusCode = 200, Body = System.Text.Encoding.UTF8.GetBytes(big) };

        EnvelopeCodec.WouldCompress(big.Length).Should().BeTrue();
        var decoded = _codec.Decode<ResponseEnvelope>(_codec.Encode(env, _key, "r"u8), _key, "r"u8);
        System.Text.Encoding.UTF8.GetString(decoded.Body!).Should().Be(big);
    }

    [Fact]
    public void SmallPayload_IsNotCompressed()
    {
        EnvelopeCodec.WouldCompress(100).Should().BeFalse();
    }

    [Fact]
    public void Decode_WithWrongAad_Throws()
    {
        var env = new RequestEnvelope { RequestId = "r1" };
        var encoded = _codec.Encode(env, _key, "r1"u8);
        var act = () => _codec.Decode<RequestEnvelope>(encoded, _key, "r2"u8);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void BrotliRoundTrip_Helper_Works()
    {
        var data = System.Text.Encoding.UTF8.GetBytes(new string('Z', 3000));
        var compressed = EnvelopeCodec.BrotliCompress(data);
        compressed.Length.Should().BeLessThan(data.Length);
        EnvelopeCodec.BrotliDecompress(compressed).Should().Equal(data);
    }

    // UT-X-R3-901: images are binary sub-resources served over the encrypted channel. Prove that
    // arbitrary image bytes (incl. all 256 byte values and a compressible large PNG-like payload)
    // survive the Envelope serialize → seal → open → deserialize path byte-for-byte, and that the
    // image MIME is preserved — so a correctly-fetched image cannot be corrupted by the transport.
    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    [InlineData("image/svg+xml")]
    [InlineData("image/x-icon")]
    public void ImageResponse_PreservesBytesAndMime_AcrossEnvelopeRoundTrip(string mime)
    {
        // 4 KiB body spanning every byte value → forces Brotli compression and exercises binary
        // integrity (a text/UTF-8 mishandling would mangle bytes like 0x00/0xFF).
        var body = new byte[4096];
        for (var i = 0; i < body.Length; i++)
            body[i] = (byte)(i % 256);

        var env = new ResponseEnvelope
        {
            RequestId = "img-1",
            StatusCode = 200,
            ContentType = mime,
            FinalUrl = "https://example.com/logo",
            Body = body,
        };

        var decoded = _codec.Decode<ResponseEnvelope>(_codec.Encode(env, _key, "img-1"u8), _key, "img-1"u8);

        decoded.ContentType.Should().Be(mime);
        decoded.Body.Should().Equal(body);
    }
}
