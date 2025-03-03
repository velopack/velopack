using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NuGet.Versioning;
using Velopack.Logging;

namespace Velopack.Locators
{
    /// <summary>
    /// A base class describing where Velopack can find key folders and files.
    /// </summary>
    public abstract class VelopackLocator : IVelopackLocator
    {
        private static IVelopackLocator? _current;

        /// <summary>
        /// Check if a VelopackLocator has been set for the current process.
        /// </summary>
        public static bool IsCurrentSet => _current != null;

        /// <summary>
        /// Get the current locator in use, this process-wide locator can be set/overriden during VelopackApp.Build().
        /// Alternatively, most methods which use locators also accept an IVelopackLocator as a parameter.
        /// </summary>
        public static IVelopackLocator Current {
            get {
                if (_current == null)
                    throw new InvalidOperationException(
                        "No VelopackLocator has been set. Either call VelopackApp.Build() or provide IVelopackLocator as a method parameter.");
                return _current;
            }
        }

        /// <summary> Create a new default locator based on the current operating system. </summary>
        public static IVelopackLocator CreateDefaultForPlatform(IVelopackLogger? logger = null)
        {
            var process = Process.GetCurrentProcess();
            var processExePath = process.MainModule?.FileName
                                 ?? throw new InvalidOperationException("Could not determine process path, please construct IVelopackLocator manually.");
            var processId = (uint) process.Id;

            if (VelopackRuntimeInfo.IsWindows)
                return _current = new WindowsVelopackLocator(processExePath, processId, logger);

            if (VelopackRuntimeInfo.IsOSX)
                return _current = new OsxVelopackLocator(processExePath, processId, logger);

            if (VelopackRuntimeInfo.IsLinux)
                return _current = new LinuxVelopackLocator(processExePath, processId, logger);

            throw new PlatformNotSupportedException($"OS platform '{VelopackRuntimeInfo.SystemOs.GetOsLongName()}' is not supported.");
        }

        internal static void SetCurrentLocator(IVelopackLocator locator)
        {
            _current = locator;
        }

        internal static IVelopackLocator GetCurrentOrCreateDefault(IVelopackLogger? logger = null)
        {
            _current ??= CreateDefaultForPlatform(logger);
            return _current;
        }

        /// <inheritdoc/>
        public abstract string? AppId { get; }

        /// <inheritdoc/>
        public abstract string? RootAppDir { get; }

        /// <inheritdoc/>
        public abstract string? PackagesDir { get; }

        /// <inheritdoc/>
        public virtual string? AppTempDir => CreateSubDirIfDoesNotExist(PackagesDir, "VelopackTemp");

        /// <inheritdoc/>
        public abstract string? UpdateExePath { get; }

        /// <inheritdoc/>
        public abstract string? AppContentDir { get; }

        /// <inheritdoc/>
        public abstract string? Channel { get; }

        /// <inheritdoc/>
        public abstract IVelopackLogger Log { get; }

        /// <inheritdoc/>
        public abstract uint ProcessId { get; }

        /// <inheritdoc/>
        public abstract string ProcessExePath { get; }

        /// <inheritdoc/>
        public virtual bool IsPortable => false;

        /// <inheritdoc/>
        public virtual string? ThisExeRelativePath {
            get {
                if (AppContentDir == null) return null;
                var path = ProcessExePath;
                if (path.StartsWith(AppContentDir, StringComparison.OrdinalIgnoreCase)) {
                    return path.Substring(AppContentDir.Length + 1);
                } else {
                    throw new InvalidOperationException(path + " is not contained in " + AppContentDir);
                }
            }
        }

        /// <inheritdoc/>
        public abstract SemanticVersion? CurrentlyInstalledVersion { get; }

        /// <inheritdoc/>
        public virtual List<VelopackAsset> GetLocalPackages()
        {
            try {
                if (CurrentlyInstalledVersion == null)
                    return new List<VelopackAsset>(0);

                var list = new List<VelopackAsset>();
                if (PackagesDir != null) {
                    foreach (var pkg in Directory.EnumerateFiles(PackagesDir, "*.nupkg")) {
                        try {
                            var asset = VelopackAsset.FromNupkg(pkg);
                            if (asset?.Version != null) {
                                list.Add(asset);
                            }
                        } catch (Exception ex) {
                            Log.Warn(ex, $"Error while reading local package '{pkg}'.");
                        }
                    }
                }

                return list;
            } catch (Exception ex) {
                Log.Error(ex, "Error while reading local packages.");
                return new List<VelopackAsset>(0);
            }
        }

        /// <inheritdoc/>
        public virtual VelopackAsset? GetLatestLocalFullPackage()
        {
            return GetLocalPackages()
                .OrderByDescending(x => x.Version)
                .FirstOrDefault(a => a.Type == VelopackAssetType.Full);
        }

        /// <summary>
        /// Given a base dir and a directory name, will create a new sub directory of that name.
        /// Will return null if baseDir is null, or if baseDir does not exist. 
        /// </summary>
        protected static string? CreateSubDirIfDoesNotExist(string? baseDir, string? newDir)
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
            if (PackagesDir == null) return null;
            var stagedUserIdFile = Path.Combine(PackagesDir, ".betaId");
            Guid ret;

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
                Log.Warn($"No staging userId in file '{stagedUserIdFile}', creating a new one.");
            }

            ret = Guid.NewGuid();
            try {
                File.WriteAllText(stagedUserIdFile, ret.ToString("N"), Encoding.UTF8);
                Log.Info($"Generated new staging userId: {ret}");
                return ret;
            } catch (Exception ex) {
                Log.Warn(ex, "Couldn't write out staging userId.");
                return null;
            }
        }
    }
}