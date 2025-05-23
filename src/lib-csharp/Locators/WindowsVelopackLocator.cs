using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        private readonly Lazy<string?> _packagesDir;

        /// <inheritdoc />
        public override string? PackagesDir => _packagesDir.Value;

        /// <inheritdoc />
        public override bool IsPortable => RootAppDir != null && File.Exists(Path.Combine(RootAppDir, ".portable"));

        /// <inheritdoc />
        public override IProcessImpl Process { get; }

        /// <inheritdoc />
        public override string? Channel { get; }

        /// <inheritdoc cref="WindowsVelopackLocator" />
        public WindowsVelopackLocator(IProcessImpl? processImpl, IVelopackLogger? customLog)
        {
            if (!VelopackRuntimeInfo.IsWindows)
                throw new NotSupportedException($"Cannot instantiate {nameof(WindowsVelopackLocator)} on a non-Windows system.");

            _packagesDir = new(GetPackagesDir);
            CombinedLogger = new CombinedVelopackLogger(customLog);

            Process = processImpl ??= new DefaultProcessImpl(CombinedLogger);
            var ourPath = processImpl.GetCurrentProcessPath();
            var currentProcessId = processImpl.GetCurrentProcessId();

            using var initLog = new CachedVelopackLogger(CombinedLogger);
            initLog.Info($"Initializing {nameof(WindowsVelopackLocator)}");

            // We try various approaches here. Firstly, if Update.exe is in the parent directory,
            // we use that. If it's not present, we search for a parent "current" or "app-{ver}" directory,
            // which could designate that this executable is running in a nested sub-directory.
            // There is some legacy code here, because it's possible that we're running in an "app-{ver}" 
            // directory which is NOT containing a sq.version, in which case we need to infer a lot of info.

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

            if (UpdateExePath != null
                && Path.GetDirectoryName(UpdateExePath) is { } updateExeDirectory
                && !PathUtil.IsDirectoryWritable(updateExeDirectory)) {
                var tempTargetUpdateExe = Path.Combine(TempAppRootDirectory, "Update.exe");
                if (File.Exists(UpdateExePath) && !File.Exists(tempTargetUpdateExe)) {
                    initLog.Warn("Application directory is not writable. Copying Update.exe to temp location: " + tempTargetUpdateExe);
                    Debugger.Launch();
                    Directory.CreateDirectory(TempAppRootDirectory);
                    File.Copy(UpdateExePath, tempTargetUpdateExe);
                }

                UpdateExePath = tempTargetUpdateExe;
            }

            //bool fileLogCreated = false;
            Exception? fileLogException = null;
            if (!String.IsNullOrEmpty(AppId) && !String.IsNullOrEmpty(RootAppDir)) {
                try {
                    var logFilePath = Path.Combine(RootAppDir, DefaultLoggingFileName);
                    var fileLog = new FileVelopackLogger(logFilePath, currentProcessId);
                    CombinedLogger.Add(fileLog);
                    //fileLogCreated = true;
                } catch (Exception ex) {
                    fileLogException = ex;
                }
            }

            // if the RootAppDir was unwritable, or we don't know the app id, we could try to write to the temp folder instead.
            Exception? tempFileLogException = null;
            if (fileLogException is not null) {
                try {
                    var logFileName = String.IsNullOrEmpty(AppId) ? DefaultLoggingFileName : $"velopack_{AppId}.log";
                    var logFilePath = Path.Combine(Path.GetTempPath(), logFileName);
                    var fileLog = new FileVelopackLogger(logFilePath, currentProcessId);
                    CombinedLogger.Add(fileLog);
                } catch (Exception ex) {
                    tempFileLogException = ex;
                }
            }

            if (tempFileLogException is not null) {
                //NB: fileLogException is not null here
                initLog.Error("Unable to create file logger: " + new AggregateException(fileLogException!, tempFileLogException));
            } else if (fileLogException is not null) {
                initLog.Info("Unable to create file logger; using temp directory for log instead");
                initLog.Trace($"File logger exception: {fileLogException}");
            }

            if (AppId == null) {
                initLog.Warn(
                    $"Failed to initialize {nameof(WindowsVelopackLocator)}. This could be because the program is not installed or packaged properly.");
            } else {
                initLog.Info($"Initialized {nameof(WindowsVelopackLocator)} for {AppId} v{CurrentlyInstalledVersion}");
            }
        }

        private string? GetPackagesDir()
        {
            const string PackagesDirName = "packages";

            string? writableRootDir = PossibleDirectories()
                .FirstOrDefault(IsWritable);

            if (writableRootDir == null) {
                Log.Warn("Unable to find a writable root directory for package.");
                return null;
            }

            Log.Trace("Using writable root directory: " + writableRootDir);

            return CreateSubDirIfDoesNotExist(writableRootDir, PackagesDirName);

            static bool IsWritable(string? directoryPath)
            {
                if (directoryPath == null) return false;

                try {
                    if (!Directory.Exists(directoryPath)) {
                        Directory.CreateDirectory(directoryPath);
                    }

                    return PathUtil.IsDirectoryWritable(directoryPath);
                } catch {
                    return false;
                }
            }

            IEnumerable<string?> PossibleDirectories()
            {
                yield return RootAppDir;
                yield return TempAppRootDirectory;
            }
        }

        private string TempAppRootDirectory => Path.Combine(Path.GetTempPath(), "velopack_" + AppId);
    }
}