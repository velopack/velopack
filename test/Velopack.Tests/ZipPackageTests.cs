using Velopack.Util;

namespace Velopack.Tests;

public class ZipPackageTests
{
    private readonly ITestOutputHelper _output;
    public ZipPackageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void EazyZipPreservesSymlinks()
    {
        using var logger = _output.BuildLoggerFor<ZipPackageTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        using var _2 = TempUtil.GetTempDirectory(out var zipDir);
        using var _3 = TempUtil.GetTempDirectory(out var extractedDir);

        var actual = Path.Combine(tempDir, "actual");
        var actualFile = Path.Combine(actual, "file.txt");

        var other = Path.Combine(tempDir, "other");
        var symlink = Path.Combine(other, "syml");
        var symfile = Path.Combine(other, "sym.txt");
        var zipFile = Path.Combine(zipDir, "test.zip");

        Directory.CreateDirectory(actual);
        Directory.CreateDirectory(other);
        File.WriteAllText(actualFile, "hello");
        SymbolicLink.Create(symlink, actual);
        SymbolicLink.Create(symfile, actualFile);

        Compression.EasyZip.CreateZipFromDirectoryAsync(logger, zipFile, tempDir).GetAwaiterResult();
        Compression.EasyZip.ExtractZipToDirectory(logger, zipFile, extractedDir, expandSymlinks: true);

        Assert.True(File.Exists(Path.Combine(extractedDir, "actual", "file.txt")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(extractedDir, "actual", "file.txt")));
        Assert.False(SymbolicLink.Exists(Path.Combine(extractedDir, "actual", "file.txt")));

        Assert.True(Directory.Exists(Path.Combine(extractedDir, "other", "syml")));
        Assert.True(File.Exists(Path.Combine(extractedDir, "other", "sym.txt")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(extractedDir, "other", "sym.txt")));
        Assert.True(SymbolicLink.Exists(Path.Combine(extractedDir, "other", "syml")));
        Assert.True(SymbolicLink.Exists(Path.Combine(extractedDir, "other", "sym.txt")));

        Assert.Equal($"..{Path.DirectorySeparatorChar}actual{Path.DirectorySeparatorChar}file.txt", SymbolicLink.GetTarget(Path.Combine(extractedDir, "other", "sym.txt"), relative: true));
    }
}
