using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

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
        public override string AppId { get; }

        /// <inheritdoc />
        public override string RootAppDir { get; }

        /// <inheritdoc />
        public override string UpdateExePath { get; }

        /// <inheritdoc />
        public override SemanticVersion CurrentlyInstalledVersion { get; }

        /// <inheritdoc />
        public override string AppContentDir { get; }

        /// <inheritdoc />
        public override string AppTempDir => CreateSubDirIfDoesNotExist(Utility.GetDefaultTempBaseDirectory(), AppId);

        /// <inheritdoc />
        public override string PackagesDir => CreateSubDirIfDoesNotExist(PersistentTempDir, "packages");

        /// <summary> /var/tmp/{velopack}/{appid}, for storing app specific files which need to be preserved. </summary>
        public string PersistentTempDir => CreateSubDirIfDoesNotExist(PersistentVelopackDir, AppId);

        /// <summary> A pointer to /var/tmp/{velopack}, a location on linux which is semi-persistent. </summary>
        public string PersistentVelopackDir => CreateSubDirIfDoesNotExist("/var/tmp", "velopack");

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

            throw new NotImplementedException();
        }
    }
}
