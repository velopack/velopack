using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
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
        public static SquirrelLocator GetDefault(ILogger logger)
        {
            if (_current != null)
                return _current;

            if (SquirrelRuntimeInfo.IsWindows)
                return _current ??= new WindowsSquirrelLocator(logger);

            if (SquirrelRuntimeInfo.IsOSX)
                return _current ??= new OsxSquirrelLocator(logger);

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
        public abstract string AppContentDir { get; }

        /// <inheritdoc/>
        public virtual string ThisExeRelativePath {
            get {
                var path = SquirrelRuntimeInfo.EntryExePath;
                if (path.StartsWith(AppContentDir, StringComparison.OrdinalIgnoreCase)) {
                    return path.Substring(AppContentDir.Length + 1);
                } else {
                    throw new InvalidOperationException(path + " is not contained in " + AppContentDir);
                }
            }
        }

        /// <inheritdoc/>
        public abstract SemanticVersion CurrentlyInstalledVersion { get; }

        /// <summary> The log interface to use for diagnostic messages. </summary>
        protected ILogger Log { get; }

        /// <inheritdoc cref="SquirrelLocator"/>
        protected SquirrelLocator(ILogger logger)
        {
            Log = logger;
        }

        /// <inheritdoc/>
        public virtual List<ReleaseEntry> GetLocalPackages()
        {
            return Directory.EnumerateFiles(PackagesDir, "*.nupkg")
                .Select(x => ReleaseEntry.GenerateFromFile(x))
                .Where(x => x?.Version != null)
                .ToList();
        }

        /// <inheritdoc/>
        public ReleaseEntry GetLatestLocalPackage()
        {
            return GetLocalPackages()
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
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

        /// <inheritdoc/>
        public Guid? GetOrCreateStagedUserId()
        {
            var stagedUserIdFile = Path.Combine(PackagesDir, ".betaId");
            var ret = default(Guid);

            if (File.Exists(stagedUserIdFile)) {
                try {
                    if (!Guid.TryParse(File.ReadAllText(stagedUserIdFile, Encoding.UTF8), out ret)) {
                        throw new Exception("File was read but contents were invalid");
                    }
                    Log.Info($"Loaded existing staging userId: {ret}");
                    return ret;
                } catch (Exception ex) {
                    Log.Debug(ex, "Couldn't read staging userId, creating a new one");
                }
            } else {
                Log.Warn($"No userId could not be parsed from '{stagedUserIdFile}', creating a new one.");
            }

            var prng = new Random();
            var buf = new byte[4096];
            prng.NextBytes(buf);

            ret = Utility.CreateGuidFromHash(buf);
            try {
                File.WriteAllText(stagedUserIdFile, ret.ToString(), Encoding.UTF8);
                Log.Info($"Generated new staging userId: {ret}");
                return ret;
            } catch (Exception ex) {
                Log.Warn(ex, "Couldn't write out staging userId.");
                return null;
            }
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