using Ico.Binary;
using Ico.Model;
using Ico.Validation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ico.Codecs
{
    public static class BmpDecoder
    {
        public static void DoBitmapEntry(ByteReader reader, ParseContext context, IcoFrame source)
        {
            var biSize = reader.NextUint32();
            var biWidth = reader.NextInt32();
            var biHeight = reader.NextInt32();
            var biPlanes = reader.NextUint16();
            var biBitCount = reader.NextUint16();
            var biCompression = reader.NextUint32();
            var biSizeImage = reader.NextUint32();
            var biXPelsPerMeter = reader.NextInt32();
            var biYPelsPerMeter = reader.NextInt32();
            var biClrUsed = reader.NextUint32();
            var biClrImportant = reader.NextUint32();

            if (biSize != FileFormatConstants._bitmapInfoHeaderSize)
            {
                throw new InvalidIcoFileException(IcoErrorCode.InvalidBitapInfoHeader_ciSize, $"BITMAPINFOHEADER.ciSize should be {FileFormatConstants._bitmapInfoHeaderSize}, was {biSize}.", context);
            }

            if (biXPelsPerMeter != 0)
            {
                context.Reporter.WarnLine(IcoErrorCode.InvalidBitapInfoHeader_biXPelsPerMeter, $"BITMAPINFOHEADER.biXPelsPerMeter should be 0, was {biXPelsPerMeter}.", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            if (biYPelsPerMeter != 0)
            {
                context.Reporter.WarnLine(IcoErrorCode.InvalidBitapInfoHeader_biYPelsPerMeter, $"BITMAPINFOHEADER.biYPelsPerMeter should be 0, was {biYPelsPerMeter}.", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            if (biCompression == FileFormatConstants.BI_BITFIELDS)
            {
                throw new InvalidIcoFileException(IcoErrorCode.BitfieldCompressionNotSupported, $"This tool does not implement icon bitmaps that use BI_BITFIELDS compression.  (The .ICO file may be okay, although it is certainly unusual.)", context);
            }

            if (biCompression != FileFormatConstants.BI_RGB)
            {
                throw new InvalidIcoFileException(IcoErrorCode.BitmapCompressionNotSupported, $"BITMAPINFOHEADER.biCompression is unknown value ({biCompression}).", context);
            }

            if (biHeight != source.Encoding.ClaimedHeight * 2)
            {
                context.Reporter.WarnLine(IcoErrorCode.MismatchedHeight, $"BITMAPINFOHEADER.biHeight is not exactly double ICONDIRECTORY.bHeight ({biHeight} != 2 * {source.Encoding.ClaimedHeight}).", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            if (biWidth != source.Encoding.ClaimedWidth)
            {
                context.Reporter.WarnLine(IcoErrorCode.MismatchedWidth, $"BITMAPINFOHEADER.biWidth is not exactly equal to ICONDIRECTORY.bWidth ({biWidth} != 2 * {source.Encoding.ClaimedWidth}).", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            var height = biHeight / 2;
            var width = biWidth;

            source.Encoding.ActualHeight = (uint)height;
            source.Encoding.ActualWidth = (uint)width;
            source.Encoding.ActualBitDepth = biBitCount;
            source.Encoding.Type = IcoEncodingType.Bitmap;
            source.CookedData = new Image<Rgba32>(width, height);

            switch (biBitCount)
            {
                case 1:
                case 2:
                case 4:
                case 8:
                    ReadIndexedBitmap(reader, context, biBitCount, biClrUsed, height, width, source);
                    break;
                case 16:
                    ReadBitmap16(reader, context, height, width, source);
                    break;
                case 24:
                    ReadBitmap24(reader, context, biClrUsed, height, width, source);
                    break;
                case 32:
                    ReadBitmap32(reader, context, height, width, source);
                    break;
                default:
                    throw new InvalidIcoFileException(IcoErrorCode.InvalidBitapInfoHeader_biBitCount, $"BITMAPINFOHEADER.biBitCount is unknown value ({biBitCount}); expected 1, 4, 8, 16, or 32 bit depth.", context);
            }
        }


        private static void ReadIndexedBitmap(ByteReader reader, ParseContext context, uint bitDepth, uint colorTableSize, int height, int width, IcoFrame source)
        {
            var anyReservedChannel = false;
            var anyIndexOutOfBounds = false;

            if (colorTableSize == 0)
            {
                colorTableSize = 1u << (int)bitDepth;
            }

            source.Encoding.PaletteSize = colorTableSize;

            if (colorTableSize > 1u << (int)bitDepth)
            {
                throw new InvalidIcoFileException(IcoErrorCode.InvalidBitapInfoHeader_biClrUsed, $"BITMAPINFOHEADER.biClrUsed is greater than 2^biBitCount (biClrUsed == {colorTableSize}, biBitCount = {bitDepth}).", context);
            }
            else if (colorTableSize < 1u << (int)bitDepth)
            {
                context.Reporter.WarnLine(IcoErrorCode.UndersizedColorTable, $"This bitmap uses a color table that is smaller than the bit depth ({colorTableSize} < 2^{bitDepth})", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            var colorTable = new Rgba32[colorTableSize];
            for (var i = 0; i < colorTableSize; i++)
            {
                var c = new Bgra32
                {
                    PackedValue = reader.NextUint32()
                };

                if (c.A != 0)
                {
                    anyReservedChannel = true;
                }

                c.A = 255;
                c.ToRgba32(ref colorTable[i]);
            }

            var padding = reader.SeekOffset % 4;

            for (var y = height - 1; y >= 0; y--)
            {
                var bits = new BitReader(reader);

                for (var x = 0; x < width; x++)
                {
                    var colorIndex = bits.NextBit(bitDepth);

                    if (colorIndex >= colorTableSize)
                    {
                        anyIndexOutOfBounds = true;
                        source.CookedData[x, y] = Color.Black;
                    }
                    else
                    {
                        source.CookedData[x, y] = colorTable[colorIndex];
                    }
                }

                while ((reader.SeekOffset % 4) != padding)
                {
                    reader.SeekOffset += 1;
                }
            }

            switch (bitDepth)
            {
                case 1:
                    source.Encoding.PixelFormat = BitmapEncoding.Pixel_indexed1;
                    break;
                case 2:
                    source.Encoding.PixelFormat = BitmapEncoding.Pixel_indexed2;
                    break;
                case 4:
                    source.Encoding.PixelFormat = BitmapEncoding.Pixel_indexed4;
                    break;
                case 8:
                    source.Encoding.PixelFormat = BitmapEncoding.Pixel_indexed8;
                    break;
            }

            ReadBitmapMask(reader, context, height, width, source);

            if (anyReservedChannel)
            {
                context.Reporter.WarnLine(IcoErrorCode.NonzeroAlpha, $"Reserved Alpha channel used in color table.", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            if (anyIndexOutOfBounds)
            {
                context.Reporter.WarnLine(IcoErrorCode.IndexedColorOutOfBounds, $"Bitmap uses color at illegal index; pixel filled with Black color.", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }
        }

        private static void ReadBitmap16(ByteReader reader, ParseContext context, int height, int width, IcoFrame source)
        {
            for (var y = height - 1; y >= 0; y--)
            {
                for (var x = 0; x < width; x++)
                {
                    var colorValue = reader.NextUint16();
                    source.CookedData[x, y] = new Rgba32(
                        _5To8[colorValue >> 10],
                        _5To8[(colorValue >> 5) & 0x1f],
                        _5To8[colorValue & 0x1f],
                        255);
                }
            }

            source.Encoding.PixelFormat = BitmapEncoding.Pixel_rgb15;
            ReadBitmapMask(reader, context, height, width, source);
        }

        private static readonly byte[] _5To8 = new byte[]
        {
            0, 8, 16, 25, 33, 41, 49, 58, 66, 74, 82, 90, 99, 107, 115, 123, 132, 140, 148, 156, 165, 173, 181, 189, 197, 206, 214, 222, 230, 239, 247, 255,
        };

        private static void ReadBitmap24(ByteReader reader, ParseContext context, uint colorTableSize, int height, int width, IcoFrame source)
        {
            reader.SeekOffset += (int)colorTableSize * 4;

            for (var y = height - 1; y >= 0; y--)
            {
                for (var x = 0; x < width; x++)
                {
                    var b = reader.NextUint8();
                    var g = reader.NextUint8();
                    var r = reader.NextUint8();

                    source.CookedData[x, y] = new Rgba32(r, g, b, 255);
                }
            }

            source.Encoding.PixelFormat = BitmapEncoding.Pixel_rgb24;
            ReadBitmapMask(reader, context, height, width, source);
        }

        private static void ReadBitmap32(ByteReader reader, ParseContext context, int height, int width, IcoFrame source)
        {
            for (var y = height - 1; y >= 0; y--)
            {
                for (var x = 0; x < width; x++)
                {
                    var colorValue = new Bgra32 { PackedValue = reader.NextUint32() };
                    Rgba32 rgba32Value = source.CookedData[x, y]; // the ref keyword cannot be used on this indexer, so we need a temporary
                    colorValue.ToRgba32(ref rgba32Value);
                    source.CookedData[x, y] = rgba32Value;
                }
            }

            source.Encoding.PixelFormat = BmpUtil.IsAnyAlphaChannel(source.CookedData)
                ? BitmapEncoding.Pixel_argb32
                : BitmapEncoding.Pixel_0rgb32;

            ReadBitmapMask(reader, context, height, width, source);
        }

        private static void ReadBitmapMask(ByteReader reader, ParseContext context, int height, int width, IcoFrame source)
        {
            source.Mask = new bool[width, height];

            var anyMask = false;
            var anyMaskedColors = false;

            var padding = reader.SeekOffset % 4;

            for (var y = height - 1; y >= 0; y--)
            {
                var bits = new BitReader(reader);

                for (var x = 0; x < width; x++)
                {
                    var mask = bits.NextBit1();

                    if (mask == 0)
                    {
                        continue;
                    }

                    source.Mask[x, y] = true;

                    anyMask = true;

                    if (source.CookedData[x, y].R != 0 || source.CookedData[x, y].G != 0 || source.CookedData[x, y].B != 0)
                    {
                        anyMaskedColors = true;
                    }

                    //source.CookedData[x, y] = new Rgba32(0, 0, 0, 0);
                }

                while ((reader.SeekOffset % 4) != padding)
                {
                    reader.SeekOffset += 1;
                }
            }

            if (!anyMask)
            {
                context.Reporter.WarnLine(IcoErrorCode.NoMaskedPixels, $"No bitmap mask.", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            if (anyMaskedColors)
            {
                context.Reporter.WarnLine(IcoErrorCode.MaskedPixelWithColor, $"Non-black image pixels masked out.", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }
        }
    }
}

