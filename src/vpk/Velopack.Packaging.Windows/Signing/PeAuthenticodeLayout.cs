#nullable enable
using System.Buffers.Binary;
using Velopack.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// The offsets inside a PE image that matter for Authenticode: the optional header CheckSum field, the security
/// (certificate table) data directory entry, and the certificate table itself.
/// </summary>
public sealed class PeAuthenticodeLayout
{
    private const int SecurityDirectoryIndex = 4;

    /// <summary>Total length of the file when the layout was read.</summary>
    public long FileLength { get; }

    /// <summary>True for PE32+ (64-bit) images, false for PE32.</summary>
    public bool IsPe32Plus { get; }

    /// <summary>File offset of the 4-byte optional header CheckSum field.</summary>
    public long CheckSumOffset { get; }

    /// <summary>File offset of the 8-byte security data directory entry (certificate table offset + size).</summary>
    public long SecurityDirectoryOffset { get; }

    /// <summary>File offset of the certificate table (the security directory stores a file offset, not an RVA).</summary>
    public uint CertificateTableOffset { get; }

    /// <summary>Size in bytes of the certificate table.</summary>
    public uint CertificateTableSize { get; }

    /// <summary>
    /// End of the headers and section raw data (max of SizeOfHeaders and PointerToRawData + SizeOfRawData), clamped to
    /// the file length. A certificate table can never start before this offset.
    /// </summary>
    public long EndOfSectionData { get; }

    /// <summary>True when the security directory points at a certificate table.</summary>
    public bool HasCertificateTable => CertificateTableOffset != 0 && CertificateTableSize != 0;

    /// <summary>End of the content covered by the Authenticode digest (everything before the certificate table).</summary>
    public long ContentEnd => HasCertificateTable ? CertificateTableOffset : FileLength;

    private PeAuthenticodeLayout(long fileLength, bool isPe32Plus, long checkSumOffset, long securityDirectoryOffset,
        uint certificateTableOffset, uint certificateTableSize, long endOfSectionData)
    {
        FileLength = fileLength;
        IsPe32Plus = isPe32Plus;
        CheckSumOffset = checkSumOffset;
        SecurityDirectoryOffset = securityDirectoryOffset;
        CertificateTableOffset = certificateTableOffset;
        CertificateTableSize = certificateTableSize;
        EndOfSectionData = endOfSectionData;
    }

    /// <summary>
    /// Reads and validates the PE headers of <paramref name="stream"/>. Throws a <see cref="UserInfoException"/> if the
    /// file is not a PE image that can be Authenticode signed, if an existing certificate table is not at the end of
    /// the file (replacing it would destroy whatever data follows it), or if the certificate table is corrupted (a
    /// bogus directory entry pointing into the image would otherwise make signing truncate real image bytes).
    /// </summary>
    public static PeAuthenticodeLayout Read(Stream stream, string displayName)
    {
        var layout = TryReadHeaders(stream, out var invalidReason);
        if (layout == null) {
            throw new UserInfoException($"'{displayName}' is not a valid PE image ({invalidReason}), cannot Authenticode sign it.");
        }

        if (layout.HasCertificateTable) {
            long tableEnd = (long) layout.CertificateTableOffset + layout.CertificateTableSize;
            if (layout.CertificateTableOffset < layout.SecurityDirectoryOffset + 8 || tableEnd != layout.FileLength) {
                throw new UserInfoException(
                    $"'{displayName}' has an Authenticode certificate table that is not at the end of the file, refusing to sign it.");
            }

            if (layout.CertificateTableOffset < layout.EndOfSectionData || !IsCertificateTableWellFormed(stream, layout)) {
                throw new UserInfoException($"'{displayName}' has a corrupted Authenticode certificate table, refusing to sign it.");
            }
        }

        return layout;
    }

    /// <summary>
    /// Walks the WIN_CERTIFICATE entries like osslsigncode's pe_check_file: every dwLength must be at least 8 and lie
    /// within the table, and the 8-byte rounded lengths must add up to the table size (the final entry's padding may
    /// be missing, as some older signers wrote it that way).
    /// </summary>
    private static bool IsCertificateTableWellFormed(Stream stream, PeAuthenticodeLayout layout)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        long tableSize = layout.CertificateTableSize;
        long position = 0;
        while (position < tableSize) {
            if (tableSize - position < 8) return false;
            ReadAt(stream, layout.CertificateTableOffset + position, lengthBytes);
            uint entryLength = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
            if (entryLength < 8 || entryLength > tableSize - position) return false;
            position += entryLength + (8 - entryLength % 8) % 8;
        }

