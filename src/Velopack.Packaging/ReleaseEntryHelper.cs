using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.NuGet;
using Velopack.Packaging.Exceptions;
using Velopack.Sources;

namespace Velopack.Packaging
{
    public class ReleaseEntryHelper
    {
        private readonly string _outputDir;
        private readonly ILogger _logger;
        private readonly string _channel;
        private Dictionary<string, List<ReleaseEntry>> _releases;

        public ReleaseEntryHelper(string outputDir, string channel, ILogger logger)
        {
            _outputDir = outputDir;
            _logger = logger;
            _channel = channel;
            _releases = GetReleasesFromDir(outputDir);
        }

        private static Dictionary<string, List<ReleaseEntry>> GetReleasesFromDir(string dir)
        {
            var rel = new Dictionary<string, List<ReleaseEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var releaseFile in Directory.EnumerateFiles(dir, "*.nupkg")) {
                var zip = new ZipPackage(releaseFile);
                var ch = zip.Channel ?? GetDefaultChannel(VelopackRuntimeInfo.SystemOs);
                if (!rel.ContainsKey(ch))
                    rel[ch] = new List<ReleaseEntry>();
                rel[ch].Add(new ReleaseEntry(Utility.CalculateFileSHA1(releaseFile), Path.GetFileName(releaseFile), new FileInfo(releaseFile).Length));
            }
            return rel;
        }

        public void ValidateForPackaging(SemanticVersion version)
        {
            if (!_releases.ContainsKey(_channel) || !_releases[_channel].Any())
                return;
            foreach (var release in _releases[_channel]) {
                if (version <= release.Version) {
                    throw new UserInfoException($"Release {release.OriginalFilename} in channel {_channel} is equal or greater to the current version {version}. Please increase the current package version or remove that release.");
                }
            }
        }

        public ReleasePackage GetPreviousFullRelease(SemanticVersion version)
        {
            var releases = _releases.ContainsKey(_channel) ? _releases[_channel] : null;
            if (releases == null || !releases.Any()) return null;
            var entry = releases
                .Where(x => x.IsDelta == false)
                .Where(x => VersionComparer.Version.Compare(x.Version, version) < 0)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (entry == null) return null;
            var file = Path.Combine(_outputDir, entry.OriginalFilename);
            return new ReleasePackage(file);
        }

        public ReleaseEntry GetLatestFullRelease(string channel)
        {
            channel ??= GetDefaultChannel(VelopackRuntimeInfo.SystemOs);
            var releases = _releases.ContainsKey(channel) ? _releases[channel] : null;
            if (releases == null || !releases.Any()) return null;
            return releases.Where(z => !z.IsDelta).MaxBy(z => z.Version).First();
        }

        public static void UpdateReleaseFiles(string outputDir)
        {
            var releases = GetReleasesFromDir(outputDir);
            foreach (var releaseFile in Directory.EnumerateFiles(outputDir, "RELEASES*")) {
                File.Delete(releaseFile);
            }
            foreach (var kvp in releases) {
                var releasesName = Utility.GetReleasesFileName(kvp.Key);
                var path = Path.Combine(outputDir, releasesName);
                ReleaseEntry.WriteReleaseFile(kvp.Value, path);
            }
        }

        public static string GetSuggestedReleaseName(string id, string version, string channel, bool delta)
        {
            var suffix = GetUniqueAssetSuffix(channel);
            return $"{id}-{version}{suffix}{(delta ? "-delta" : "-full")}.nupkg";
        }

        public static string GetSuggestedPortableName(string id, string channel)
        {
            var suffix = GetUniqueAssetSuffix(channel);
            if (VelopackRuntimeInfo.IsLinux) {
                if (channel == GetDefaultChannel(RuntimeOs.Linux)) {
                    return $"{id}.AppImage";
                } else {
                    return $"{id}{suffix}.AppImage";
                }
            } else {
                return $"{id}{suffix}-Portable.zip";
            }
        }

