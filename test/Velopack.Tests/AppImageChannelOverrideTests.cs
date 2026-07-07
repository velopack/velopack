using System.Security.Cryptography;
using System.Text;
using Velopack.Util;

namespace Velopack.Tests;

public class AppImageChannelOverrideTests
{
    private static byte[] Magic => AppImageChannelOverride.Magic;

    private static byte[] Trailer(string channel, ushort? lengthOverride = null)
    {
        var channelBytes = Encoding.UTF8.GetBytes(channel);
        return TrailerRaw(channelBytes, lengthOverride);
    }

    private static byte[] TrailerRaw(byte[] channelBytes, ushort? lengthOverride = null)
    {
        var length = lengthOverride ?? (ushort) channelBytes.Length;
        var buf = new byte[Magic.Length + 2 + channelBytes.Length];
        Magic.CopyTo(buf, 0);
        buf[Magic.Length] = (byte) (length & 0xFF);
        buf[Magic.Length + 1] = (byte) (length >> 8);
        channelBytes.CopyTo(buf, Magic.Length + 2);
        return buf;
    }

    private static byte[] Payload(int size, int seed)
    {
        var rng = new Random(seed);
        var buf = new byte[size];
        rng.NextBytes(buf);
        return buf;
    }

    private static byte[] Concat(params byte[][] arrays)
    {
        var buf = new byte[arrays.Sum(a => a.Length)];
        int offset = 0;
        foreach (var a in arrays) {
            a.CopyTo(buf, offset);
            offset += a.Length;
        }

        return buf;
    }

