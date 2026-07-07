using System.Text;
using Velopack.Packaging.Unix;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class AppImageChannelTrailerTests
{
    private const int TrailerHeaderSize = 34; // 32 magic + 2 u16-le length

    private static byte[] RawTrailer(string channel, ushort? lengthOverride = null)
    {
        var channelBytes = Encoding.UTF8.GetBytes(channel);
        var length = lengthOverride ?? (ushort) channelBytes.Length;
        var magic = AppImageChannelOverride.Magic;
        var buf = new byte[magic.Length + 2 + channelBytes.Length];
        magic.CopyTo(buf, 0);
        buf[magic.Length] = (byte) (length & 0xFF);
        buf[magic.Length + 1] = (byte) (length >> 8);
        channelBytes.CopyTo(buf, magic.Length + 2);
        return buf;
    }

    private static string WriteSyntheticAppImage(string dir, int size, int seed)
    {
        var rng = new Random(seed);
        var buf = new byte[size];
        rng.NextBytes(buf);
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".AppImage");
        File.WriteAllBytes(path, buf);
        return path;
    }

    private static void AppendRaw(string path, byte[] bytes)
    {
        using var fs = File.Open(path, FileMode.Append, FileAccess.Write, FileShare.None);
        fs.Write(bytes, 0, bytes.Length);
    }

    private static void AssertPrefixIntact(byte[] original, string path)
    {
        var current = File.ReadAllBytes(path);
        Assert.True(current.Length >= original.Length, "File shrank below the original length.");
        for (int i = 0; i < original.Length; i++) {
            if (current[i] != original[i]) {
                Assert.Fail($"Original file contents were modified at offset {i}.");
            }
        }
    }

    [Fact]
    public void WriteChannelOverride_RoundTrips_OnSyntheticFile()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 1024 * 1024, 42);
        var original = File.ReadAllBytes(path);

        AppImageChannelTrailer.WriteChannelOverride(path, "stable");

        Assert.Equal(original.Length + TrailerHeaderSize + 6, new FileInfo(path).Length);
        Assert.True(AppImageChannelTrailer.TryReadChannelOverride(path, out var channel));
        Assert.Equal("stable", channel);
        AssertPrefixIntact(original, path);
    }

    [Fact]
    public void WriteChannelOverride_RoundTrips_OnRealAppImageFixture()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var path = PathHelper.CopyFixtureTo("LegacyTestApp-Velopack1298-linux.AppImage", dir);
        var original = File.ReadAllBytes(path);

        Assert.False(AppImageChannelTrailer.TryReadChannelOverride(path, out _));

        AppImageChannelTrailer.WriteChannelOverride(path, "win-x64-stable");

        Assert.True(AppImageChannelTrailer.TryReadChannelOverride(path, out var channel));
        Assert.Equal("win-x64-stable", channel);
        AssertPrefixIntact(original, path);
    }

    [Fact]
    public void WriteChannelOverride_StripsExistingTrailer_OnRePromotion()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 100_000, 43);
        long originalLength = new FileInfo(path).Length;

        AppImageChannelTrailer.WriteChannelOverride(path, "beta");
        AppImageChannelTrailer.WriteChannelOverride(path, "stable");

        Assert.Equal(originalLength + TrailerHeaderSize + 6, new FileInfo(path).Length);
        Assert.True(AppImageChannelTrailer.TryReadChannelOverride(path, out var channel));
        Assert.Equal("stable", channel);

        // repeated re-promotion must not grow the file
        for (int i = 0; i < 5; i++) {
            AppImageChannelTrailer.WriteChannelOverride(path, "stable");
        }

        Assert.Equal(originalLength + TrailerHeaderSize + 6, new FileInfo(path).Length);
    }

    [Fact]
    public void WriteChannelOverride_StripsMultipleTrailers_FromThirdPartyDoubleAppend()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 100_000, 44);
        long originalLength = new FileInfo(path).Length;

        AppImageChannelTrailer.WriteChannelOverride(path, "beta");
        AppendRaw(path, RawTrailer("stable")); // naive third-party append, bypassing the helper

        // last-valid-wins: the raw appended trailer is what the reader sees
        Assert.True(AppImageChannelTrailer.TryReadChannelOverride(path, out var channel));
        Assert.Equal("stable", channel);

        // the writer strips BOTH stacked trailers before appending
        AppImageChannelTrailer.WriteChannelOverride(path, "next");
        Assert.Equal(originalLength + TrailerHeaderSize + 4, new FileInfo(path).Length);
        Assert.True(AppImageChannelTrailer.TryReadChannelOverride(path, out channel));
        Assert.Equal("next", channel);
    }

    [Fact]
    public void WriteChannelOverride_SupersedesInvalidEofTrailer()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 100_000, 45);

        AppendRaw(path, RawTrailer("", lengthOverride: 0)); // invalid: length 0

        AppImageChannelTrailer.WriteChannelOverride(path, "stable");
        Assert.True(AppImageChannelTrailer.TryReadChannelOverride(path, out var channel));
        Assert.Equal("stable", channel);
    }

    [Fact]
    public void WriteChannelOverride_Throws_WhenChannelIsNull()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 1000, 46);
        Assert.ThrowsAny<ArgumentException>(() => AppImageChannelTrailer.WriteChannelOverride(path, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("tab\tchar")]
    [InlineData(" stable ")] // CONTRACTS §1.3: writers reject surrounding whitespace rather than trimming it
    [InlineData("stable ")]
    [InlineData(" stable")]
    [InlineData("\tstable")]
    [InlineData("héllo")]
    [InlineData("chan\u007Fnel")] // contains DEL (0x7F), outside the allowed 0x21..0x7E range
    public void WriteChannelOverride_Throws_WhenChannelIsInvalid(string channel)
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 1000, 47);
        Assert.Throws<ArgumentException>(() => AppImageChannelTrailer.WriteChannelOverride(path, channel));
    }

    [Fact]
    public void WriteChannelOverride_Throws_WhenChannelTooLong()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 1000, 48);
        Assert.Throws<ArgumentException>(() => AppImageChannelTrailer.WriteChannelOverride(path, new string('a', 256)));
    }

    [Fact]
    public void WriteChannelOverride_NormalizesChannelToLowercase()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 1000, 49);
        AppImageChannelTrailer.WriteChannelOverride(path, "MiXeD-Case");
        Assert.True(AppImageChannelTrailer.TryReadChannelOverride(path, out var channel));
        Assert.Equal("mixed-case", channel);
    }

    [Fact]
    public void WriteChannelOverride_RoundTrips_255CharChannel()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = WriteSyntheticAppImage(dir, 1000, 50);
        long originalLength = new FileInfo(path).Length;
        var channel = new string('x', 255);

        AppImageChannelTrailer.WriteChannelOverride(path, channel);

        Assert.Equal(originalLength + TrailerHeaderSize + 255, new FileInfo(path).Length);
        Assert.True(AppImageChannelTrailer.TryReadChannelOverride(path, out var read));
        Assert.Equal(channel, read);
    }

    [Fact]
    public void MissingFile_ThrowsOnWrite_ReturnsFalseOnRead()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, "does-not-exist.AppImage");
        Assert.Throws<FileNotFoundException>(() => AppImageChannelTrailer.WriteChannelOverride(path, "stable"));
        Assert.False(AppImageChannelTrailer.TryReadChannelOverride(path, out var channel));
        Assert.Null(channel);
    }
}
