using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Packaging.Unix;
using Velopack.TestCommon;
using Velopack.Util;
using static Velopack.TestCommon.OsxTestUtil;

namespace Velopack.Packaging.Tests;

/// <summary>
/// vpk [osx] pack off macOS: the portable zip written without ditto, which has to carry the Unix modes and symlinks
/// that ditto would, from whichever OS wrote it.
/// </summary>
public class OsxPortableZipTests(ITestOutputHelper output)
{
    private const UnixFileMode Mode755 = (UnixFileMode) 0x1ED;
    private const UnixFileMode Mode644 = (UnixFileMode) 0x1A4;

    /// <summary>
    /// A bundle with one of each kind of file whose mode Windows derives from content. On Unix the modes are read from
    /// the filesystem instead, so they are set here to what Windows derives, and both must write the same entries.
    /// Symlinks are optional so the tests that do not need them run on a Windows machine that cannot create them.
    /// </summary>
    private static string MakeBundle(string root, bool withSymlinks)
    {
        var app = Path.Combine(root, "My.app");
        var contents = Path.Combine(app, "Contents");
        var macos = Directory.CreateDirectory(Path.Combine(contents, "MacOS")).FullName;
        var resources = Directory.CreateDirectory(Path.Combine(contents, "Resources")).FullName;
        var helperMacos = Directory.CreateDirectory(Path.Combine(contents, "Frameworks", "Helper.app", "Contents", "MacOS")).FullName;
        var framework = Path.Combine(contents, "Frameworks", "Foo.framework");
        var versionA = Path.Combine(framework, "Versions", "A");

        WriteFile(Path.Combine(contents, "Info.plist"), "<plist/>", Mode644);
        WriteMachO(Path.Combine(macos, "MyApp"));
        WriteFile(Path.Combine(macos, "MyApp.dll"), "MZ", Mode755); // not Mach-O, but in Contents/MacOS
        WriteFile(Path.Combine(resources, "helper.sh"), "#!/bin/sh\necho hello\n", Mode755);
        WriteMachO(Path.Combine(resources, "libnative.dylib"));
        WriteFile(Path.Combine(resources, "data.json"), "{}", Mode644);
        WriteFile(Path.Combine(resources, "sq.version"), "<package/>", Mode644);
        // Neither Mach-O nor #!, but in a nested bundle's Contents/MacOS, as a helper app's launcher may be.
        WriteFile(Path.Combine(helperMacos, "launcher"), "exec ../Resources/helper\n", Mode755);
        if (withSymlinks) {
            WriteMachO(Path.Combine(versionA, "Foo"));
        }

        if (!VelopackRuntimeInfo.IsWindows) {
            // Directory.CreateDirectory leaves them to the umask.
            foreach (var d in Directory.EnumerateDirectories(app, "*", SearchOption.AllDirectories).Prepend(app)) {
                new DirectoryInfo(d).UnixFileMode = Mode755;
            }
        }

        if (withSymlinks) {
            // Created the way OsxPackCommandRunner creates sq.version's link, which on Windows stores relative targets
            // with backslashes: Velopack's own manifest link, and a framework's Versions/Current and top-level links.
            SymbolicLink.Create(Path.Combine(macos, "sq.version"), Path.Combine(resources, "sq.version"), relative: true);
            SymbolicLink.Create(Path.Combine(framework, "Versions", "Current"), versionA, relative: true);
            SymbolicLink.Create(Path.Combine(framework, "Foo"), Path.Combine(framework, "Versions", "Current", "Foo"), relative: true);
        }

        return app;
    }

    private static void WriteFile(string path, string content, UnixFileMode mode)
    {
        File.WriteAllText(path, content);
        if (!VelopackRuntimeInfo.IsWindows) {
            File.SetUnixFileMode(path, mode);
        }
    }

    /// <summary>Each entry as "name mode", the mode in octal as ls and zipinfo print it, for readable diffs.</summary>
    private static IEnumerable<string> Listing(IEnumerable<ZipArchiveEntry> entries) =>
        entries.Select(e => e.FullName + " " + Convert.ToString(UnixModeOf(e), 8)).Order(StringComparer.Ordinal);

    private static string ReadLink(ZipArchiveEntry entry)
    {
        Assert.Equal(S_IFLNK, UnixModeOf(entry) & S_IFMT);
        return ReadText(entry);
    }

    /// <summary>The host byte of "version made by" of every central directory entry.</summary>
    private static List<byte> CentralDirectoryHosts(Stream zip)
    {
        return CentralDirectoryEntries(zip).Select(offset => {
            zip.Position = offset + 5;
            return (byte) zip.ReadByte();
        }).ToList();
    }

