﻿using System.Text;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.NuGet;
using Velopack.Util;

namespace Velopack.Packaging;

public class ReleaseEntryHelper
{
    private readonly string _outputDir;
    private readonly ILogger _logger;
    private readonly string _channel;
    private Dictionary<string, List<VelopackAsset>> _releases;

    public ReleaseEntryHelper(string outputDir, string channel, ILogger logger, RuntimeOs os)
    {
        _outputDir = outputDir;
        _logger = logger;
        _channel = channel ?? GetDefaultChannel(os);
        _releases = GetReleasesFromDir(outputDir);
    }

    private static Dictionary<string, List<VelopackAsset>> GetReleasesFromDir(string dir)
    {
        var rel = new Dictionary<string, List<VelopackAsset>>(StringComparer.OrdinalIgnoreCase);
        foreach (var releaseFile in Directory.EnumerateFiles(dir, "*.nupkg")) {
            var zip = new ZipPackage(releaseFile);
            var ch = zip.Channel ?? GetDefaultChannel(VelopackRuntimeInfo.SystemOs);
            if (!rel.ContainsKey(ch))
                rel[ch] = new List<VelopackAsset>();
            rel[ch].Add(VelopackAsset.FromZipPackage(zip));
        }
        return rel;
    }

    public bool DoesSimilarVersionExist(SemanticVersion version)
    {
        if (!_releases.ContainsKey(_channel) || !_releases[_channel].Any())
            return false;
        foreach (var release in _releases[_channel]) {
            if (version <= release.Version) {
                return true;
            }
        }
        return false;
    }

    public ReleasePackage GetPreviousFullRelease(SemanticVersion version)
    {
        var releases = _releases.ContainsKey(_channel) ? _releases[_channel] : null;
        if (releases == null || !releases.Any()) return null;
        var entry = releases
            .Where(x => x.Type == VelopackAssetType.Full)
            .Where(x => x.Version < version)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();
        if (entry == null) return null;
        var file = Path.Combine(_outputDir, entry.FileName);
        return new ReleasePackage(file);
    }

    public VelopackAsset GetLatestFullRelease()
    {
        var releases = _releases.ContainsKey(_channel) ? _releases[_channel] : null;
        if (releases == null || !releases.Any()) return null;
        return releases.Where(z => z.Type == VelopackAssetType.Full).MaxByPolyfill(z => z.Version).First();
    }

    public IEnumerable<VelopackAsset> GetLatestAssets()
    {
        if (!_releases.ContainsKey(_channel) || !_releases[_channel].Any())
            return Enumerable.Empty<VelopackAsset>();

        var latest = _releases[_channel].MaxByPolyfill(x => x.Version).First();
        _logger.Info($"Latest release: {latest.FileName}");

        var assets = _releases[_channel]
            .Where(x => x.Version == latest.Version)
            .OrderByDescending(x => x.Version)
            .ThenBy(x => x.Type)
            .ToArray();

        foreach (var asset in assets) {
            _logger.Info($"    Discovered asset: {asset.FileName}");
        }

        return assets;
    }

    public static void UpdateReleaseFiles(string outputDir, ILogger log)
    {
        var releases = GetReleasesFromDir(outputDir);
        foreach (var releaseFile in Directory.EnumerateFiles(outputDir, "RELEASES*")) {
            File.Delete(releaseFile);
        }
        foreach (var kvp in releases) {
            var exclude = kvp.Value.Where(x => x.Version.ReleaseLabels.Any(r => r.Contains('.')) || x.Version.HasMetadata).ToArray();
            if (exclude.Any()) {
                log.Warn($"Excluding {exclude.Length} asset(s) from legacy RELEASES file, because they " +
                    $"contain an invalid character in the version: {string.Join(", ", exclude.Select(x => x.FileName))}");
            }

            // We write a legacy RELEASES file to allow older applications to update to velopack
#pragma warning disable CS0618 // Type or member is obsolete
            var name = CoreUtil.GetReleasesFileName(kvp.Key);
            var path = Path.Combine(outputDir, name);

            ReleaseEntry.WriteReleaseFile(
                kvp.Value
                    .Except(exclude)
                    .Select(ReleaseEntry.FromVelopackAsset)
                    .Where(entry => !entry.IsDelta),
                path);
#pragma warning restore CS0618 // Type or member is obsolete

            var indexPath = Path.Combine(outputDir, CoreUtil.GetVeloReleaseIndexName(kvp.Key));
            var feed = new VelopackAssetFeed() {
                Assets = kvp.Value.OrderByDescending(v => v.Version).ThenBy(v => v.Type).ToArray(),
            };
            File.WriteAllText(indexPath, GetAssetFeedJson(feed));
        }
    }

    public static IEnumerable<VelopackAsset> MergeAssets(IEnumerable<VelopackAsset> priority, IEnumerable<VelopackAsset> secondary)
    {
#if NET6_0_OR_GREATER
        return priority.Concat(secondary).DistinctBy(x => x.FileName);
#else
        return priority.Concat(secondary).GroupBy(x => x.FileName).Select(g => g.First());
#endif
    }

    public static string GetAssetFeedJson(VelopackAssetFeed feed)
    {
        return SimpleJson.SerializeObject(feed);
    }

    public static string GetLegacyMigrationReleaseFeedString(VelopackAssetFeed feed)
    {
        var newestRelease = feed.Assets
            .OrderByDescending(f => f.Version)
            .Where(f => f.Type == VelopackAssetType.Full)
            .FirstOrDefault();

        var ms = new MemoryStream();
#pragma warning disable CS0618 // Type or member is obsolete
        ReleaseEntry.WriteReleaseFile([ReleaseEntry.FromVelopackAsset(newestRelease)], ms);
#pragma warning restore CS0618 // Type or member is obsolete

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static string GetSuggestedReleaseName(string id, string version, string channel, bool delta, RuntimeOs os)
    {
        var suffix = GetUniqueAssetSuffix(channel);
        version = SemanticVersion.Parse(version).ToNormalizedString();
        if (os == RuntimeOs.Windows && channel == GetDefaultChannel(RuntimeOs.Windows)) {
            return $"{id}-{version}{(delta ? "-delta" : "-full")}.nupkg";
        }
        return $"{id}-{version}{suffix}{(delta ? "-delta" : "-full")}.nupkg";
    }

    public static string GetSuggestedPortableName(string id, string channel, RuntimeOs os)
    {
        var suffix = GetUniqueAssetSuffix(channel);
        if (os == RuntimeOs.Linux) {
            if (channel == GetDefaultChannel(RuntimeOs.Linux)) {
                return $"{id}.AppImage";
            } else {
                return $"{id}{suffix}.AppImage";
            }
        } else {
            return $"{id}{suffix}-Portable.zip";
        }
    }

    public static string GetSuggestedSetupName(string id, string channel, RuntimeOs os)
    {
        var suffix = GetUniqueAssetSuffix(channel);
        if (os == RuntimeOs.Windows)
            return $"{id}{suffix}-Setup.exe";
        else if (os == RuntimeOs.OSX)
            return $"{id}{suffix}-Setup.pkg";
        else
            throw new PlatformNotSupportedException("Platform not supported.");
    }

    private static string GetUniqueAssetSuffix(string channel)
    {
        return "-" + channel;
    }

    public static string GetDefaultChannel(RuntimeOs os)
    {
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
