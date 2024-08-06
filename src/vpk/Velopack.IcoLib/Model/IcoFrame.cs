using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ico.Model
{
    public class IcoFrame
    {
        public IcoFrameEncoding Encoding { get; set; }

        public byte[] RawData { get; set; }

        public Image<Rgba32> CookedData { get; set; }

        public bool[,] Mask { get; set; }

        public uint TotalDiskUsage { get; set; }
    }
}
