using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack.Locators;
using Velopack.Util;

namespace Velopack
{
    /// <summary>
    /// A static helper class to assist in running Update.exe CLI commands. You probably should not invoke this directly, 
    /// instead you should use the relevant methods on <see cref="UpdateManager"/>. For example: 
    /// <see cref="UpdateManager.ApplyUpdatesAndExit(VelopackAsset)"/>, or <see cref="UpdateManager.ApplyUpdatesAndRestart(VelopackAsset, string[])"/>.
    /// </summary>
    public static class UpdateExe
    {
        [DllImport("user32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private static Process StartUpdateExe(ILogger logger, IVelopackLocator locator, IEnumerable<string> args)
        {
            var psi = new ProcessStartInfo() {
                CreateNoWindow = true,
                FileName = locator.UpdateExePath!,
                WorkingDirectory = Path.GetDirectoryName(locator.UpdateExePath)!,
            };

            psi.AppendArgumentListSafe(args, out var debugArgs);
            logger.Debug($"Running: {psi.FileName} {debugArgs}");

            var p = Process.Start(psi);
            if (p == null) {
                throw new Exception("Failed to launch Update.exe process.");
            }

            if (VelopackRuntimeInfo.IsWindows) {
                try {
                    // this is an attempt to work around a bug where the restarted app fails to come to foreground.
                    if (!AllowSetForegroundWindow(p.Id))
                        throw new Win32Exception();
                } catch (Exception ex) {
                    logger.LogWarning(ex, "Failed to allow Update.exe to set foreground window.");
                }
            }

            logger.Info("Update.exe executed successfully.");
            return p;
        }

        /// <summary>
        /// Runs Update.exe in the current working directory with the 'start' command which will simply start the application.
        /// Combined with the `waitForExit` parameter, this can be used to gracefully restart the application.
        /// </summary>
        /// <param name="waitForExit">If true, Update.exe will wait for the current process to exit before re-starting the application.</param>
        /// <param name="locator">The locator to use to find the path to Update.exe and the packages directory.</param>
        /// <param name="startArgs">The arguments to pass to the application when it is restarted.</param>
        /// <param name="logger">The logger to use for diagnostic messages</param>
        public static void Start(IVelopackLocator? locator = null, bool waitForExit = true, string[]? startArgs = null, ILogger? logger = null)
        {
            logger ??= NullLogger.Instance;
            locator ??= VelopackLocator.GetDefault(logger);

            var args = new List<string>();
            args.Add("start");

            if (waitForExit) {
                args.Add("--waitPid");
                args.Add(Process.GetCurrentProcess().Id.ToString());
            }

            if (startArgs != null && startArgs.Length > 0) {
                args.Add("--");
                foreach (var a in startArgs) {
                    args.Add(a);
                }
            }

            StartUpdateExe(logger, locator, args);
        }

        private static Process ApplyImpl(IVelopackLocator? locator, VelopackAsset? toApply, bool silent, bool restart, string[]? restartArgs = null,
            ILogger? logger = null)
        {
            logger ??= NullLogger.Instance;
            locator ??= VelopackLocator.GetDefault(logger);

            var args = new List<string>();
            if (silent) args.Add("--silent");
            args.Add("apply");

            var entry = toApply ?? locator.GetLatestLocalFullPackage();
            if (entry != null && locator.PackagesDir != null) {
                var pkg = Path.Combine(locator.PackagesDir, entry.FileName);
                if (File.Exists(pkg)) {
                    args.Add("--package");
                    args.Add(pkg);
                }
            }

            args.Add("--waitPid");
            args.Add(Process.GetCurrentProcess().Id.ToString());

            if (!restart) args.Add("--norestart"); // restarting is now the default Update.exe behavior

            if (restart && restartArgs != null && restartArgs.Length > 0) {
                args.Add("--");
                foreach (var a in restartArgs) {
                    args.Add(a);
                }
            }

            return StartUpdateExe(logger, locator, args);
        }

        /// <summary>
        /// Runs Update.exe in the current working directory to apply updates, optionally restarting the application.
        /// </summary>
        /// <param name="silent">If true, no dialogs will be shown during the update process. This could result 
        /// in an update failing to install, such as when we need to ask the user for permission to install 
        /// a new framework dependency.</param>
        /// <param name="restart">If true, restarts the application after updates are applied (or if they failed)</param>
        /// <param name="locator">The locator to use to find the path to Update.exe and the packages directory.</param>
        /// <param name="toApply">The update package you wish to apply, can be left null.</param>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        /// <param name="logger">The logger to use for diagnostic messages</param>
        /// <exception cref="Exception">Thrown if Update.exe does not initialize properly.</exception>
        public static void Apply(IVelopackLocator? locator, VelopackAsset? toApply, bool silent, bool restart, string[]? restartArgs = null,
            ILogger? logger = null)
        {
            var process = ApplyImpl(locator, toApply, silent, restart, restartArgs, logger);
            Thread.Sleep(500);

            if (process.HasExited) {
                throw new Exception($"Update.exe process exited too soon ({process.ExitCode}).");
            }
        }

        /// <inheritdoc cref="Apply"/>
        public static async Task ApplyAsync(IVelopackLocator? locator, VelopackAsset? toApply, bool silent, bool restart, string[]? restartArgs = null,
            ILogger? logger = null)
        {
            var process = ApplyImpl(locator, toApply, silent, restart, restartArgs, logger);
            await Task.Delay(500).ConfigureAwait(false);

            if (process.HasExited) {
                throw new Exception($"Update.exe process exited too soon ({process.ExitCode}).");
            }
        }
    }
}