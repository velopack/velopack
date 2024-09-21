using System.Diagnostics;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.Packaging.Tests;

public static class TestApp
{
    public static void PackTestApp(string id, string version, string testString, string releaseDir, ILogger logger,
        string releaseNotes = null, string channel = null, RID targetRid = null, string packTitle = null)
    {
        targetRid ??= RID.Parse(VelopackRuntimeInfo.SystemRid);

        var projDir = PathHelper.GetTestRootPath("TestApp");
        var testStringFile = Path.Combine(projDir, "Const.cs");
        var oldText = File.ReadAllText(testStringFile);

        try {
            File.WriteAllText(testStringFile, $"class Const {{ public const string TEST_STRING = \"{testString}\"; }}");

            var args = new string[] { "publish", "--no-self-contained", "-c", "Release", "-r", targetRid.ToString(), "-o", "publish" };

            var psi = new ProcessStartInfo("dotnet");
            psi.WorkingDirectory = projDir;
            psi.AppendArgumentListSafe(args, out var debug);

            logger.Info($"TEST: Running {psi.FileName} {debug}");

            using var p = Process.Start(psi);
            p.WaitForExit();

            if (p.ExitCode != 0)
                throw new Exception($"dotnet publish failed with exit code {p.ExitCode}");

            var console = new BasicConsole(logger, new VelopackDefaults(false));

            if (targetRid.BaseRID == RuntimeOs.Windows) {
                var options = new WindowsPackOptions {
                    EntryExecutableName = "TestApp.exe",
                    ReleaseDir = new DirectoryInfo(releaseDir),
                    PackTitle = packTitle,
                    PackId = id,
                    TargetRuntime = targetRid,
                    PackVersion = version,
                    PackDirectory = Path.Combine(projDir, "publish"),
                    ReleaseNotes = releaseNotes,
                    Channel = channel,
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
                    PackDirectory = Path.Combine(projDir, "publish"),
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
                    PackDirectory = Path.Combine(projDir, "publish"),
                    ReleaseNotes = releaseNotes,
                    Channel = channel,
                };
                var runner = new LinuxPackCommandRunner(logger, console);
                runner.Run(options).GetAwaiterResult();
            } else {
                throw new PlatformNotSupportedException();
            }
        } finally {
            File.WriteAllText(testStringFile, oldText);
        }
    }
}
