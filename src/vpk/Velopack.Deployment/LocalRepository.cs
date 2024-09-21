using Microsoft.Extensions.Logging;
using Velopack.Packaging;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Deployment;

public class LocalDownloadOptions : RepositoryOptions, IObjectDownloadOptions
{
    public DirectoryInfo TargetPath { get; set; }
}

public class LocalUploadOptions : LocalDownloadOptions, IObjectUploadOptions
{
    public bool ForceRegenerate { get; set; }
    public int KeepMaxReleases { get; set; }
}

public class LocalRepository(ILogger logger) : ObjectRepository<LocalDownloadOptions, LocalUploadOptions, DirectoryInfo>(logger)
{
    protected override DirectoryInfo CreateClient(LocalDownloadOptions options)
    {
        return options.TargetPath;
    }

    protected override Task DeleteObject(DirectoryInfo client, string key)
    {
        var target = Path.Combine(client.FullName, key);
        Log.Info("Deleting: " + target);
        IoUtil.DeleteFileOrDirectoryHard(target);
        return Task.CompletedTask;
    }

    protected override Task<byte[]> GetObjectBytes(DirectoryInfo client, string key)
    {
        var target = Path.Combine(client.FullName, key);
        Log.Info("Reading: " + target);
        return File.ReadAllBytesAsync(target);
    }

    protected override Task UploadObject(DirectoryInfo client, string key, FileInfo f, bool overwriteRemote, bool noCache)
    {
        var target = Path.Combine(client.FullName, key);
        if (File.Exists(target) && !overwriteRemote) {
            Log.Info($"Skipping upload of {key} as it already exists in the repository and overwrite=false.");
            return Task.CompletedTask;
        }

        Log.Info($"Uploading '{f.FullName}' to '{target}'.");
        File.Copy(f.FullName, target, overwriteRemote);
        return Task.CompletedTask;
    }

    public override Task UploadMissingAssetsAsync(LocalUploadOptions options)
    {
        // create directory if it doesn't exist
        Directory.CreateDirectory(options.TargetPath.FullName);

        if (options.ForceRegenerate) {
            Log.Info("Force regenerating release index files...");
            ReleaseEntryHelper.UpdateReleaseFiles(options.TargetPath.FullName, Log);
        }

        return base.UploadMissingAssetsAsync(options);
    }

    protected override Task SaveEntryToFileAsync(LocalDownloadOptions options, VelopackAsset entry, string filePath)
    {
        var source = new SimpleFileSource(options.TargetPath);
        return source.DownloadReleaseEntry(Log, entry, filePath, (i) => { }, default);
    }

    protected override Task<VelopackAssetFeed> GetReleasesAsync(LocalDownloadOptions options)
    {
        var source = new SimpleFileSource(options.TargetPath);
        return source.GetReleaseFeed(channel: options.Channel, logger: Log);
    }
}
