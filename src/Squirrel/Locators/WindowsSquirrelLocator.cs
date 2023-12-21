using System;
using System.IO;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Squirrel.NuGet;

namespace Squirrel.Locators
{
    /// <summary>
    /// An implementation for Windows which uses the Squirrel default paths.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsSquirrelLocator : SquirrelLocator
    {
        /// <inheritdoc />
        public override string AppId { get; }

        /// <inheritdoc />
        public override string RootAppDir { get; }

        /// <inheritdoc />
        public override string UpdateExePath { get; }

        /// <inheritdoc />
        public override string AppContentDir { get; }

        /// <inheritdoc />
        public override SemanticVersion CurrentlyInstalledVersion { get; }

        /// <inheritdoc />
        public override string PackagesDir => CreateSubDirIfDoesNotExist(RootAppDir, "packages");

        /// <inheritdoc />
        public override string AppTempDir => CreateSubDirIfDoesNotExist(PackagesDir, "SquirrelClowdTemp");

        /// <inheritdoc cref="WindowsSquirrelLocator" />
        public WindowsSquirrelLocator(ILogger logger) : this(SquirrelRuntimeInfo.EntryExePath, logger)
        {
        }

        /// <summary>
        /// Internal use only. Auto detect app details from the specified EXE path.
        /// </summary>
        internal WindowsSquirrelLocator(string ourExePath, ILogger logger)
            : base(logger)
        {
            if (!SquirrelRuntimeInfo.IsWindows)
                throw new NotSupportedException("Cannot instantiate WindowsLocator on a non-Windows system.");

            // We try various approaches here. Firstly, if Update.exe is in the parent directory,
            // we use that. If it's not present, we search for a parent "current" or "app-{ver}" directory,
            // which could designate that this executable is running in a nested sub-directory.
            // There is some legacy code here, because it's possible that we're running in an "app-{ver}" 
            // directory which is NOT containing a sq.version, in which case we need to infer a lot of info.

            ourExePath = Path.GetFullPath(ourExePath);
            var myDirPath = Path.GetDirectoryName(ourExePath);
            var myDirName = Path.GetFileName(myDirPath);
            var possibleUpdateExe = Path.GetFullPath(Path.Combine(myDirPath, "..\\Update.exe"));
            var ixCurrent = ourExePath.LastIndexOf("/current/", StringComparison.InvariantCultureIgnoreCase);

            Log.Info("Initialising WindowsSquirrelLocator");

            if (File.Exists(possibleUpdateExe)) {
                Log.Info("Update.exe found in parent directory");
                // we're running in a directory with an Update.exe in the parent directory
                var manifestFile = Path.Combine(myDirPath, "sq.version");
                if (NuspecManifest.TryParseFromFile(manifestFile, out var manifest)) {
                    // ideal, the info we need is in a manifest file.
                    Log.Info("Located valid manifest file at: " + manifestFile);
                    AppId = manifest.Id;
                    CurrentlyInstalledVersion = manifest.Version;
                    RootAppDir = Path.GetDirectoryName(possibleUpdateExe);
                    UpdateExePath = possibleUpdateExe;
                    AppContentDir = myDirPath;
                } else if (Utility.PathPartStartsWith(myDirName, "app-") && NuGetVersion.TryParse(myDirName.Substring(4), out var version)) {
                    // this is a legacy case, where we're running in an 'root/app-*/' directory, and there is no manifest.
                    Log.Warn("Legacy app-* directory detected, sq.version not found. Using directory name for AppId and Version.");
                    AppId = Path.GetFileName(Path.GetDirectoryName(possibleUpdateExe));
                    CurrentlyInstalledVersion = version;
                    RootAppDir = Path.GetDirectoryName(possibleUpdateExe);
                    UpdateExePath = possibleUpdateExe;
                    AppContentDir = myDirPath;
                }
            } else if (ixCurrent > 0) {
                // this is an attempt to handle the case where we are running in a nested current directory.
                var rootDir = ourExePath.Substring(0, ixCurrent);
                var currentDir = Path.Combine(rootDir, "current");
                var manifestFile = Path.Combine(currentDir, "sq.version");
                possibleUpdateExe = Path.GetFullPath(Path.Combine(rootDir, "Update.exe"));
                // we only support parsing a manifest when we're in a nested current directory. no legacy fallback.
                if (File.Exists(possibleUpdateExe) && NuspecManifest.TryParseFromFile(manifestFile, out var manifest)) {
                    Log.Warn("Running in deeply nested directory. This is not an advised use-case.");
                    Log.Info("Located valid manifest file at: " + manifestFile);
                    RootAppDir = Path.GetDirectoryName(possibleUpdateExe);
                    UpdateExePath = possibleUpdateExe;
                    AppId = manifest.Id;
                    CurrentlyInstalledVersion = manifest.Version;
                    AppContentDir = currentDir;
                }
            }
        }
    }
}