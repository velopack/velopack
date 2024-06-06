using Ico.Binary;
using Ico.Model;
using System.Collections.Generic;
using System.IO;

namespace Ico.Codecs
{
    public static class IcoEncoder
    {
        public static void EmitIco(string outputPath, ParseContext context)
        {
            var writer = new ByteWriter(ByteOrder.LittleEndian);

            writer.AddUint16(FileFormatConstants._iconMagicHeader);
            writer.AddUint16(FileFormatConstants._iconMagicType);
            writer.AddUint16((ushort)context.GeneratedFrames.Count);

            var offsets = new Queue<uint>();

            foreach (var frame in context.GeneratedFrames)
            {
                var width = frame.Encoding.ClaimedWidth;
                var height = frame.Encoding.ClaimedHeight;
                var bitDepth = frame.Encoding.ClaimedBitDepth;

                writer.AddUint8((byte)(width >= 256 ? 0 : width)); // bWidth
                writer.AddUint8((byte)(height >= 256 ? 0 : height)); // bHeight
                writer.AddUint8((byte)(bitDepth < 8 ? 1u << (int)bitDepth : 0)); // bColorCount
                writer.AddUint8(0); // bReserved
                writer.AddUint16(1); // wPlanes
                writer.AddUint16((ushort)bitDepth); // wBitCount
                writer.AddUint32((uint)frame.RawData.Length); // dwBytesInRes

                offsets.Enqueue((uint)writer.SeekOffset);
                writer.AddUint32(0); // dwImageOffset (will fix later)
            }

            foreach (var frame in context.GeneratedFrames)
            {
                var currentOffset = writer.SeekOffset;
                writer.SeekOffset = (int)offsets.Dequeue();
                writer.AddUint32((uint)currentOffset); // dwImageOffset
                writer.SeekOffset = currentOffset;

                writer.AddBlob(frame.RawData);

                frame.TotalDiskUsage = (uint)frame.RawData.Length + /* sizeof(ICONDIRENTRY) */ 16;
            }

            File.WriteAllBytes(outputPath, writer.Data.ToArray());
        }
    }
}
