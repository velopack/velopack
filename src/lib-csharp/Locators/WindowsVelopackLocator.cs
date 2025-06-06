using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using NuGet.Versioning;
using Velopack.Logging;
using Velopack.NuGet;
using Velopack.Util;

namespace Velopack.Locators
{
    /// <summary>
    /// An implementation for Windows which uses the default paths.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsVelopackLocator : VelopackLocator
    {
        /// <inheritdoc />
        public override string? AppId { get; }

        /// <inheritdoc />
        public override string? RootAppDir { get; }

        /// <inheritdoc />
        public override string? UpdateExePath { get; }

        /// <inheritdoc />
        public override string? AppContentDir { get; }

        /// <inheritdoc />
        public override SemanticVersion? CurrentlyInstalledVersion { get; }

        /// <inheritdoc />
        public override string? PackagesDir => CreateSubDirIfDoesNotExist(RootAppDir, "packages");

        /// <inheritdoc />
        public override IVelopackLogger Log { get; }

        /// <inheritdoc />
        public override bool IsPortable => RootAppDir != null && File.Exists(Path.Combine(RootAppDir, ".portable"));

        /// <inheritdoc />
        public override string? Channel { get; }

        /// <inheritdoc />
        public override uint ProcessId { get; }

        /// <inheritdoc />
        public override string ProcessExePath { get; }

        /// <inheritdoc cref="WindowsVelopackLocator" />
        public WindowsVelopackLocator(string currentProcessPath, uint currentProcessId, IVelopackLogger? customLog)
        {
            if (!VelopackRuntimeInfo.IsWindows)
                throw new NotSupportedException($"Cannot instantiate {nameof(WindowsVelopackLocator)} on a non-Windows system.");

            ProcessId = currentProcessId;
            var ourPath = ProcessExePath = currentProcessPath;

            var combinedLog = new CombinedVelopackLogger();
            combinedLog.Add(customLog);
            Log = combinedLog;

            using var initLog = new CachedVelopackLogger(combinedLog);
            initLog.Info($"Initialising {nameof(WindowsVelopackLocator)}");

            // We try various approaches here. Firstly, if Update.exe is in the parent directory,
            // we use that. If it's not present, we search for a parent "current" or "app-{ver}" directory,
            // which could designate that this executable is running in a nested sub-directory.
            // There is some legacy code here, because it's possible that we're running in an "app-{ver}" 
            // directory which is NOT containing a sq.version, in which case we need to infer a lot of info.

            ProcessExePath = Path.GetFullPath(ourPath);
            string myDirPath = Path.GetDirectoryName(ourPath)!;
            var myDirName = Path.GetFileName(myDirPath);
            var possibleUpdateExe = Path.GetFullPath(Path.Combine(myDirPath, "..", "Update.exe"));
            var ixCurrent = ourPath.LastIndexOf("/current/", StringComparison.InvariantCultureIgnoreCase);

            if (File.Exists(possibleUpdateExe)) {
                // we're running in a directory with an Update.exe in the parent directory
                var manifestFile = Path.Combine(myDirPath, CoreUtil.SpecVersionFileName);
                var rootDir = Path.GetDirectoryName(possibleUpdateExe)!;
                if (PackageManifest.TryParseFromFile(manifestFile, out var manifest)) {
                    // ideal, the info we need is in a manifest file.
                    initLog.Info($"{nameof(WindowsVelopackLocator)}: Update.exe in parent dir, Located valid manifest file at: " + manifestFile);
                    AppId = manifest.Id;
                    CurrentlyInstalledVersion = manifest.Version;
                    RootAppDir = rootDir;
                    UpdateExePath = possibleUpdateExe;
                    AppContentDir = myDirPath;
                    Channel = manifest.Channel;
                } else if (PathUtil.PathPartStartsWith(myDirName, "app-") && NuGetVersion.TryParse(myDirName.Substring(4), out var version)) {
                    // this is a legacy case, where we're running in an 'root/app-*/' directory, and there is no manifest.
                    initLog.Warn(
                        "Update.exe in parent dir, Legacy app-* directory detected, sq.version not found. Using directory name for AppId and Version.");
                    AppId = Path.GetFileName(Path.GetDirectoryName(possibleUpdateExe));
                    CurrentlyInstalledVersion = version;
                    RootAppDir = rootDir;
                    UpdateExePath = possibleUpdateExe;
                    AppContentDir = myDirPath;
                } else {
                    initLog.Error("Update.exe in parent dir, but unable to locate a valid manifest file at: " + manifestFile);
                }
            } else if (ixCurrent > 0) {
                // this is an attempt to handle the case where we are running in a nested current directory.
                var rootDir = ourPath.Substring(0, ixCurrent);
                var currentDir = Path.Combine(rootDir, "current");
                var manifestFile = Path.Combine(currentDir, CoreUtil.SpecVersionFileName);
                possibleUpdateExe = Path.GetFullPath(Path.Combine(rootDir, "Update.exe"));
                // we only support parsing a manifest when we're in a nested current directory. no legacy fallback.
                if (File.Exists(possibleUpdateExe) && PackageManifest.TryParseFromFile(manifestFile, out var manifest)) {
                    initLog.Warn("Running in deeply nested directory. This is not an advised use-case.");
                    initLog.Info("Located valid manifest file at: " + manifestFile);
                    RootAppDir = Path.GetDirectoryName(possibleUpdateExe);
                    UpdateExePath = possibleUpdateExe;
                    AppId = manifest.Id;
                    CurrentlyInstalledVersion = manifest.Version;
                    AppContentDir = currentDir;
                    Channel = manifest.Channel;
                }
            }

            bool fileLogCreated = false;
            if (!String.IsNullOrEmpty(AppId) && !String.IsNullOrEmpty(RootAppDir)) {
                try {
                    var logFilePath = Path.Combine(RootAppDir, DefaultLoggingFileName);
                    var fileLog = new FileVelopackLogger(logFilePath, currentProcessId);
                    combinedLog.Add(fileLog);
                    fileLogCreated = true;
                } catch (Exception ex2) {
                    initLog.Error("Unable to create default file logger: " + ex2);
                }
            }

            // if the RootAppDir was unwritable, or we don't know the app id, we could try to write to the temp folder instead.
            if (!fileLogCreated) {
                try {
                    var logFileName = String.IsNullOrEmpty(AppId) ? DefaultLoggingFileName : $"velopack_{AppId}.log";
                    var logFilePath = Path.Combine(Path.GetTempPath(), logFileName);
                    var fileLog = new FileVelopackLogger(logFilePath, currentProcessId);
                    combinedLog.Add(fileLog);
                } catch (Exception ex2) {
                    initLog.Error("Unable to create temp folder file logger: " + ex2);
                }
            }

            if (AppId == null) {
                initLog.Warn(
                    $"Failed to initialise {nameof(WindowsVelopackLocator)}. This could be because the program is not installed or packaged properly.");
            } else {
                initLog.Info($"Initialised {nameof(WindowsVelopackLocator)} for {AppId} v{CurrentlyInstalledVersion}");
            }
        }
    }
}