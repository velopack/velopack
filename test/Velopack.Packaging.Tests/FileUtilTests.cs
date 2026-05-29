using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class FileUtilTests(ITestOutputHelper output)
{
    [Fact]
    public void CopiesFilesAndDirectoriesRecursively()
    {
        using var logger = output.BuildLoggerFor<FileUtilTests>();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        File.WriteAllText(Path.Combine(src, "root.txt"), "root");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "nested.txt"), "nested");
        Directory.CreateDirectory(Path.Combine(src, "sub", "deep"));
        File.WriteAllText(Path.Combine(src, "sub", "deep", "deep.txt"), "deep");

        FileUtil.CopyDirectoryContents(src, dest, logger);

        Assert.Equal("root", File.ReadAllText(Path.Combine(dest, "root.txt")));
        Assert.Equal("nested", File.ReadAllText(Path.Combine(dest, "sub", "nested.txt")));
        Assert.Equal("deep", File.ReadAllText(Path.Combine(dest, "sub", "deep", "deep.txt")));
    }

    [Fact]
    public void CopiesEmptyDirectory()
    {
        using var logger = output.BuildLoggerFor<FileUtilTests>();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        Directory.CreateDirectory(Path.Combine(src, "empty"));

        FileUtil.CopyDirectoryContents(src, dest, logger);

        Assert.True(Directory.Exists(Path.Combine(dest, "empty")));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(dest, "empty")));
    }

    [Fact]
    public void CreatesDestinationIfNotExists()
    {
        using var logger = output.BuildLoggerFor<FileUtilTests>();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var tmpRoot);
        var dest = Path.Combine(tmpRoot, "nonexistent", "nested");

        File.WriteAllText(Path.Combine(src, "file.txt"), "hello");

        FileUtil.CopyDirectoryContents(src, dest, logger);

        Assert.True(Directory.Exists(dest));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "file.txt")));
    }

    [Fact]
    public void ThrowsWhenSourceDoesNotExist()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tmpRoot);
        var bogus = Path.Combine(tmpRoot, "does_not_exist");

        Assert.Throws<ArgumentException>(() => FileUtil.CopyDirectoryContents(bogus, tmpRoot));
    }

    [Fact]
    public void PreservesInternalFileSymlink()
    {
        using var logger = output.BuildLoggerFor<FileUtilTests>();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        File.WriteAllText(Path.Combine(src, "real.txt"), "content");
        File.CreateSymbolicLink(Path.Combine(src, "link.txt"), "real.txt");

        FileUtil.CopyDirectoryContents(src, dest, logger);

        var linkInfo = new FileInfo(Path.Combine(dest, "link.txt"));
        Assert.Equal("real.txt", linkInfo.LinkTarget);
        Assert.Equal("content", File.ReadAllText(Path.Combine(dest, "link.txt")));
    }

    [Fact]
    public void PreservesInternalDirectorySymlink()
    {
        using var logger = output.BuildLoggerFor<FileUtilTests>();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        Directory.CreateDirectory(Path.Combine(src, "real_dir"));
        File.WriteAllText(Path.Combine(src, "real_dir", "file.txt"), "content");
        Directory.CreateSymbolicLink(Path.Combine(src, "link_dir"), "real_dir");

        FileUtil.CopyDirectoryContents(src, dest, logger);

        var linkInfo = new DirectoryInfo(Path.Combine(dest, "link_dir"));
        Assert.Equal("real_dir", linkInfo.LinkTarget);
        Assert.Equal("content", File.ReadAllText(Path.Combine(dest, "link_dir", "file.txt")));
    }

    [Fact]
    public void RejectsSymlinkPointingOutsideSourceRoot()
    {
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);
        using var _3 = TempUtil.GetTempDirectory(out var outside);

        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        File.CreateSymbolicLink(Path.Combine(src, "escape.txt"), Path.Combine(outside, "secret.txt"));

        var ex = Assert.Throws<UserInfoException>(() => FileUtil.CopyDirectoryContents(src, dest));
        Assert.Contains("outside the source directory", ex.Message);
    }

    [Fact]
    public void RejectsRelativeSymlinkEscapingSourceRoot()
    {
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        File.CreateSymbolicLink(Path.Combine(src, "escape.txt"), "../../etc/passwd");

        var ex = Assert.Throws<UserInfoException>(() => FileUtil.CopyDirectoryContents(src, dest));
        Assert.Contains("outside the source directory", ex.Message);
    }

    [Fact]
    public void RejectsDirectorySymlinkPointingOutsideSourceRoot()
    {
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);
        using var _3 = TempUtil.GetTempDirectory(out var outside);

        Directory.CreateSymbolicLink(Path.Combine(src, "escape_dir"), outside);

        var ex = Assert.Throws<UserInfoException>(() => FileUtil.CopyDirectoryContents(src, dest));
        Assert.Contains("outside the source directory", ex.Message);
    }

    [Fact]
    public void PreservesFileAndDirectoryTimestamps()
    {
        using var logger = output.BuildLoggerFor<FileUtilTests>();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        var pastDate = new DateTime(2020, 6, 15, 12, 30, 0, DateTimeKind.Utc);

        var subDir = Path.Combine(src, "sub");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "file.txt");
        File.WriteAllText(filePath, "content");

        File.SetLastWriteTimeUtc(filePath, pastDate);
        File.SetCreationTimeUtc(filePath, pastDate);
        Directory.SetLastWriteTimeUtc(subDir, pastDate);
        Directory.SetCreationTimeUtc(subDir, pastDate);

        FileUtil.CopyDirectoryContents(src, dest, logger);

        var copiedFile = Path.Combine(dest, "sub", "file.txt");
        var copiedDir = Path.Combine(dest, "sub");

        Assert.Equal(pastDate, File.GetLastWriteTimeUtc(copiedFile));
        Assert.Equal(pastDate, File.GetCreationTimeUtc(copiedFile));
        Assert.Equal(pastDate, Directory.GetLastWriteTimeUtc(copiedDir));
        Assert.Equal(pastDate, Directory.GetCreationTimeUtc(copiedDir));
    }

    [Fact]
    public void AllowsNestedInternalSymlinks()
    {
        using var logger = output.BuildLoggerFor<FileUtilTests>();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        // Mimics a typical macOS framework structure:
        // Versions/A/lib.dylib (real)
        // Versions/Current -> A (dir symlink)
        // lib.dylib -> Versions/Current/lib.dylib (file symlink)
        var versionsA = Path.Combine(src, "Versions", "A");
        Directory.CreateDirectory(versionsA);
        File.WriteAllText(Path.Combine(versionsA, "lib.dylib"), "binary");
        Directory.CreateSymbolicLink(Path.Combine(src, "Versions", "Current"), "A");
        File.CreateSymbolicLink(Path.Combine(src, "lib.dylib"), Path.Combine("Versions", "Current", "lib.dylib"));

        FileUtil.CopyDirectoryContents(src, dest, logger);

        var currentLink = new DirectoryInfo(Path.Combine(dest, "Versions", "Current"));
        Assert.Equal("A", currentLink.LinkTarget);

        var libLink = new FileInfo(Path.Combine(dest, "lib.dylib"));
        Assert.Equal(Path.Combine("Versions", "Current", "lib.dylib"), libLink.LinkTarget);

        Assert.Equal("binary", File.ReadAllText(Path.Combine(dest, "Versions", "A", "lib.dylib")));
        Assert.Equal("binary", File.ReadAllText(Path.Combine(dest, "lib.dylib")));
    }
}