    private static string WriteTempFile(string dir, byte[] contents)
    {
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".AppImage");
        File.WriteAllBytes(path, contents);
        return path;
    }

    [Fact]
    public void ParseWindow_ReturnsNull_WhenNoTrailer()
    {
        Assert.Null(AppImageChannelOverride.ParseWindow(Payload(4096, 1)));
    }

    [Fact]
    public void TryReadFromFile_ReturnsNull_WhenNoTrailer()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteTempFile(dir, Payload(4096, 2));
        Assert.Null(AppImageChannelOverride.TryReadFromFile(path, null));
    }

    [Fact]
    public void ParseWindow_ReturnsChannel_WhenTrailerPresent()
    {
        var data = Concat(Payload(4096, 3), Trailer("stable"));
        Assert.Equal("stable", AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void TryReadFromFile_ReturnsChannel_WhenTrailerPresent()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteTempFile(dir, Concat(Payload(4096, 4), Trailer("stable")));
        Assert.Equal("stable", AppImageChannelOverride.TryReadFromFile(path, null));
    }

    [Fact]
    public void TryReadFromFile_ReturnsNull_WhenFileTooSmall()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var tiny = WriteTempFile(dir, Payload(10, 5));
        Assert.Null(AppImageChannelOverride.TryReadFromFile(tiny, null));
        var empty = WriteTempFile(dir, Array.Empty<byte>());
        Assert.Null(AppImageChannelOverride.TryReadFromFile(empty, null));
    }

    [Fact]
    public void TryReadFromFile_ReturnsChannel_WhenFileIsExactlyOneTrailer()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteTempFile(dir, Trailer("stable"));
        Assert.Equal("stable", AppImageChannelOverride.TryReadFromFile(path, null));
    }

    [Fact]
    public void TryReadFromFile_ReturnsNull_WhenFileMissing()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, "does-not-exist.AppImage");
        Assert.Null(AppImageChannelOverride.TryReadFromFile(path, null));
    }

    [Fact]
    public void ParseWindow_ReturnsNull_WhenLengthIsZero()
    {
        var data = Concat(Payload(1000, 6), Trailer("junked", lengthOverride: 0));
        Assert.Null(AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void ParseWindow_ReturnsNull_WhenLengthTooLarge()
    {
        var channel = new string('a', 300);
        var data = Concat(Payload(500, 7), Trailer(channel, lengthOverride: 300));
        Assert.Null(AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void ParseWindow_ReturnsNull_WhenChannelTruncated()
    {
        var data = Concat(Payload(500, 8), Trailer("hello", lengthOverride: 20));
        Assert.Null(AppImageChannelOverride.ParseWindow(data));
    }

    [Theory]
    [InlineData((byte) 0x20)] // space
    [InlineData((byte) 0x00)] // NUL
    [InlineData((byte) 0x7F)] // DEL
    [InlineData((byte) 0xC3)] // multi-byte UTF-8 lead byte
    public void ParseWindow_ReturnsNull_WhenChannelContainsInvalidByte(byte invalid)
    {
        var channelBytes = new byte[] { (byte) 'c', (byte) 'h', (byte) 'a', invalid, (byte) 'n', (byte) 'l' };
        var data = Concat(Payload(500, 9), TrailerRaw(channelBytes));
        Assert.Null(AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void ParseWindow_SkipsInvalidEofTrailer_AndReturnsEarlierValidOne()
    {
        var data = Concat(Payload(500, 10), Trailer("beta"), Trailer("nope", lengthOverride: 0));
        Assert.Equal("beta", AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void ParseWindow_LastValidTrailerWins_WhenDoubleAppended()
    {
        var data = Concat(Payload(500, 11), Trailer("beta"), Trailer("stable"));
        Assert.Equal("stable", AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void ParseWindow_ToleratesTrailingGarbageAfterValidTrailer()
    {
        var data = Concat(Payload(500, 12), Trailer("stable"), Payload(40, 13));
        Assert.Equal("stable", AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void ParseWindow_ReturnsLongChannel_Verbatim()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 255; i++) {
            sb.Append((char) (0x21 + (i % (0x7E - 0x21 + 1))));
        }

        var channel = sb.ToString();
        Assert.Equal(255, channel.Length);
        var data = Concat(Payload(500, 14), Trailer(channel));
        Assert.Equal(channel, AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void ParseWindow_DoesNotCaseFoldChannel()
    {
        var data = Concat(Payload(500, 15), Trailer("StAbLe"));
        Assert.Equal("StAbLe", AppImageChannelOverride.ParseWindow(data));
    }

    [Fact]
    public void TryReadFromFile_FindsTrailer_AtStartOfScanWindow()
    {
        // magic starts exactly at (size - ScanWindowSize) — the first byte of the window
        const int size = 4096;
        var trailer = Trailer("stable");
        var data = Payload(size, 16);
        int pos = size - AppImageChannelOverride.ScanWindowSize;
        trailer.CopyTo(data, pos);
        // ensure nothing after the trailer resembles magic (Payload is magic-free by construction odds; overwrite deterministically)
        for (int i = pos + trailer.Length; i < size; i++) data[i] = 0x11;
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteTempFile(dir, data);
        Assert.Equal("stable", AppImageChannelOverride.TryReadFromFile(path, null));
    }

    [Fact]
    public void TryReadFromFile_MissesTrailer_OneByteBeforeScanWindow()
    {
        // magic starts at (size - ScanWindowSize - 1) — first magic byte falls outside the window
        const int size = 4096;
        var trailer = Trailer("stable");
        var data = Payload(size, 17);
        int pos = size - AppImageChannelOverride.ScanWindowSize - 1;
        trailer.CopyTo(data, pos);
        for (int i = pos + trailer.Length; i < size; i++) data[i] = 0x11;
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteTempFile(dir, data);
        Assert.Null(AppImageChannelOverride.TryReadFromFile(path, null));
    }

    [Fact]
    public void TryReadFromFile_FindsTrailer_WhenFileSmallerThanScanWindow()
    {
        const int size = 500;
        var trailer = Trailer("stable");
        var data = new byte[size];
        for (int i = 0; i < size; i++) data[i] = 0x22;
        trailer.CopyTo(data, 0); // trailer at start of file, garbage after
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteTempFile(dir, data);
        Assert.Equal("stable", AppImageChannelOverride.TryReadFromFile(path, null));
    }

    [Fact]
    public void Magic_IsSha256OfWellKnownString()
    {
        using var sha = SHA256.Create();
        var expected = sha.ComputeHash(Encoding.ASCII.GetBytes("velopack appimage channel override"));
        Assert.Equal(expected, AppImageChannelOverride.Magic);
    }

    [Fact]
    public void GoldenVector_MatchesCrossRepoContract()
    {
        // this exact byte string is asserted in the Rust tests (lib-rust locator.rs) and the
        // velopack.api promotion worker tests — do not change it without changing all three.
        const string goldenHex =
            "deed1bad3015b1969e6ebf7d093f5dca6c6c52a1a0a257571991628311d80351" + // magic
            "0600" +           // u16-le length = 6
            "737461626c65";    // "stable"
        var expected = new byte[goldenHex.Length / 2];
        for (int i = 0; i < expected.Length; i++) {
            expected[i] = Convert.ToByte(goldenHex.Substring(i * 2, 2), 16);
        }

        Assert.Equal(expected, Trailer("stable"));
        Assert.Equal("stable", AppImageChannelOverride.ParseWindow(expected));
    }
}
