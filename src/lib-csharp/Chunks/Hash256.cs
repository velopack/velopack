using System;
using System.Globalization;

namespace Velopack.Chunks
{
    /// <summary>
    /// A SHA-256 digest as a value type (four little-endian 64-bit words), usable as a dictionary key without
    /// per-hash heap allocations. The hex form is upper-case; parsing is case-insensitive.
    /// </summary>
    internal readonly struct Hash256 : IEquatable<Hash256>
    {
        public const int Size = 32;

        private readonly ulong _w0, _w1, _w2, _w3;

        private Hash256(ulong w0, ulong w1, ulong w2, ulong w3)
        {
            _w0 = w0;
            _w1 = w1;
            _w2 = w2;
            _w3 = w3;
        }

        public static Hash256 FromBytes(byte[] buffer, int offset)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || buffer.Length - offset < Size) throw new ArgumentOutOfRangeException(nameof(offset));
            return new Hash256(
                LittleEndian.ReadUInt64(buffer, offset),
                LittleEndian.ReadUInt64(buffer, offset + 8),
                LittleEndian.ReadUInt64(buffer, offset + 16),
                LittleEndian.ReadUInt64(buffer, offset + 24));
        }

        public static Hash256 FromBytes(byte[] hash)
        {
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            if (hash.Length != Size) throw new ArgumentException("A SHA-256 digest is 32 bytes.", nameof(hash));
            return FromBytes(hash, 0);
        }

        public static bool TryParse(string? hex, out Hash256 hash)
        {
            hash = default;
            if (hex == null || hex.Length != Size * 2) return false;
            var bytes = new byte[Size];
            for (int i = 0; i < Size; i++) {
                int hi = HexValue(hex[i * 2]);
                int lo = HexValue(hex[(i * 2) + 1]);
                if (hi < 0 || lo < 0) return false;
                bytes[i] = (byte) ((hi << 4) | lo);
            }
            hash = FromBytes(bytes, 0);
            return true;
        }

        public static Hash256 Parse(string hex)
        {
            if (!TryParse(hex, out var h)) throw new FormatException("Invalid SHA-256 hex string.");
            return h;
        }

        public void CopyTo(byte[] buffer, int offset)
        {
            LittleEndian.WriteUInt64(buffer, offset, _w0);
            LittleEndian.WriteUInt64(buffer, offset + 8, _w1);
            LittleEndian.WriteUInt64(buffer, offset + 16, _w2);
            LittleEndian.WriteUInt64(buffer, offset + 24, _w3);
        }

        public byte[] ToBytes()
        {
            var b = new byte[Size];
            CopyTo(b, 0);
            return b;
        }

        public string ToHex()
        {
            var b = ToBytes();
            var c = new char[Size * 2];
            const string digits = "0123456789ABCDEF";
            for (int i = 0; i < Size; i++) {
                c[i * 2] = digits[b[i] >> 4];
                c[(i * 2) + 1] = digits[b[i] & 0xF];
            }
            return new string(c);
        }

        public bool IsZero => _w0 == 0 && _w1 == 0 && _w2 == 0 && _w3 == 0;

        public bool Equals(Hash256 other) => _w0 == other._w0 && _w1 == other._w1 && _w2 == other._w2 && _w3 == other._w3;

        public override bool Equals(object? obj) => obj is Hash256 other && Equals(other);

        public override int GetHashCode() => unchecked((int) _w0 ^ (int) (_w0 >> 32));

        public override string ToString() => ToHex();

        public static bool operator ==(Hash256 left, Hash256 right) => left.Equals(right);

        public static bool operator !=(Hash256 left, Hash256 right) => !left.Equals(right);

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }

    /// <summary>
    /// Little-endian integer access into byte arrays, independent of the host byte order.
    /// </summary>
    internal static class LittleEndian
    {
        public static ushort ReadUInt16(byte[] b, int o) => (ushort) (b[o] | (b[o + 1] << 8));

        public static uint ReadUInt32(byte[] b, int o) => (uint) (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

        public static ulong ReadUInt64(byte[] b, int o) => ReadUInt32(b, o) | ((ulong) ReadUInt32(b, o + 4) << 32);

        public static void WriteUInt16(byte[] b, int o, ushort v)
        {
            b[o] = (byte) v;
            b[o + 1] = (byte) (v >> 8);
        }

        public static void WriteUInt32(byte[] b, int o, uint v)
        {
            b[o] = (byte) v;
            b[o + 1] = (byte) (v >> 8);
            b[o + 2] = (byte) (v >> 16);
            b[o + 3] = (byte) (v >> 24);
        }

        public static void WriteUInt64(byte[] b, int o, ulong v)
        {
            WriteUInt32(b, o, (uint) v);
            WriteUInt32(b, o + 4, (uint) (v >> 32));
        }
    }
}
