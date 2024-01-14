using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;

namespace Velopack.Locators
{
    /// <summary>
    /// A base class describing where Velopack can find key folders and files.
    /// </summary>
    public abstract class VelopackLocator : IVelopackLocator
    {
        private static VelopackLocator _current;

        /// <summary>
        /// Auto-detect the platform from the current operating system.
        /// </summary>
        public static VelopackLocator GetDefault(ILogger logger)
        {
            var log = logger ?? NullLogger.Instance;

            if (_current != null)
                return _current;

            if (VelopackRuntimeInfo.IsWindows)
                return _current = new WindowsVelopackLocator(log);

            if (VelopackRuntimeInfo.IsOSX)
                return _current = new OsxVelopackLocator(log);

            if (VelopackRuntimeInfo.IsLinux)
                return _current = new LinuxVelopackLocator(log);

            throw new PlatformNotSupportedException($"OS platform '{VelopackRuntimeInfo.SystemOs.GetOsLongName()}' is not supported.");
        }

        /// <inheritdoc/>
        public abstract string AppId { get; }

        /// <inheritdoc/>
        public abstract string RootAppDir { get; }

        /// <inheritdoc/>
        public abstract string PackagesDir { get; }

        /// <inheritdoc/>
        public virtual string AppTempDir => CreateSubDirIfDoesNotExist(PackagesDir, "VelopackTemp");

        /// <inheritdoc/>
        public abstract string UpdateExePath { get; }

        /// <inheritdoc/>
        public abstract string AppContentDir { get; }

        /// <inheritdoc/>
        public virtual string ThisExeRelativePath {
            get {
                var path = VelopackRuntimeInfo.EntryExePath;
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

        /// <inheritdoc cref="VelopackLocator"/>
        protected VelopackLocator(ILogger logger)
        {
            Log = logger;
        }

        /// <inheritdoc/>
        public virtual List<ReleaseEntry> GetLocalPackages()
        {
            try {
                if (CurrentlyInstalledVersion == null)
                    return new List<ReleaseEntry>(0);

                return Directory.EnumerateFiles(PackagesDir, "*.nupkg")
                    .Select(x => ReleaseEntry.GenerateFromFile(x))
                    .Where(x => x?.Version != null)
                    .ToList();
            } catch (Exception ex) {
                Log.Error(ex, "Error while reading local packages.");
                return new List<ReleaseEntry>(0);
            }
        }

        /// <inheritdoc/>
        public ReleaseEntry GetLatestLocalFullPackage()
        {
            return GetLocalPackages()
                .OrderByDescending(x => x.Version)
                .Where(x => !x.IsDelta)
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
    }
}