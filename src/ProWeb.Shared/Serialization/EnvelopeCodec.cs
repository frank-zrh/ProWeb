using System.IO.Compression;
using MessagePack;
using ProWeb.Shared.Crypto;

namespace ProWeb.Shared.Serialization;

/// <summary>
/// Encodes and decodes protocol envelopes for the wire. Pipeline:
/// object -&gt; MessagePack -&gt; (Brotli when &gt; threshold) -&gt; AES-256-GCM seal.
/// The first output byte is a flags byte (bit0 = Brotli-compressed) which is
/// included in the AEAD plaintext, so tampering with it fails decryption.
/// </summary>
public sealed class EnvelopeCodec
{
    public const int CompressionThresholdBytes = 1024;

    /// <summary>Default cap on decompressed payload size to defend against decompression bombs.</summary>
    public const long DefaultMaxDecompressedBytes = 52_428_800; // 50 MiB

    private const byte FlagBrotli = 0x01;

    private readonly CryptoService _crypto;
    private readonly long _maxDecompressedBytes;

    public EnvelopeCodec(CryptoService crypto, long maxDecompressedBytes = DefaultMaxDecompressedBytes)
    {
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        if (maxDecompressedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxDecompressedBytes));
        _maxDecompressedBytes = maxDecompressedBytes;
    }

    /// <summary>Serializes and seals an object. <paramref name="associatedData"/> binds context (e.g. RequestId).</summary>
    public byte[] Encode<T>(T value, byte[] key, ReadOnlySpan<byte> associatedData)
    {
        var raw = MessagePackSerializer.Serialize(value);
        byte flags = 0;
        byte[] payload = raw;
        if (raw.Length > CompressionThresholdBytes)
        {
            payload = BrotliCompress(raw);
            flags |= FlagBrotli;
        }

        var framed = new byte[payload.Length + 1];
        framed[0] = flags;
        Buffer.BlockCopy(payload, 0, framed, 1, payload.Length);
        return _crypto.Seal(key, framed, associatedData);
    }

    /// <summary>Opens and deserializes a sealed payload produced by <see cref="Encode"/>.</summary>
    public T Decode<T>(byte[] sealedData, byte[] key, ReadOnlySpan<byte> associatedData)
    {
        var framed = _crypto.Open(key, sealedData, associatedData);
        if (framed.Length < 1) throw new InvalidDataException("Empty framed payload.");
        var flags = framed[0];
        var payload = new byte[framed.Length - 1];
        Buffer.BlockCopy(framed, 1, payload, 0, payload.Length);
        if ((flags & FlagBrotli) != 0)
            payload = BrotliDecompress(payload, _maxDecompressedBytes);
        return MessagePackSerializer.Deserialize<T>(payload);
    }

    /// <summary>Indicates whether a raw MessagePack buffer of this length would be Brotli-compressed.</summary>
    public static bool WouldCompress(int rawLength) => rawLength > CompressionThresholdBytes;

    internal static byte[] BrotliCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    internal static byte[] BrotliDecompress(byte[] data, long maxBytes = DefaultMaxDecompressedBytes)
    {
        using var input = new MemoryStream(data);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        // Copy with a hard upper bound so a small highly-compressed payload cannot inflate to GBs.
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = brotli.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    $"Decompressed payload exceeds the {maxBytes}-byte limit (possible decompression bomb).");
            }
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
