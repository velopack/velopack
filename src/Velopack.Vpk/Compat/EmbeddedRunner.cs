using System.Runtime.Versioning;
using Squirrel.Csq.Commands;
using Squirrel.Deployment;
using Squirrel.Packaging.Commands;
using Squirrel.Packaging.OSX.Commands;
using Squirrel.Packaging.Windows.Commands;

namespace Squirrel.Csq.Compat;

public class EmbeddedRunner : ICommandRunner
{
    private readonly ILogger _logger;

    public EmbeddedRunner(ILogger logger)
    {
        _logger = logger;
    }

    [SupportedOSPlatform("osx")]
    public virtual Task ExecuteBundleOsx(OsxBundleCommand command)
    {
        var options = new OsxBundleOptions {
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
        new OsxBundleCommandRunner(_logger).Bundle(options);
        return Task.CompletedTask;
    }

    [SupportedOSPlatform("osx")]
    public virtual Task ExecuteReleasifyOsx(OsxReleasifyCommand command)
    {
        var options = new OsxReleasifyOptions {
            TargetRuntime = command.GetRid(),
            ReleaseDir = command.GetReleaseDirectory(),
            BundleDirectory = command.BundleDirectory,
            IncludePdb = command.IncludePdb,
            DeltaMode = command.Delta,
            NoPackage = command.NoPackage,
            NotaryProfile = command.NotaryProfile,
            PackageConclusion = command.PackageConclusion,
            PackageLicense = command.PackageLicense,
            PackageReadme = command.PackageReadme,
            PackageWelcome = command.PackageWelcome,
            ReleaseNotes = command.ReleaseNotes,
            SigningAppIdentity = command.SigningAppIdentity,
            SigningEntitlements = command.SigningEntitlements,
            SigningInstallIdentity = command.SigningInstallIdentity,
        };
        new OsxReleasifyCommandRunner(_logger).Releasify(options);
        return Task.CompletedTask;
    }

    public virtual Task ExecutePackWindows(WindowsPackCommand command)
    {
        var options = new WindowsPackOptions {
            TargetRuntime = command.GetRid(),
            ReleaseDir = command.GetReleaseDirectory(),
            Package = command.Package,
            Icon = command.Icon,
            DeltaMode = command.Delta,
            IncludePdb = command.IncludePdb,
            SignParameters = command.SignParameters,
            EntryExecutableName = command.EntryExecutableName,
            PackAuthors = command.PackAuthors,
            PackDirectory = command.PackDirectory,
            PackId = command.PackId,
            PackTitle = command.PackTitle,
            PackVersion = command.PackVersion,
            ReleaseNotes = command.ReleaseNotes,
            Runtimes = command.Runtimes,
            SignParallel = command.SignParallel,
            SignSkipDll = command.SignSkipDll,
            SignTemplate = command.SignTemplate,
            SplashImage = command.SplashImage,
        };
        new WindowsPackCommandRunner(_logger).Pack(options);
        return Task.CompletedTask;
    }

    public virtual Task ExecuteReleasifyWindows(WindowsReleasifyCommand command)
    {
        var options = new WindowsReleasifyOptions {
            TargetRuntime = command.GetRid(),
            ReleaseDir = command.GetReleaseDirectory(),
            Package = command.Package,
            Icon = command.Icon,
            DeltaMode = command.Delta,
            SignParameters = command.SignParameters,
            EntryExecutableName = command.EntryExecutableName,
            Runtimes = command.Runtimes,
            SignParallel = command.SignParallel,
            SignSkipDll = command.SignSkipDll,
            SignTemplate = command.SignTemplate,
            SplashImage = command.SplashImage,
        };
        new WindowsReleasifyCommandRunner(_logger).Releasify(options);
        return Task.CompletedTask;
    }

    public virtual Task ExecuteGithubDownload(GitHubDownloadCommand command)
    {
        var options = new GitHubDownloadOptions {
            Pre = command.Pre,
            ReleaseDir = command.GetReleaseDirectory(),
            RepoUrl = command.RepoUrl,
            Token = command.Token,
        };
        return new GitHubRepository(_logger).DownloadRecentPackages(options);
    }

    public virtual Task ExecuteGithubUpload(GitHubUploadCommand command)
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

    public virtual Task ExecuteHttpDownload(HttpDownloadCommand command)
    {
        var options = new HttpDownloadOptions {
            ReleaseDir = command.GetReleaseDirectory(),
            Url = command.Url,
        };
        return new SimpleWebRepository(_logger).DownloadRecentPackages(options);
    }

    public virtual Task ExecuteS3Download(S3DownloadCommand command)
    {
        var options = new S3Options {
            Bucket = command.Bucket,
            Endpoint = command.Endpoint,
            KeyId = command.KeyId,
            PathPrefix = command.PathPrefix,
            Region = command.Region,
            ReleaseDir = command.GetReleaseDirectory(),
            Session = command.Session,
            Secret = command.Secret,
        };
        return new S3Repository(_logger).DownloadRecentPackages(options);
    }

    public virtual Task ExecuteS3Upload(S3UploadCommand command)
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

    public virtual Task ExecuteDeltaGen(DeltaGenCommand command)
    {
        var options = new DeltaGenOptions {
            BasePackage = command.BasePackage,
            NewPackage = command.NewPackage,
            OutputFile = command.OutputFile,
            DeltaMode = command.Delta,
        };
        return new DeltaGenCommandRunner().Run(options, _logger);
    }

    public virtual Task ExecuteDeltaPatch(DeltaPatchCommand command)
    {
        var options = new DeltaPatchOptions {
            BasePackage = command.BasePackage,
            PatchFiles = command.PatchFiles,
            OutputFile = command.OutputFile,
        };
        return new DeltaPatchCommandRunner().Run(options, _logger);
    }
}
