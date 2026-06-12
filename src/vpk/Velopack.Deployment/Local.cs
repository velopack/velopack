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
        RuleFor(x => x.TargetPath)
            .Must(v => v == null || !File.Exists(v.FullName))
            .WithMessage("{PropertyName} must be a directory, but a file already exists at this location ('{PropertyValue}').");
    }
}

public class LocalObjectStoreClient(DirectoryInfo targetPath, ILogger logger)
    : ObjectStoreClient(logger)
{
    protected override Task<RemoteObjectInfo> GetRemoteObjectInfoAsync(string key)
    {
        var target = Path.Combine(targetPath.FullName, key);
        if (!File.Exists(target)) {
            return Task.FromResult<RemoteObjectInfo>(null);
        }

        var md5 = Convert.ToHexString(ObjectStoreUtil.GetFileMD5Checksum(target)).ToLowerInvariant();
        return Task.FromResult(new RemoteObjectInfo { Md5Hex = md5 });
    }

    protected override Task UploadObjectCoreAsync(string key, FileInfo file, bool overwriteRemote, bool noCache)
    {
        var target = Path.Combine(targetPath.FullName, key);
        File.Copy(file.FullName, target, overwriteRemote);
        return Task.CompletedTask;
    }

    protected override Task<byte[]> GetObjectBytesCoreAsync(string key)
    {
        var target = Path.Combine(targetPath.FullName, key);
        return File.ReadAllBytesAsync(target);
    }

    protected override Task DownloadToFileCoreAsync(string key, string filePath)
    {
        var target = Path.Combine(targetPath.FullName, key);
        File.Copy(target, filePath, true);
        return Task.CompletedTask;
    }

    protected override Task DeleteObjectCoreAsync(string key)
    {
        var target = Path.Combine(targetPath.FullName, key);
        IoUtil.DeleteFileOrDirectoryHard(target);
        return Task.CompletedTask;
    }

    protected override bool IsNotFoundException(Exception ex)
    {
        return ex is FileNotFoundException or DirectoryNotFoundException;
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
        // only attempt to create the directory when it does not already exist - CreateDirectory
        // can throw 'already exists' for targets such as junctions or network share roots.
        if (!Directory.Exists(options.TargetPath.FullName)) {
            Directory.CreateDirectory(options.TargetPath.FullName);
        }

        if (options.ForceRegenerate) {
            Log.Info("Force regenerating release index files...");
            await ReleaseEntryHelper.UpdateReleaseFilesAsync(options.TargetPath.FullName, Log).ConfigureAwait(false);
        }

        await base.RunCoreAsync(options).ConfigureAwait(false);
    }

    protected override Task<VelopackAssetFeed> GetReleasesAsync(LocalUploadOptions options, IObjectStoreClient client)
    {
        // read the feed via SimpleFileSource, which tolerates a missing releases file
        // and falls back to scanning *.nupkg in the target directory.
        var source = new SimpleFileSource(options.TargetPath);
        return source.GetReleaseFeed(Log.ToVelopackLogger(), null, options.Channel);
    }
}
