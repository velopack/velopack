using System;
using System.IO;
using System.Text;
using Velopack.Chunks;

namespace Velopack.Util
{
    /// <summary>The location of one zip entry's data bytes inside the archive.</summary>
    internal sealed class ZipEntryLocation
    {
        /// <summary>Offset of the entry's first data byte, relative to the start of the archive.</summary>
        public long Offset { get; }
        /// <summary>Length of the entry data as stored (for a stored entry, also the uncompressed length).</summary>
        public long Length { get; }

        public ZipEntryLocation(long offset, long length)
        {
            Offset = offset;
            Length = length;
        }
    }

    /// <summary>
    /// A dependency-free reader of a zip archive's central directory (§2.7), used to locate a stored entry's bytes
    /// by name so that they can be addressed with byte ranges (the delta-v2 index trailer).
    /// </summary>
    internal static class ZipCentralDirectory
    {
        private const uint EocdSignature = 0x06054B50; // PK 05 06
        private const uint Zip64LocatorSignature = 0x07064B50; // PK 06 07
        private const uint Zip64EocdSignature = 0x06064B50; // PK 06 06
        private const uint CentralHeaderSignature = 0x02014B50; // PK 01 02
        private const uint LocalHeaderSignature = 0x04034B50; // PK 03 04
        private const int EocdSize = 22;
        private const int MaxCommentLength = 0xFFFF;
        private const long MaxCentralDirectorySize = 256L * 1024 * 1024;

        /// <summary>
        /// Finds the entry named <paramref name="entryName"/> (ordinal match) and returns where its data lives, or null
        /// when the archive has no such entry. The entry must be stored (method 0) with equal compressed and
        /// uncompressed sizes; a malformed archive or a non-stored match throws <see cref="InvalidDataException"/>.
        /// </summary>
        public static ZipEntryLocation? FindEntryData(Stream stream, string entryName)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (entryName == null) throw new ArgumentNullException(nameof(entryName));
            if (!stream.CanSeek) throw new ArgumentException("A seekable stream is required.", nameof(stream));

            long length = stream.Length;
            if (length < EocdSize) throw new InvalidDataException("Not a zip archive: too short.");

            // 1. end of central directory record, scanning backwards over a possible archive comment
            int tailLength = (int) Math.Min(length, EocdSize + MaxCommentLength);
            var tail = new byte[tailLength];
            stream.Seek(length - tailLength, SeekOrigin.Begin);
            ReadExactly(stream, tail, 0, tailLength);
            int eocd = -1;
            for (int i = tailLength - EocdSize; i >= 0; i--) {
                if (LittleEndian.ReadUInt32(tail, i) == EocdSignature) {
                    eocd = i;
                    break;
                }
            }
            if (eocd < 0) throw new InvalidDataException("Not a zip archive: end of central directory record not found.");
            long eocdPosition = length - tailLength + eocd;

            long entryCount = LittleEndian.ReadUInt16(tail, eocd + 10);
            long cdSize = LittleEndian.ReadUInt32(tail, eocd + 12);
            long cdOffset = LittleEndian.ReadUInt32(tail, eocd + 16);

            // 2. Zip64 end of central directory
            if (entryCount == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF) {
                if (eocdPosition < 20) throw new InvalidDataException("Zip64 locator missing.");
                var locator = new byte[20];
                stream.Seek(eocdPosition - 20, SeekOrigin.Begin);
                ReadExactly(stream, locator, 0, 20);
                if (LittleEndian.ReadUInt32(locator, 0) != Zip64LocatorSignature) throw new InvalidDataException("Zip64 locator missing.");
                long zip64EocdOffset = (long) LittleEndian.ReadUInt64(locator, 8);
                if (zip64EocdOffset < 0 || zip64EocdOffset + 56 > length) {
                    throw new InvalidDataException("Zip64 end of central directory record out of range.");
                }
                var record = new byte[56];
                stream.Seek(zip64EocdOffset, SeekOrigin.Begin);
                ReadExactly(stream, record, 0, 56);
                if (LittleEndian.ReadUInt32(record, 0) != Zip64EocdSignature) {
                    throw new InvalidDataException("Zip64 end of central directory record missing.");
                }
                entryCount = (long) LittleEndian.ReadUInt64(record, 32);
                cdSize = (long) LittleEndian.ReadUInt64(record, 40);
                cdOffset = (long) LittleEndian.ReadUInt64(record, 48);
            }

