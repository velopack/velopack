namespace Ico.Codecs
{
    public static class FileFormatConstants
    {
        public static int MaxIcoFileSize = 20 * 1024 * 1024;
        internal static ushort _iconMagicHeader = 0;
        internal static ushort _iconMagicType = 1;
        internal static ushort _iconMaxEntries = 256;
        internal static byte _iconEntryReserved = 0;
        internal static ulong _pngHeader = 0x89504e470d0a1a0a;

        internal static uint _bitmapInfoHeaderSize = 40;
        internal static uint BI_RGB = 0;
        internal static uint BI_BITFIELDS = 3;

        internal static ushort _bitmapFileMagic = 0x4d42; // 'BM'
        internal static ushort _bitmapFileHeaderSize = 14;
        internal static uint _72dpiInPixelsPerMeter = 2835u;
    }
}
