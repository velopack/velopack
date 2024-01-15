using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.NuGet;

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
        public override string AppId { get; }

        /// <inheritdoc />
        public override string RootAppDir { get; }

        /// <inheritdoc />
        public override string UpdateExePath { get; }

        /// <inheritdoc />
        public override SemanticVersion CurrentlyInstalledVersion { get; }

        /// <inheritdoc />
        public override string AppContentDir => RootAppDir;

        /// <inheritdoc />
        public override string AppTempDir => CreateSubDirIfDoesNotExist(Utility.GetDefaultTempBaseDirectory(), AppId);

        /// <inheritdoc />
        public override string PackagesDir => CreateSubDirIfDoesNotExist(AppTempDir, "packages");

        /// <inheritdoc />
        public override string Channel { get; }

        /// <summary>
        /// Creates a new <see cref="OsxVelopackLocator"/> and auto-detects the
        /// app information from metadata embedded in the .app.
        /// </summary>
        public OsxVelopackLocator(ILogger logger)
            : base(logger)
        {
            if (!VelopackRuntimeInfo.IsOSX)
                throw new NotSupportedException("Cannot instantiate OsxLocator on a non-osx system.");

            Log.Info($"Initialising {nameof(OsxVelopackLocator)}");

            // are we inside a .app?
            var ourPath = VelopackRuntimeInfo.EntryExePath;
            var ix = ourPath.IndexOf(".app/", StringComparison.InvariantCultureIgnoreCase);
            if (ix <= 0) {
                Log.Warn($"Unable to locate .app root from '{ourPath}'");
                return;
            }

            var appPath = ourPath.Substring(0, ix + 4);
            var contentsDir = Path.Combine(appPath, "Contents");
            var macosDir = Path.Combine(contentsDir, "MacOS");
            var updateExe = Path.Combine(macosDir, "UpdateMac");
            var metadataPath = Path.Combine(macosDir, Utility.SpecVersionFileName);

            if (File.Exists(updateExe) && NuspecManifest.TryParseFromFile(metadataPath, out var manifest)) {
                Log.Info("Located valid manifest file at: " + metadataPath);
                AppId = manifest.Id;
                RootAppDir = appPath;
                UpdateExePath = updateExe;
                CurrentlyInstalledVersion = manifest.Version;
                Channel = manifest.Channel;
            }
        }
    }
}