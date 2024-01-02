using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Velopack.Packaging
{
    public class ReleaseEntryHelper
    {
        private readonly string _outputDir;
        private readonly ILogger _logger;
        private Dictionary<string, List<ReleaseEntry>> _releases;

        public const string DEFAULT_CHANNEL = "default";

        public ReleaseEntryHelper(string outputDir, ILogger logger)
        {
            _outputDir = outputDir;
            _logger = logger;
            _releases = new Dictionary<string, List<ReleaseEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var releaseFile in Directory.EnumerateFiles(outputDir, "RELEASES*")) {
                var fn = Path.GetFileName(releaseFile);
                var channel = fn.StartsWith("RELEASES-", StringComparison.OrdinalIgnoreCase) ? fn.Substring(9) : DEFAULT_CHANNEL;
                var releases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFile)).ToList();
                _logger.Info($"Loaded {releases.Count} entries from: {releaseFile}");
                // this allows us to collapse RELEASES files with the same channel but different case on file systems
                // which are case sensitive.
                if (_releases.ContainsKey(channel)) {
                    _releases[channel].AddRange(releases);
                } else {
                    _releases.Add(channel, releases);
                }
            }
        }

        public void ValidateEntriesForPackaging(SemanticVersion version, string channel)
        {
            if (!_releases.ContainsKey(channel) || !_releases[channel].Any())
                return;

            RID rid = null;
            foreach (var release in _releases[channel]) {
                if (rid == null) rid = release.Rid;
                if (release.Rid != rid) {
                    throw new ArgumentException("All releases in a channel must have the same RID. Please correct RELEASES file or change channel name: " + GetReleasePath(channel));
                }
                if (version <= release.Version) {
                    throw new ArgumentException($"Release {release.OriginalFilename} is equal or newer to the current version {version}. Please increase the current package version or remove that release.");
                }
            }
        }

        public ReleasePackageBuilder GetPreviousFullRelease(SemanticVersion version, string channel)
        {
            var releases = _releases.ContainsKey(channel) ? _releases[channel] : null;
            if (releases == null || !releases.Any()) return null;
            var entry = releases
                .Where(x => x.IsDelta == false)
                .Where(x => x.Version < version)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (entry == null) return null;
            var file = Path.Combine(_outputDir, entry.OriginalFilename);
            return new ReleasePackageBuilder(_logger, file, true);
        }

        public void AddRemoteReleaseEntries(IEnumerable<ReleaseEntry> entries, string channel)
        {
            if (!_releases.ContainsKey(channel))
                _releases.Add(channel, new List<ReleaseEntry>());
            var newEntries = entries.Where(x => !_releases[channel].Any(y => y.Version == x.Version && y.IsDelta == x.IsDelta));
            _releases[channel].AddRange(newEntries);
        }

        public void AddNewRelease(string nupkgPath, string channel)
        {
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

            _releases[channel].Add(newReleaseEntry);
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
                _logger.Info("Wrote RELEASES file: " + path);
            }
        }

        private string GetReleasePath(string channel)
        {
            return Path.Combine(_outputDir, channel == DEFAULT_CHANNEL ? "RELEASES" : $"RELEASES-{channel.ToLower()}");
        }

        public IEnumerable<FileInfo> GetUploadAssets()
        {
            foreach (var rel in Directory.EnumerateFiles(_outputDir, "*.nupkg")) {
                if (_releases.Any(kvp => kvp.Value.Any(x => Path.GetFileName(rel).Equals(x.OriginalFilename, StringComparison.OrdinalIgnoreCase)))) {
                    yield return new FileInfo(rel);
                } else {
                    _logger.Warn($"Asset '{rel}' is not in any RELEASES file, it will be ignored.");
                }
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, "*-Portable.zip")) {
                yield return new FileInfo(rel);
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, "*-Setup.exe")) {
                yield return new FileInfo(rel);
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, "*-Setup.pkg")) {
                yield return new FileInfo(rel);
            }

            foreach (var rel in Directory.EnumerateFiles(_outputDir, "RELEASES*")) {
                yield return new FileInfo(rel);
            }
        }

        public string GetSuggestedPortablePath(string id, RID rid)
        {
            return Path.Combine(_outputDir, $"{id}-[{rid.ToDisplay(RidDisplayType.NoVersion)}]-Portable.zip");
        }

        public string GetSuggestedSetupPath(string id, RID rid)
        {
            if (rid.BaseRID == RuntimeOs.Windows)
                return Path.Combine(_outputDir, $"{id}-[{rid.ToDisplay(RidDisplayType.NoVersion)}]-Setup.exe");
            else if (rid.BaseRID == RuntimeOs.OSX)
                return Path.Combine(_outputDir, $"{id}-[{rid.ToDisplay(RidDisplayType.NoVersion)}]-Setup.pkg");
            else
                throw new NotSupportedException("RID not supported: " + rid);
        }
    }
}
