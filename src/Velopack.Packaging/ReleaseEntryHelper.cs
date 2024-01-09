using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Sources;

namespace Velopack.Packaging
{
    public class ReleaseEntryHelper
    {
        private readonly string _outputDir;
        private readonly ILogger _logger;
        private Dictionary<string, List<ReleaseEntry>> _releases;
        private List<string> _new;

        private const string BLANK_CHANNEL = "default";

        public ReleaseEntryHelper(string outputDir, ILogger logger)
        {
            _outputDir = outputDir;
            _logger = logger;
            _releases = new Dictionary<string, List<ReleaseEntry>>(StringComparer.OrdinalIgnoreCase);
            _new = new List<string>();
            foreach (var releaseFile in Directory.EnumerateFiles(outputDir, "RELEASES*")) {
                var fn = Path.GetFileName(releaseFile);
                var channel = fn.StartsWith("RELEASES-", StringComparison.OrdinalIgnoreCase) ? fn.Substring(9) : BLANK_CHANNEL;
                var releases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFile)).ToList();
                _logger.Debug($"Loaded {releases.Count} entries from: {releaseFile}");
                // this allows us to collapse RELEASES files with the same channel but different case on file systems
                // which are case sensitive.
                if (_releases.ContainsKey(channel)) {
                    _releases[channel].AddRange(releases);
                } else {
                    _releases.Add(channel, releases);
                }
            }
        }

        public void ValidateChannelForPackaging(SemanticVersion version, string channel, RID rid)
        {
            channel ??= GetDefaultChannel(VelopackRuntimeInfo.SystemOs);
            if (!_releases.ContainsKey(channel) || !_releases[channel].Any())
                return;

            foreach (var release in _releases[channel]) {
                if (release.Rid != rid) {
                    throw new ArgumentException("All releases in a channel must have the same RID. Please correct RELEASES file or change channel name: " + GetReleasePath(channel));
                }
                if (version <= release.Version) {
                    throw new ArgumentException($"Release {release.OriginalFilename} is equal or newer to the current version {version}. Please increase the current package version or remove that release.");
                }
            }
        }

        public ReleasePackage GetPreviousFullRelease(SemanticVersion version, string channel)
        {
            channel ??= GetDefaultChannel(VelopackRuntimeInfo.SystemOs);
            var releases = _releases.ContainsKey(channel) ? _releases[channel] : null;
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

        public void AddRemoteReleaseEntries(IEnumerable<ReleaseEntry> entries, string channel)
        {
            channel ??= GetDefaultChannel(VelopackRuntimeInfo.SystemOs);
            if (!_releases.ContainsKey(channel))
                _releases.Add(channel, new List<ReleaseEntry>());
            var newEntries = entries.Where(x => !_releases[channel].Any(y => y.Version == x.Version && y.IsDelta == x.IsDelta));
            _releases[channel].AddRange(newEntries);
        }

        public void AddNewRelease(string nupkgPath, string channel)
        {
            channel ??= GetDefaultChannel(VelopackRuntimeInfo.SystemOs);
            if (!File.Exists(nupkgPath))
                throw new FileNotFoundException("Could not find nupkg file", nupkgPath);

            if (!Utility.IsFileInDirectory(nupkgPath, _outputDir))
                throw new ArgumentException($"Cannot add a nupkg ({nupkgPath}) which is outside the output directory ({_outputDir}).", nameof(nupkgPath));

            if (!_releases.ContainsKey(channel))
                _releases.Add(channel, new List<ReleaseEntry>());

            var newReleaseEntry = ReleaseEntry.GenerateFromFile(nupkgPath);
            var collision = _releases[channel].FirstOrDefault(x => x.Version == newReleaseEntry.Version && x.IsDelta == newReleaseEntry.IsDelta);
            if (collision != null) {
                _releases[channel].Remove(collision);
            }

            _new.Add(nupkgPath);
            _releases[channel].Add(newReleaseEntry);
        }

        public void RollbackNewReleases()
        {
            foreach (var n in _new) {
                Utility.Retry(() => File.Delete(n));
            }
        }

        public void SaveReleasesFiles()
        {
            foreach (var releaseFile in Directory.EnumerateFiles(_outputDir, "RELEASES*")) {
                File.Delete(releaseFile);
            }

            foreach (var ch in _releases) {
                var path = GetReleasePath(ch.Key);
                using var fs = File.Create(path);
                ReleaseEntry.WriteReleaseFile(ch.Value, fs);
                _logger.Debug("Wrote RELEASES file: " + path);
            }
        }

        public static string GetPkgSuffix(RuntimeOs os, string channel)
        {
            if (channel == null) return "";
            if (channel == BLANK_CHANNEL) return "";
            if (channel == "osx" && os == RuntimeOs.OSX) return "";
            if (channel == "linux" && os == RuntimeOs.Linux) return "";
            return "-" + channel.ToLower();
        }

        public static string GetDefaultChannel(RuntimeOs os)
        {
            if (os == RuntimeOs.Windows) return BLANK_CHANNEL;
            if (os == RuntimeOs.OSX) return "osx";
            if (os == RuntimeOs.Linux) return "linux";
            throw new NotSupportedException("Unsupported OS: " + os);
        }

        public string GetReleasePath(string channel)
        {
            return Path.Combine(_outputDir, SourceBase.GetReleasesFileNameImpl(channel));
        }

        public enum AssetsMode
        {
            AllPackages,
            OnlyLatest,
        }

        public class AssetUploadInfo
        {
            public List<FileInfo> Files { get; } = new List<FileInfo>();

            public List<ReleaseEntry> Releases { get; } = new List<ReleaseEntry>();

            public string ReleasesFileName { get; set; }
        }

        public AssetUploadInfo GetUploadAssets(string channel, AssetsMode mode)
        {
            var ret = new AssetUploadInfo();
            var os = VelopackRuntimeInfo.SystemOs;
            channel ??= GetDefaultChannel(os);
            var suffix = GetPkgSuffix(os, channel);

            if (!_releases.ContainsKey(channel))
                throw new ArgumentException("No releases found for channel: " + channel);

            ret.ReleasesFileName = SourceBase.GetReleasesFileNameImpl(channel);
            var relPath = GetReleasePath(channel);
            if (!File.Exists(relPath))
                throw new FileNotFoundException("Could not find RELEASES file for channel: " + channel, relPath);

            ReleaseEntry latest = GetLatestFullRelease(channel);
            if (latest == null) {
                throw new ArgumentException("No full releases found for channel: " + channel);
            } else {
                _logger.Info("Latest local release: " + latest.OriginalFilename);
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, "*.nupkg")) {
                var entry = _releases[channel].FirstOrDefault(x => Path.GetFileName(rel).Equals(x.OriginalFilename, StringComparison.OrdinalIgnoreCase));
                if (entry != null) {
                    if (mode != AssetsMode.OnlyLatest || latest.Version == entry.Version) {
                        _logger.Info($"Discovered asset: {rel}");
                        ret.Files.Add(new FileInfo(rel));
                        ret.Releases.Add(entry);
                    }
                }
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, $"*{suffix}-Portable.zip")) {
                _logger.Info($"Discovered asset: {rel}");
                ret.Files.Add(new FileInfo(rel));
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, $"*{suffix}.AppImage")) {
                _logger.Info($"Discovered asset: {rel}");
                ret.Files.Add(new FileInfo(rel));
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, $"*{suffix}-Setup.exe")) {
                _logger.Info($"Discovered asset: {rel}");
                ret.Files.Add(new FileInfo(rel));
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, $"*{suffix}-Setup.pkg")) {
                _logger.Info($"Discovered asset: {rel}");
                ret.Files.Add(new FileInfo(rel));
            }

            return ret;
        }

        public string GetSuggestedPortablePath(string id, string channel, RID rid)
        {
            var suffix = GetPkgSuffix(rid.BaseRID, channel);
            if (VelopackRuntimeInfo.IsLinux) {
                return Path.Combine(_outputDir, $"{id}-{rid.ToDisplay(RidDisplayType.NoVersion)}{suffix}.AppImage");
            } else {
                return Path.Combine(_outputDir, $"{id}-{rid.ToDisplay(RidDisplayType.NoVersion)}{suffix}-Portable.zip");
            }
        }

        public string GetSuggestedSetupPath(string id, string channel, RID rid)
        {
            var suffix = GetPkgSuffix(rid.BaseRID, channel);
            if (rid.BaseRID == RuntimeOs.Windows)
                return Path.Combine(_outputDir, $"{id}-{rid.ToDisplay(RidDisplayType.NoVersion)}{suffix}-Setup.exe");
            else if (rid.BaseRID == RuntimeOs.OSX)
                return Path.Combine(_outputDir, $"{id}-{rid.ToDisplay(RidDisplayType.NoVersion)}{suffix}-Setup.pkg");
            else
                throw new NotSupportedException("RID not supported: " + rid);
        }
    }
}