        return true;
    }

    /// <summary>
    /// Reads the PE headers without validating where the certificate table is. Returns null (and a reason) if the
    /// headers are not a valid PE32/PE32+ image with a security data directory entry.
    /// </summary>
    internal static PeAuthenticodeLayout? TryReadHeaders(Stream stream, out string? invalidReason)
    {
        long length = stream.Length;
        if (length < 64) {
            invalidReason = "file is too small";
            return null;
        }

        if (length > uint.MaxValue) {
            invalidReason = "file is larger than 4 GiB";
            return null;
        }

        Span<byte> dosHeader = stackalloc byte[64];
        ReadAt(stream, 0, dosHeader);
        if (dosHeader[0] != 'M' || dosHeader[1] != 'Z') {
            invalidReason = "missing MZ signature";
            return null;
        }

        long peOffset = BinaryPrimitives.ReadUInt32LittleEndian(dosHeader.Slice(0x3C));
        if (peOffset < 4 || peOffset + 24 > length) {
            invalidReason = "PE header offset is out of range";
            return null;
        }

        Span<byte> coffHeader = stackalloc byte[24];
        ReadAt(stream, peOffset, coffHeader);
        if (coffHeader[0] != 'P' || coffHeader[1] != 'E' || coffHeader[2] != 0 || coffHeader[3] != 0) {
            invalidReason = "missing PE signature";
            return null;
        }

        int numberOfSections = BinaryPrimitives.ReadUInt16LittleEndian(coffHeader.Slice(6));
        int sizeOfOptionalHeader = BinaryPrimitives.ReadUInt16LittleEndian(coffHeader.Slice(20));
        long optionalHeaderOffset = peOffset + 24;
        if (sizeOfOptionalHeader < 2 || optionalHeaderOffset + sizeOfOptionalHeader > length) {
            invalidReason = "optional header is truncated";
            return null;
        }

        var optionalHeader = new byte[sizeOfOptionalHeader];
        ReadAt(stream, optionalHeaderOffset, optionalHeader);

        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(optionalHeader);
        bool isPe32Plus;
        int numberOfRvaAndSizesOffset, dataDirectoryOffset;
        if (magic == 0x10B) {
            isPe32Plus = false;
            numberOfRvaAndSizesOffset = 92;
            dataDirectoryOffset = 96;
        } else if (magic == 0x20B) {
            isPe32Plus = true;
            numberOfRvaAndSizesOffset = 108;
            dataDirectoryOffset = 112;
        } else {
            invalidReason = $"unknown optional header magic 0x{magic:X4}";
            return null;
        }

        int securityDirectoryOffset = dataDirectoryOffset + SecurityDirectoryIndex * 8;
        if (securityDirectoryOffset + 8 > sizeOfOptionalHeader) {
            invalidReason = "optional header is too small to contain a security directory";
            return null;
        }

        uint numberOfRvaAndSizes = BinaryPrimitives.ReadUInt32LittleEndian(optionalHeader.AsSpan(numberOfRvaAndSizesOffset));
        if (numberOfRvaAndSizes <= SecurityDirectoryIndex) {
            invalidReason = $"NumberOfRvaAndSizes is {numberOfRvaAndSizes}, so there is no security directory";
            return null;
        }

        uint certificateTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(optionalHeader.AsSpan(securityDirectoryOffset));
        uint certificateTableSize = BinaryPrimitives.ReadUInt32LittleEndian(optionalHeader.AsSpan(securityDirectoryOffset + 4));
        if (certificateTableOffset == 0 || certificateTableSize == 0) {
            // a half-populated entry describes no table; it gets overwritten when the signature is embedded.
            certificateTableOffset = 0;
            certificateTableSize = 0;
        }

        long sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;
        if (sectionTableOffset + numberOfSections * 40L > length) {
            invalidReason = "section table is truncated";
            return null;
        }

        long endOfSectionData = sizeOfOptionalHeader >= 64 ? BinaryPrimitives.ReadUInt32LittleEndian(optionalHeader.AsSpan(60)) : 0;
        if (numberOfSections > 0) {
            var sectionTable = new byte[numberOfSections * 40];
            ReadAt(stream, sectionTableOffset, sectionTable);
            for (int i = 0; i < numberOfSections; i++) {
                var section = sectionTable.AsSpan(i * 40, 40);
                uint sizeOfRawData = BinaryPrimitives.ReadUInt32LittleEndian(section.Slice(16));
                uint pointerToRawData = BinaryPrimitives.ReadUInt32LittleEndian(section.Slice(20));
                if (sizeOfRawData != 0 && pointerToRawData != 0) {
                    endOfSectionData = Math.Max(endOfSectionData, (long) pointerToRawData + sizeOfRawData);
                }
            }
        }

        invalidReason = null;
        return new PeAuthenticodeLayout(
            length,
            isPe32Plus,
            optionalHeaderOffset + 64,
            optionalHeaderOffset + securityDirectoryOffset,
            certificateTableOffset,
            certificateTableSize,
            Math.Min(endOfSectionData, length));
    }

    private static void ReadAt(Stream stream, long offset, Span<byte> buffer)
    {
        stream.Position = offset;
        stream.ReadExactly(buffer);
    }
}