        public static string GetSuggestedSetupName(string id, string channel)
        {
            var suffix = GetUniqueAssetSuffix(channel);
            if (VelopackRuntimeInfo.IsWindows)
                return $"{id}{suffix}-Setup.exe";
            else if (VelopackRuntimeInfo.IsOSX)
                return $"{id}{suffix}-Setup.pkg";
            else
                throw new PlatformNotSupportedException("Platform not supported.");
        }

        private static string GetUniqueAssetSuffix(string channel)
        {
            return "-" + channel;
        }

        public static string GetDefaultChannel(RuntimeOs? os = null)
        {
            os ??= VelopackRuntimeInfo.SystemOs;
            if (os == RuntimeOs.Windows) return "win";
            if (os == RuntimeOs.OSX) return "osx";
            if (os == RuntimeOs.Linux) return "linux";
            throw new NotSupportedException("Unsupported OS: " + os);
        }

        public enum AssetsMode
        {
            AllPackages,
            OnlyLatest,
        }

        //public class AssetUploadInfo
        //{
        //    public List<FileInfo> Files { get; } = new List<FileInfo>();

        //    public List<ReleaseEntry> Releases { get; } = new List<ReleaseEntry>();
        //}

        //public static AssetUploadInfo GetUploadAssets(AssetsMode mode, string channel, string releasesDir)
        //{
        //    var ret = new AssetUploadInfo();
        //    var os = VelopackRuntimeInfo.SystemOs;
        //    channel ??= GetDefaultChannel(os);
        //    var suffix = GetPkgSuffix(os, channel);

        //    if (!_releases.ContainsKey(channel))
        //        throw new UserInfoException("No releases found for channel: " + channel);

        //    ret.ReleasesFileName = Utility.GetReleasesFileName(channel);
        //    var relPath = GetReleasePath(channel);
        //    if (!File.Exists(relPath))
        //        throw new UserInfoException($"Could not find RELEASES file for channel {channel} at {relPath}");

        //    ReleaseEntry latest = GetLatestFullRelease(channel);
        //    if (latest == null) {
        //        throw new UserInfoException("No full releases found for channel: " + channel);
        //    } else {
        //        _logger.Info("Latest local release: " + latest.OriginalFilename);
        //    }

        //    foreach (var rel in Directory.EnumerateFiles(_outputDir, "*.nupkg")) {
        //        var entry = _releases[channel].FirstOrDefault(x => Path.GetFileName(rel).Equals(x.OriginalFilename, StringComparison.OrdinalIgnoreCase));
        //        if (entry != null) {
        //            if (mode != AssetsMode.OnlyLatest || latest.Version == entry.Version) {
        //                _logger.Info($"Discovered asset: {rel}");
        //                ret.Files.Add(new FileInfo(rel));
        //                ret.Releases.Add(entry);
        //            }
        //        }
        //    }

        //    foreach (var rel in Directory.EnumerateFiles(_outputDir, $"*{suffix}-Portable.zip")) {
        //        _logger.Info($"Discovered asset: {rel}");
        //        ret.Files.Add(new FileInfo(rel));
        //    }

        //    foreach (var rel in Directory.EnumerateFiles(_outputDir, $"*{suffix}.AppImage")) {
        //        _logger.Info($"Discovered asset: {rel}");
        //        ret.Files.Add(new FileInfo(rel));
        //    }

        //    foreach (var rel in Directory.EnumerateFiles(_outputDir, $"*{suffix}-Setup.exe")) {
        //        _logger.Info($"Discovered asset: {rel}");
        //        ret.Files.Add(new FileInfo(rel));
        //    }

        //    foreach (var rel in Directory.EnumerateFiles(_outputDir, $"*{suffix}-Setup.pkg")) {
        //        _logger.Info($"Discovered asset: {rel}");
        //        ret.Files.Add(new FileInfo(rel));
        //    }

        //    return ret;
        //}


    }
}
