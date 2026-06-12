using System.Text;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Core.Validation;
using Velopack.NuGet;
using Velopack.Packaging;
using Velopack.Util;

namespace Velopack.Deployment;

public class GitReleaseDownloadOptions : RepositoryOptions
{
    public bool Prerelease { get; set; }

    public string RepoUrl { get; set; }

    public string Token { get; set; }
}

public class GitReleaseUploadOptions : GitReleaseDownloadOptions
{
    public bool Publish { get; set; }

    public string ReleaseName { get; set; }

    public string TagName { get; set; }

    public string TargetCommitish { get; set; }

    public bool Merge { get; set; }
}

public class GitReleaseDownloadOptionsValidator<T> : RepositoryOptionsValidator<T> where T : GitReleaseDownloadOptions
{
    public GitReleaseDownloadOptionsValidator()
    {
        RuleFor(x => x.RepoUrl).NotEmpty().MustBeValidHttpUri()
            .Must(v => {
                if (string.IsNullOrEmpty(v) || !Uri.TryCreate(v, UriKind.Absolute, out var uri)) return true;
                var parts = uri.AbsolutePath.Trim('/').Split('/');
                return parts.Length == 2 && parts.All(p => p.Length > 0);
            })
            .WithMessage("{PropertyName} must be in the format 'https://host/owner/repo' ('{PropertyValue}').");
    }
}

public abstract class GitReleaseUploadOptionsValidator<T> : GitReleaseDownloadOptionsValidator<T> where T : GitReleaseUploadOptions
{
    protected GitReleaseUploadOptionsValidator(string providerName)
    {
        AddReleaseDirRules();
        RuleFor(x => x.Token).NotEmpty().WithMessage($"{{PropertyName}} is required when uploading to {providerName}.");
    }
}

public static class GitUrl
{
    public static (string owner, string repo) GetOwnerAndRepo(string repoUrl)
    {
        var repoUri = new Uri(repoUrl);
        var repoParts = repoUri.AbsolutePath.Trim('/').Split('/');
        if (repoParts.Length != 2)
            throw new Exception($"Invalid repository URL, '{repoUri.AbsolutePath}' should be in the format 'owner/repo'");

        var repoOwner = repoParts[0];
        var repoName = repoParts[1];
        return (repoOwner, repoName);
    }
}

public sealed class GitRelease
{
    public long Id { get; init; }

    public string Name { get; init; }

    public string TagName { get; init; }

    public bool Draft { get; init; }

    public IReadOnlyList<string> AssetNames { get; init; }
}

public interface IGitReleaseClient
{
    string ProviderName { get; }

    Task<IReadOnlyList<GitRelease>> GetReleasesAsync();

    Task<GitRelease> CreateDraftReleaseAsync(string tagName, string name, string body, bool prerelease, string targetCommitish);

    Task UploadAssetAsync(GitRelease release, string assetName, Stream content, string contentType, TimeSpan? timeout = null);

    Task PublishReleaseAsync(GitRelease release);
}

public abstract class GitReleaseUploadCommandRunner<TOpt, TValidator>(ILogger logger) : ValidatedCommand<TOpt, TValidator>
    where TOpt : GitReleaseUploadOptions
    where TValidator : IValidator<TOpt>, new()
{
    protected ILogger Log { get; } = logger;

    protected override async Task RunCoreAsync(TOpt options)
    {
        var client = CreateClient(options);
        var helper = await ReleaseEntryHelper.CreateAsync(options.ReleaseDir.FullName, options.Channel, Log, options.TargetOs).ConfigureAwait(false);
        var build = BuildAssets.Read(options.ReleaseDir.FullName, options.Channel);
        var latest = helper.GetLatestFullRelease();
        var latestPath = Path.Combine(options.ReleaseDir.FullName, latest.FileName);
        var releaseNotes = new ZipPackage(latestPath).ReleaseNotes;
        var semVer = options.TagName ?? latest.Version.ToString();
        var releaseName = string.IsNullOrWhiteSpace(options.ReleaseName) ? semVer : options.ReleaseName;

        Log.Info($"Preparing to upload {build.Count} asset(s) to {client.ProviderName}");

        var existingReleases = await client.GetReleasesAsync();
        if (!options.Merge) {
            if (existingReleases.Any(r => r.TagName == semVer)) {
                throw new UserInfoException(
                    $"There is already an existing release tagged '{semVer}'. Please delete this release or provide a new version number.");
            }

            if (existingReleases.Any(r => r.Name == releaseName)) {
                throw new UserInfoException(
                    $"There is already an existing release named '{releaseName}'. Please delete this release or provide a new release name.");
            }
        }

        // create or retrieve the draft release
        var release = existingReleases.FirstOrDefault(r => r.TagName == semVer)
                      ?? existingReleases.FirstOrDefault(r => r.Name == releaseName);

        if (release != null) {
            if (release.TagName != semVer)
                throw new UserInfoException(
                    $"Found existing release matched by name ({release.Name} [{release.TagName}]), but tag name does not match ({semVer}).");
            Log.Info($"Found existing release ({release.Name} [{release.TagName}]). Merge flag is enabled.");
        } else {
            Log.Info($"Creating draft release titled '{releaseName}'");
            release = await client.CreateDraftReleaseAsync(semVer, releaseName, releaseNotes, options.Prerelease, options.TargetCommitish);
        }

        // check if there is an existing releasesFile to merge
        var releasesFileName = CoreUtil.GetVeloReleaseIndexName(options.Channel);
        if (release.AssetNames.Any(a => a == releasesFileName)) {
            throw new UserInfoException(
                $"There is already a remote asset named '{releasesFileName}', and merging release files on {client.ProviderName} is not supported.");
        }

        // upload all assets (incl packages)
        foreach (var a in build.GetFilePaths()) {
            await Retry.RetryAsync(
                Log,
                async () => {
                    using var stream = File.OpenRead(a);
                    await client.UploadAssetAsync(release, Path.GetFileName(a), stream, "application/octet-stream");
                },
                $"Uploading asset '{Path.GetFileName(a)}'..");
        }

        var feed = new VelopackAssetFeed {
            Assets = (await build.GetReleaseEntriesAsync().ConfigureAwait(false)).ToArray(),
        };
        var json = ReleaseEntryHelper.GetAssetFeedJson(feed);

        await Retry.RetryAsync(
            Log,
            async () => {
                await client.UploadAssetAsync(release, releasesFileName, new MemoryStream(Encoding.UTF8.GetBytes(json)), "application/json");
            },
            "Uploading " + releasesFileName);

        if (options.Channel == DefaultName.GetDefaultChannel(RuntimeOs.Windows)) {
            var legacyReleasesContent = ReleaseEntryHelper.GetLegacyMigrationReleaseFeedString(feed);
            var legacyReleasesBytes = Encoding.UTF8.GetBytes(legacyReleasesContent);
            await Retry.RetryAsync(
                Log,
                async () => {
                    await client.UploadAssetAsync(release, "RELEASES", new MemoryStream(legacyReleasesBytes), "application/octet-stream", TimeSpan.FromMinutes(5));
                },
                "Uploading legacy RELEASES (compatibility)");
        }

        // convert draft to full release
        if (options.Publish) {
            if (release.Draft) {
                Log.Info("Converting draft to full published release.");
                await client.PublishReleaseAsync(release);
            } else {
                Log.Info("Skipping publish, release is already not a draft.");
            }
        }
    }

    protected abstract IGitReleaseClient CreateClient(TOpt options);
}
