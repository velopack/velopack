using Ico.Binary;
using Ico.Model;
using Ico.Validation;
using System;

namespace Ico.Codecs
{
    public static class IcoDecoder
    {
        public static void DoFile(byte[] data, ParseContext context, Action<IcoFrame> processFrame)
        {
            var reader = new ByteReader(data, ByteOrder.LittleEndian);

            var idReserved = reader.NextUint16();
            var idType = reader.NextUint16();
            var idCount = reader.NextUint16();

            if (idReserved != FileFormatConstants._iconMagicHeader)
            {
                throw new InvalidIcoFileException(IcoErrorCode.InvalidIcoHeader_idReserved, $"ICONDIR.idReserved should be {FileFormatConstants._iconMagicHeader}, was {idReserved}.", context);
            }

            if (idType != FileFormatConstants._iconMagicType)
            {
                throw new InvalidIcoFileException(IcoErrorCode.InvalidIconHeader_idType, $"ICONDIR.idType should be {FileFormatConstants._iconMagicType}, was {idType}.", context);
            }

            if (idCount == 0 || idCount > FileFormatConstants._iconMaxEntries)
            {
                throw new InvalidIcoFileException(IcoErrorCode.TooManyFrames, $"ICONDIR.idCount is {idCount}, an implausible value for an ICO file.", context);
            }

            for (var i = 0u; i < idCount; i++)
            {
                context.ImageDirectoryIndex = i;
                var source = ProcessIcoFrame(reader, context);
                processFrame(source);
            }

            context.ImageDirectoryIndex = null;
        }

        private static IcoFrame ProcessIcoFrame(ByteReader reader, ParseContext context)
        {
            var bWidth = reader.NextUint8();
            var bHeight = reader.NextUint8();
            var bColorCount = reader.NextUint8();
            var bReserved = reader.NextUint8();
            var wPlanes = reader.NextUint16();
            var wBitCount = reader.NextUint16();
            var dwBytesInRes = reader.NextUint32();
            var dwImageOffset = reader.NextUint32();

            if (bWidth != bHeight)
            {
                context.Reporter.WarnLine(IcoErrorCode.NotSquare, $"Icon is not square ({bWidth}x{bHeight}).", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            if (bReserved != FileFormatConstants._iconEntryReserved)
            {
                throw new InvalidIcoFileException(IcoErrorCode.InvalidFrameHeader_bReserved, $"ICONDIRECTORY.bReserved should be {FileFormatConstants._iconEntryReserved}, was {bReserved}.", context);
            }

            if (wPlanes > 1)
            {
                throw new InvalidIcoFileException(IcoErrorCode.InvalidFrameHeader_wPlanes, $"ICONDIRECTORY.wPlanes is {wPlanes}.  Only single-plane bitmaps are supported.", context);
            }

            if (dwBytesInRes > int.MaxValue)
            {
                throw new InvalidIcoFileException(IcoErrorCode.InvalidFrameHeader_dwBytesInRes, $"ICONDIRECTORY.dwBytesInRes == {dwBytesInRes}, which is unreasonably large.", context);
            }

            if (dwImageOffset > int.MaxValue)
            {
                throw new InvalidIcoFileException(IcoErrorCode.InvalidFrameHeader_dwImageOffset, $"ICONDIRECTORY.dwImageOffset == {dwImageOffset}, which is unreasonably large.", context);
            }

            var source = new IcoFrame
            {
                TotalDiskUsage = dwBytesInRes + /* sizeof(ICONDIRENTRY) */ 16,

                Encoding = new IcoFrameEncoding
                {
                    ClaimedBitDepth = wBitCount,
                    ClaimedHeight = bHeight > 0 ? bHeight : 256u,
                    ClaimedWidth = bWidth > 0 ? bWidth : 256u,
                },
            };

            source.RawData = reader.Data.Slice((int)dwImageOffset, (int)dwBytesInRes).ToArray();
            var bitmapHeader = new ByteReader(source.RawData, ByteOrder.LittleEndian);

            var signature = bitmapHeader.NextUint64();
            bitmapHeader.SeekOffset = 0;

            if (PngDecoder.IsProbablyPngFile(ByteOrderConverter.To(ByteOrder.NetworkEndian, signature)))
            {
                PngDecoder.DoPngEntry(bitmapHeader, context, source);
            }
            else
            {
                BmpDecoder.DoBitmapEntry(bitmapHeader, context, source);
            }

            return source;
        }

    }
}
