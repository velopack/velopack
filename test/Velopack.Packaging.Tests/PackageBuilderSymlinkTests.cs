using System.IO.Compression;
using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Compression;
using Velopack.TestCommon;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;
using static Velopack.TestCommon.OsxTestUtil;

namespace Velopack.Packaging.Tests;

/// <summary>
/// Runs the shared <see cref="PackageBuilder{T, TValidator}"/> pipeline (pre-process copy with excludes, then the release
/// package copy and zip) on a bundle containing symlinks, as every OS does when packing for macOS.
/// </summary>
public class PackageBuilderSymlinkTests(ITestOutputHelper output)
{
    private const string AppDir = "lib/app/";

    [Fact]
    public async Task MacReleasePackageKeepsSymlinksAsLinks()
    {
        using var logger = output.BuildLoggerFor<PackageBuilderSymlinkTests>();
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _ = TempUtil.GetTempDirectory(out var root);
        var app = CreateBundleWithSymlinks(root);

        var entries = await PackAndListEntries(RuntimeOs.OSX, app, Path.Combine(root, "releases"), logger);

        // file symlink, stored the way OsxPackCommandRunner links sq.version out of Contents/MacOS
        Assert.Equal("../Resources/sq.version", entries["Contents/MacOS/sq.version.__symlink"]);
        Assert.DoesNotContain("Contents/MacOS/sq.version", entries.Keys);
        Assert.Equal("manifest", entries["Contents/Resources/sq.version"]);

        // directory symlink: one entry, its target's contents are not duplicated beneath it
        const string framework = "Contents/Frameworks/Lib.framework/";
        Assert.Equal("A/", entries[framework + "Versions/Current.__symlink"]);
        Assert.DoesNotContain(entries.Keys, e => e.StartsWith(framework + "Versions/Current/", StringComparison.Ordinal));
        Assert.Equal("Versions/Current/Lib", entries[framework + "Lib.__symlink"]);
        Assert.Equal("binary", entries[framework + "Versions/A/Lib"]);
        Assert.Single(entries.Values, v => v == "binary");

        // the pre-process copy still applies --exclude
        Assert.DoesNotContain("Contents/MacOS/App.pdb", entries.Keys);
        Assert.Equal("main", entries["Contents/MacOS/App"]);
    }

    [Fact]
    public async Task MacReleasePackageRejectsSymlinkEscapingBundle()
    {
        using var logger = output.BuildLoggerFor<PackageBuilderSymlinkTests>();
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _ = TempUtil.GetTempDirectory(out var root);
        var app = CreateBundleWithSymlinks(root);
        var outside = Path.Combine(root, "outside.txt");
        File.WriteAllText(outside, "secret");
        SymbolicLink.Create(Path.Combine(app, "Contents", "Resources", "escape.txt"), outside, relative: true);

        var ex = await Assert.ThrowsAsync<UserInfoException>(
            () => PackAndListEntries(RuntimeOs.OSX, app, Path.Combine(root, "releases"), logger));
        Assert.Contains("outside the source directory", ex.Message);
    }

    [Fact]
    public async Task MacReleasePackageIgnoresExcludedSymlinkEscapingBundle()
    {
        using var logger = output.BuildLoggerFor<PackageBuilderSymlinkTests>();
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _ = TempUtil.GetTempDirectory(out var root);
        var app = CreateBundleWithSymlinks(root);
        var outside = Path.Combine(root, "outside.pdb");
        File.WriteAllText(outside, "secret");
        SymbolicLink.Create(Path.Combine(app, "Contents", "MacOS", "escape.pdb"), outside, relative: true);

        // --exclude drops the link before anything is zipped, so it does not have to stay inside the bundle
        var entries = await PackAndListEntries(RuntimeOs.OSX, app, Path.Combine(root, "releases"), logger);

        Assert.DoesNotContain(entries.Keys, e => e.StartsWith("Contents/MacOS/escape.pdb", StringComparison.Ordinal));
        Assert.Equal("../Resources/sq.version", entries["Contents/MacOS/sq.version.__symlink"]);
    }

