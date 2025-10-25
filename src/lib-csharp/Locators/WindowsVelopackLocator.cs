using System;
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
        public override string? PackagesDir => _embeddedPackagesDir
            ? CreateSubDirIfDoesNotExist(RootAppDir, "packages")
            : CreateSubDirIfDoesNotExist(VelopackAppSpecificDir, "packages");

        private readonly bool _embeddedPackagesDir;

        /// <summary>
        /// Provides a semi-persistent directory for storing app specific data outside the installation directory.
        /// </summary>
        public virtual string? VelopackAppSpecificDir => CreateSubDirIfDoesNotExist(VelopackDataDir, AppId);

        private string? VelopackDataDir => CreateSubDirIfDoesNotExist(AppDataDir, "velopack");
        private string? AppDataDir => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

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

            // Determine if we should use embedded packages (inside RootAppDir) or external packages (in AppData)
            // EmbeddedPackagesDir = IsPortable || RootAppDir is inside AppDataDir
            var wouldBeEmbedded = false;
            if (IsPortable) {
                initLog.Info("Portable install detected (.portable file present)");
                wouldBeEmbedded = true;
            } else if (RootAppDir != null && AppDataDir != null && PathUtil.IsFileInDirectory(RootAppDir, AppDataDir)) {
                initLog.Info("Root directory is inside AppData, using embedded packages directory");
                wouldBeEmbedded = true;
            }

            // If embedded, verify we can actually write to the directory
            if (RootAppDir != null) {
                var isWritable = PathUtil.IsDirectoryWritable(RootAppDir);
                initLog.Info($"Root directory writable: {isWritable}");

                if (wouldBeEmbedded && !isWritable) {
                    initLog.Warn("Root directory is not writable, switching to external packages directory");
                    wouldBeEmbedded = false;
                }
            }

            _embeddedPackagesDir = wouldBeEmbedded;

            if (_embeddedPackagesDir) {
                initLog.Info($"Using embedded packages directory: {PackagesDir}");
            } else {
                initLog.Info($"Using external packages directory: {PackagesDir}");
            }

            var logFilePath = Path.Combine(Path.GetTempPath(), DefaultLoggingFileName);
            if (!string.IsNullOrEmpty(AppId)) {
                logFilePath = Path.Combine(Path.GetTempPath(), $"velopack_{AppId}.log");
            }

            try {
                var fileLog = new FileVelopackLogger(logFilePath, currentProcessId);
                CombinedLogger.Add(fileLog);
            } catch (Exception ex) {
                initLog.Error("Unable to create file logger: " + ex);
            }

            if (PackagesDir != null && !_embeddedPackagesDir) {
                var writableUpdateExe = Path.GetFullPath(Path.Combine(PackagesDir, "..", "Update.exe"));
                if (UpdateExePath != null && !File.Exists(writableUpdateExe)) {
                    try {
                        File.Copy(UpdateExePath, writableUpdateExe);
                        initLog.Info($"Copied Update.exe to writable location: {writableUpdateExe}");
                        UpdateExePath = writableUpdateExe;
                    } catch (Exception ex) {
                        initLog.Error("Unable to copy Update.exe to writable location: " + ex);
                    }
                }
            }

            if (AppId is null) {
                initLog.Warn(
                    $"Failed to initialize {nameof(WindowsVelopackLocator)}. This could be because the program is not installed or packaged properly.");
            } else {
                initLog.Info($"Initialized {nameof(WindowsVelopackLocator)} for {AppId} v{CurrentlyInstalledVersion}");
            }
        }
    }
}