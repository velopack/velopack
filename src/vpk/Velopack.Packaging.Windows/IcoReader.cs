using System.Buffers.Binary;
using Microsoft.Extensions.Logging;
using Velopack.Core;

namespace Velopack.Packaging.Windows;

/// <summary>
/// One image within an ICO file, described the way a Windows RT_GROUP_ICON directory entry
/// describes it. <see cref="PixelData"/> is the image payload copied verbatim out of the ICO
/// file, which is exactly what an RT_ICON resource holds — either a DIB (BITMAPINFOHEADER plus
/// XOR/AND pixel data, no BITMAPFILEHEADER) or a complete PNG file.
/// </summary>
public class IcoFrame
{
    /// <summary>Width as stored in an icon directory. Zero means 256, as 256 does not fit a byte.</summary>
    public byte Width { get; init; }

    /// <summary>Height as stored in an icon directory. Zero means 256, as 256 does not fit a byte.</summary>
    public byte Height { get; init; }

    /// <summary>Number of palette entries, or zero when the image has 256 or more colors.</summary>
    public byte ColorCount { get; init; }

    /// <summary>Bits per pixel of the image payload.</summary>
    public ushort BitsPerPixel { get; init; }

    /// <summary>The image payload, verbatim from the ICO file.</summary>
    public byte[] PixelData { get; init; } = [];

    /// <summary>Width in pixels, resolving the directory's zero-means-256 encoding.</summary>
    public int PixelWidth => Width == 0 ? 256 : Width;

    /// <summary>Height in pixels, resolving the directory's zero-means-256 encoding.</summary>
    public int PixelHeight => Height == 0 ? 256 : Height;
}

/// <summary>
/// Reads the frame directory of an ICO file. Only headers are parsed — image payloads are copied
/// through untouched, because that is the form Windows icon resources store. This is deliberately
/// permissive: ICO files in the wild mix DIB and PNG frames, carry DIB header versions from
/// BITMAPCOREHEADER through BITMAPV5HEADER, use compression, and are often out of spec about a
/// frame's size or bit depth. A frame is dropped only when it could not produce a drawable icon
/// resource, and the file is rejected only when no frame survives.
/// </summary>
public static class IcoReader
{
    private const int DirectoryHeaderSize = 6;
    private const int DirectoryEntrySize = 16;
    private const int IconResourceType = 1;
    private const int CursorResourceType = 2;

    /// <summary>
    /// An icon directory cannot describe anything past 256 pixels, and the largest frame anyone
    /// ships is 1024, so a header claiming more than this is corrupt rather than ambitious.
    /// </summary>
    private const int MaxPlausibleDimension = ushort.MaxValue;

    // BITMAPINFOHEADER.biCompression values. Only the uncompressed ones let us predict how many
    // bytes of pixel data the header implies.
    private const uint BI_RGB = 0;
    private const uint BI_BITFIELDS = 3;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Reads the frames of an ICO file from disk.
    /// </summary>
    /// <exception cref="UserInfoException">The file is not a usable ICO file.</exception>
    public static List<IcoFrame> ReadFrames(string icoPath, ILogger logger)
    {
        byte[] data;
        try {
            data = File.ReadAllBytes(icoPath);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            throw new UserInfoException($"Unable to read icon '{icoPath}': {ex.Message}", ex);
        }

        return ReadFrames(data, Path.GetFileName(icoPath), logger);
    }

    /// <summary>
    /// Reads the frames of an ICO file already in memory. <paramref name="displayName"/> is used
    /// in diagnostics only.
    /// </summary>
    /// <exception cref="UserInfoException">The data is not a usable ICO file.</exception>
    public static List<IcoFrame> ReadFrames(byte[] data, string displayName, ILogger logger)
    {
        if (data.Length < DirectoryHeaderSize) {
            throw new UserInfoException($"'{displayName}' is too small to be an icon file ({data.Length} bytes).");
        }

        var reserved = ReadUInt16(data, 0);
        var type = ReadUInt16(data, 2);
        var count = ReadUInt16(data, 4);

        if (reserved != 0 || (type != IconResourceType && type != CursorResourceType)) {
            throw new UserInfoException(
                $"'{displayName}' is not a valid icon file. Expected an ICONDIR header, found " +
                $"idReserved={reserved}, idType={type}.");
        }

        if (type == CursorResourceType) {
            throw new UserInfoException($"'{displayName}' is a cursor (.cur) file, not an icon (.ico) file.");
        }

        if (count == 0) {
            throw new UserInfoException($"'{displayName}' contains no images.");
        }

        var directoryEnd = DirectoryHeaderSize + ((long) count * DirectoryEntrySize);
        if (directoryEnd > data.Length) {
            throw new UserInfoException(
                $"'{displayName}' is truncated: its directory claims {count} images, which needs " +
                $"{directoryEnd} bytes but the file is only {data.Length} bytes.");
        }

        var frames = new List<IcoFrame>(count);

        for (var i = 0; i < count; i++) {
            var frame = ReadFrame(data, directoryEnd, i, displayName, logger);
            if (frame is not null) {
                frames.Add(frame);
            }
        }

        if (frames.Count == 0) {
            throw new UserInfoException(
                $"'{displayName}' contains no usable images. See the log for why each image was skipped.");
        }

        return frames;
    }

