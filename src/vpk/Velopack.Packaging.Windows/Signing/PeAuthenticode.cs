#nullable enable
// The PE Authenticode digest / WIN_CERTIFICATE handling in this file is adapted from PowerShell-OpenAuthenticode
// (https://github.com/jborean93/PowerShell-OpenAuthenticode, MIT License, Copyright (c) 2023 Jordan Borean), with the
// digest walk, padding and checksum behaviour aligned to signtool / osslsigncode / jsign.

using System.Buffers;
using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Velopack.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Low level PE image operations needed for Authenticode: computing the image digest, embedding a PKCS#7 signature
/// as a WIN_CERTIFICATE, and recomputing the optional header CheckSum.
/// </summary>
public static class PeAuthenticode
{
    public const ushort WinCertificateRevision2 = 0x0200;
    public const ushort WinCertificateTypePkcsSignedData = 0x0002;

    private const int BufferSize = 1 << 20;

    /// <summary>
    /// Computes the Authenticode digest of a PE image: the whole file in order, excluding the CheckSum field, the
    /// security directory entry and any existing certificate table, followed by the zero padding that aligns the
    /// certificate table to 8 bytes. This is the same linear walk signtool, osslsigncode and jsign use, so overlay
    /// data (e.g. a setup bundle or a single-file host payload) is covered by the signature.
    /// </summary>
    public static byte[] ComputeDigest(Stream stream, PeAuthenticodeLayout layout, HashAlgorithmName hashAlgorithm)
    {
        using var hash = IncrementalHash.CreateHash(hashAlgorithm);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try {
            HashRange(stream, hash, buffer, 0, layout.CheckSumOffset);
            HashRange(stream, hash, buffer, layout.CheckSumOffset + 4, layout.SecurityDirectoryOffset);
            HashRange(stream, hash, buffer, layout.SecurityDirectoryOffset + 8, layout.ContentEnd);
            int pad = GetAlignmentPadding(layout.ContentEnd);
            if (pad > 0) {
                hash.AppendData(new byte[pad]);
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Writes <paramref name="pkcs7Der"/> as the image's only WIN_CERTIFICATE (replacing any existing certificate
    /// table), points the security directory at it and recomputes the PE CheckSum.
    /// </summary>
    public static void EmbedSignature(FileStream stream, PeAuthenticodeLayout layout, ReadOnlySpan<byte> pkcs7Der)
    {
        long contentEnd = layout.ContentEnd;
        int pad = GetAlignmentPadding(contentEnd);
        long certificateOffset = contentEnd + pad;
        int derPad = GetAlignmentPadding(pkcs7Der.Length);
        long winCertificateLength = 8L + pkcs7Der.Length + derPad;

        if (certificateOffset + winCertificateLength > uint.MaxValue) {
            throw new UserInfoException($"'{stream.Name}' would be larger than 4 GiB after signing, which PE images do not support.");
        }

        stream.SetLength(contentEnd);
        stream.Position = contentEnd;
        stream.Write(new byte[pad]);

        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint) winCertificateLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4), WinCertificateRevision2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6), WinCertificateTypePkcsSignedData);
        stream.Write(header);
        stream.Write(pkcs7Der);
        stream.Write(new byte[derPad]);

        Span<byte> directory = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(directory, (uint) certificateOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(directory.Slice(4), (uint) winCertificateLength);
        stream.Position = layout.SecurityDirectoryOffset;
        stream.Write(directory);
        stream.Flush();

        uint checksum = ComputeChecksum(stream, layout.CheckSumOffset);
        Span<byte> checksumBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(checksumBytes, checksum);
        stream.Position = layout.CheckSumOffset;
        stream.Write(checksumBytes);
        stream.Flush();
    }

