using System.Buffers.Binary;
using Velopack.Core;
using Velopack.Packaging.Windows;

namespace Velopack.Packaging.Tests;

public class IcoReaderTests
{
    private readonly ITestOutputHelper _output;

    public IcoReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ReadsEveryFrameOfRealIcon()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var path = PathHelper.GetFixture("clowd.ico");
        var frames = IcoReader.ReadFrames(path, logger);

        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64 }, frames.Select(f => f.PixelWidth));
        Assert.All(frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));
        Assert.All(frames, f => Assert.Equal(32, f.BitsPerPixel));
        Assert.All(frames, f => Assert.Equal(0, f.ColorCount));
        Assert.All(frames, f => Assert.False(IcoReader.IsPng(f.PixelData)));

        // Payloads must be the exact bytes the ICO file holds, since they are copied straight
        // into RT_ICON resources.
        var data = File.ReadAllBytes(path);
        for (var i = 0; i < frames.Count; i++) {
            var entry = 6 + (i * 16);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry + 8, 4));
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entry + 12, 4));
            Assert.Equal(data.AsSpan((int) offset, (int) length).ToArray(), frames[i].PixelData);
        }
    }

    [Fact]
    public void ReadsPngFrame()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var frames = ReadIco(logger, Entry(64, 64, 32, Png(64, 64, bitDepth: 8, colorType: 6)));

        var frame = Assert.Single(frames);
        Assert.True(IcoReader.IsPng(frame.PixelData));
        Assert.Equal(64, frame.PixelWidth);
        Assert.Equal(32, frame.BitsPerPixel);
        Assert.Equal(0, frame.ColorCount);
    }

    [Fact]
    public void ReadsPngFrameOf256Pixels()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // 256 does not fit the directory's size byte, so the file records zero.
        var frames = ReadIco(logger, Entry(0, 0, 32, Png(256, 256, bitDepth: 8, colorType: 6)));

        var frame = Assert.Single(frames);
        Assert.Equal(0, frame.Width);
        Assert.Equal(0, frame.Height);
        Assert.Equal(256, frame.PixelWidth);
        Assert.Equal(256, frame.PixelHeight);
    }

    [Fact]
    public void ListsFrameLargerThan256PixelsAs256()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // Icon sets converted from macOS .icns routinely carry a 512px frame. The directory
        // cannot describe it, but Windows still decodes the payload.
        var frames = ReadIco(logger, Entry(0, 0, 32, Png(512, 512, bitDepth: 8, colorType: 6)));

        var frame = Assert.Single(frames);
        Assert.Equal(0, frame.Width);
        Assert.Equal(0, frame.Height);
        Assert.Equal(256, frame.PixelWidth);
    }

    [Fact]
    public void ReadsPalettePngFrame()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var frames = ReadIco(logger, Entry(32, 32, 0, Png(32, 32, bitDepth: 4, colorType: 3)));

        var frame = Assert.Single(frames);
        Assert.Equal(4, frame.BitsPerPixel);
        Assert.Equal(16, frame.ColorCount);
    }

    [Fact]
    public void FallsBackToDirectoryDepthFor16BitPng()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // 16 bits per channel is 64bpp, more than an icon directory can record.
        var frames = ReadIco(logger, Entry(32, 32, 32, Png(32, 32, bitDepth: 16, colorType: 6)));

        var frame = Assert.Single(frames);
        Assert.Equal(32, frame.BitsPerPixel);
        Assert.Equal(32, frame.PixelWidth);
    }

    [Theory]
    [InlineData(40)] // BITMAPINFOHEADER
    [InlineData(52)] // BITMAPV2INFOHEADER
    [InlineData(56)] // BITMAPV3INFOHEADER
    [InlineData(108)] // BITMAPV4HEADER
    [InlineData(124)] // BITMAPV5HEADER
    [InlineData(12)] // BITMAPCOREHEADER
    [InlineData(16)] // OS/2 2.x short header
    public void ReadsEveryDibHeaderVersion(int headerSize)
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var frames = ReadIco(logger, Entry(32, 32, 32, Dib(32, 32, 32, headerSize)));

        var frame = Assert.Single(frames);
        Assert.Equal(32, frame.PixelWidth);
        Assert.Equal(32, frame.PixelHeight);
        Assert.Equal(32, frame.BitsPerPixel);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(4, 16)]
    [InlineData(8, 0)] // 256 colors does not fit a byte, so the directory records zero
    [InlineData(24, 0)]
    [InlineData(32, 0)]
    public void DerivesColorCountFromBitDepth(int bitsPerPixel, int expectedColorCount)
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var frames = ReadIco(logger, Entry(32, 32, (ushort) bitsPerPixel, Dib(32, 32, bitsPerPixel)));

        var frame = Assert.Single(frames);
        Assert.Equal(bitsPerPixel, frame.BitsPerPixel);
        Assert.Equal(expectedColorCount, frame.ColorCount);
    }

    [Fact]
    public void DerivesBitDepthFromPayloadWhenDirectoryOmitsIt()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // Plenty of authoring tools leave wBitCount at zero and expect Windows to work it out.
        var frames = ReadIco(logger, Entry(32, 32, 0, Dib(32, 32, 8)));

        var frame = Assert.Single(frames);
        Assert.Equal(8, frame.BitsPerPixel);
    }

    [Fact]
    public void PrefersPayloadBitDepthOverDirectory()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // The payload is what Windows actually draws, so it wins when the two disagree.
        var frames = ReadIco(logger, Entry(32, 32, 8, Dib(32, 32, 32)));

        var frame = Assert.Single(frames);
        Assert.Equal(32, frame.BitsPerPixel);
    }

    [Fact]
    public void FallsBackTo32BitsWhenNeitherDepthIsUsable()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var frames = ReadIco(logger, Entry(32, 32, 0xFFFF, Dib(32, 32, 0)));

        var frame = Assert.Single(frames);
        Assert.Equal(32, frame.BitsPerPixel);
    }

    [Fact]
    public void PrefersPayloadSizeOverDirectory()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // Windows selects a frame by the size the directory advertises and then scales the
        // payload to it, so the payload's real size is what must be advertised.
        var frames = ReadIco(logger, Entry(32, 32, 32, Dib(48, 48, 32)));

        var frame = Assert.Single(frames);
        Assert.Equal(48, frame.PixelWidth);
        Assert.Equal(48, frame.PixelHeight);
    }

    [Fact]
    public void DerivesSizeFromPayloadWhenDirectorySizeIsZero()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // A zero size byte means 256, but some tools write zero for every entry. The payload
        // proves this one is 48px, so 48 is recorded rather than 256.
        var frames = ReadIco(logger, Entry(0, 0, 32, Dib(48, 48, 32)));

        var frame = Assert.Single(frames);
        Assert.Equal(48, frame.PixelWidth);
        Assert.Equal(48, frame.PixelHeight);
    }

    [Fact]
    public void KeepsNonSquareFrame()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var frames = ReadIco(logger, Entry(16, 32, 32, Dib(16, 32, 32)));

        var frame = Assert.Single(frames);
        Assert.Equal(16, frame.PixelWidth);
        Assert.Equal(32, frame.PixelHeight);
    }

    [Fact]
    public void MixesDibAndPngFrames()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var frames = ReadIco(
            logger,
            Entry(16, 16, 32, Dib(16, 16, 32)),
            Entry(48, 48, 8, Dib(48, 48, 8)),
            Entry(0, 0, 32, Png(256, 256, 8, 6)));

        Assert.Equal(3, frames.Count);
        Assert.False(IcoReader.IsPng(frames[0].PixelData));
        Assert.False(IcoReader.IsPng(frames[1].PixelData));
        Assert.True(IcoReader.IsPng(frames[2].PixelData));
        Assert.Equal(new[] { 16, 48, 256 }, frames.Select(f => f.PixelWidth));
    }

    public static TheoryData<string, byte[]> UndrawablePayloads()
    {
        // Windows picks a frame from the directory and only then decodes it, so an undecodable
        // payload makes the icon fail to load at that size instead of falling back to another
        // frame. Every one of these must be dropped rather than passed through.
        var truncatedPng = Png(32, 32, 8, 6).AsSpan(0, 20).ToArray();

        var wrongFirstChunk = Png(32, 32, 8, 6);
        "IDAT"u8.CopyTo(wrongFirstChunk.AsSpan(12, 4));

        var bitmapFile = new byte[128];
        "BM"u8.CopyTo(bitmapFile); // a .bmp file, complete with BITMAPFILEHEADER

        var unknownDibHeader = Dib(32, 32, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(unknownDibHeader.AsSpan(0, 4), 200);

        var zeroWidth = Dib(0, 32, 32);
        var zeroHeight = Dib(32, 0, 32);

        // Math.Abs(int.MinValue) overflows, so this must be rejected without throwing.
        var minHeight = Dib(32, 32, 32);
        BinaryPrimitives.WriteInt32LittleEndian(minHeight.AsSpan(8, 4), int.MinValue);

        var maxHeight = Dib(32, 32, 32);
        BinaryPrimitives.WriteInt32LittleEndian(maxHeight.AsSpan(8, 4), int.MaxValue);

        var hugeWidth = Dib(32, 32, 32);
        BinaryPrimitives.WriteInt32LittleEndian(hugeWidth.AsSpan(4, 4), 70_000);

        // A height of 1 halves to zero, leaving nothing to draw.
        var oneHeight = Dib(32, 32, 32);
        BinaryPrimitives.WriteInt32LittleEndian(oneHeight.AsSpan(8, 4), 1);

        // GDI has no top-down icon bitmaps, and the stacked XOR/mask layout is bottom-up only.
        var topDown = Dib(32, 32, 32, negativeHeight: true);

        // Declares 256x256x32 but carries nowhere near the 4 + 262144 + 8192 bytes that needs.
        var truncatedPixels = Dib(32, 32, 32);
        BinaryPrimitives.WriteInt32LittleEndian(truncatedPixels.AsSpan(4, 4), 256);
        BinaryPrimitives.WriteInt32LittleEndian(truncatedPixels.AsSpan(8, 4), 512);

        // A 4bpp DIB whose biClrUsed pushes the palette past the end of the payload.
        var oversizedPalette = Dib(32, 32, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(oversizedPalette.AsSpan(32, 4), 4096);

        return new TheoryData<string, byte[]> {
            { "all zeroes", new byte[128] },
            { "truncated png", truncatedPng },
            { "png without ihdr", wrongFirstChunk },
            { "png with unknown colour type", Png(32, 32, 8, colorType: 7) },
            { "bitmap file", bitmapFile },
            { "unknown dib header size", unknownDibHeader },
            { "dib with no width", zeroWidth },
            { "dib with no height", zeroHeight },
            { "dib with int.MinValue height", minHeight },
            { "dib with int.MaxValue height", maxHeight },
            { "dib with an implausible width", hugeWidth },
            { "dib with a height of one", oneHeight },
            { "top-down dib", topDown },
            { "dib too small for its declared pixels", truncatedPixels },
            { "dib whose biClrUsed overruns the payload", oversizedPalette },
            { "png with an implausible width", Png(70_000, 32, 8, 6) },
        };
    }

    [Theory]
    [MemberData(nameof(UndrawablePayloads))]
    public void SkipsUndrawablePayload(string description, byte[] payload)
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        _output.WriteLine(description);

        // Kept alongside a good frame, only the bad one is dropped.
        var frames = ReadIco(logger, Entry(16, 16, 32, Dib(16, 16, 32)), Entry(32, 32, 32, payload));
        var frame = Assert.Single(frames);
        Assert.Equal(16, frame.PixelWidth);

        // On its own there is nothing left, which is an error the user needs to see.
        var ex = Assert.Throws<UserInfoException>(() => ReadIco(logger, Entry(32, 32, 32, payload)));
        Assert.Contains("no usable images", ex.Message);
    }

    [Fact]
    public void ReadsDibWithAnOddHeightWithoutThrowing()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // An odd stored height cannot be an XOR/mask pair. Windows just halves it, so we do too.
        var payload = Dib(32, 32, 32);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 33);

        var frames = ReadIco(logger, Entry(32, 32, 32, payload));
        var frame = Assert.Single(frames);
        Assert.Equal(32, frame.PixelWidth);
        Assert.Equal(16, frame.PixelHeight);
    }

    [Fact]
    public void AcceptsCompressedDibWithoutCheckingItsSize()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // RLE, PNG and JPEG encoded pixel data has no predictable length, so the size check has
        // to stand aside rather than reject the frame.
        var payload = Dib(32, 32, 8).AsSpan(0, 128).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16, 4), 1); // BI_RLE8

        var frames = ReadIco(logger, Entry(32, 32, 8, payload));
        var frame = Assert.Single(frames);
        Assert.Equal(32, frame.PixelWidth);
        Assert.Equal(8, frame.BitsPerPixel);
    }

    [Fact]
    public void AccountsForTheColourMasksOfABitfieldsDib()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // BI_BITFIELDS puts three colour masks between a BITMAPINFOHEADER and its pixels.
        var withMasks = Dib(32, 32, 32).Concat(new byte[12]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(withMasks.AsSpan(16, 4), 3); // BI_BITFIELDS

        var frame = Assert.Single(ReadIco(logger, Entry(32, 32, 32, withMasks)));
        Assert.Equal(32, frame.PixelWidth);

        // Without room for them the payload is too short for what it declares.
        var withoutMasks = Dib(32, 32, 32);
        BinaryPrimitives.WriteUInt32LittleEndian(withoutMasks.AsSpan(16, 4), 3);

        var ex = Assert.Throws<UserInfoException>(() => ReadIco(logger, Entry(32, 32, 32, withoutMasks)));
        Assert.Contains("no usable images", ex.Message);
    }

    [Fact]
    public void UsesBiClrUsedForThePaletteSize()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // An 8bpp DIB carrying only 16 palette entries is legal and is smaller than a full
        // 256 entry palette would make it, so it must not be mistaken for truncated.
        var payload = Dib(32, 32, 8).AsSpan(0, 40 + (16 * 4) + ((32 + 4) * 32)).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(32, 4), 16);

        var frame = Assert.Single(ReadIco(logger, Entry(32, 32, 8, payload)));
        Assert.Equal(8, frame.BitsPerPixel);
        Assert.Equal(0, frame.ColorCount);
    }

    [Fact]
    public void SkipsFramesPointingOutsideTheFile()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var ico = BuildIco(Entry(16, 16, 32, Dib(16, 16, 32)), Entry(32, 32, 32, Dib(32, 32, 32)));

        // Push the second entry's data offset past the end of the file.
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 16 + 12, 4), (uint) ico.Length + 1024);

        var frames = IcoReader.ReadFrames(ico, "broken.ico", logger);
        var frame = Assert.Single(frames);
        Assert.Equal(16, frame.PixelWidth);
    }

    [Fact]
    public void SkipsFramesWhoseOffsetAndLengthWouldOverflow()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var ico = BuildIco(Entry(16, 16, 32, Dib(16, 16, 32)), Entry(32, 32, 32, Dib(32, 32, 32)));

        var secondEntry = 6 + 16;
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(secondEntry + 8, 4), 64);
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(secondEntry + 12, 4), uint.MaxValue - 16);

        var frames = IcoReader.ReadFrames(ico, "broken.ico", logger);
        var frame = Assert.Single(frames);
        Assert.Equal(16, frame.PixelWidth);
    }

    [Fact]
    public void SkipsFramesPointingIntoTheDirectory()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var ico = BuildIco(Entry(16, 16, 32, Dib(16, 16, 32)), Entry(32, 32, 32, Dib(32, 32, 32)));

        // Offset 8 is inside the directory itself, so it cannot be image data.
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 16 + 12, 4), 8);

        var frames = IcoReader.ReadFrames(ico, "broken.ico", logger);
        var frame = Assert.Single(frames);
        Assert.Equal(16, frame.PixelWidth);
    }

    [Fact]
    public void SkipsEmptyFrames()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var ico = BuildIco(Entry(16, 16, 32, Dib(16, 16, 32)), Entry(32, 32, 32, Dib(32, 32, 32)));

        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 16 + 8, 4), 0);

        var frames = IcoReader.ReadFrames(ico, "broken.ico", logger);
        var frame = Assert.Single(frames);
        Assert.Equal(16, frame.PixelWidth);
    }

    [Fact]
    public void ThrowsWhenNoFrameIsUsable()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var ico = BuildIco(Entry(16, 16, 32, Dib(16, 16, 32)));

        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 12, 4), (uint) ico.Length + 1024);

        var ex = Assert.Throws<UserInfoException>(() => IcoReader.ReadFrames(ico, "broken.ico", logger));
        Assert.Contains("no usable images", ex.Message);
    }

    [Fact]
    public void ThrowsWhenDirectoryIsEmpty()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var ico = new byte[] { 0, 0, 1, 0, 0, 0 };

        var ex = Assert.Throws<UserInfoException>(() => IcoReader.ReadFrames(ico, "empty.ico", logger));
        Assert.Contains("no images", ex.Message);
    }

    [Fact]
    public void ThrowsWhenDirectoryIsTruncated()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        // Claims three images but holds only one directory entry.
        var ico = new byte[6 + 16];
        ico[2] = 1;
        ico[4] = 3;

        var ex = Assert.Throws<UserInfoException>(() => IcoReader.ReadFrames(ico, "truncated.ico", logger));
        Assert.Contains("truncated", ex.Message);
    }

    [Fact]
    public void ThrowsOnCursorFile()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var ico = BuildIco(Entry(32, 32, 32, Dib(32, 32, 32)));
        ico[2] = 2; // idType 2 is a cursor

        var ex = Assert.Throws<UserInfoException>(() => IcoReader.ReadFrames(ico, "pointer.cur", logger));
        Assert.Contains("cursor", ex.Message);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, 0 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })] // a PNG, not an ICO
    [InlineData(new byte[] { 0, 0, 7, 0, 1, 0 })] // nonsense idType
    [InlineData(new byte[] { 1, 0, 1, 0, 1, 0 })] // nonsense idReserved
    public void ThrowsOnNonIconData(byte[] data)
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        Assert.Throws<UserInfoException>(() => IcoReader.ReadFrames(data, "junk.ico", logger));
    }

    [Fact]
    public void ThrowsWhenFileIsMissing()
    {
        using var logger = _output.BuildLoggerFor<IcoReaderTests>();
        var path = Path.Combine(Path.GetTempPath(), $"velopack-missing-{Guid.NewGuid():N}.ico");

        Assert.Throws<UserInfoException>(() => IcoReader.ReadFrames(path, logger));
    }

    #region ICO builders

    private static List<IcoFrame> ReadIco(ILogger logger, params (byte Width, byte Height, ushort BitsPerPixel, byte[] Payload)[] entries)
    {
        return IcoReader.ReadFrames(BuildIco(entries), "synthetic.ico", logger);
    }

    private static (byte Width, byte Height, ushort BitsPerPixel, byte[] Payload) Entry(
        byte width, byte height, ushort bitsPerPixel, byte[] payload)
    {
        return (width, height, bitsPerPixel, payload);
    }

    private static byte[] BuildIco(params (byte Width, byte Height, ushort BitsPerPixel, byte[] Payload)[] entries)
    {
        var directorySize = 6 + (entries.Length * 16);
        var ico = new byte[directorySize + entries.Sum(e => e.Payload.Length)];

        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(0, 2), 0); // idReserved
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2, 2), 1); // idType: icon
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4, 2), (ushort) entries.Length);

        var payloadOffset = directorySize;
        for (var i = 0; i < entries.Length; i++) {
            var (width, height, bitsPerPixel, payload) = entries[i];
            var entry = 6 + (i * 16);

            ico[entry + 0] = width;
            ico[entry + 1] = height;
            ico[entry + 2] = 0; // bColorCount
            ico[entry + 3] = 0; // bReserved
            BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(entry + 4, 2), 1); // wPlanes
            BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(entry + 6, 2), bitsPerPixel);
            BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(entry + 8, 4), (uint) payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(entry + 12, 4), (uint) payloadOffset);

            payload.CopyTo(ico, payloadOffset);
            payloadOffset += payload.Length;
        }

        return ico;
    }

    /// <summary>
    /// A DIB icon payload: a header of the requested version followed by enough bytes to stand in
    /// for the XOR image and AND mask. Only the header is ever parsed.
    /// </summary>
    private static byte[] Dib(int width, int height, int bitsPerPixel, int headerSize = 40, bool negativeHeight = false)
    {
        // An icon DIB stacks the XOR image and the AND mask, so it records twice the height.
        var storedHeight = negativeHeight ? -(height * 2) : height * 2;

        // The payload has to be big enough for the pixels the header declares, or the reader
        // rightly treats it as truncated.
        var paletteBytes = bitsPerPixel is > 0 and <= 8 ? (1 << bitsPerPixel) * (headerSize == 12 ? 3 : 4) : 0;
        var payload = new byte[headerSize + paletteBytes + ((RowSize(width, bitsPerPixel) + RowSize(width, 1)) * Math.Abs(height))];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), (uint) headerSize);

        if (headerSize == 12) {
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4, 2), (short) width);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(6, 2), (short) storedHeight);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8, 2), 1); // planes
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10, 2), (ushort) bitsPerPixel);
        } else {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), width);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), storedHeight);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), 1); // planes
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14, 2), (ushort) bitsPerPixel);
        }

        return payload;
    }

    private static int RowSize(int width, int bitsPerPixel) => (((width * bitsPerPixel) + 31) / 32) * 4;

    /// <summary>
    /// A PNG icon payload: a signature and IHDR chunk, which is all that gets parsed, followed by
    /// filler. Not a decodable PNG.
    /// </summary>
    private static byte[] Png(int width, int height, byte bitDepth, byte colorType)
    {
        var payload = new byte[64];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(payload);

        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8, 4), 13); // IHDR length
        "IHDR"u8.CopyTo(payload.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(16, 4), (uint) width);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(20, 4), (uint) height);
        payload[24] = bitDepth;
        payload[25] = colorType;

        return payload;
    }

    #endregion
}