    /// <summary>
    /// True when <paramref name="payload"/> begins with the PNG file signature.
    /// </summary>
    public static bool IsPng(byte[] payload) => payload.AsSpan().StartsWith(PngSignature);

    /// <summary>
    /// Reads one directory entry and its payload, or null if the entry cannot produce a drawable
    /// icon resource, in which case the reason is logged.
    /// </summary>
    private static IcoFrame? ReadFrame(byte[] data, long directoryEnd, int index, string displayName, ILogger logger)
    {
        var entry = DirectoryHeaderSize + (index * DirectoryEntrySize);
        var dirBitsPerPixel = ReadUInt16(data, entry + 6);
        var byteCount = ReadUInt32(data, entry + 8);
        var offset = ReadUInt32(data, entry + 12);

        // The remaining directory fields (bWidth, bHeight, bColorCount, bReserved, wPlanes) are
        // re-derived from the payload below rather than trusted. They disagree with the payload
        // often enough in real files, and Windows only needs them self-consistent.

        if (byteCount == 0) {
            logger.LogWarning("Skipping image {Index} of '{File}': it has no image data.", index, displayName);
            return null;
        }

        if (offset < directoryEnd || offset + (long) byteCount > data.Length) {
            logger.LogWarning(
                "Skipping image {Index} of '{File}': its data (offset {Offset}, {Length} bytes) lies outside the file.",
                index, displayName, offset, byteCount);
            return null;
        }

        var payload = new byte[byteCount];
        Buffer.BlockCopy(data, (int) offset, payload, 0, (int) byteCount);

        var header = InspectPayload(payload);
        if (header is null) {
            // Windows selects a frame from the directory and only then decodes it, so passing an
            // undecodable payload through would make the icon fail to load at that size rather
            // than fall back to another frame.
            logger.LogWarning(
                "Skipping image {Index} of '{File}': its {Length} bytes of data are neither a PNG nor a bitmap Windows can draw.",
                index, displayName, byteCount);
            return null;
        }

        var (width, height, payloadBitsPerPixel) = header.Value;

        // The payload is what Windows draws, and it picks a frame by the advertised size, so
        // advertising the payload's own size is what keeps it from scaling the image. The bit
        // depth comes from the payload for the same reason, falling back to the directory for
        // formats whose depth we cannot read.
        var bitsPerPixel = payloadBitsPerPixel is > 0 and <= 32 ? payloadBitsPerPixel
            : dirBitsPerPixel is > 0 and <= 32 ? dirBitsPerPixel
            : (ushort) 32;

        var frame = new IcoFrame {
            Width = ToDirectoryByte(width),
            Height = ToDirectoryByte(height),
            // An icon directory records a palette size only when it fits a byte, so 256 colors
            // and above are recorded as zero.
            ColorCount = bitsPerPixel < 8 ? (byte) (1 << bitsPerPixel) : (byte) 0,
            BitsPerPixel = bitsPerPixel,
            PixelData = payload,
        };

        if (width != height) {
            logger.LogDebug("Image {Index} of '{File}' is not square ({Width}x{Height}).", index, displayName, width, height);
        }

        if (IsPng(payload) && bitsPerPixel != 32) {
            // Windows only documents 32bpp RGBA for PNG icon frames, and tools have shipped
            // greyscale, palette and 16 bit ones anyway. They are kept, since they may well
            // render, but it is worth a breadcrumb if an icon turns out blank at this size.
            logger.LogDebug(
                "Image {Index} of '{File}' is a {BitsPerPixel}bpp PNG; icon frames are normally 32bpp RGBA.",
                index, displayName, bitsPerPixel);
        }

        if (width > 256 || height > 256) {
            logger.LogDebug(
                "Image {Index} of '{File}' is {Width}x{Height}, larger than the 256x256 an icon directory can describe; " +
                "it will be listed as 256x256.",
                index, displayName, width, height);
        }

        return frame;
    }

    /// <summary>
    /// An icon directory holds each dimension in one byte, where zero means 256. Anything larger
    /// than a byte can hold is therefore recorded as zero too.
    /// </summary>
    private static byte ToDirectoryByte(int pixels) => pixels < 256 ? (byte) pixels : (byte) 0;

    private readonly record struct PayloadHeader(int Width, int Height, ushort BitsPerPixel);

    /// <summary>
    /// Reads size and bit depth out of an image payload, or null when the payload is neither a PNG
    /// nor a DIB.
    /// </summary>
    private static PayloadHeader? InspectPayload(byte[] payload)
    {
        return IsPng(payload) ? InspectPng(payload) : InspectDib(payload);
    }

    /// <summary>
    /// Both dimensions must be positive, and small enough that they cannot be the result of
    /// reading a corrupt header — an icon directory stops describing sizes at 256.
    /// </summary>
    private static bool IsPlausibleSize(long width, long height)
    {
        return width is > 0 and <= MaxPlausibleDimension && height is > 0 and <= MaxPlausibleDimension;
    }