    /// <summary>
    /// Computes the PE image CheckSum with the same algorithm as imagehlp's CheckSumMappedFile: a 16-bit one's
    /// complement style sum of the file (with the CheckSum field treated as zero) plus the file length.
    /// </summary>
    public static uint ComputeChecksum(Stream stream, long checkSumOffset)
    {
        long fileLength = stream.Length;
        uint sum = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try {
            stream.Position = 0;
            long position = 0;
            while (position < fileLength) {
                // always fill whole (even sized) chunks so that 16-bit words never straddle two reads
                int count = (int) Math.Min(BufferSize, fileLength - position);
                stream.ReadExactly(buffer, 0, count);
                var chunk = buffer.AsSpan(0, count);

                for (long i = Math.Max(checkSumOffset, position); i < Math.Min(checkSumOffset + 4, position + count); i++) {
                    chunk[(int) (i - position)] = 0;
                }

                var words = MemoryMarshal.Cast<byte, ushort>(chunk.Slice(0, count & ~1));
                foreach (ushort word in words) {
                    sum += BitConverter.IsLittleEndian ? word : BinaryPrimitives.ReverseEndianness(word);
                    sum = (sum & 0xFFFF) + (sum >> 16);
                }

                if ((count & 1) != 0) {
                    sum += chunk[count - 1];
                    sum = (sum & 0xFFFF) + (sum >> 16);
                }

                position += count;
            }
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        sum = (sum & 0xFFFF) + (sum >> 16);
        sum &= 0xFFFF;
        return unchecked(sum + (uint) fileLength);
    }

    /// <summary>
    /// Returns true if <paramref name="path"/> is a PE image that already contains an Authenticode certificate table.
    /// Never throws: files that are missing, unreadable or not PE images return false.
    /// </summary>
    public static bool HasCertificateTable(string path)
    {
        try {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var layout = PeAuthenticodeLayout.TryReadHeaders(stream, out _);
            if (layout == null || !layout.HasCertificateTable) return false;
            if (layout.CertificateTableOffset < layout.SecurityDirectoryOffset + 8) return false;
            if ((long) layout.CertificateTableOffset + layout.CertificateTableSize > layout.FileLength) return false;
            if (layout.CertificateTableSize < 8) return false;

            Span<byte> header = stackalloc byte[8];
            stream.Position = layout.CertificateTableOffset;
            stream.ReadExactly(header);
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6));
            return length > 8 && length <= layout.CertificateTableSize && type == WinCertificateTypePkcsSignedData;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Reads the PKCS#7 SignedData of the first WIN_CERTIFICATE in the certificate table, without its trailing
    /// alignment padding. Returns null if the image has no certificate table or the first entry is not PKCS#7.
    /// </summary>
    public static byte[]? ReadEmbeddedPkcs7(Stream stream, PeAuthenticodeLayout layout)
    {
        if (!layout.HasCertificateTable || layout.CertificateTableSize < 8) return null;
        if ((long) layout.CertificateTableOffset + layout.CertificateTableSize > layout.FileLength) return null;

        var table = new byte[layout.CertificateTableSize];
        stream.Position = layout.CertificateTableOffset;
        stream.ReadExactly(table);

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(table);
        ushort revision = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(4));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(6));
        if (length <= 8 || length > table.Length || type != WinCertificateTypePkcsSignedData) return null;
        if (revision != WinCertificateRevision2 && revision != 0x0100) return null;

        var blob = table.AsMemory(8, (int) length - 8);
        if (!AsnDecoder.TryReadEncodedValue(blob.Span, AsnEncodingRules.BER, out _, out _, out _, out int consumed)) {
            return null;
        }

        return blob.Slice(0, consumed).ToArray();
    }

    /// <summary>Number of zero bytes needed to align <paramref name="offset"/> to 8 bytes.</summary>
    public static int GetAlignmentPadding(long offset) => (int) ((8 - offset % 8) % 8);

    private static void HashRange(Stream stream, IncrementalHash hash, byte[] buffer, long start, long end)
    {
        stream.Position = start;
        long remaining = end - start;
        while (remaining > 0) {
            int count = (int) Math.Min(buffer.Length, remaining);
            stream.ReadExactly(buffer, 0, count);
            hash.AppendData(buffer, 0, count);
            remaining -= count;
        }
    }
}
