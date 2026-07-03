using System.IO.Compression;
using FluentAssertions;
using ProWeb.Shared.Serialization;
using Xunit;

namespace ProWeb.Shared.Tests;

/// <summary>Decompression-bomb defense for EnvelopeCodec (UT-C-R1-003).</summary>
public class DecompressionLimitTests
{
    [Fact]
    public void BrotliDecompress_WithinLimit_RoundTrips()
    {
        var data = new byte[4096];
        new Random(1).NextBytes(data);
        var compressed = EnvelopeCodec.BrotliCompress(data);

        var restored = EnvelopeCodec.BrotliDecompress(compressed, maxBytes: 1_000_000);
        restored.Should().Equal(data);
    }

    [Fact]
    public void BrotliDecompress_ExceedingLimit_Throws()
    {
        // Highly-compressible payload (all zeros) that inflates well beyond a tiny cap.
        var big = new byte[5_000_000];
        var compressed = EnvelopeCodec.BrotliCompress(big);
        compressed.Length.Should().BeLessThan(big.Length);

        var act = () => EnvelopeCodec.BrotliDecompress(compressed, maxBytes: 64 * 1024);
        act.Should().Throw<InvalidDataException>().WithMessage("*decompression bomb*");
    }
}