    [Fact]
    public async Task LinuxPackageAcceptsSymlinkEscapingAppDir()
    {
        using var logger = output.BuildLoggerFor<PackageBuilderSymlinkTests>();
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _ = TempUtil.GetTempDirectory(out var root);
        var appDir = Path.Combine(root, "App.AppDir");
        var bin = Directory.CreateDirectory(Path.Combine(appDir, "usr", "bin")).FullName;
        File.WriteAllText(Path.Combine(bin, "App"), "main");
        var systemLib = Path.Combine(root, "libfoo.so.1");
        File.WriteAllText(systemLib, "system library");
        // e.g. usr/lib/libfoo.so -> /usr/lib/x86_64-linux-gnu/libfoo.so.1, which mksquashfs stores in the AppImage as-is
        var lib = Directory.CreateDirectory(Path.Combine(appDir, "usr", "lib")).FullName;
        SymbolicLink.Create(Path.Combine(lib, "libfoo.so"), systemLib, relative: false);

        var builder = await Pack(RuntimeOs.Linux, appDir, Path.Combine(root, "releases"), "usr/bin/App", logger);

        var (linkTarget, text) = builder.LinuxAppDirFiles["usr/lib/libfoo.so"];
        Assert.Equal("system library", text);
        if (VelopackRuntimeInfo.IsWindows) {
            Assert.Null(linkTarget); // Windows hosts copy what links point to
        } else {
            Assert.Equal(systemLib, linkTarget);
        }
    }

