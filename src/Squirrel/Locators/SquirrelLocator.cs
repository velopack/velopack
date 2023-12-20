using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NuGet.Versioning;

namespace Squirrel.Locators
{
    /// <summary>
    /// A base class describing where Squirrel can find key folders and files.
    /// </summary>
    public abstract class SquirrelLocator : ISquirrelLocator
    {
        private static SquirrelLocator _current;

        /// <summary>
        /// Auto-detect the platform from the current operating system.
        /// </summary>
        public static SquirrelLocator GetDefault()
        {
            if (SquirrelRuntimeInfo.IsWindows)
                return _current ??= new WindowsSquirrelLocator();

            if (SquirrelRuntimeInfo.IsOSX)
                return _current ??= new OsxSquirrelLocator();

            throw new NotSupportedException($"OS platform '{SquirrelRuntimeInfo.SystemOs.GetOsLongName()}' is not supported.");
        }

        /// <inheritdoc/>
        public abstract string AppId { get; }

        /// <inheritdoc/>
        public abstract string RootAppDir { get; }

        /// <inheritdoc/>
        public abstract string PackagesDir { get; }

        /// <inheritdoc/>
        public abstract string AppTempDir { get; }

        /// <inheritdoc/>
        public abstract string UpdateExePath { get; }

        /// <inheritdoc/>
        public abstract SemanticVersion CurrentlyInstalledVersion { get; }

        /// <inheritdoc/>
        public virtual List<ReleaseEntryName> GetLocalPackages()
        {
            var query = from x in Directory.EnumerateFiles(PackagesDir, "*.nupkg")
                        let re = ReleaseEntryName.FromEntryFileName(Path.GetFileName(x))
                        where re.Version != null
                        select re;
            return query.ToList();
        }

        /// <inheritdoc/>
        public ReleaseEntryName GetLatestLocalPackage()
        {
            var packages = GetLocalPackages();
            return packages.OrderByDescending(x => x.Version).FirstOrDefault();
        }

        /// <summary>
        /// Given a base dir and a directory name, will create a new sub directory of that name.
        /// Will return null if baseDir is null, or if baseDir does not exist. 
        /// </summary>
        protected static string CreateSubDirIfDoesNotExist(string baseDir, string newDir)
        {
            if (String.IsNullOrEmpty(baseDir) || string.IsNullOrEmpty(newDir)) return null;
            var infoBase = new DirectoryInfo(baseDir);
            if (!infoBase.Exists) return null;
            var info = new DirectoryInfo(Path.Combine(baseDir, newDir));
            if (!info.Exists) info.Create();
            return info.FullName;
        }

        // /// <summary>
        // /// Starts Update.exe with the correct arguments to restart this process.
        // /// Update.exe will wait for this process to exit, and apply any pending version updates
        // /// before re-launching the latest version.
        // /// </summary>
        // public virtual Process StartRestartingProcess(string exeToStart = null, string arguments = null)
        // {
        //     exeToStart = exeToStart ?? Path.GetFileName(SquirrelRuntimeInfo.EntryExePath);
        // 
        //     List<string> args = new() {
        //         "--processStartAndWait",
        //         exeToStart,
        //     };
        // 
        //     if (arguments != null) {
        //         args.Add("-a");
        //         args.Add(arguments);
        //     }
        // 
        //     return PlatformUtil.StartProcessNonBlocking(UpdateExePath, args, Path.GetDirectoryName(UpdateExePath));
        // }
    }
}