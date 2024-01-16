using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.NuGet;

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
        public override SemanticVersion? CurrentlyInstalledVersion { get; }

        /// <inheritdoc />
        public override string? AppContentDir { get; }

        /// <inheritdoc />
        public override string? Channel { get; }

        /// <inheritdoc />
        public override string? AppTempDir => CreateSubDirIfDoesNotExist(Utility.GetDefaultTempBaseDirectory(), AppId);

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
        public LinuxVelopackLocator(ILogger logger)
            : base(logger)
        {
            if (!VelopackRuntimeInfo.IsLinux)
                throw new NotSupportedException("Cannot instantiate LinuxVelopackLocator on a non-linux system.");

            Log.Info($"Initialising {nameof(LinuxVelopackLocator)}");

            // are we inside a mounted .AppImage?
            var ourPath = VelopackRuntimeInfo.EntryExePath;
            var ix = ourPath.IndexOf("/usr/bin/", StringComparison.InvariantCultureIgnoreCase);
            if (ix <= 0) {
                Log.Warn($"Unable to locate .AppImage root from '{ourPath}'");
                return;
            }

            var rootDir = ourPath.Substring(0, ix);
            var contentsDir = Path.Combine(rootDir, "usr", "bin");
            var updateExe = Path.Combine(contentsDir, "UpdateNix");
            var metadataPath = Path.Combine(contentsDir, Utility.SpecVersionFileName);

            if (!String.IsNullOrEmpty(AppImagePath) && File.Exists(AppImagePath)) {
                if (File.Exists(updateExe) && PackageManifest.TryParseFromFile(metadataPath, out var manifest)) {
                    Log.Info("Located valid manifest file at: " + metadataPath);
                    AppId = manifest.Id;
                    RootAppDir = rootDir;
                    AppContentDir = contentsDir;
                    UpdateExePath = updateExe;
                    CurrentlyInstalledVersion = manifest.Version;
                    Channel = manifest.Channel;
                } else {
                    logger.Error("Unable to locate UpdateNix in " + contentsDir);
                }
            } else {
                logger.Error("Unable to locate .AppImage ($APPIMAGE)");
            }
        }
    }
}
