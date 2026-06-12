using FluentValidation;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Validation;
using Velopack.Packaging;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Deployment;

public class LocalDownloadOptions : RepositoryOptions
{
    public DirectoryInfo TargetPath { get; set; }
}

public class LocalUploadOptions : LocalDownloadOptions, IObjectUploadOptions
{
    public bool ForceRegenerate { get; set; }

    public int KeepMaxReleases { get; set; }
}

public class LocalDownloadOptionsValidator<T> : RepositoryOptionsValidator<T> where T : LocalDownloadOptions
{
    public LocalDownloadOptionsValidator()
    {
        RuleFor(x => x.TargetPath).NotNull();
    }
}

public sealed class LocalDownloadOptionsValidator : LocalDownloadOptionsValidator<LocalDownloadOptions>
{
    public LocalDownloadOptionsValidator()
    {
        RuleFor(x => x.TargetPath).MustBeNonEmptyDirectory();
    }
}

public sealed class LocalUploadOptionsValidator : LocalDownloadOptionsValidator<LocalUploadOptions>
{
    public LocalUploadOptionsValidator()
    {
        AddReleaseDirRules();
    }
}

public class LocalObjectStoreClient(DirectoryInfo targetPath, ILogger logger) : IObjectStoreClient
{
    public Task UploadObject(string key, FileInfo f, bool overwriteRemote, bool noCache)
    {
        var target = Path.Combine(targetPath.FullName, key);
        if (File.Exists(target) && !overwriteRemote) {
            logger.Info($"Skipping upload of {key} as it already exists in the repository and overwrite=false.");
            return Task.CompletedTask;
        }

        logger.Info($"Uploading '{f.FullName}' to '{target}'.");
        File.Copy(f.FullName, target, overwriteRemote);
        return Task.CompletedTask;
    }

    public Task DeleteObject(string key)
    {
        var target = Path.Combine(targetPath.FullName, key);
        logger.Info("Deleting: " + target);
        IoUtil.DeleteFileOrDirectoryHard(target);
        return Task.CompletedTask;
    }

    public Task<byte[]> GetObjectBytes(string key)
    {
        var target = Path.Combine(targetPath.FullName, key);
        if (!File.Exists(target)) {
            return Task.FromResult<byte[]>(null);
        }

        logger.Info("Reading: " + target);
        return File.ReadAllBytesAsync(target);
    }

    public Task DownloadToFile(string key, string filePath)
    {
        var target = Path.Combine(targetPath.FullName, key);
        logger.Info($"Copying '{target}' to '{filePath}'.");
        File.Copy(target, filePath, true);
        return Task.CompletedTask;
    }
}

public class LocalDownloadCommandRunner(ILogger logger)
    : SourceDownloadCommandRunner<LocalDownloadOptions, LocalDownloadOptionsValidator>(logger)
{
    protected override IUpdateSource CreateSource(LocalDownloadOptions options)
    {
        return new SimpleFileSource(options.TargetPath);
    }
}

public class LocalUploadCommandRunner(ILogger logger)
    : ObjectUploadCommandRunner<LocalUploadOptions, LocalUploadOptionsValidator>(logger)
{
    protected override IObjectStoreClient CreateClient(LocalUploadOptions options)
    {
        return new LocalObjectStoreClient(options.TargetPath, Log);
    }

    protected override async Task RunCoreAsync(LocalUploadOptions options)
    {
        // create directory if it doesn't exist
        Directory.CreateDirectory(options.TargetPath.FullName);

        if (options.ForceRegenerate) {
            Log.Info("Force regenerating release index files...");
            await ReleaseEntryHelper.UpdateReleaseFilesAsync(options.TargetPath.FullName, Log).ConfigureAwait(false);
        }

        await base.RunCoreAsync(options).ConfigureAwait(false);
    }

    protected override Task<VelopackAssetFeed> GetReleasesAsync(LocalUploadOptions options)
    {
        // read the feed via SimpleFileSource, which tolerates a missing releases file
        // and falls back to scanning *.nupkg in the target directory.
        var source = new SimpleFileSource(options.TargetPath);
        return source.GetReleaseFeed(Log.ToVelopackLogger(), null, options.Channel);
    }
}
