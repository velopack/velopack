using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack.Locators;
using Velopack.NuGet;

namespace Velopack.Windows
{
    /// <summary>
    /// Specifies several common places where shortcuts can be installed on a user's system
    /// </summary>
    [Flags]
    public enum ShortcutLocation
    {
        /// <summary>
        /// A shortcut in ProgramFiles within a publisher sub-directory
        /// </summary>
        StartMenu = 1 << 0,

        /// <summary>
        /// A shortcut on the current user desktop
        /// </summary>
        Desktop = 1 << 1,

        /// <summary>
        /// A shortcut in Startup/Run folder will cause the app to be automatially started on user login.
        /// </summary>
        Startup = 1 << 2,

        /// <summary>
        /// A shortcut in the application folder, useful for portable applications.
        /// </summary>
        AppRoot = 1 << 3,

        /// <summary>
        /// A shortcut in ProgramFiles root folder (not in a company/publisher sub-directory). This is commonplace as of more recent versions of windows.
        /// </summary>
        StartMenuRoot = 1 << 4,
    }

    /// <summary>
    /// A helper class to create or delete windows shortcuts.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class Shortcuts
    {
        /// <summary> Log for diagnostic messages. </summary>
        protected ILogger Log { get; }

        /// <summary> Locator to use for finding important application paths. </summary>
        protected IVelopackLocator Locator { get; }

        /// <inheritdoc cref="Shortcuts"/>
        public Shortcuts(ILogger logger = null, IVelopackLocator locator = null)
        {
            Log = logger ?? NullLogger.Instance;
            Locator = locator ?? VelopackLocator.GetDefault(Log);
        }

        /// <summary>
        /// Create a shortcut to the currently running executable at the specified locations. 
        /// See <see cref="CreateShortcut"/> to create a shortcut to a different program
        /// </summary>
        public void CreateShortcutForThisExe(ShortcutLocation location = ShortcutLocation.Desktop | ShortcutLocation.StartMenuRoot)
        {
            CreateShortcut(
                Locator.ThisExeRelativePath,
                location,
                false,
                null,  // shortcut arguments 
                null); // shortcut icon
        }

        /// <summary>
        /// Removes a shortcut for the currently running executable at the specified locations
        /// </summary>
        public void RemoveShortcutForThisExe(ShortcutLocation location = ShortcutLocation.Desktop | ShortcutLocation.StartMenu | ShortcutLocation.StartMenuRoot)
        {
            DeleteShortcuts(
                Locator.ThisExeRelativePath,
                location);
        }

        /// <summary>
        /// Searches for existing shortcuts to an executable inside the current package.
        /// </summary>
        /// <param name="relativeExeName">The relative path or filename of the executable (from the current app dir).</param>
        /// <param name="locations">The locations to search.</param>
        public Dictionary<ShortcutLocation, ShellLink> FindShortcuts(string relativeExeName, ShortcutLocation locations)
        {
            var release = Locator.GetLatestLocalFullPackage();
            var pkgDir = Locator.PackagesDir;
            var currentDir = Locator.AppContentDir;
            var rootAppDirectory = Locator.RootAppDir;

            var ret = new Dictionary<ShortcutLocation, ShellLink>();
            var pkgPath = Path.Combine(pkgDir, release.OriginalFilename);
            var zf = new ZipPackage(pkgPath);
            var exePath = Path.Combine(currentDir, relativeExeName);
            if (!File.Exists(exePath))
                return ret;

            var fileVerInfo = FileVersionInfo.GetVersionInfo(exePath);

            foreach (var f in (ShortcutLocation[]) Enum.GetValues(typeof(ShortcutLocation))) {
                if (!locations.HasFlag(f)) continue;
                var file = LinkPathForVersionInfo(f, zf, fileVerInfo, rootAppDirectory);
                if (File.Exists(file)) {
                    Log.Info($"Opening existing shortcut for {relativeExeName} ({file})");
                    ret.Add(f, new ShellLink(file));
                }
            }

            return ret;
        }

        /// <summary>
        /// Creates new shortcuts to the specified executable at the specified locations.
        /// </summary>
        /// <param name="relativeExeName">The relative path or filename of the executable (from the current app dir).</param>
        /// <param name="locations">The locations to create shortcuts.</param>
        /// <param name="updateOnly">If true, shortcuts will be updated instead of created.</param>
        /// <param name="programArguments">The arguments the application should be launched with</param>
        /// <param name="icon">Path to a specific icon to use instead of the exe icon.</param>
        public void CreateShortcut(string relativeExeName, ShortcutLocation locations, bool updateOnly, string programArguments, string icon = null)
        {
            var release = Locator.GetLatestLocalFullPackage();
            var pkgDir = Locator.PackagesDir;
            var currentDir = Locator.AppContentDir;
            var rootAppDirectory = Locator.RootAppDir;
            Log.Info($"About to create shortcuts for {relativeExeName}, rootAppDir {rootAppDirectory}");

            var pkgPath = Path.Combine(pkgDir, release.OriginalFilename);
            var zf = new ZipPackage(pkgPath);
            var exePath = Path.Combine(currentDir, relativeExeName);
            if (!File.Exists(exePath))
                throw new FileNotFoundException($"Could not find: {exePath}");

            var fileVerInfo = FileVersionInfo.GetVersionInfo(exePath);

            foreach (var f in (ShortcutLocation[]) Enum.GetValues(typeof(ShortcutLocation))) {
                if (!locations.HasFlag(f)) continue;

                var file = LinkPathForVersionInfo(f, zf, fileVerInfo, rootAppDirectory);
                var fileExists = File.Exists(file);

                // NB: If we've already installed the app, but the shortcut
                // is no longer there, we have to assume that the user didn't
                // want it there and explicitly deleted it, so we shouldn't
                // annoy them by recreating it.
                if (!fileExists && updateOnly) {
                    Log.Warn($"Wanted to update shortcut {file} but it appears user deleted it");
                    continue;
                }

                Log.Info($"Creating shortcut for {relativeExeName} => {file}");

                ShellLink sl;
                Utility.Retry(() => {
                    File.Delete(file);

                    var target = Path.Combine(currentDir, relativeExeName);
                    sl = new ShellLink {
                        Target = target,
                        IconPath = icon ?? target,
                        IconIndex = 0,
                        WorkingDirectory = Path.GetDirectoryName(exePath),
                        Description = zf.ProductDescription,
                    };

                    if (!String.IsNullOrWhiteSpace(programArguments)) {
                        sl.Arguments += String.Format(" -a \"{0}\"", programArguments);
                    }

                    //var appUserModelId = Utility.GetAppUserModelId(zf.Id, exeName);
                    //var toastActivatorCLSID = Utility.CreateGuidFromHash(appUserModelId).ToString();
                    //sl.SetAppUserModelId(appUserModelId);
                    //sl.SetToastActivatorCLSID(toastActivatorCLSID);

                    Log.Info($"About to save shortcut: {file} (target {sl.Target}, workingDir {sl.WorkingDirectory}, args {sl.Arguments})");
                    sl.Save(file);
                });
            }
        }

        /// <summary>
        /// Delete all the shortcuts for the specified executable in the specified locations.
        /// </summary>
        /// <param name="relativeExeName">The relative path or filename of the executable (from the current app dir).</param>
        /// <param name="locations">The locations to create shortcuts.</param>
        public void DeleteShortcuts(string relativeExeName, ShortcutLocation locations)
        {
            var release = Locator.GetLatestLocalFullPackage();
            var pkgDir = Locator.PackagesDir;
            var currentDir = Locator.AppContentDir;
            var rootAppDirectory = Locator.RootAppDir;
            Log.Info($"About to delete shortcuts for {relativeExeName}, rootAppDir {rootAppDirectory}");

            var pkgPath = Path.Combine(pkgDir, release.OriginalFilename);
            var zf = new ZipPackage(pkgPath);
            var exePath = Path.Combine(currentDir, relativeExeName);
            if (!File.Exists(exePath)) return;

            var fileVerInfo = FileVersionInfo.GetVersionInfo(exePath);

            foreach (var f in (ShortcutLocation[]) Enum.GetValues(typeof(ShortcutLocation))) {
                if (!locations.HasFlag(f)) continue;
                var file = LinkPathForVersionInfo(f, zf, fileVerInfo, rootAppDirectory);
                Log.Info($"Removing shortcut for {relativeExeName} => {file}");
                try {
                    if (File.Exists(file)) File.Delete(file);
                } catch (Exception ex) {
                    Log.Error(ex, "Couldn't delete shortcut: " + file);
                }
            }
        }

        /// <summary>
        /// Given an <see cref="IPackage"/> and <see cref="FileVersionInfo"/> return the target shortcut path.
        /// </summary>
        protected virtual string LinkPathForVersionInfo(ShortcutLocation location, IPackage package, FileVersionInfo versionInfo, string rootdir)
        {
            var possibleProductNames = new[] {
                    versionInfo.ProductName,
                    package.ProductName,
                    versionInfo.FileDescription,
                    Path.GetFileNameWithoutExtension(versionInfo.FileName)
                };

            var possibleCompanyNames = new[] {
                    versionInfo.CompanyName,
                    package.ProductCompany,
                };

            var prodName = possibleCompanyNames.First(x => !String.IsNullOrWhiteSpace(x));
            var pkgName = possibleProductNames.First(x => !String.IsNullOrWhiteSpace(x));

            return GetLinkPath(location, pkgName, prodName, rootdir);
        }

        /// <summary>
        /// Given the application info, return the shortcut target path.
        /// </summary>
        protected virtual string GetLinkPath(ShortcutLocation location, string title, string applicationName, string rootdir, bool createDirectoryIfNecessary = true)
        {
            var dir = default(string);

            switch (location) {
            case ShortcutLocation.Desktop:
                dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                break;
            case ShortcutLocation.StartMenu:
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", applicationName);
                break;
            case ShortcutLocation.StartMenuRoot:
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                break;
            case ShortcutLocation.Startup:
                dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                break;
            case ShortcutLocation.AppRoot:
                dir = rootdir;
                break;
            }

            if (createDirectoryIfNecessary && !Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            return Path.Combine(dir, title + ".lnk");
        }
    }
}
