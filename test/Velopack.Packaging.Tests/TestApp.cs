using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk.Logging;

namespace Velopack.Packaging.Tests
{
    public static class TestApp
    {
        public static void PackTestApp(string id, string version, string testString, string releaseDir, ILogger logger,
            string releaseNotes = null, string channel = null)
        {
            var projDir = PathHelper.GetTestRootPath("TestApp");
            var testStringFile = Path.Combine(projDir, "Const.cs");
            var oldText = File.ReadAllText(testStringFile);

            try {
                File.WriteAllText(testStringFile, $"class Const {{ public const string TEST_STRING = \"{testString}\"; }}");

                var args = new string[] { "publish", "--no-self-contained", "-c", "Release", "-r", VelopackRuntimeInfo.SystemRid, "-o", "publish" };

                var psi = new ProcessStartInfo("dotnet");
                psi.WorkingDirectory = projDir;
                psi.AppendArgumentListSafe(args, out var debug);

                logger.Info($"TEST: Running {psi.FileName} {debug}");

                using var p = Process.Start(psi);
                p.WaitForExit();

                if (p.ExitCode != 0)
                    throw new Exception($"dotnet publish failed with exit code {p.ExitCode}");

                if (VelopackRuntimeInfo.IsWindows) {
                    var options = new WindowsPackOptions {
                        EntryExecutableName = "TestApp.exe",
                        ReleaseDir = new DirectoryInfo(releaseDir),
                        PackId = id,
                        TargetRuntime = RID.Parse(VelopackRuntimeInfo.SystemOs.GetOsShortName()),
                        PackVersion = version,
                        PackDirectory = Path.Combine(projDir, "publish"),
                        ReleaseNotes = releaseNotes,
                        Channel = channel,
                    };
                    var runner = new WindowsPackCommandRunner(logger, new BasicConsole(logger));
                    runner.Run(options).GetAwaiterResult();
                } else if (VelopackRuntimeInfo.IsOSX) {
                    var options = new OsxPackOptions {
                        EntryExecutableName = "TestApp",
                        ReleaseDir = new DirectoryInfo(releaseDir),
                        PackId = id,
                        Icon = Path.Combine(PathHelper.GetProjectDir(), "examples", "AvaloniaCrossPlat", "Velopack.icns"),
                        TargetRuntime = RID.Parse(VelopackRuntimeInfo.SystemOs.GetOsShortName()),
                        PackVersion = version,
                        PackDirectory = Path.Combine(projDir, "publish"),
                        ReleaseNotes = releaseNotes,
                        Channel = channel,
                    };
                    var runner = new OsxPackCommandRunner(logger, new BasicConsole(logger));
                    runner.Run(options).GetAwaiterResult();
                } else if (VelopackRuntimeInfo.IsLinux) {
                    var options = new LinuxPackOptions {
                        EntryExecutableName = "TestApp",
                        ReleaseDir = new DirectoryInfo(releaseDir),
                        PackId = id,
                        Icon = Path.Combine(PathHelper.GetProjectDir(), "examples", "AvaloniaCrossPlat", "Velopack.png"),
                        TargetRuntime = RID.Parse(VelopackRuntimeInfo.SystemOs.GetOsShortName()),
                        PackVersion = version,
                        PackDirectory = Path.Combine(projDir, "publish"),
                        ReleaseNotes = releaseNotes,
                        Channel = channel,
                    };
                    var runner = new LinuxPackCommandRunner(logger, new BasicConsole(logger));
                    runner.Run(options).GetAwaiterResult();
                } else {
                    throw new PlatformNotSupportedException();
                }
            } finally {
                File.WriteAllText(testStringFile, oldText);
            }
        }
    }
}
