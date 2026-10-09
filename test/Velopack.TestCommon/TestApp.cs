#nullable enable
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Velopack.Core;
using Velopack.Packaging;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.TestCommon;

public static class TestApp
{
    /// <summary>
    /// TestApp reads this file (next to the exe) at runtime and prints its contents for the `test`
    /// command — the same convention as the rust testapp (src/bins/src/testapp.rs).
    /// </summary>
    public const string TestStringFileName = "test_string.txt";

    // The test string is injected as a file at pack time rather than compiled in, so a single
    // publish per (rid, options) can be shared by every test in the process — recompiling TestApp
    // for each test was one of the slowest parts of the suite.
    private static readonly ConcurrentDictionary<string, Lazy<string>> PublishCache = new();

    // Serializes `dotnet publish` invocations: each publish gets isolated -o/--artifacts-path dirs,
    // but referenced projects (e.g. Velopack.csproj) still build into the repo-shared
    // build/{Configuration}/ output dir, and two concurrent publishes race on files there
    // (observed in CI as GenerateDepsFile: "cannot access Velopack.deps.json ... used by another
    // process" when parallel test collections packed at the same time).
    private static readonly SemaphoreSlim PublishGate = new(1, 1);

    /// <summary>
    /// Runs fn while holding the global publish gate. Tests that invoke `dotnet publish`/`dotnet build`
    /// themselves (e.g. CompatUtilTests) must wrap the invocation with this so they cannot race a
    /// concurrent TestApp publish on the shared build output/intermediate dirs.
    /// </summary>
    public static void WithPublishLock(Action fn)
    {
        PublishGate.Wait();
        try {
            fn();
        } finally {
            PublishGate.Release();
        }
    }

    /// <summary>
    /// Copies a cached TestApp publish for the given RID into destDir (replacing destDir if it
    /// already exists) and writes testString to test_string.txt inside it, unless it is null.
    /// </summary>
    public static void PreparePublishDir(RID targetRid, string? testString, string destDir, ILogger logger, bool singleFile = false)
    {
        var cached = GetCachedPublishDir(targetRid, singleFile, logger);
        if (Directory.Exists(destDir)) {
            Directory.Delete(destDir, true);
        }

        FileUtil.CopyDirectoryContents(cached, destDir);
        if (testString != null) {
            File.WriteAllText(Path.Combine(destDir, TestStringFileName), testString);
        }
    }

    private static string GetCachedPublishDir(RID targetRid, bool singleFile, ILogger logger)
    {
        var key = targetRid + (singleFile ? "|singlefile" : "");
        return PublishCache.GetOrAdd(key, _ => new Lazy<string>(() => PublishTestApp(targetRid, singleFile, logger))).Value;
    }

    private static string PublishTestApp(RID targetRid, bool singleFile, ILogger logger)
    {
        var projDir = PathHelper.GetTestRootPath("TestApp");

        // this dir lives for the rest of the test process; the OS cleans up temp eventually
        var workDir = Path.Combine(Path.GetTempPath(), "velopack-testapp-" + Guid.NewGuid().ToString("N"));
        var publishDir = Path.Combine(workDir, "publish");
        var artifactsDir = Path.Combine(workDir, "artifacts");
        Directory.CreateDirectory(publishDir);

        var args = new List<string> {
            "publish", "--no-self-contained", "-c", "Release", "-r", targetRid.ToString(), "--tl:off",
            "-o", publishDir, "--artifacts-path", artifactsDir,
        };

        if (singleFile) {
            args.Add("-p:PublishSingleFile=true");
        }

        var psi = new ProcessStartInfo("dotnet");
        psi.WorkingDirectory = projDir;
        psi.AppendArgumentListSafe(args, out var debug);

        logger.Info($"TEST: Running {psi.FileName} {debug}");

        PublishGate.Wait();
        try {
            using var p = Process.Start(psi);
            p!.WaitForExit();

            if (p.ExitCode != 0)
                throw new Exception($"dotnet publish failed with exit code {p.ExitCode}");
        } finally {
            PublishGate.Release();
        }

        return publishDir;
    }

