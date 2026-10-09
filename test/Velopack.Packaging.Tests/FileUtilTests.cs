using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.TestCommon;
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
        TestHelper.SkipUnlessSymlinksCanBeCreated();
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
        TestHelper.SkipUnlessSymlinksCanBeCreated();
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
        TestHelper.SkipUnlessSymlinksCanBeCreated();
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
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        File.CreateSymbolicLink(Path.Combine(src, "escape.txt"), "../../etc/passwd");

        var ex = Assert.Throws<UserInfoException>(() => FileUtil.CopyDirectoryContents(src, dest));
        Assert.Contains("outside the source directory", ex.Message);
    }

    [Fact]
    public void RejectsDirectorySymlinkPointingOutsideSourceRoot()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);
        using var _3 = TempUtil.GetTempDirectory(out var outside);

        Directory.CreateSymbolicLink(Path.Combine(src, "escape_dir"), outside);

        var ex = Assert.Throws<UserInfoException>(() => FileUtil.CopyDirectoryContents(src, dest));
        Assert.Contains("outside the source directory", ex.Message);
    }

    [Fact]
    public void ValidateSymlinksIgnoresSkippedFileSymlinks()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var outside);

        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.CreateSymbolicLink(Path.Combine(src, "sub", "skipped.txt"), Path.Combine(outside, "secret.txt"));

        // The callback receives the link's path relative to the source root.
        FileUtil.ValidateSymlinks(src, path => path == Path.Combine("sub", "skipped.txt"));

        var ex = Assert.Throws<UserInfoException>(() => FileUtil.ValidateSymlinks(src, path => false));
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
        TestHelper.SkipUnlessSymlinksCanBeCreated();
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

    [Fact]
    public void CopiesSymlinkCycleAsLinkWithoutRecursing()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var logger = output.BuildLoggerFor<FileUtilTests>();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "file.txt"), "content");
        Directory.CreateSymbolicLink(Path.Combine(src, "sub", "loop"), "..");

        FileUtil.CopyDirectoryContents(src, dest, logger);

        Assert.Equal("..", new DirectoryInfo(Path.Combine(dest, "sub", "loop")).LinkTarget);
        Assert.Equal("content", File.ReadAllText(Path.Combine(dest, "sub", "file.txt")));
    }

    [Fact]
    public void CopyFilesRecursiveRewritesAbsoluteInternalSymlinkAsRelative()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        Directory.CreateDirectory(Path.Combine(src, "real_dir"));
        File.WriteAllText(Path.Combine(src, "real_dir", "file.txt"), "content");
        Directory.CreateDirectory(Path.Combine(src, "links"));
        File.CreateSymbolicLink(Path.Combine(src, "links", "file.txt"), Path.Combine(src, "real_dir", "file.txt"));
        Directory.CreateSymbolicLink(Path.Combine(src, "links", "dir"), Path.Combine(src, "real_dir"));

        FileUtil.ValidateSymlinks(src);
        FileUtil.CopyFilesRecursive(new DirectoryInfo(src), new DirectoryInfo(dest), preserveSymlinks: true);

        // an absolute target would still point into src; the copy must only reference itself
        Assert.Equal(Path.Combine("..", "real_dir", "file.txt"), new FileInfo(Path.Combine(dest, "links", "file.txt")).LinkTarget);
        Assert.Equal(Path.Combine("..", "real_dir"), new DirectoryInfo(Path.Combine(dest, "links", "dir")).LinkTarget);
        Assert.Equal("content", File.ReadAllText(Path.Combine(dest, "links", "dir", "file.txt")));
    }

    [Fact]
    public void CopyFilesRecursiveCopiesSymlinkTargetsWhenNotPreserving()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        Directory.CreateDirectory(Path.Combine(src, "real_dir"));
        File.WriteAllText(Path.Combine(src, "real_dir", "file.txt"), "content");
        File.CreateSymbolicLink(Path.Combine(src, "link.txt"), Path.Combine("real_dir", "file.txt"));
        Directory.CreateSymbolicLink(Path.Combine(src, "link_dir"), "real_dir");

        FileUtil.CopyFilesRecursive(new DirectoryInfo(src), new DirectoryInfo(dest), preserveSymlinks: false);

        var file = new FileInfo(Path.Combine(dest, "link.txt"));
        Assert.Null(file.LinkTarget);
        Assert.Equal("content", File.ReadAllText(file.FullName));
        var dir = new DirectoryInfo(Path.Combine(dest, "link_dir"));
        Assert.Null(dir.LinkTarget);
        Assert.Equal("content", File.ReadAllText(Path.Combine(dir.FullName, "file.txt")));
    }

    [Fact]
    public void CopyFilesRecursiveSkipsFilesAndReportsProgress()
    {
        using var _1 = TempUtil.GetTempDirectory(out var src);
        using var _2 = TempUtil.GetTempDirectory(out var dest);

        File.WriteAllText(Path.Combine(src, "keep.txt"), "keep");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "sub", "skip.pdb"), "skip");

        var reported = new List<int>();
        var offered = new List<string>();
        FileUtil.CopyFilesRecursive(
            new DirectoryInfo(src),
            new DirectoryInfo(dest),
            preserveSymlinks: true,
            shouldSkipFile: path => {
                offered.Add(path);
                return path.EndsWith(".pdb", StringComparison.Ordinal);
            },
            progress: reported.Add);

        // The callback receives each file's path relative to the source root, like ValidateSymlinks.
        Assert.Equal(new[] { "keep.txt", Path.Combine("sub", "skip.pdb") }, offered.Order(StringComparer.Ordinal));

        Assert.True(File.Exists(Path.Combine(dest, "keep.txt")));
        Assert.True(Directory.Exists(Path.Combine(dest, "sub")));
        Assert.False(File.Exists(Path.Combine(dest, "sub", "skip.pdb")));
        Assert.Equal(100, reported.Last());
        Assert.All(reported, p => Assert.InRange(p, 0, 100));
    }

    [Fact]
    public void EnumerateFilesWithoutFollowingSymlinksSkipsLinkedDirectories()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var src);

        Directory.CreateDirectory(Path.Combine(src, "real_dir"));
        File.WriteAllText(Path.Combine(src, "real_dir", "file.txt"), "content");
        File.CreateSymbolicLink(Path.Combine(src, "link.txt"), Path.Combine("real_dir", "file.txt"));
        Directory.CreateSymbolicLink(Path.Combine(src, "link_dir"), "real_dir");

        var files = FileUtil.EnumerateFilesWithoutFollowingSymlinks(new DirectoryInfo(src))
            .Select(f => Path.GetRelativePath(src, f.FullName))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "link.txt", Path.Combine("real_dir", "file.txt") }, files);
    }

    [Fact]
    public void CreateRelativeSymlinkStoresRelativeTargetAndOverwrites()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var root);

        Directory.CreateDirectory(Path.Combine(root, "Resources"));
        Directory.CreateDirectory(Path.Combine(root, "MacOS"));
        File.WriteAllText(Path.Combine(root, "Resources", "sq.version"), "manifest");

        // An existing file at the link path (e.g. a previous pack's link) is replaced.
        File.WriteAllText(Path.Combine(root, "MacOS", "sq.version"), "stale");

        FileUtil.CreateRelativeSymlink(Path.Combine(root, "MacOS", "sq.version"), Path.Combine(root, "Resources", "sq.version"));

        var link = new FileInfo(Path.Combine(root, "MacOS", "sq.version"));
        Assert.Equal(Path.Combine("..", "Resources", "sq.version"), link.LinkTarget);
        Assert.Equal("manifest", File.ReadAllText(link.FullName));
    }
}