    /// <summary>Overwrites the host byte of "version made by" of every central directory entry.</summary>
    private static void SetCentralDirectoryHosts(Stream zip, byte host)
    {
        foreach (var offset in CentralDirectoryEntries(zip)) {
            zip.Position = offset + 5;
            zip.WriteByte(host);
        }
    }

    /// <summary>
    /// The offset of every central directory entry, found the way unzippers find them: from the end of central
    /// directory record, or the Zip64 one its locator points to.
    /// </summary>
    private static List<long> CentralDirectoryEntries(Stream zip)
    {
        using var reader = new BinaryReader(zip, Encoding.UTF8, leaveOpen: true);
        long end = zip.Length - 22;
        zip.Position = end;
        Assert.Equal(0x06054b50u, reader.ReadUInt32());
        zip.Position = end + 10;
        long entries = reader.ReadUInt16();
        zip.Position = end + 16;
        long offset = reader.ReadUInt32();

        if (IsZip64(zip)) {
            zip.Position = end - 12;
            long record = (long) reader.ReadUInt64();
            zip.Position = record + 32;
            entries = (long) reader.ReadUInt64();
            zip.Position = record + 48;
            offset = (long) reader.ReadUInt64();
        }

        var offsets = new List<long>();
        for (long i = 0; i < entries; i++) {
            zip.Position = offset;
            Assert.Equal(0x02014b50u, reader.ReadUInt32());
            offsets.Add(offset);
            zip.Position = offset + 28;
            offset += 46 + reader.ReadUInt16() + reader.ReadUInt16() + reader.ReadUInt16();
        }

        return offsets;
    }

    private static bool IsZip64(Stream zip)
    {
        long locator = zip.Length - 22 - 20;
        if (locator < 0) {
            return false;
        }

        zip.Position = locator;
        Span<byte> signature = stackalloc byte[4];
        zip.ReadExactly(signature);
        return BitConverter.ToUInt32(signature) == 0x07064b50;
    }

    [Fact]
    public void PortableZipKeepsTheBundleAsTheTopLevelEntry()
    {
        using var logger = output.BuildLoggerFor<OsxPortableZipTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir, withSymlinks: false);
        var zipPath = Path.Combine(dir, "portable.zip");

        OsxPortableZip.Create(logger, app, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal("My.app/", zip.Entries[0].FullName);
        Assert.All(zip.Entries, e => Assert.StartsWith("My.app/", e.FullName));
        Assert.Contains(zip.Entries, e => e.FullName == "My.app/Contents/Info.plist");
    }

    [Fact]
    public void PortableZipRecordsUnixModes()
    {
        // Directories and everything a bundle may need to execute are 0755, anything else 0644, whether the modes were
        // read from the filesystem (Unix) or derived from the bundle layout and the file contents (Windows).
        using var logger = output.BuildLoggerFor<OsxPortableZipTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir, withSymlinks: false);
        var zipPath = Path.Combine(dir, "portable.zip");

        OsxPortableZip.Create(logger, app, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        string[] expected = [
            "My.app/ 40755",
            "My.app/Contents/ 40755",
            "My.app/Contents/Frameworks/ 40755",
            "My.app/Contents/Frameworks/Helper.app/ 40755",
            "My.app/Contents/Frameworks/Helper.app/Contents/ 40755",
            "My.app/Contents/Frameworks/Helper.app/Contents/MacOS/ 40755",
            "My.app/Contents/Frameworks/Helper.app/Contents/MacOS/launcher 100755",
            "My.app/Contents/Info.plist 100644",
            "My.app/Contents/MacOS/ 40755",
            "My.app/Contents/MacOS/MyApp 100755",
            "My.app/Contents/MacOS/MyApp.dll 100755",
            "My.app/Contents/Resources/ 40755",
            "My.app/Contents/Resources/data.json 100644",
            "My.app/Contents/Resources/helper.sh 100755",
            "My.app/Contents/Resources/libnative.dylib 100755",
            "My.app/Contents/Resources/sq.version 100644",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Listing(zip.Entries));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void PortableZipMasksUnsafeModesOnUnix()
    {
        // Filesystems without real modes (WSL's /mnt/c, SMB, FAT) report 0777 on everything: no world-writable bundle,
        // and no setuid, sticky or group/other write bits however they got there. Modes are otherwise kept as read.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "Windows has no modes to read; they are derived from content");
        using var logger = output.BuildLoggerFor<OsxPortableZipTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir, withSymlinks: false);
        var zipPath = Path.Combine(dir, "portable.zip");

        File.SetUnixFileMode(Path.Combine(app, "Contents", "MacOS", "MyApp"), UnixFileMode.SetUser | (UnixFileMode) 0x1FF);
        File.SetUnixFileMode(Path.Combine(app, "Contents", "Resources", "data.json"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        new DirectoryInfo(Path.Combine(app, "Contents", "Resources")).UnixFileMode = UnixFileMode.StickyBit | (UnixFileMode) 0x1FF;

        OsxPortableZip.Create(logger, app, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var listing = Listing(zip.Entries).ToList();
        Assert.Contains("My.app/Contents/MacOS/MyApp 100755", listing);
        Assert.Contains("My.app/Contents/Resources/data.json 100600", listing);
        Assert.Contains("My.app/Contents/Resources/ 40755", listing);
    }

    [Fact]
    public void PortableZipIsDeterministicAndIgnoresFileTimestamps()
    {
        // Every entry carries one fixed timestamp, so a file dated before 1980 (SOURCE_DATE_EPOCH=0), which a zip
        // entry cannot record, packs like any other, and the same bundle always gives the same bytes.
        using var logger = output.BuildLoggerFor<OsxPortableZipTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir, withSymlinks: false);
        var first = Path.Combine(dir, "first.zip");
        var second = Path.Combine(dir, "second.zip");

        OsxPortableZip.Create(logger, app, first);
        foreach (var file in Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories)) {
            File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch);
        }

        OsxPortableZip.Create(logger, app, second);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        using var zip = ZipFile.OpenRead(second);
        Assert.All(zip.Entries, e => Assert.Equal(new DateTime(1980, 1, 1), e.LastWriteTime.DateTime));
    }