    [Fact]
    public async Task WindowsReleasePackageBuiltOnWindowsCopiesSymlinkTargets()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "only Windows hosts copy what symlinks point to ('cp -a' keeps them)");
        using var logger = output.BuildLoggerFor<PackageBuilderSymlinkTests>();
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _ = TempUtil.GetTempDirectory(out var root);
        var app = CreateBundleWithSymlinks(root);

        var entries = await PackAndListEntries(RuntimeOs.Windows, app, Path.Combine(root, "releases"), logger);

        Assert.DoesNotContain(entries.Keys, e => e.EndsWith(".__symlink", StringComparison.Ordinal));
        Assert.Equal("manifest", entries["Contents/MacOS/sq.version"]);
        Assert.Equal("binary", entries["Contents/Frameworks/Lib.framework/Versions/Current/Lib"]);
    }

    /// <summary>
    /// A minimal .app: Contents/MacOS/sq.version links to Resources, and a framework has the usual Versions/Current
    /// directory link plus a top-level file link through it. All links are relative, as on a real bundle.
    /// </summary>
    private static string CreateBundleWithSymlinks(string root)
    {
        var app = Path.Combine(root, "App.app");
        var macos = Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS")).FullName;
        var resources = Directory.CreateDirectory(Path.Combine(app, "Contents", "Resources")).FullName;
        var framework = Path.Combine(app, "Contents", "Frameworks", "Lib.framework");
        var versionA = Directory.CreateDirectory(Path.Combine(framework, "Versions", "A")).FullName;

        File.WriteAllText(Path.Combine(macos, "App"), "main");
        File.WriteAllText(Path.Combine(macos, "App.pdb"), "symbols");
        File.WriteAllText(Path.Combine(resources, "sq.version"), "manifest");
        File.WriteAllText(Path.Combine(versionA, "Lib"), "binary");

        SymbolicLink.Create(Path.Combine(macos, "sq.version"), Path.Combine(resources, "sq.version"), relative: true);
        SymbolicLink.Create(Path.Combine(framework, "Versions", "Current"), versionA, relative: true);
        SymbolicLink.Create(Path.Combine(framework, "Lib"), Path.Combine(framework, "Versions", "Current", "Lib"), relative: true);
        return app;
    }

    /// <summary>Packs <paramref name="packDir"/> and returns each lib/app entry of the full nupkg (relative) with its text.</summary>
    private static async Task<Dictionary<string, string>> PackAndListEntries(RuntimeOs targetOs, string packDir, string releaseDir,
        ILogger logger)
    {
        await Pack(targetOs, packDir, releaseDir, "Contents/MacOS/App", logger);

        var nupkg = Directory.GetFiles(releaseDir, "*-full.nupkg").Single();
        using var zip = ZipFile.OpenRead(nupkg);
        return zip.Entries
            .Where(e => e.FullName.StartsWith(AppDir, StringComparison.Ordinal) && !e.FullName.EndsWith('/'))
            .ToDictionary(e => e.FullName.Substring(AppDir.Length), ReadText);
    }

    private static async Task<TestPackageBuilder> Pack(RuntimeOs targetOs, string packDir, string releaseDir, string mainExe,
        ILogger logger)
    {
        var options = new TestPackOptions {
            PackId = "SymlinkApp",
            PackVersion = "1.0.0",
            PackDirectory = packDir,
            ReleaseDir = new DirectoryInfo(releaseDir),
            EntryExecutableName = mainExe,
            TargetRuntime = RID.Parse(targetOs.GetOsShortName() + "-x64"),
            Exclude = @".*\.pdb",
            NoPortable = true,
            NoInst = true,
        };

        var builder = new TestPackageBuilder(targetOs, logger, new BasicConsole(logger, new VelopackDefaults(false)));
        await builder.PackAsync(options);
        return builder;
    }

    private sealed class TestPackOptions : IPackOptions
    {
        public string PackId { get; set; }
        public string PackVersion { get; set; }
        public string PackDirectory { get; set; }
        public string PackAuthors { get; set; }
        public string PackTitle { get; set; }
        public string ReleaseNotes { get; set; }
        public DirectoryInfo ReleaseDir { get; set; }
        public RID TargetRuntime { get; set; }
        public string Channel { get; set; }
        public DeltaMode DeltaMode { get; set; } = DeltaMode.None;
        public string EntryExecutableName { get; set; }
        public string Icon { get; set; }
        public string Exclude { get; set; }
        public bool NoDefaultExclude { get; set; }
        public bool NoPortable { get; set; }
        public bool NoInst { get; set; }
    }

    /// <summary>
    /// Only the base class's own steps: the pre-process step copies the input like the platform runners do (with excludes),
    /// and no installer is built. A Linux target always builds its portable package, which stands in for the AppImage here
    /// and, as in <c>LinuxPackCommandRunner</c>, is all its release package contains.
    /// </summary>
    private sealed class TestPackageBuilder(RuntimeOs targetOs, ILogger logger, IFancyConsole console)
        : PackageBuilder<TestPackOptions, PackOptionsValidator<TestPackOptions>>(targetOs, logger, console)
    {
        private string _appImagePath;

        /// <summary>For a Linux target, each file of the pre-processed AppDir: its link target (null if not a link) and text.</summary>
        public Dictionary<string, (string LinkTarget, string Text)> LinuxAppDirFiles { get; } = [];

        // RunCoreAsync directly: the shared validator refuses NoPortable together with NoInst.
        public Task PackAsync(TestPackOptions options) => RunCoreAsync(options);

        protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir)
        {
            var dir = TempDir.CreateSubdirectory("PreprocessPackDir");
            CopyFiles(new DirectoryInfo(packDir), dir, progress, true);
            return Task.FromResult(dir.FullName);
        }

        protected override Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
        {
            if (TargetOs != RuntimeOs.Linux) {
                throw new InvalidOperationException("NoPortable is set");
            }

            foreach (var file in FileUtil.EnumerateFilesWithoutFollowingSymlinks(new DirectoryInfo(packDir))) {
                var relativePath = Path.GetRelativePath(packDir, file.FullName).Replace('\\', '/');
                LinuxAppDirFiles[relativePath] = (file.LinkTarget, File.ReadAllText(file.FullName));
            }

            File.WriteAllText(outputPath, "appimage");
            _appImagePath = outputPath;
            return Task.CompletedTask;
        }

        protected override Task CreateReleasePackage(Action<int> progress, string packDir, string outputPath)
        {
            if (TargetOs != RuntimeOs.Linux) {
                return base.CreateReleasePackage(progress, packDir, outputPath);
            }

            var dir = TempDir.CreateSubdirectory("CreateReleasePackage.Linux");
            File.Copy(_appImagePath, Path.Combine(dir.FullName, Options.PackId + ".AppImage"), true);
            return base.CreateReleasePackage(progress, dir.FullName, outputPath);
        }

        protected override string[] GetMainExeSearchPaths(string packDirectory, string mainExeName) =>
            [Path.Combine(packDirectory, mainExeName)];
    }
}
