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
    /// The default for OSX. All application files will remain in the '.app'.
    /// All additional files (log, etc) will be placed in a temporary directory.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public class LinuxVelopackLocator : VelopackLocator
    {
        /// <inheritdoc />
        public override string? AppId { get; }

        /// <inheritdoc />
        public override string? RootAppDir { get; }

        /// <inheritdoc />
        public override string? UpdateExePath { get; }

        /// <inheritdoc />
        public override IProcessImpl Process { get; }

        /// <inheritdoc />
        public override SemanticVersion? CurrentlyInstalledVersion { get; }

        /// <inheritdoc />
        public override string? AppContentDir { get; }

        /// <inheritdoc />
        public override string? Channel { get; }

        /// <inheritdoc />
        public override string? AppTempDir => CreateSubDirIfDoesNotExist(TempUtil.GetDefaultTempBaseDirectory(), AppId);

        /// <inheritdoc />
        public override string? PackagesDir => CreateSubDirIfDoesNotExist(PersistentTempDir, "packages");

        /// <summary> /var/tmp/{velopack}/{appid}, for storing app specific files which need to be preserved. </summary>
        public string? PersistentTempDir => CreateSubDirIfDoesNotExist(PersistentVelopackDir, AppId);

        /// <summary> A pointer to /var/tmp/{velopack}, a location on linux which is semi-persistent. </summary>
        public string? PersistentVelopackDir => CreateSubDirIfDoesNotExist("/var/tmp", "velopack");

        /// <summary> File path of the .AppImage which mounted and ran this application. </summary>
        public string? AppImagePath => Environment.GetEnvironmentVariable("APPIMAGE");

        /// <summary>
        /// Creates a new <see cref="OsxVelopackLocator"/> and auto-detects the
        /// app information from metadata embedded in the .app.
        /// </summary>
        public LinuxVelopackLocator(IProcessImpl? processImpl, IVelopackLogger? customLog)
        {
            if (!VelopackRuntimeInfo.IsLinux)
                throw new NotSupportedException($"Cannot instantiate {nameof(LinuxVelopackLocator)} on a non-linux system.");

            CombinedLogger = new CombinedVelopackLogger(customLog);

            Process = processImpl ??= new DefaultProcessImpl(CombinedLogger);
            var ourPath = processImpl.GetCurrentProcessPath();
            var currentProcessId = processImpl.GetCurrentProcessId();

            using var initLog = new CachedVelopackLogger(CombinedLogger);
            initLog.Info($"Initializing {nameof(LinuxVelopackLocator)}");
            var logFilePath = Path.Combine(Path.GetTempPath(), DefaultLoggingFileName);

            // are we inside a mounted .AppImage?
            var ix = ourPath.IndexOf("/usr/bin/", StringComparison.InvariantCultureIgnoreCase);
            if (ix > 0) {
                var rootDir = ourPath.Substring(0, ix);
                var contentsDir = Path.Combine(rootDir, "usr", "bin");
                var updateExe = Path.Combine(contentsDir, "UpdateNix");
                var metadataPath = Path.Combine(contentsDir, CoreUtil.SpecVersionFileName);

                if (!String.IsNullOrEmpty(AppImagePath) && File.Exists(AppImagePath)) {
                    if (File.Exists(updateExe) && PackageManifest.TryParseFromFile(metadataPath, out var manifest)) {
                        initLog.Info("Located valid manifest file at: " + metadataPath);
                        AppId = manifest.Id;
                        RootAppDir = rootDir;
                        AppContentDir = contentsDir;
                        UpdateExePath = updateExe;
                        CurrentlyInstalledVersion = manifest.Version;
                        Channel = manifest.Channel;
                        logFilePath = Path.Combine(Path.GetTempPath(), $"velopack_{manifest.Id}.log");
                    } else {
                        initLog.Error("Unable to locate UpdateNix in " + contentsDir);
                    }
                } else {
                    initLog.Error("Unable to locate .AppImage ($APPIMAGE)");
                }
            } else {
                initLog.Warn(
                    $"Unable to locate .AppImage root from '{ourPath}'. " +
                    $"This warning indicates that the application is not running from a mounted .AppImage, for example during development.");
            }

            try {
                var fileLog = new FileVelopackLogger(logFilePath, currentProcessId);
                CombinedLogger.Add(fileLog);
            } catch (Exception ex) {
                initLog.Error("Unable to create file logger: " + ex);
            }

            if (AppId == null) {
                initLog.Warn($"Failed to initialise {nameof(LinuxVelopackLocator)}. This could be because the program is not in an .AppImage.");
            } else {
                initLog.Info($"Initialised {nameof(LinuxVelopackLocator)} for {AppId} v{CurrentlyInstalledVersion}");
            }
        }
    }
}