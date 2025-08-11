using System.Collections.Generic;
using System.IO;
using Velopack.Locators;
using Velopack.Logging;

namespace Velopack
{
    /// <summary>
    /// A static helper class to assist in running Update.exe CLI commands. You probably should not invoke this directly, 
    /// instead you should use the relevant methods on <see cref="UpdateManager"/>. For example: 
    /// <see cref="UpdateManager.ApplyUpdatesAndExit(VelopackAsset)"/>, or <see cref="UpdateManager.ApplyUpdatesAndRestart(VelopackAsset, string[])"/>.
    /// </summary>
    public static class UpdateExe
    {
        /// <summary>
        /// Runs Update.exe in the current working directory with the 'start' command which will simply start the application.
        /// Combined with the `waitForExit` parameter, this can be used to gracefully restart the application.
        /// </summary>
        /// <param name="waitPid">Optionally wait for the specified process to exit before continuing.</param>
        /// <param name="locator">The locator to use to find the path to Update.exe and the packages directory.</param>
        /// <param name="startArgs">The arguments to pass to the application when it is restarted.</param>
        public static void Start(IVelopackLocator? locator = null, uint waitPid = 0, string[]? startArgs = null)
        {
            locator ??= VelopackLocator.Current;

            var args = new List<string>();
            args.Add("start");

            if (waitPid > 0) {
                args.Add("--waitPid");
                args.Add(waitPid.ToString());
            }

            if (startArgs != null && startArgs.Length > 0) {
                args.Add("--");
                foreach (var a in startArgs) {
                    args.Add(a);
                }
            }

            var updatePath = locator.UpdateExePath!;
            var workingDir = Path.GetDirectoryName(updatePath)!;
            locator.Process.StartProcess(updatePath, args, workingDir, false);
            locator.Log.Info("Update.exe [start] executed successfully.");
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
        /// <param name="waitPid">Optionally wait for the specified process to exit before continuing.</param>
        /// <param name="restartArgs">The arguments to pass to the application when it is restarted.</param>
        public static void Apply(IVelopackLocator? locator, VelopackAsset? toApply, bool silent, uint waitPid, bool restart,
            string[]? restartArgs = null)
        {
            locator ??= VelopackLocator.Current;

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

            if (waitPid > 0) {
                args.Add("--waitPid");
                args.Add(waitPid.ToString());
            }

            if (!restart) args.Add("--norestart"); // restarting is now the default Update.exe behavior

            args.Add("--root");
            args.Add(locator.RootAppDir!);

            if (restart && restartArgs != null && restartArgs.Length > 0) {
                args.Add("--");
                foreach (var a in restartArgs) {
                    args.Add(a);
                }
            }

            var updatePath = locator.GetUpdateExePathForUpdate();
            var workingDir = Path.GetDirectoryName(updatePath)!;
            locator.Process.StartProcess(updatePath, args, workingDir, false);
            locator.Log.Info("Update.exe [apply] executed successfully.");
        }
    }
}