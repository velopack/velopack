namespace Ico.Model
{
    public enum PngColorType
    {
        Grayscale = 0,
        RGB = 2,
        Palette = 3,
        GrayscaleAlpha = 4,
        RGBA = 6,
    }

    public class PngFileEncoding
    {
        // 1, 2, 4, 8, or 16
        public uint BitsPerChannel { get; set; }

        public PngColorType ColorType { get; set; }

        public uint Width { get; set; }

        public uint Height { get; set; }
    }
}
