using System;

namespace Ico.Binary
{
    public class ByteReader
    {
        public Memory<byte> Data { get; }
        public ByteOrder Endianness { get; }
        public int SeekOffset { get; set; }

        public ByteReader(Memory<byte> data, ByteOrder endianness)
        {
            Data = data;
            Endianness = endianness;
            SeekOffset = 0;
        }

        public byte NextUint8()
        {
            return Data.Span[SeekOffset++];
        }

        public ushort NextUint16()
        {
            var result = BitConverter.ToUInt16(Data.Span.Slice(SeekOffset, 2).ToArray(), 0);
            SeekOffset += 2;
            return ByteOrderConverter.To(Endianness, result);
        }

        public uint NextUint32()
        {
            var result = BitConverter.ToUInt32(Data.Span.Slice(SeekOffset, 4).ToArray(), 0);
            SeekOffset += 4;
            return ByteOrderConverter.To(Endianness, result);
        }

        public int NextInt32()
        {
            var result = BitConverter.ToInt32(Data.Span.Slice(SeekOffset, 4).ToArray(), 0);
            SeekOffset += 4;
            return ByteOrderConverter.To(Endianness, result);
        }

        public ulong NextUint64()
        {
            var result = BitConverter.ToUInt64(Data.Span.Slice(SeekOffset, 8).ToArray(), 0);
            SeekOffset += 8;
            return ByteOrderConverter.To(Endianness, result);
        }

        public bool IsEof => SeekOffset == Data.Length;
    }
}