    /// <summary>
    /// Returns why this machine cannot pack the TestApp for macOS, or null when it can. A macOS release needs a Mach-O
    /// UpdateMac: CI vendors the universal one (the rust-macos artifact) into target/release on every OS, but a local
    /// Debug build off macOS only has the host's own update binary, which macOS cannot run. The bundle also needs a
    /// symlink, which a Windows machine without Developer Mode or elevation cannot create.
    /// </summary>
    public static string? GetOsxPackBlocker(ILogger logger)
    {
        string update;
        try {
            update = HelperFile.GetUpdatePath(RID.Parse("osx-arm64"), logger);
        } catch (Exception ex) {
            return "no UpdateMac to pack for macOS with: " + ex.Message;
        }

        if (!BinDetect.IsMachOImage(update)) {
            return $"'{update}' is not a Mach-O image, so it cannot be packed as UpdateMac (off macOS, only CI's vendored binaries are)";
        }

        // the bundle links Contents/MacOS/sq.version to Contents/Resources/sq.version
        return TestHelper.GetSymlinkBlocker();
    }

    /// <param name="testString">Written to test_string.txt next to the exe, or left out when null.</param>
    /// <param name="singleFile">Packs a single-file publish of the TestApp.</param>
    /// <param name="configureOsx">Adjusts the options of a macOS pack (signing, entitlements) before it runs.</param>
    public static void PackTestApp(string id, string version, string? testString, string releaseDir, ILogger logger,
        string? releaseNotes = null, string? channel = null, RID? targetRid = null, string? packTitle = null, string? azureTrustedSignFile = null,
        bool singleFile = false, Action<OsxPackOptions>? configureOsx = null)
    {
        targetRid ??= RID.Parse(VelopackRuntimeInfo.SystemRid);

        var workDir = Path.Combine(Path.GetTempPath(), "velopack-pack-" + Guid.NewGuid().ToString("N"));
        var publishDir = Path.Combine(workDir, "publish");

        try {
            PreparePublishDir(targetRid, testString, publishDir, logger, singleFile);

            var console = new BasicConsole(logger, new VelopackDefaults(false));

            if (targetRid.BaseRID == RuntimeOs.Windows) {
                var options = new WindowsPackOptions {
                    EntryExecutableName = "TestApp.exe",
                    ReleaseDir = new DirectoryInfo(releaseDir),
                    PackTitle = packTitle,
                    PackId = id,
                    TargetRuntime = targetRid,
                    PackVersion = version,
                    PackDirectory = publishDir,
                    ReleaseNotes = releaseNotes,
                    Channel = channel,
                    AzureTrustedSignFile = azureTrustedSignFile
                };
                var runner = new WindowsPackCommandRunner(logger, console);
                runner.Run(options).GetAwaiterResult();
            } else if (targetRid.BaseRID == RuntimeOs.OSX) {
                var options = new OsxPackOptions {
                    EntryExecutableName = "TestApp",
                    ReleaseDir = new DirectoryInfo(releaseDir),
                    PackTitle = packTitle,
                    PackId = id,
                    TargetRuntime = targetRid,
                    PackVersion = version,
                    PackDirectory = publishDir,
                    ReleaseNotes = releaseNotes,
                    Channel = channel,
                };
                configureOsx?.Invoke(options);
                var runner = new OsxPackCommandRunner(logger, console);
                runner.Run(options).GetAwaiterResult();
            } else if (targetRid.BaseRID == RuntimeOs.Linux) {
                var options = new LinuxPackOptions {
                    EntryExecutableName = "TestApp",
                    ReleaseDir = new DirectoryInfo(releaseDir),
                    PackTitle = packTitle,
                    PackId = id,
                    TargetRuntime = targetRid,
                    PackVersion = version,
                    PackDirectory = publishDir,
                    ReleaseNotes = releaseNotes,
                    Channel = channel
                };
                var runner = new LinuxPackCommandRunner(logger, console);
                runner.Run(options).GetAwaiterResult();
            } else {
                throw new PlatformNotSupportedException();
            }
        } finally {
            try {
                Directory.Delete(workDir, true);
            } catch {
                // best effort — abandoned temp dirs are cleaned by the OS eventually
            }
        }
    }
}