            if (cdSize < 0 || cdSize > MaxCentralDirectorySize) throw new InvalidDataException("Central directory too large.");
            if (cdOffset < 0 || cdOffset + cdSize > length) throw new InvalidDataException("Central directory out of range.");

            // 3. walk the central directory
            var cd = new byte[(int) cdSize];
            stream.Seek(cdOffset, SeekOrigin.Begin);
            ReadExactly(stream, cd, 0, cd.Length);
            var wanted = Encoding.UTF8.GetBytes(entryName);
            int pos = 0;
            for (long e = 0; e < entryCount; e++) {
                if (pos + 46 > cd.Length || LittleEndian.ReadUInt32(cd, pos) != CentralHeaderSignature) {
                    throw new InvalidDataException("Malformed central directory header.");
                }
                int method = LittleEndian.ReadUInt16(cd, pos + 10);
                long compressedSize = LittleEndian.ReadUInt32(cd, pos + 20);
                long uncompressedSize = LittleEndian.ReadUInt32(cd, pos + 24);
                int nameLength = LittleEndian.ReadUInt16(cd, pos + 28);
                int extraLength = LittleEndian.ReadUInt16(cd, pos + 30);
                int commentLength = LittleEndian.ReadUInt16(cd, pos + 32);
                long localOffset = LittleEndian.ReadUInt32(cd, pos + 42);
                int nameStart = pos + 46;
                int extraStart = nameStart + nameLength;
                int next = extraStart + extraLength + commentLength;
                if (next > cd.Length) throw new InvalidDataException("Malformed central directory header.");

                bool matches = nameLength == wanted.Length;
                for (int i = 0; matches && i < nameLength; i++) {
                    matches = cd[nameStart + i] == wanted[i];
                }
                if (!matches) {
                    pos = next;
                    continue;
                }

                // Zip64 extra field: only the fields that are 0xFFFFFFFF are present, in this order
                if (uncompressedSize == 0xFFFFFFFF || compressedSize == 0xFFFFFFFF || localOffset == 0xFFFFFFFF) {
                    int x = extraStart;
                    bool found = false;
                    while (x + 4 <= extraStart + extraLength) {
                        int id = LittleEndian.ReadUInt16(cd, x);
                        int size = LittleEndian.ReadUInt16(cd, x + 2);
                        int fieldPos = x + 4;
                        if (fieldPos + size > extraStart + extraLength) throw new InvalidDataException("Malformed extra field.");
                        if (id == 0x0001) {
                            int fieldEnd = fieldPos + size;
                            if (uncompressedSize == 0xFFFFFFFF) uncompressedSize = ReadZip64Field(cd, ref fieldPos, fieldEnd);
                            if (compressedSize == 0xFFFFFFFF) compressedSize = ReadZip64Field(cd, ref fieldPos, fieldEnd);
                            if (localOffset == 0xFFFFFFFF) localOffset = ReadZip64Field(cd, ref fieldPos, fieldEnd);
                            found = true;
                            break;
                        }
                        x = fieldPos + size;
                    }
                    if (!found) throw new InvalidDataException("Zip64 extra field missing.");
                }

                // 4. local header → data offset
                if (localOffset < 0 || localOffset + 30 > length) throw new InvalidDataException("Local header out of range.");
                var local = new byte[30];
                stream.Seek(localOffset, SeekOrigin.Begin);
                ReadExactly(stream, local, 0, 30);
                if (LittleEndian.ReadUInt32(local, 0) != LocalHeaderSignature) throw new InvalidDataException("Local header signature mismatch.");
                long dataOffset = localOffset + 30 + LittleEndian.ReadUInt16(local, 26) + LittleEndian.ReadUInt16(local, 28);

                if (method != 0 || compressedSize != uncompressedSize) {
                    throw new InvalidDataException($"Entry '{entryName}' must be stored (method 0), but has method {method}.");
                }
                if (dataOffset + compressedSize > length) throw new InvalidDataException("Entry data out of range.");
                return new ZipEntryLocation(dataOffset, compressedSize);
            }

            return null;
        }

        private static long ReadZip64Field(byte[] buffer, ref int pos, int end)
        {
            if (pos + 8 > end) throw new InvalidDataException("Malformed Zip64 extra field.");
            long value = (long) LittleEndian.ReadUInt64(buffer, pos);
            pos += 8;
            if (value < 0) throw new InvalidDataException("Malformed Zip64 extra field.");
            return value;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0) {
                int n = stream.Read(buffer, offset, count);
                if (n <= 0) throw new EndOfStreamException();
                offset += n;
                count -= n;
            }
        }
    }
}
