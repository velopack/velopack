using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack.Locators;

namespace Velopack
{
    /// <summary>
    /// A static helper class to assist in running Update.exe CLI commands. You probably should not invoke this directly, 
    /// instead you should use the relevant methods on <see cref="UpdateManager"/>. For example: 
    /// <see cref="UpdateManager.ApplyUpdatesAndExit()"/>, or <see cref="UpdateManager.ApplyUpdatesAndRestart(string[])"/>.
    /// </summary>
    public static class UpdateExe
    {
        /// <summary>
        /// Runs Update.exe in the current working directory to apply updates, optionally restarting the application.
        /// </summary>
        /// <param name="silent">If true, no dialogs will be shown during the update process. This could result 
        /// in an update failing to install, such as when we need to ask the user for permission to install 
        /// a new framework dependency.</param>
        /// <param name="restart">If true, restarts the application after updates are applied (or if they failed)</param>
        /// <param name="locator">The locator to use to find the path to Update.exe and the packages directory.</param>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        /// <param name="logger">The logger to use for diagnostic messages</param>
        /// <exception cref="Exception">Thrown if Update.exe does not initialize properly.</exception>
        public static void Apply(IVelopackLocator locator, bool silent, bool restart, string[]? restartArgs, ILogger? logger = null)
        {
            logger ??= NullLogger.Instance;
            var psi = new ProcessStartInfo() {
                CreateNoWindow = true,
                FileName = locator.UpdateExePath,
                WorkingDirectory = Path.GetDirectoryName(locator.UpdateExePath),
            };

            var args = new List<string>();
            if (silent) args.Add("--silent");
            args.Add("apply");
            args.Add("--wait");

            var entry = locator.GetLatestLocalFullPackage();
            if (entry != null && locator.PackagesDir != null) {
                var pkg = Path.Combine(locator.PackagesDir, entry.FileName);
                if (File.Exists(pkg)) {
                    args.Add("--package");
                    args.Add(pkg);
                }
            }

            if (restart) args.Add("--restart");
            if (restart && restartArgs != null && restartArgs.Length > 0) {
                args.Add("--");
                foreach (var a in restartArgs) {
                    args.Add(a);
                }
            }

            psi.AppendArgumentListSafe(args, out var debugArgs);
            logger.Debug($"Restarting app to apply updates. Running: {psi.FileName} {debugArgs}");

            var p = Process.Start(psi);
            Thread.Sleep(300);
            if (p == null) {
                throw new Exception("Failed to launch Update.exe process.");
            }
            if (p.HasExited) {
                throw new Exception($"Update.exe process exited too soon ({p.ExitCode}).");
            }
            logger.Info("Update.exe apply triggered successfully.");
        }
    }
}
