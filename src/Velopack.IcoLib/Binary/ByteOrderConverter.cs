using System;
using System.Collections.Generic;
using System.Text;

namespace Ico.Binary
{
    public enum ByteOrder
    {
        LittleEndian,
        BigEndian,
        NetworkEndian = BigEndian,
    }

    public static class ByteOrderConverter
    {
        private static bool NeedsSwap(ByteOrder endian) => BitConverter.IsLittleEndian != (endian == ByteOrder.LittleEndian);

        public static ushort To(ByteOrder endian, ushort value) => NeedsSwap(endian) ? Swap(value) : value;

        public static uint To(ByteOrder endian, uint value) => NeedsSwap(endian) ? Swap(value) : value;

        public static ulong To(ByteOrder endian, ulong value) => NeedsSwap(endian) ? Swap(value) : value;

        public static int To(ByteOrder endian, int value) => NeedsSwap(endian) ? (int)Swap((uint)value) : value;

        public static ushort Swap(ushort value) =>
                (ushort)((value & 0xffu) << 8 | (value & 0xff00u) >> 8);

        private static uint Swap(uint value) =>
                (value & 0x000000FFU) << 24 | (value & 0x0000FF00U) << 8 |
                (value & 0x00FF0000U) >> 8  | (value & 0xFF000000U) >> 24;

        private static ulong Swap(ulong value) =>
                (value & 0x00000000000000FFUL) << 56 | (value & 0x000000000000FF00UL) << 40 |
                (value & 0x0000000000FF0000UL) << 24 | (value & 0x00000000FF000000UL) << 8  |
                (value & 0x000000FF00000000UL) >> 8  | (value & 0x0000FF0000000000UL) >> 24 |
                (value & 0x00FF000000000000UL) >> 40 | (value & 0xFF00000000000000UL) >> 56;
    }
}
