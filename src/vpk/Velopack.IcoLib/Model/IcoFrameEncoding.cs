namespace Ico.Model
{
    public enum IcoEncodingType
    {
        Bitmap,
        Png,
    }

    public enum BitmapEncoding
    {
        Pixel_indexed1,
        Pixel_indexed2,
        Pixel_indexed4,
        Pixel_indexed8,
        Pixel_rgb15,
        Pixel_rgb24,
        Pixel_0rgb32,
        Pixel_argb32,
    }

    public class IcoFrameEncoding
    {
        public IcoEncodingType Type { get; set; }

        // In the ICO header

        public uint ClaimedBitDepth { get; set; }

        public uint ClaimedWidth { get; set; }

        public uint ClaimedHeight { get; set; }

        public uint ActualWidth { get; set; }

        public uint ActualHeight { get; set; }

        // Bitmap only

        public BitmapEncoding PixelFormat { get; set; }

        public uint ActualBitDepth { get; set; }

        public uint PaletteSize { get; set; }
    }
}
