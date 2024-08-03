using Ico.Binary;
using Ico.Model;
using Ico.Validation;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ico.Codecs
{
    public static class BmpEncoder
    {
        public enum Dialect
        {
            Ico,
            Bmp,
        }

        public static byte[] EncodeBitmap(ParseContext context, BitmapEncoding encoding, Dialect dialect, IcoFrame source)
        {
            context.LastEncodeError = IcoErrorCode.NoError;

            return (BmpUtil.GetBitDepthForPixelFormat(encoding) < 16)
                ? EncodeIndexedBitmap(context, encoding, dialect, source)
                : EncodeRgbBitmap(source, context, encoding, dialect);
        }

        private static byte[] EncodeIndexedBitmap(ParseContext context, BitmapEncoding encoding, Dialect dialect, IcoFrame source)
        {
            var numBits = BmpUtil.GetBitDepthForPixelFormat(encoding);

            var colorTable = BuildColorTable(1u << numBits, context, source);
            if (colorTable == null)
            {
                context.LastEncodeError = IcoErrorCode.TooManyColorsForBitDepth;
                return null;
            }

            var writer = new ByteWriter(ByteOrder.LittleEndian);

            EncodeBitmapHeader(source, dialect, encoding, colorTable, writer, out var offsetToImageSize);

            var reverseTable = new Dictionary<Rgba32, int>();
            for (var i = 0; i < colorTable.Length; i++)
            {
                if (!reverseTable.ContainsKey(colorTable[i]))
                {
                    reverseTable.Add(colorTable[i], i);
                }
            }

            var offsetToData = (uint)writer.Data.Count;
            var padding = writer.Data.Count % 4;

            for (var y = source.CookedData.Height - 1; y >= 0; y--)
            {
                var bits = new BitWriter(writer);

                for (var x = 0; x < source.CookedData.Width; x++)
                {
                    var color = source.CookedData[x, y];

                    if (source.Mask[x, y])
                    {
                        switch (context.MaskedImagePixelEmitOptions)
                        {
                            case StrictnessPolicy.Compliant:
                                color = new Rgba32(0, 0, 0, 255);
                                break;
                            case StrictnessPolicy.PreserveSource:
                                // Pass through whatever the original pixel was.
                                break;
                            case StrictnessPolicy.Loose:
                                color = colorTable.First();
                                break;
                        }
                    }

                    color.A = 255;

                    var index = reverseTable[color];
                    bits.AddBits((uint)numBits, (byte)index);
                }

                while ((writer.Data.Count % 4) != padding)
                {
                    writer.AddUint8(0);
                }
            }

            return FinalizeBitmap(source, encoding, dialect, writer, offsetToData, offsetToImageSize);
        }

        private static byte[] EncodeRgbBitmap(IcoFrame source, ParseContext context, BitmapEncoding encoding, Dialect dialect)
        {
            var writer = new ByteWriter(ByteOrder.LittleEndian);
            EncodeBitmapHeader(source, dialect, encoding, null, writer, out var offsetToImageSize);

            var offsetToData = (uint)writer.Data.Count;
            var padding = writer.Data.Count % 4;

            for (var y = source.CookedData.Height - 1; y >= 0; y--)
            {
                var bits = new BitWriter(writer);

                for (var x = 0; x < source.CookedData.Width; x++)
                {
                    var color = source.CookedData[x, y];

                    if (source.Mask[x, y])
                    {
                        switch (context.MaskedImagePixelEmitOptions)
                        {
                            case StrictnessPolicy.Compliant:
                            case StrictnessPolicy.Loose:
                                color = new Rgba32(0, 0, 0, 0);
                                break;
                            case StrictnessPolicy.PreserveSource:
                                // Pass through whatever the original pixel was.
                                break;
                        }
                    }

                    switch (encoding)
                    {
                        case BitmapEncoding.Pixel_rgb15:
                            var value = X8To5(color.R) << 10 | X8To5(color.G) << 5 | X8To5(color.B);
                            writer.AddUint16((ushort)value);
                            break;
                        case BitmapEncoding.Pixel_rgb24:
                            writer.AddUint8(color.B);
                            writer.AddUint8(color.G);
                            writer.AddUint8(color.R);
                            break;
                        case BitmapEncoding.Pixel_0rgb32:
                            writer.AddUint8(color.B);
                            writer.AddUint8(color.G);
                            writer.AddUint8(color.R);
                            writer.AddUint8(0);
                            break;
                        case BitmapEncoding.Pixel_argb32:
                            writer.AddUint8(color.B);
                            writer.AddUint8(color.G);
                            writer.AddUint8(color.R);
                            writer.AddUint8(color.A);
                            break;
                    }
                }

                while ((writer.Data.Count % 4) != padding)
                {
                    writer.AddUint8(0);
                }
            }

            return FinalizeBitmap(source, encoding, dialect, writer, offsetToData, offsetToImageSize);
        }

        private static byte X8To5(uint b)
        {
            return (byte)(b * 32 / 256);
        }

        private static void EncodeBitmapHeader(IcoFrame source, Dialect dialect, BitmapEncoding encoding, Rgba32[] colorTable, ByteWriter writer, out uint offsetToImageSize)
        {
            if (dialect != Dialect.Ico)
            {
                writer.AddUint16(FileFormatConstants._bitmapFileMagic);
                writer.AddUint32(0); // Size will be filled in later
                writer.AddUint32(0); // Reserved
                writer.AddUint32(0); // Offset will be filled in later
            }

            writer.AddUint32(FileFormatConstants._bitmapInfoHeaderSize);
            writer.AddUint32((uint)source.CookedData.Width);
            writer.AddUint32((uint)source.CookedData.Height * ((dialect == Dialect.Ico) ? 2u : 1u));
            writer.AddUint16(1); // biPlanes
            writer.AddUint16((ushort)BmpUtil.GetBitDepthForPixelFormat(encoding)); // biBitCount
            writer.AddUint32(FileFormatConstants.BI_RGB); // biCompression
            offsetToImageSize = (uint)writer.SeekOffset;
            writer.AddUint32(0); // biSizeImage
            writer.AddUint32((dialect == Dialect.Ico) ? 0u : FileFormatConstants._72dpiInPixelsPerMeter); // biXPelsPerMeter
            writer.AddUint32((dialect == Dialect.Ico) ? 0u : FileFormatConstants._72dpiInPixelsPerMeter); // biYPelsPerMeter
            writer.AddUint32((uint)(colorTable?.Length ?? 0)); // biClrUsed
            writer.AddUint32(0); // biClrImportant

            if (colorTable != null)
            {
                foreach (var color in colorTable)
                {
                    writer.AddUint8(color.B);
                    writer.AddUint8(color.G);
                    writer.AddUint8(color.R);
                    writer.AddUint8(0);
                }
            }

            if (dialect != Dialect.Ico)
            {
                while (writer.Data.Count % 4 != 0)
                {
                    writer.AddUint8(0);
                }
            }
        }

        private static byte[] FinalizeBitmap(IcoFrame source, BitmapEncoding encoding, Dialect dialect, ByteWriter writer, uint offsetToData, uint offsetToImageSize)
        {
            var offsetToEndOfData = writer.SeekOffset;

            if (dialect == Dialect.Ico)
            {
                var padding = writer.Data.Count % 4;

                var inferMaskFromAlpha = (source.Encoding.PixelFormat == BitmapEncoding.Pixel_argb32 && encoding != BitmapEncoding.Pixel_argb32);

                for (var y = source.CookedData.Height - 1; y >= 0; y--)
                {
                    var bits = new BitWriter(writer);

                    for (var x = 0; x < source.CookedData.Width; x++)
                    {
                        var mask = inferMaskFromAlpha
                            ? (source.CookedData[x, y].A == 0)
                            : source.Mask[x, y];

                        bits.AddBit1((byte)(mask ? 1 : 0));
                    }

                    while ((writer.Data.Count % 4) != padding)
                    {
                        writer.AddUint8(0);
                    }
                }
            }

            if (dialect != Dialect.Ico)
            {
                writer.SeekOffset = 2;
                writer.AddUint32((uint)writer.Data.Count);

                writer.SeekOffset = 10;
                writer.AddUint32(offsetToData);
            }

            writer.SeekOffset = (int)offsetToImageSize;
            writer.AddUint32((uint)(offsetToEndOfData - offsetToData)); // biSizeImage

            return writer.Data.ToArray();
        }

        private static Rgba32[] BuildColorTable(uint maxColorTableSize, ParseContext context, IcoFrame source)
        {
            var colorTable = new Dictionary<Rgba32, uint>();

            for (var y = source.CookedData.Height - 1; y >= 0; y--)
            {
                for (var x = 0; x < source.CookedData.Width; x++)
                {
                    var color = source.CookedData[x, y];

                    if (source.Mask[x, y])
                    {
                        switch (context.MaskedImagePixelEmitOptions)
                        {
                            case StrictnessPolicy.Compliant:
                                // Ensure an entry is added for black.
                                color = new Rgba32(0, 0, 0, 0);
                                break;
                            case StrictnessPolicy.PreserveSource:
                                // Pass through whatever the original pixel was.
                                break;
                            case StrictnessPolicy.Loose:
                                // Don't create a palette entry for this pixel.
                                continue;
                        }
                    }

                    color.A = 255;

                    if (colorTable.ContainsKey(color))
                    {
                        colorTable[color] += 1;
                    }
                    else if (colorTable.Count == maxColorTableSize)
                    {
                        return null;
                    }
                    else
                    {
                        colorTable.Add(color, 1);
                    }
                }
            }

            if (colorTable.Count == 0)
            {
                colorTable.Add(new Rgba32(0, 0, 0, 255), 1);
            }

            var table = (from c in colorTable
                         orderby c.Value descending
                         select c.Key).ToList();

            var targetPaletteSize = 0u;

            switch (context.AllowPaletteTruncation)
            {
                case StrictnessPolicy.Compliant:
                    targetPaletteSize = maxColorTableSize;
                    break;
                case StrictnessPolicy.PreserveSource:
                    targetPaletteSize = source.Encoding.PaletteSize;
                    break;
                case StrictnessPolicy.Loose:
                    targetPaletteSize = (uint)table.Count;
                    break;
            }

            while (table.Count < targetPaletteSize)
            {
                table.Add(new Rgba32(0, 0, 0, 255));
            }

            return table.ToArray();
        }
    }
}
