using Ico.Binary;
using Ico.Model;
using Ico.Validation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Threading;

namespace Ico.Codecs
{
    public static class PngDecoder
    {
        private const int _ihdrChunkName = 0x49484452; // "IHDR"

        public static bool IsProbablyPngFile(ulong first8Bytes)
        {
            return FileFormatConstants._pngHeader == first8Bytes;
        }

        public static void DoPngEntry(ByteReader bitmapHeader, ParseContext context, IcoFrame source)
        {
            if (source.Encoding.ClaimedBitDepth != 32)
            {
                context.Reporter.WarnLine(IcoErrorCode.PngNot32Bit, $"PNG-encoded image with bit depth {source.Encoding.ClaimedBitDepth} (expected 32).", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            using (var stream = new MemoryStream(bitmapHeader.Data.ToArray()))
            {
                var decoder = new SixLabors.ImageSharp.Formats.Png.PngDecoder();
                source.CookedData = decoder.Decode<Rgba32>(new Configuration(), stream, CancellationToken.None);
            }

            source.Encoding.Type = IcoEncodingType.Png;
            source.Encoding.PixelFormat = BmpUtil.IsAnyPartialTransparency(source.CookedData) ? BitmapEncoding.Pixel_argb32 : BitmapEncoding.Pixel_0rgb32;
            source.Mask = GenerateMaskFromAlpha(source.CookedData);

            // Conservatively assume that the output wouldn't have used palette trimming, if it had been a bmp frame.
            if (source.Encoding.ClaimedBitDepth < 16)
            {
                source.Encoding.PaletteSize = 1u << (int)source.Encoding.ClaimedBitDepth;
            }

            var encoding = GetPngFileEncoding(bitmapHeader.Data);
            if (encoding.ColorType != PngColorType.RGBA)
            {
                context.Reporter.WarnLine(IcoErrorCode.PngNotRGBA32, $"ICO files require the embedded PNG image to be encoded in RGBA32 format; this is {encoding.ColorType}", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }
            else if (encoding.BitsPerChannel != 8)
            {
                context.Reporter.WarnLine(IcoErrorCode.PngNotRGBA32, $"ICO files require the embedded PNG image to be encoded in RGBA32 format; this is RGBA{encoding.BitsPerChannel * 4}", context.DisplayedPath, context.ImageDirectoryIndex.Value);
            }

            uint numChannels = 0;
            switch (encoding.ColorType)
            {
                case PngColorType.Grayscale:
                    numChannels = 1;
                    break;
                case PngColorType.RGB:
                    numChannels = 3;
                    break;
                case PngColorType.GrayscaleAlpha:
                    numChannels = 2;
                    break;
                case PngColorType.RGBA:
                    numChannels = 4;
                    break;
                case PngColorType.Palette:
                default:
                    break;
            }

            source.Encoding.ActualHeight = encoding.Height;
            source.Encoding.ActualWidth = encoding.Width;
            source.Encoding.ActualBitDepth = encoding.BitsPerChannel * numChannels;
        }

        public static PngFileEncoding GetPngFileEncoding(Memory<byte> data)
        {
            var reader = new ByteReader(data, Ico.Binary.ByteOrder.NetworkEndian);
            if (FileFormatConstants._pngHeader != reader.NextUint64())
            {
                throw new InvalidPngFileException(IcoErrorCode.NotPng, $"Data stream does not begin with the PNG magic constant");
            }

            var chunkLength = reader.NextUint32();
            var chunkType = reader.NextUint32();

            if (chunkType != _ihdrChunkName)
            {
                throw new InvalidPngFileException(IcoErrorCode.PngBadIHDR, $"PNG file should begin with IHDR chunk; found {chunkType} instead");
            }

            if (chunkLength < 13)
            {
                throw new InvalidPngFileException(IcoErrorCode.PngBadIHDR, $"IHDR chunk is invalid length {chunkLength}; expected at least 13 bytes");
            }

            var result = new PngFileEncoding
            {
                Width = reader.NextUint32(),
                Height = reader.NextUint32(),
                BitsPerChannel = reader.NextUint8(),
                ColorType = (PngColorType)reader.NextUint8(),
            };

            if (result.Width == 0 || result.Height == 0)
            {
                throw new InvalidPngFileException(IcoErrorCode.PngIllegalInputDimensions, $"Illegal Width x Height of {result.Width} x {result.Height}");
            }

            switch (result.BitsPerChannel)
            {
                case 1:
                case 2:
                case 4:
                case 8:
                case 16:
                    break;
                default:
                    throw new InvalidPngFileException(IcoErrorCode.PngIllegalInputDepth, $"Illegal bits per color channel / palette entry of {result.BitsPerChannel}");
            }

            switch (result.ColorType)
            {
                case PngColorType.Grayscale:
                case PngColorType.RGB:
                case PngColorType.Palette:
                case PngColorType.GrayscaleAlpha:
                case PngColorType.RGBA:
                    break;
                default:
                    throw new InvalidPngFileException(IcoErrorCode.PngIllegalColorType, $"Illegal color type {result.ColorType}");
            }

            return result;
        }

        private static bool[,] GenerateMaskFromAlpha(Image<Rgba32> image)
        {
            var mask = new bool[image.Width, image.Height];

            for (var x = 0; x < image.Width; x++)
            {
                for (var y = 0; y < image.Height; y++)
                {
                    var alpha = image[x, y].A;
                    mask[x, y] = alpha == 0;
                }
            }

            return mask;
        }

    }
}
