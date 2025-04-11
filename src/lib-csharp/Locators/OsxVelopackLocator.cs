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
    [SupportedOSPlatform("osx")]
    public class OsxVelopackLocator : VelopackLocator
    {
        /// <inheritdoc />
        public override string? AppId { get; }

        /// <inheritdoc />
        public override string? RootAppDir { get; }

        /// <inheritdoc />
        public override string? UpdateExePath { get; }

        /// <inheritdoc />
        public override SemanticVersion? CurrentlyInstalledVersion { get; }

        /// <inheritdoc />
        public override string? AppContentDir => RootAppDir;

        /// <inheritdoc />
        public override string? AppTempDir => CreateSubDirIfDoesNotExist(TempUtil.GetDefaultTempBaseDirectory(), AppId);

        /// <inheritdoc />
        public override string? PackagesDir => CreateSubDirIfDoesNotExist(CachesAppDir, "packages");

        /// <inheritdoc />
        public override IVelopackLogger Log { get; }

        private string? CachesAppDir => CreateSubDirIfDoesNotExist(CachesVelopackDir, AppId);
        private string? CachesVelopackDir => CreateSubDirIfDoesNotExist(CachesDir, "velopack");
        private string? CachesDir => CreateSubDirIfDoesNotExist(LibraryDir, "Caches");
        private string? LibraryDir => CreateSubDirIfDoesNotExist(HomeDir, "Library");
        private string? HomeDir => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <inheritdoc />
        public override string? Channel { get; }

        /// <inheritdoc />
        public override uint ProcessId { get; }

        /// <inheritdoc />
        public override string ProcessExePath { get; }

        /// <summary>
        /// Creates a new <see cref="OsxVelopackLocator"/> and auto-detects the
        /// app information from metadata embedded in the .app.
        /// </summary>
        public OsxVelopackLocator(string currentProcessPath, uint currentProcessId, IVelopackLogger? customLog)
        {
            if (!VelopackRuntimeInfo.IsOSX)
                throw new NotSupportedException($"Cannot instantiate {nameof(OsxVelopackLocator)} on a non-osx system.");

            ProcessId = currentProcessId;
            var ourPath = ProcessExePath = currentProcessPath;

            var combinedLog = new CombinedVelopackLogger();
            combinedLog.Add(customLog);
            Log = combinedLog;

            using var initLog = new CachedVelopackLogger(combinedLog);
            initLog.Info($"Initializing {nameof(OsxVelopackLocator)}");

            string logFolder = Path.GetTempPath();

            if (!string.IsNullOrEmpty(HomeDir) && Directory.Exists(HomeDir)) {
                var userLogsFolder = Path.Combine(HomeDir!, "Library", "Logs");
                if (!Directory.Exists(userLogsFolder)) {
                    logFolder = userLogsFolder;
                }
            }

            var logFileName = DefaultLoggingFileName;

            // are we inside a .app?
            var ix = ourPath.IndexOf(".app/", StringComparison.InvariantCultureIgnoreCase);
            if (ix > 0) {
                var appPath = ourPath.Substring(0, ix + 4);
                var contentsDir = Path.Combine(appPath, "Contents");
                var macosDir = Path.Combine(contentsDir, "MacOS");
                var updateExe = Path.Combine(macosDir, "UpdateMac");
                var metadataPath = Path.Combine(macosDir, CoreUtil.SpecVersionFileName);

                if (File.Exists(updateExe) && PackageManifest.TryParseFromFile(metadataPath, out var manifest)) {
                    initLog.Info("Located valid manifest file at: " + metadataPath);
                    AppId = manifest.Id;
                    RootAppDir = appPath;
                    UpdateExePath = updateExe;
                    CurrentlyInstalledVersion = manifest.Version;
                    Channel = manifest.Channel;
                    logFileName = $"velopack_{manifest.Id}.log";
                }
            } else {
                initLog.Warn($"Unable to locate .app root from '{ourPath}'");
            }

            try {
                var logFilePath = Path.Combine(logFolder, logFileName);
                var fileLog = new FileVelopackLogger(logFilePath, currentProcessId);
                combinedLog.Add(fileLog);
            } catch (Exception ex) {
                initLog.Error("Unable to create file logger: " + ex);
            }

            if (AppId == null) {
                initLog.Warn($"Failed to initialise {nameof(OsxVelopackLocator)}. This could be because the program is not in a .app bundle.");
            } else {
                initLog.Info($"Initialised {nameof(OsxVelopackLocator)} for {AppId} v{CurrentlyInstalledVersion}");
            }
        }
    }
}