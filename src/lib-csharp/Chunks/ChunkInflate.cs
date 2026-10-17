using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Velopack.Chunks
{
    /// <summary>
    /// Decoding of one fetched chunk (§4.2) and the SHA-256 helpers shared by the chunk path. A deflate chunk is a
    /// self-contained, non-final raw deflate segment ending in a sync flush (<c>00 00 FF FF</c>); appending the
    /// stored final block <c>01 00 00 FF FF</c> turns it into a complete stream for any inflater. Integrity rests on
    /// the plaintext length and SHA-256 only: .NET's <see cref="DeflateStream"/> cannot report trailing garbage or
    /// truncation, so every deviation is reported as "chunk mismatch" by returning null.
    /// </summary>
    internal static class ChunkInflate
    {
        /// <summary>The stored, final, empty deflate block appended to every fetched deflate chunk before inflating.</summary>
        public static readonly byte[] Terminator = { 0x01, 0x00, 0x00, 0xFF, 0xFF };

        /// <summary>The empty final fixed-Huffman block the writer appends after the last chunk of an entry (never fetched).</summary>
        public static readonly byte[] FinalBlock = { 0x03, 0x00 };

        /// <summary>
        /// Decodes one chunk: inflates it (method 8) or copies it (method 0), then checks the plaintext length and SHA-256.
        /// Returns the plaintext, or null when anything deviates (the transient "chunk mismatch" class).
        /// </summary>
        public static byte[]? TryDecodeChunk(byte[] data, int offset, int count, byte method, int expectedLength, Hash256 expectedHash)
        {
            byte[]? plain;
            if (method == ChunkIndex.MethodStored) {
                if (count != expectedLength) return null;
                plain = new byte[count];
                Buffer.BlockCopy(data, offset, plain, 0, count);
            } else if (method == ChunkIndex.MethodDeflate) {
                plain = TryInflate(data, offset, count, expectedLength);
                if (plain == null) return null;
            } else {
                return null;
            }
            return Sha256(plain) == expectedHash ? plain : null;
        }

        /// <summary>
        /// Inflates a non-final deflate segment by appending <see cref="Terminator"/>. Returns the plaintext only when
        /// exactly <paramref name="expectedLength"/> bytes were produced and the stream then ended; otherwise null.
        /// </summary>
        public static byte[]? TryInflate(byte[] data, int offset, int count, int expectedLength)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || count < 0 || data.Length - offset < count) throw new ArgumentOutOfRangeException(nameof(count));
            if (expectedLength <= 0) return null;

            var input = new byte[count + Terminator.Length];
            Buffer.BlockCopy(data, offset, input, 0, count);
            Buffer.BlockCopy(Terminator, 0, input, count, Terminator.Length);

            try {
                using var inflater = new DeflateStream(new MemoryStream(input, false), CompressionMode.Decompress);
                var output = new byte[expectedLength];
                int total = 0;
                while (total < expectedLength) {
                    int n = inflater.Read(output, total, expectedLength - total);
                    if (n <= 0) return null; // truncated or corrupt input
                    total += n;
                }
                var probe = new byte[1];
                if (inflater.Read(probe, 0, 1) != 0) return null; // produced more than the chunk length
                return output;
            } catch (InvalidDataException) {
                return null;
            }
        }

        public static Hash256 Sha256(byte[] data) => Sha256(data, 0, data.Length);

        public static Hash256 Sha256(byte[] data, int offset, int count)
        {
            using var sha = SHA256.Create();
            return Hash256.FromBytes(sha.ComputeHash(data, offset, count));
        }

        /// <summary>Hashes the stream from its current position to its end.</summary>
        public static Hash256 Sha256(Stream stream)
        {
            using var sha = SHA256.Create();
            return Hash256.FromBytes(sha.ComputeHash(stream));
        }

        /// <summary>Hashes exactly <paramref name="count"/> bytes from the stream's current position; throws if the stream ends early.</summary>
        public static Hash256 Sha256(Stream stream, long count)
        {
            using var sha = SHA256.Create();
            var buffer = new byte[64 * 1024];
            long remaining = count;
            while (remaining > 0) {
                int n = stream.Read(buffer, 0, (int) Math.Min(buffer.Length, remaining));
                if (n <= 0) throw new EndOfStreamException("Stream ended before the requested number of bytes was read.");
                sha.TransformBlock(buffer, 0, n, null, 0);
                remaining -= n;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Hash256.FromBytes(sha.Hash!);
        }

        public static Hash256 Sha256File(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            return Sha256(fs);
        }
    }
}
