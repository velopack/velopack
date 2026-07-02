#nullable enable
using System.Diagnostics;
using Velopack.Core;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.TestCommon;

public static class TestApp
{
    /// <summary>
    /// Builds the `-p:TestAppTestString=...` publish argument. MSBuild splits CLI property values on
    /// commas/semicolons even when the shell argument is quoted, so those (and the escape character
    /// itself) must be %-escaped; MSBuild unescapes them again when the property is read.
    /// </summary>
    public static string TestStringMsBuildArg(string testString)
        => "-p:TestAppTestString=" + testString.Replace("%", "%25").Replace(",", "%2C").Replace(";", "%3B");

    // Serializes `dotnet publish` invocations: each publish gets isolated -o/--artifacts-path dirs,
    // but referenced projects (e.g. Velopack.csproj) still build into the repo-shared
    // build/{Configuration}/ output dir, and two concurrent publishes race on files there
    // (observed in CI as GenerateDepsFile: "cannot access Velopack.deps.json ... used by another
    // process" when parallel test collections packed at the same time).
    private static readonly SemaphoreSlim PublishGate = new(1, 1);

    public static void PackTestApp(string id, string version, string testString, string releaseDir, ILogger logger,
        string? releaseNotes = null, string? channel = null, RID? targetRid = null, string? packTitle = null, string? azureTrustedSignFile = null)
    {
        targetRid ??= RID.Parse(VelopackRuntimeInfo.SystemRid);

        var projDir = PathHelper.GetTestRootPath("TestApp");

        // The test string is injected as a compile-time constant via -p:TestAppTestString (see
        // TestApp.csproj), and every invocation publishes into its own output + MSBuild artifacts
        // dirs — no shared mutable state, so concurrent packs from parallel test collections are safe.
        var workDir = Path.Combine(Path.GetTempPath(), "velopack-pack-" + Guid.NewGuid().ToString("N"));
        var publishDir = Path.Combine(workDir, "publish");
        var artifactsDir = Path.Combine(workDir, "artifacts");
        Directory.CreateDirectory(publishDir);

        try {
            var args = new string[] {
                "publish", "--no-self-contained", "-c", "Release", "-r", targetRid.ToString(),
                "-o", publishDir, "--artifacts-path", artifactsDir, TestStringMsBuildArg(testString),
            };

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
                if (VelopackRuntimeInfo.IsOSX) {
                    var runner = new OsxPackCommandRunner(logger, console);
                    runner.Run(options).GetAwaiterResult();
                } else {
                    throw new PlatformNotSupportedException();
                }
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
