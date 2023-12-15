using System.Runtime.Versioning;
using Squirrel.Csq.Commands;
using Squirrel.Deployment;
using Squirrel.Packaging.OSX;

namespace Squirrel.Csq.Compat;

public class EmbeddedRunner : ICommandRunner
{
    private readonly ILogger _logger;

    public EmbeddedRunner(ILogger logger)
    {
        _logger = logger;
    }

    [SupportedOSPlatform("osx")]
    public Task ExecuteBundleOsx(BundleOsxCommand command)
    {
        var options = new BundleOsxOptions {
            BundleId = command.BundleId,
            PackAuthors = command.PackAuthors,
            EntryExecutableName = command.EntryExecutableName,
            Icon = command.Icon,
            PackDirectory = command.PackDirectory,
            PackId = command.PackId,
            PackTitle = command.PackTitle,
            PackVersion = command.PackVersion,
            ReleaseDir = command.GetReleaseDirectory(),
        };
        new OsxCommands(_logger).Bundle(options);
        return Task.CompletedTask;
    }

    public Task ExecuteGithubDownload(GitHubDownloadCommand command)
    {
        var options = new GitHubDownloadOptions {
            Pre = command.Pre,
            ReleaseDir = command.GetReleaseDirectory(),
            RepoUrl = command.RepoUrl,
            Token = command.Token,
        };
        return new GitHubRepository(_logger).DownloadRecentPackages(options);
    }

    public Task ExecuteGithubUpload(GitHubUploadCommand command)
    {
        var options = new GitHubUploadOptions {
            ReleaseDir = command.GetReleaseDirectory(),
            RepoUrl = command.RepoUrl,
            Token = command.Token,
            Publish = command.Publish,
            ReleaseName = command.ReleaseName,
        };
        return new GitHubRepository(_logger).UploadMissingPackages(options);
    }

    public Task ExecuteHttpDownload(HttpDownloadCommand command)
    {
        var options = new HttpDownloadOptions {
            ReleaseDir = command.GetReleaseDirectory(),
            Url = command.Url,
        };
        return new SimpleWebRepository(_logger).DownloadRecentPackages(options);
    }

    public Task ExecutePackWindows(PackWindowsCommand command)
    {
        throw new NotImplementedException();
    }

    [SupportedOSPlatform("osx")]
    public Task ExecuteReleasifyOsx(ReleasifyOsxCommand command)
    {
        throw new NotImplementedException();
    }

    public Task ExecuteReleasifyWindows(ReleasifyWindowsCommand command)
    {
        throw new NotImplementedException();
    }

    public Task ExecuteS3Download(S3DownloadCommand command)
    {
        var options = new S3Options {
            Bucket = command.Bucket,
            Endpoint = command.Endpoint,
            KeyId = command.KeyId,
            PathPrefix = command.PathPrefix,
            Region = command.Region,
            ReleaseDir = command.GetReleaseDirectory(),
            Secret = command.Secret,
        };
        return new S3Repository(_logger).DownloadRecentPackages(options);
    }

    public Task ExecuteS3Upload(S3UploadCommand command)
    {
        var options = new S3UploadOptions {
            Bucket = command.Bucket,
            Endpoint = command.Endpoint,
            KeyId = command.KeyId,
            PathPrefix = command.PathPrefix,
            Region = command.Region,
            ReleaseDir = command.GetReleaseDirectory(),
            Secret = command.Secret,
            KeepMaxReleases = command.KeepMaxReleases,
            Overwrite = command.Overwrite,
        };
        return new S3Repository(_logger).UploadMissingPackages(options);
    }
}