    private static PayloadHeader? InspectPng(byte[] payload)
    {
        // IHDR is required to be the first chunk: 8 byte signature, 4 byte length, "IHDR",
        // then width, height (big endian), bit depth and color type.
        if (payload.Length < 26 || !payload.AsSpan(12, 4).SequenceEqual("IHDR"u8)) {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(20, 4));
        var bitDepth = payload[24];
        var colorType = payload[25];

        var channels = colorType switch {
            0 => 1, // greyscale
            2 => 3, // truecolor
            3 => 1, // palette
            4 => 2, // greyscale + alpha
            6 => 4, // truecolor + alpha
            _ => 0,
        };

        if (channels == 0 || !IsPlausibleSize(width, height)) {
            return null;
        }

        // 16 bit channels give depths above the 32 an icon directory can hold; the caller falls
        // back to the directory's own value for those.
        return new PayloadHeader((int) width, (int) height, (ushort) (bitDepth * channels));
    }

    private static PayloadHeader? InspectDib(byte[] payload)
    {
        if (payload.Length < 4) {
            return null;
        }

        var headerSize = ReadUInt32(payload, 0);

        // A BITMAPCOREHEADER is the only version with 16 bit dimensions and no fields past them.
        // BITMAPINFOHEADER (40) and every other version from OS/2 2.x (16, 64) through
        // BITMAPV5HEADER (124) share the same first 16 bytes, and add biCompression and
        // biClrUsed at fixed offsets once they are long enough to hold them.
        var isCoreHeader = headerSize == 12;
        if (!isCoreHeader && headerSize is < 16 or > 124) {
            return null;
        }

        if (payload.Length < (isCoreHeader ? 12 : 16)) {
            return null;
        }

        var width = isCoreHeader ? ReadInt16(payload, 4) : ReadInt32(payload, 4);
        var height = isCoreHeader ? ReadInt16(payload, 6) : ReadInt32(payload, 8);
        var bitsPerPixel = ReadUInt16(payload, isCoreHeader ? 10 : 14);

        // An icon DIB stacks the XOR image and the AND mask into one bitmap, so the recorded
        // height is twice the icon's height; Windows likewise just halves it. A negative height
        // means a top-down bitmap, which GDI does not accept for icons, and the stacked layout is
        // only defined bottom-up — so there is nothing drawable to describe.
        if (width <= 0 || height <= 0) {
            return null;
        }

        height /= 2;
        if (!IsPlausibleSize(width, height)) {
            return null;
        }

        var compression = headerSize >= 20 && payload.Length >= 20 ? ReadUInt32(payload, 16) : BI_RGB;
        var paletteEntries = headerSize >= 36 && payload.Length >= 36 ? ReadUInt32(payload, 32) : 0;

        // Windows selects a frame from the directory and only then decodes it, so a payload too
        // small for the pixels its own header declares would leave the icon undrawable at that
        // size. Only uncompressed pixel data has a predictable length; the compressed encodings
        // are let through unchecked.
        if ((compression is BI_RGB or BI_BITFIELDS)
            && DeclaredDibSize(headerSize, compression, isCoreHeader, width, height, bitsPerPixel, paletteEntries) > payload.Length) {
            return null;
        }

        return new PayloadHeader(width, height, bitsPerPixel);
    }

    /// <summary>
    /// The smallest a DIB icon payload can be given what its header declares: the header itself,
    /// any colour masks, the palette, then the XOR image and the 1bpp AND mask, each of whose rows
    /// is padded to a 4 byte boundary.
    /// </summary>
    private static long DeclaredDibSize(
        uint headerSize, uint compression, bool isCoreHeader, int width, int height, ushort bitsPerPixel, uint paletteEntries)
    {
        var paletteBytes = 0L;
        if (bitsPerPixel is > 0 and <= 8) {
            // biClrUsed is authoritative when set, and a BITMAPCOREHEADER palette holds RGBTRIPLE
            // rather than RGBQUAD entries.
            var entries = paletteEntries != 0 ? paletteEntries : 1u << bitsPerPixel;
            paletteBytes = entries * (isCoreHeader ? 3L : 4L);
        }

        // BI_BITFIELDS puts three colour masks between a BITMAPINFOHEADER and its pixels. Later
        // header versions have fields of their own for them, already counted in headerSize.
        var colorMaskBytes = compression == BI_BITFIELDS && headerSize == 40 ? 12L : 0L;

        return headerSize + colorMaskBytes + paletteBytes + ((RowSize(width, bitsPerPixel) + RowSize(width, 1)) * height);
    }

    /// <summary>The length of one bottom-up bitmap row, padded to a 4 byte boundary.</summary>
    private static long RowSize(int width, int bitsPerPixel) => ((((long) width * bitsPerPixel) + 31) / 32) * 4;

    private static short ReadInt16(byte[] data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2));

    private static ushort ReadUInt16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static int ReadInt32(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));

    private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
}
