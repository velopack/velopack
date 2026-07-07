using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Win32;
using NuGet.Packaging;
using Velopack.Core;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Compression;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;
using Velopack.Windows;

using Velopack.TestCommon;

namespace Velopack.Pack.Tests;

[SupportedOSPlatform("windows")]
public class WindowsPackTests
{
    private readonly ITestOutputHelper _output;

    public WindowsPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PackBuildValidPackageMostOptions()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);

        var exe = "testapp.exe";
        var pdb = Path.ChangeExtension(exe, ".pdb");
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(pdb, tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win10.0.19043-x64"),
            Runtimes = "net6",
            PackAuthors = "author",
            PackTitle = "Test Squirrel App",
            PackDirectory = tmpOutput,
            Channel = "asd123",
            Exclude = @".*\.pdb",
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        var nupkgPath = Path.Combine(tmpReleaseDir, $"{id}-{version}-asd123-full.nupkg");
        Assert.True(File.Exists(nupkgPath));

        var setupPath = Path.Combine(tmpReleaseDir, $"{id}-asd123-Setup.exe");
        Assert.True(File.Exists(setupPath));

        //var releasesPath = Path.Combine(tmpReleaseDir, $"RELEASES-asd123");
        //Assert.True(File.Exists(releasesPath));
        var releasesPath2 = Path.Combine(tmpReleaseDir, $"releases.asd123.json");
        Assert.True(File.Exists(releasesPath2));

        EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), nupkgPath, unzipDir);

        // does nuspec exist and is it valid
        var nuspecPath = Path.Combine(unzipDir, $"{id}.nuspec");
        Assert.True(File.Exists(nuspecPath));
        var xml = XDocument.Load(nuspecPath);

        Assert.Equal(id, xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("id").Single().Value);
        Assert.Equal(version, xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("version").Single().Value);
        Assert.Equal(exe, xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("mainExe").Single().Value);
        Assert.Equal("Test Squirrel App", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("title").Single().Value);
        Assert.Equal("author", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("authors").Single().Value);
        Assert.Equal("x64", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("machineArchitecture").Single().Value);
        Assert.Equal("net6-x64-desktop", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("runtimeDependencies").Single().Value);
        Assert.Equal("win", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("os").Single().Value);
        Assert.Equal("10.0.19043", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("osMinVersion").Single().Value);

        // check for other files
        Assert.True(File.Exists(Path.Combine(unzipDir, "lib", "app", Path.GetFileName(exe))));
        Assert.False(File.Exists(Path.Combine(unzipDir, "lib", "app", Path.GetFileName(pdb))));
    }

    [Fact]
    public void PortableStubNameMatchesUpdateStubName()
    {
        // Regression test for https://github.com/velopack/velopack/issues/982
        // When --packTitle differs from --mainExe, the portable launcher created at
        // pack time must have the same name as the launcher the updater re-extracts
        // from the nupkg on update (nupkg stub with the "_ExecutionStub" suffix
        // stripped). Otherwise updating leaves two differently-named launchers behind.
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var nupkgDir);
        using var _4 = TempUtil.GetTempDirectory(out var portableDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";
        var version = "1.0.0";
        var title = "Test Squirrel App"; // deliberately different from the main exe name

        PathHelper.CopyRustAssetTo(exe, tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win-x64"),
            PackTitle = title,
            PackDirectory = tmpOutput,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        // The launcher the updater will produce: the "_ExecutionStub.exe" inside the
        // nupkg, with the suffix stripped (mirrors Bundle.extract_stubs_to_dir in Rust).
        var nupkgPath = Path.Combine(tmpReleaseDir, $"{id}-{version}-full.nupkg");
        Assert.True(File.Exists(nupkgPath));
        EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), nupkgPath, nupkgDir);
        var nupkgStub = Directory.EnumerateFiles(nupkgDir, "*_ExecutionStub.exe", SearchOption.AllDirectories).Single();
        var updaterLauncherName = Path.GetFileName(nupkgStub).Replace("_ExecutionStub.exe", ".exe");

        // The launcher created at pack time in the portable package root.
        var portablePath = Directory.EnumerateFiles(tmpReleaseDir, "*-Portable.zip").Single();
        EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), portablePath, portableDir);
        var portableLauncherName = Directory.EnumerateFiles(portableDir, "*.exe")
            .Select(Path.GetFileName)
            .Single(n => !n.Equals("Update.exe", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(portableLauncherName, updaterLauncherName);
    }

    [Fact]
    public void PackBuildRefuseSameVersion()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var pdb = Path.ChangeExtension(exe, ".pdb");
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(pdb, tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            PackDirectory = tmpOutput,
            TargetRuntime = RID.Parse("win-x64"),
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        Assert.Throws<UserInfoException>(() => runner.Run(options).GetAwaiterResult());
    }

    [Fact]
    public void PackBuildRefuseChannelMultipleRids()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var pdb = Path.ChangeExtension(exe, ".pdb");
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(pdb, tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win10.0.19043-x64"),
            Runtimes = "net6",
            PackAuthors = "author",
            PackTitle = "Test Squirrel App",
            PackDirectory = tmpOutput,
            Channel = "hello",
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        options.TargetRuntime = RID.Parse("win10.0.19043-x86");
        Assert.Throws<UserInfoException>(() => runner.Run(options).GetAwaiterResult());
    }

    [Fact]
    public async Task TestPackGeneratesValidDelta()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        string id = "SquirrelDeltaTest";
        await PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);
        await PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger, true);
        await PackTestApp(id, "3.0.0", "version 3 test", releaseDir, logger);

        // did a zsdiff get created for the changed file in our v2 update? (test_string.txt is the
        // only file that differs between versions now that the test string is not compiled in)
        var deltaPath = Path.Combine(releaseDir, $"{id}-2.0.0-delta.nupkg");
        Assert.True(File.Exists(deltaPath));
        using var _2 = TempUtil.GetTempDirectory(out var extractDir);
        EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), deltaPath, extractDir);
        var extractStringDiff = Path.Combine(extractDir, "lib", "app", "test_string.txt.zsdiff");
        var extractStringShasum = Path.Combine(extractDir, "lib", "app", "test_string.txt.shasum");
        Assert.True(File.Exists(extractStringDiff));
        Assert.True(new FileInfo(extractStringDiff).Length > 0);
        Assert.True(File.Exists(extractStringShasum));
        Assert.True(new FileInfo(extractStringShasum).Length > 0);

        // unchanged files get zero-length dummy .diff markers
        var extractDllDiff = Path.Combine(extractDir, "lib", "app", "TestApp.dll.diff");
        Assert.True(File.Exists(extractDllDiff));
        Assert.True(new FileInfo(extractDllDiff).Length == 0);
        var extractAppDiff = Path.Combine(extractDir, "lib", "app", "TestApp.exe.diff");
        Assert.True(File.Exists(extractAppDiff));
        Assert.True(new FileInfo(extractAppDiff).Length == 0);

        // new file should exist but not have shasum
        var extractNewFile = Path.Combine(extractDir, "lib", "app", "NewFile.txt");
        Assert.True(File.Exists(extractNewFile));
        Assert.True(new FileInfo(extractNewFile).Length > 0);
        var extractNewFileShasum = Path.Combine(extractDir, "lib", "app", "NewFile.txt.shasum");
        Assert.False(File.Exists(extractNewFileShasum));

        // apply delta and check package
        var output = Path.Combine(releaseDir, "delta.patched");
        new DeltaPatchCommandRunner(logger, new BasicConsole(logger, new VelopackDefaults(false))).Run(
            new DeltaPatchOptions {
                BasePackage = Path.Combine(releaseDir, $"{id}-1.0.0-full.nupkg"),
                OutputFile = output,
                PatchFiles = [new FileInfo(deltaPath)],
            }).GetAwaiterResult();

        // are the packages the same?
        Assert.True(File.Exists(output));
        var v2 = Path.Combine(releaseDir, $"{id}-2.0.0-full.nupkg");
        var f1 = File.ReadAllBytes(output);
        var f2 = File.ReadAllBytes(v2);
        Assert.True(new ReadOnlySpan<byte>(f1).SequenceEqual(new ReadOnlySpan<byte>(f2)));
        Assert.True(DeltaPackageBuilder.AreFilesEqualFast(output, v2));

        // can apply multiple deltas, and handle add/removing files?
        output = Path.Combine(releaseDir, "delta.patched2");
        var deltav3 = Path.Combine(releaseDir, $"{id}-3.0.0-delta.nupkg");
        new DeltaPatchCommandRunner(logger, new BasicConsole(logger, new VelopackDefaults(false))).Run(
            new DeltaPatchOptions {
                BasePackage = Path.Combine(releaseDir, $"{id}-1.0.0-full.nupkg"),
                OutputFile = output,
                PatchFiles = [new FileInfo(deltaPath), new FileInfo(deltav3)],
            }).GetAwaiterResult();

        // are the packages the same?
        Assert.True(File.Exists(output));
        var v3 = Path.Combine(releaseDir, $"{id}-3.0.0-full.nupkg");
        var f4 = File.ReadAllBytes(output);
        var f5 = File.ReadAllBytes(v3);
        Assert.True(new ReadOnlySpan<byte>(f4).SequenceEqual(new ReadOnlySpan<byte>(f5)));
        Assert.True(DeltaPackageBuilder.AreFilesEqualFast(output, v3));
    }

    internal static async Task PackTestAppVariant(string variant, string id, string version, string testString, string releaseDir, ILogger logger)
    {
        if (variant == "csharp") {
            await PackTestApp(id, version, testString, releaseDir, logger);
        } else if (variant == "rust") {
            await PackRustTestApp(id, version, testString, releaseDir, logger);
        } else {
            throw new ArgumentException($"Unknown variant: {variant}");
        }
    }

    internal static async Task PackRustTestApp(string id, string version, string testString, string releaseDir, ILogger logger)
    {
        using var _ = TempUtil.GetTempDirectory(out var packDir);

        var rustBinary = PathHelper.GetRustAsset("testapp.exe");
        if (!File.Exists(rustBinary))
            throw new FileNotFoundException($"Rust testapp not found at: {rustBinary}. Run 'cargo build -p velopack_bins' first.");
        File.Copy(rustBinary, Path.Combine(packDir, "testapp.exe"));

        File.WriteAllText(Path.Combine(packDir, "test_string.txt"), testString);

        logger.Info($"TEST: Packing Rust testapp v{version} with test string '{testString}'");

        var options = new WindowsPackOptions {
            EntryExecutableName = "testapp.exe",
            ReleaseDir = new DirectoryInfo(releaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = packDir,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);
    }

    internal static async Task PackTestApp(string id, string version, string testString, string releaseDir, ILogger logger,
        bool addNewFile = false, string assemblyNameOverride = null, string mainExeSubfolder = null)
    {
        using var _ = TempUtil.GetTempDirectory(out var workDir);

        {
            var publishDir = Path.Combine(workDir, "publish");
            TestApp.PreparePublishDir(RID.Parse("win-x64"), testString, publishDir, logger, singleFile: assemblyNameOverride != null);

            if (addNewFile) {
                File.WriteAllText(Path.Combine(publishDir, "NewFile.txt"), "New File Test");
            }

            if (assemblyNameOverride != null) {
                var targetExe = Path.Combine(publishDir, assemblyNameOverride + ".exe");
                if (File.Exists(targetExe)) {
                    File.Delete(targetExe);
                }
                File.Move(Path.Combine(publishDir, "TestApp.exe"), targetExe);
            }

            var exeName = (assemblyNameOverride ?? "TestApp") + ".exe";

            if (mainExeSubfolder != null) {
                var subDir = Path.Combine(publishDir, mainExeSubfolder);
                Directory.CreateDirectory(subDir);
                foreach (var file in Directory.GetFiles(publishDir)) {
                    File.Move(file, Path.Combine(subDir, Path.GetFileName(file)), true);
                }
                exeName = Path.Combine(mainExeSubfolder, exeName);
            }

            var options = new WindowsPackOptions {
                EntryExecutableName = exeName,
                ReleaseDir = new DirectoryInfo(releaseDir),
                PackId = id,
                PackVersion = version,
                TargetRuntime = RID.Parse("win-x64"),
                PackDirectory = publishDir,
            };

            var runner = WindowsTestHelper.GetPackRunner(logger);
            await runner.Run(options);
        }
    }

    [Theory]
    [InlineData("x86", AsmResolver.PE.File.MachineType.I386)]
    [InlineData("x64", AsmResolver.PE.File.MachineType.Amd64)]
    [InlineData("arm64", AsmResolver.PE.File.MachineType.Arm64)]
    public void PackIncludesCorrectArchitectureBinaries(string architecture, AsmResolver.PE.File.MachineType expectedMachineType)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        Assert.SkipWhen(IsDebugBuild(), "Architecture-specific binary selection only applies to Release builds.");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse($"win-{architecture}"),
            PackDirectory = tmpOutput,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        var nupkgPath = Path.Combine(tmpReleaseDir, $"{id}-{version}-full.nupkg");
        Assert.True(File.Exists(nupkgPath));
        EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), nupkgPath, unzipDir);

        var squirrelExePath = Path.Combine(unzipDir, "lib", "app", "Squirrel.exe");
        Assert.True(File.Exists(squirrelExePath), "Expected Squirrel.exe (Update.exe) in the package");
        AssertMachineType(squirrelExePath, expectedMachineType);

        var setupPath = Path.Combine(tmpReleaseDir, $"{id}-win-Setup.exe");
        Assert.True(File.Exists(setupPath), "Expected Setup.exe in the release dir");
        AssertMachineType(setupPath, expectedMachineType);
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private void AssertMachineType(string pePath, AsmResolver.PE.File.MachineType expected)
    {
        var actual = AsmResolver.PE.PEImage.FromFile(pePath).MachineType;
        _output.WriteLine($"{Path.GetFileName(pePath)} machine type: {actual}, expected: {expected}");
        Assert.Equal(expected, actual);
    }
}
