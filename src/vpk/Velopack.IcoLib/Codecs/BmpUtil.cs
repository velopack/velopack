using Ico.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;

namespace Ico.Codecs
{
    public static class BmpUtil
    {
        public static bool IsAlphaSignificant(IcoFrame source)
        {
            if (IsAnyPartialTransparency(source.CookedData))
            {
                return true;
            }

            return IsAnyPixel(source.CookedData, (x, y, pixel) 
                => IsCompletelyTransparent(pixel) != source.Mask[x, y]);
        }

        public static bool IsAnyPartialTransparency(Image<Rgba32> image)
        {
            return IsAnyPixel(image, (x, y, pixel) 
                => !IsCompletelyOpaque(pixel) && !IsCompletelyTransparent(pixel));
        }

        public static bool IsAnyAlphaChannel(Image<Rgba32> image)
        {
            return IsAnyPixel(image, (x, y, pixel)
                => !IsCompletelyOpaque(pixel));
        }

        public static ulong GetNumberOfDistinctColors(Image<Rgba32> image, bool includeAlpha)
        {
            var colors = new HashSet<uint>();

            if (includeAlpha)
            {
                ForeachPixel(image, (x, y, pixel)
                    => colors.Add(pixel.PackedValue));
            }
            else
            {
                ForeachPixel(image, (x, y, pixel)
                    => colors.Add((uint)((pixel.R << 16) | (pixel.G << 8) | pixel.B)));
            }

            return (uint)colors.Count;
        }

        private class NumBitsPerChannel
        {
            public int R { get; set; } = 1;
            public int G { get; set; } = 1;
            public int B { get; set; } = 1;
            public int A { get; set; } = 1;

            public bool RgbLessThan(int depth)
            {
                return R < depth && G < depth && B < depth;
            }
        }

        public static int GetMinimumColorDepthForDisplay(Image<Rgba32> image)
        {
            if (IsAnyPartialTransparency(image))
            {
                return 32;
            }

            if (!IsAnyPixel(image, (x, y, pixel) => !IsBlack(pixel) && !IsWhite(pixel)))
            {
                return 1;
            }

            var bpc = new NumBitsPerChannel();
            ForeachPixel(image, (x, y, pixel) => UpdateNumBitsPerChannel(pixel, bpc));

            if (bpc.RgbLessThan(2))
                return 3 * 1;
            if (bpc.RgbLessThan(3))
                return 3 * 2;
            if (bpc.RgbLessThan(5))
                return 3 * 3;

            return 3 * 4;
        }

        public static BitmapEncoding GetIdealBitmapEncoding(Image<Rgba32> image, bool hasIcoMask)
        {
            if (IsAnyPartialTransparency(image))
            {
                return BitmapEncoding.Pixel_argb32;
            }

            var numColors = GetNumberOfDistinctColors(image, !hasIcoMask);
            if (numColors <= 2)
                return BitmapEncoding.Pixel_indexed1;
            if (numColors <= 4)
                return BitmapEncoding.Pixel_indexed2;
            if (numColors <= 16)
                return BitmapEncoding.Pixel_indexed4;
            if (numColors <= 256)
                return BitmapEncoding.Pixel_indexed8;

            var bpc = new NumBitsPerChannel();
            ForeachPixel(image, (x, y, pixel) => UpdateNumBitsPerChannel(pixel, bpc));

            if (hasIcoMask || !IsAnyAlphaChannel(image))
            {
                if (bpc.RgbLessThan(6))
                    return BitmapEncoding.Pixel_rgb15;
                return BitmapEncoding.Pixel_rgb24;
            }

            return BitmapEncoding.Pixel_argb32;
        }

        private static void UpdateNumBitsPerChannel(Rgba32 pixel, NumBitsPerChannel bpc)
        {
            bpc.R = Math.Max(bpc.R, GetMinimumColorDepth(pixel.R));
            bpc.G = Math.Max(bpc.G, GetMinimumColorDepth(pixel.G));
            bpc.B = Math.Max(bpc.B, GetMinimumColorDepth(pixel.B));
            bpc.A = Math.Max(bpc.A, GetMinimumColorDepth(pixel.A));
        }

        private static int GetMinimumColorDepth(byte channel)
        {
            if (channel == 255)
                return 1;

            var mask = 255;
            int depth;

            for (depth = 1; depth < 8; depth++)
            {
                if ((channel & mask) == 0)
                    break;

                mask >>= 1;
            }

            return depth;
        }

        public static bool[,] CreateMaskFromImage(Image<Rgba32> image, bool blackIsTransparent)
        {
            var mask = new bool[image.Width, image.Height];

            ForeachPixel(image, (x, y, pixel) =>
            {
                if (IsCompletelyTransparent(pixel) ||
                   (blackIsTransparent && IsBlack(pixel)))
                {
                    mask[x, y] = true;
                }
            });

            return mask;
        }

        public static int GetBitDepthForPixelFormat(BitmapEncoding pixelFormat)
        {
            switch (pixelFormat)
            {
                case BitmapEncoding.Pixel_indexed1:
                    return 1;
                case BitmapEncoding.Pixel_indexed2:
                    return 2;
                case BitmapEncoding.Pixel_indexed4:
                    return 4;
                case BitmapEncoding.Pixel_indexed8:
                    return 8;
                case BitmapEncoding.Pixel_rgb15:
                    return 16;
                case BitmapEncoding.Pixel_rgb24:
                    return 24;
                case BitmapEncoding.Pixel_0rgb32:
                    return 32;
                case BitmapEncoding.Pixel_argb32:
                    return 32;
            }
            throw new ArgumentException(nameof(pixelFormat));
        }

        private static void ForeachPixel(Image<Rgba32> image, Action<int, int, Rgba32> action)
        {
            var mask = new bool[image.Width, image.Height];

            for (int x = 0; x < image.Width; x++)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    action(x, y, image[x,y]);
                }
            }
        }

        private static bool IsAnyPixel(Image<Rgba32> image, Func<int, int, Rgba32, bool> predicate)
        {
            var mask = new bool[image.Width, image.Height];

            for (int x = 0; x < image.Width; x++)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    if (predicate(x, y, image[x, y]))
                        return true;
                }
            }

            return false;
        }

        private static bool IsCompletelyTransparent(Rgba32 pixel)
        {
            return pixel.A == 0;
        }

        private static bool IsCompletelyOpaque(Rgba32 pixel)
        {
            return pixel.A == 255;
        }

        private static bool IsBlack(Rgba32 pixel)
        {
            return pixel.R == 0 && pixel.G == 0 && pixel.B == 0;
        }

        private static bool IsWhite(Rgba32 pixel)
        {
            return pixel.R == 255 && pixel.G == 255 && pixel.B == 255;
        }
    }
}