    [Fact]
    public void PortableZipStoresSymlinksAsRelativeLinks()
    {
        using var logger = output.BuildLoggerFor<OsxPortableZipTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        var app = MakeBundle(dir, withSymlinks: true);
        var zipPath = Path.Combine(dir, "portable.zip");

        // An absolute target inside the bundle would point at this machine once expanded on the Mac: stored relative.
        var resources = Path.Combine(app, "Contents", "Resources");
        File.CreateSymbolicLink(Path.Combine(resources, "absolute.json"), Path.Combine(resources, "data.json"));

        OsxPortableZip.Create(logger, app, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal("../Resources/sq.version", ReadLink(zip.GetEntry("My.app/Contents/MacOS/sq.version")!));
        Assert.Equal("data.json", ReadLink(zip.GetEntry("My.app/Contents/Resources/absolute.json")!));

        // A directory link is one entry, never followed: Versions/A is not zipped a second time under Current.
        const string framework = "My.app/Contents/Frameworks/Foo.framework/";
        Assert.Equal("A", ReadLink(zip.GetEntry(framework + "Versions/Current")!));
        Assert.Equal("Versions/Current/Foo", ReadLink(zip.GetEntry(framework + "Foo")!));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith(framework + "Versions/Current/", StringComparison.Ordinal));
        Assert.Contains(framework + "Versions/A/Foo 100755", Listing(zip.Entries));

        Assert.All(zip.Entries.Where(e => (UnixModeOf(e) & S_IFMT) == S_IFLNK), e => Assert.Equal(S_IFLNK | 0x1FF, UnixModeOf(e)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PortableZipRefusesSymlinksOutOfTheBundle(bool absolute)
    {
        using var logger = output.BuildLoggerFor<OsxPortableZipTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        var app = MakeBundle(dir, withSymlinks: false);
        var zipPath = Path.Combine(dir, "portable.zip");

        var outside = Path.Combine(dir, "outside.txt");
        File.WriteAllText(outside, "not part of the app");
        var target = absolute ? outside : Path.Combine("..", "..", "..", "outside.txt");
        File.CreateSymbolicLink(Path.Combine(app, "Contents", "MacOS", "escape"), target);

        var ex = Assert.Throws<UserInfoException>(() => OsxPortableZip.Create(logger, app, zipPath));
        Assert.Contains("My.app/Contents/MacOS/escape", ex.Message);
        Assert.False(File.Exists(zipPath), "a failed pack should not leave a partial zip behind");
    }

    [Fact]
    public void PortableZipIsMarkedAsMadeOnUnix()
    {
        // Without host 3 in "version made by", unzippers ignore the modes: a zip .NET writes on Windows says Windows.
        using var logger = output.BuildLoggerFor<OsxPortableZipTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir, withSymlinks: false);
        var zipPath = Path.Combine(dir, "portable.zip");

        OsxPortableZip.Create(logger, app, zipPath);

        List<byte> hosts;
        using (var stream = File.OpenRead(zipPath)) {
            hosts = CentralDirectoryHosts(stream);
        }

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(zip.Entries.Count, hosts.Count);
        Assert.All(hosts, h => Assert.Equal(3, h));
    }

    [Fact]
    public void MarkingMadeOnUnixFollowsZip64Records()
    {
        // Past 65534 entries ZipArchive writes Zip64 end records, and the count in the ordinary one is saturated.
        // A bundle that large is cheaper to fake in memory than on disk; the patching is the same either way.
        const int Count = 0x10000;
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            for (int i = 0; i < Count; i++) {
                archive.CreateEntry("My.app/" + i);
            }
        }

        Assert.True(IsZip64(stream), "ZipArchive was expected to write Zip64 records for this many entries");

        // ZipArchive already writes host 3 when it runs on Unix, so every entry is reset to 0 (MS-DOS) first, or this
        // would pass there without MarkMadeOnUnix doing anything. Stopping at the saturated count in the ordinary end
        // record would leave the last entry at 0.
        SetCentralDirectoryHosts(stream, 0);
        Assert.Equal(new byte[] { 0 }, CentralDirectoryHosts(stream).Distinct().ToArray());

        OsxPortableZip.MarkMadeOnUnix(stream);

        var hosts = CentralDirectoryHosts(stream);
        Assert.Equal(Count, hosts.Count);
        Assert.Equal(new byte[] { 3 }, hosts.Distinct().ToArray());

        stream.Position = 0;
        using var reread = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        Assert.Equal(Count, reread.Entries.Count);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void PortableZipExpandsWithUnixMetadataIntact()
    {
        // The zip is only as good as what an unzipper does with it. On macOS that is ditto, which shares Archive
        // Utility's (Finder's) unzipper; on Linux, Info-ZIP, which honours the same attributes. Not on Windows, which
        // has no modes to expand into (the zip's own attributes are checked above on every OS).
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "extracting Unix modes needs a Unix filesystem");
        using var logger = output.BuildLoggerFor<OsxPortableZipTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir, withSymlinks: true);
        var zipPath = Path.Combine(dir, "portable.zip");
        OsxPortableZip.Create(logger, app, zipPath);

        var extractTo = Directory.CreateDirectory(Path.Combine(dir, "out")).FullName;
        if (VelopackRuntimeInfo.IsOSX) {
            RunTool("ditto", "-x", "-k", zipPath, extractTo);
        } else {
            RunTool("unzip", "-q", zipPath, "-d", extractTo);
        }

        var contents = Path.Combine(extractTo, "My.app", "Contents");
        Assert.True(File.GetUnixFileMode(Path.Combine(contents, "MacOS", "MyApp")).HasFlag(UnixFileMode.UserExecute));
        Assert.True(File.GetUnixFileMode(Path.Combine(contents, "Resources", "helper.sh")).HasFlag(UnixFileMode.UserExecute));
        Assert.False(File.GetUnixFileMode(Path.Combine(contents, "Info.plist")).HasFlag(UnixFileMode.UserExecute));
        var launcher = Path.Combine(contents, "Frameworks", "Helper.app", "Contents", "MacOS", "launcher");
        Assert.True(File.GetUnixFileMode(launcher).HasFlag(UnixFileMode.UserExecute));

        var link = new FileInfo(Path.Combine(contents, "MacOS", "sq.version"));
        Assert.Equal("../Resources/sq.version", link.LinkTarget);
        Assert.Equal("<package/>", File.ReadAllText(link.FullName));

        var framework = Path.Combine(contents, "Frameworks", "Foo.framework");
        Assert.Equal("A", new DirectoryInfo(Path.Combine(framework, "Versions", "Current")).LinkTarget);
        var packedFoo = Path.Combine(app, "Contents", "Frameworks", "Foo.framework", "Versions", "A", "Foo");
        Assert.Equal(File.ReadAllBytes(packedFoo), File.ReadAllBytes(Path.Combine(framework, "Foo")));
    }

    /// <summary>
    /// Runs an unzipper to completion, with its output in the test log. Skips locally where it is not installed; in CI
    /// it is part of the runner image, so its absence fails the test.
    /// </summary>
    private void RunTool(string tool, params string[] args)
    {
        (int ExitCode, string StdOutput, string StdErr, string Command) result;
        try {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            result = Exe.InvokeProcess(tool, args, null, timeout.Token);
        } catch (Win32Exception) when (!PathHelper.IsCI) {
            Assert.Skip($"{tool} is not installed");
            return;
        }

        output.WriteLine(result.Command);
        output.WriteLine(result.StdOutput);
        output.WriteLine(result.StdErr);
        Assert.True(result.ExitCode == 0, $"{tool} exited with code {result.ExitCode}");
    }
}
