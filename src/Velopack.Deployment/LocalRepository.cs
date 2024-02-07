using Microsoft.Extensions.Logging;
using Velopack.Packaging;
using Velopack.Sources;

namespace Velopack.Deployment;

public class LocalDownloadOptions : RepositoryOptions
{
    public DirectoryInfo Path { get; set; }
}

public class LocalUploadOptions : LocalDownloadOptions
{
    public DirectoryInfo SecondPath { get; set; }
    public bool SkipUploadPortable { get; set; }
    public bool SkipUploadInstaller { get; set; }
}

public class LocalRepository(ILogger logger) : SourceRepository<LocalDownloadOptions, SimpleFileSource>(logger), IRepositoryCanUpload<LocalUploadOptions>
{
    public override SimpleFileSource CreateSource(LocalDownloadOptions options)
    {
        return new SimpleFileSource(options.Path);
    }

    public async Task UploadMissingAssetsAsync(LocalUploadOptions options)
    {
        var build = BuildAssets.Read(options.ReleaseDir.FullName, options.Channel);
        Log.Info($"Preparing to upload {build.Files.Count} local assets to local path {options.Path}");

        var remoteReleases = await GetReleasesAsync(options);
        Log.Info($"There are {remoteReleases.Assets.Length} assets in remote RELEASES file.");

        var localEntries = build.GetReleaseEntries();
        var releaseEntries = ReleaseEntryHelper.MergeAssets(localEntries, remoteReleases.Assets).ToArray();
        Log.Info($"{releaseEntries.Length} merged local/remote releases.");

        foreach (var asset in build.Files) {
            if (Path.GetExtension(asset).Contains(".zip")) {
                if (options.SkipUploadPortable == true) { continue; }
                if (options.SecondPath != null) {
                    if (!Directory.Exists(options.SecondPath.FullName)) { Directory.CreateDirectory(options.SecondPath.FullName); }
                    File.Copy(asset, Path.Combine(options.SecondPath.FullName, Path.GetFileName(asset)));
                    continue;
                }
            } else if (Path.GetExtension(asset).Contains(".exe")) {
                if (options.SkipUploadInstaller == true) { continue; }
                if (options.SecondPath != null) {
                    if (!Directory.Exists(options.SecondPath.FullName)) { Directory.CreateDirectory(options.SecondPath.FullName); }
                    File.Copy(asset, Path.Combine(options.SecondPath.FullName, Path.GetFileName(asset)));
                    continue;
                }
            }
            File.Copy(asset, Path.Combine(options.Path.FullName, Path.GetFileName(asset)));
        }

        var releasesName = Utility.GetVeloReleaseIndexName(options.Channel);
        var releasesFile = Path.Combine(options.ReleaseDir.FullName, releasesName);
        File.WriteAllText(releasesFile, ReleaseEntryHelper.GetAssetFeedJson(new VelopackAssetFeed { Assets = releaseEntries }));
        File.Copy(releasesFile, Path.Combine(options.Path.FullName, releasesName), true);

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
        var legacyReleasesName = Utility.GetReleasesFileName(options.Channel);
        var legacyReleasesFile = Path.Combine(options.ReleaseDir.FullName, legacyReleasesName);
        ReleaseEntry.WriteReleaseFile(releaseEntries.Select(ReleaseEntry.FromVelopackAsset), legacyReleasesFile);
        File.Copy(legacyReleasesFile, Path.Combine(options.Path.FullName, legacyReleasesName), true);
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0612 // Type or member is obsolete

        Log.Info("Done.");
    }
}
