using Microsoft.Extensions.Logging;
using Octokit;
using Velopack.NuGet;
using Velopack.Packaging;
using Velopack.Sources;

namespace Velopack.Deployment;

public class GitHubDownloadOptions : RepositoryOptions
{
    public bool Prerelease { get; set; }

    public string RepoUrl { get; set; }

    public string Token { get; set; }
}

public class GitHubUploadOptions : GitHubDownloadOptions
{
    public bool Publish { get; set; }

    public string ReleaseName { get; set; }
}

public class GitHubRepository : SourceRepository<GitHubDownloadOptions, GithubSource>, IRepositoryCanUpload<GitHubUploadOptions>
{
    public GitHubRepository(ILogger logger) : base(logger)
    {
    }

    public override GithubSource CreateSource(GitHubDownloadOptions options)
    {
        return new GithubSource(options.RepoUrl, options.Token, options.Prerelease, options.Channel, logger: Log);
    }

    public static (string owner, string repo) GetOwnerAndRepo(string repoUrl)
    {
        var repoUri = new Uri(repoUrl);
        var repoParts = repoUri.AbsolutePath.Trim('/').Split('/');
        if (repoParts.Length != 2)
            throw new Exception($"Invalid GitHub URL, '{repoUri.AbsolutePath}' should be in the format 'owner/repo'");

        var repoOwner = repoParts[0];
        var repoName = repoParts[1];
        return (repoOwner, repoName);
    }

    public async Task UploadMissingAssetsAsync(GitHubUploadOptions options)
    {
        var (repoOwner, repoName) = GetOwnerAndRepo(options.RepoUrl);

        var helper = new ReleaseEntryHelper(options.ReleaseDir.FullName, Log);
        var assets = helper.GetUploadAssets(options.Channel, ReleaseEntryHelper.AssetsMode.OnlyLatest);
        var latest = helper.GetLatestFullRelease(options.Channel);
        var latestPath = Path.Combine(options.ReleaseDir.FullName, latest.OriginalFilename);
        var releaseNotes = new ZipPackage(latestPath).ReleaseNotes;
        var semVer = latest.Version;

        Log.Info($"Preparing to upload {assets.Files.Count} assets to GitHub");

        var client = new GitHubClient(new ProductHeaderValue("Velopack")) {
            Credentials = new Credentials(options.Token)
        };

        var newReleaseReq = new NewRelease(semVer.ToString()) {
            Body = releaseNotes,
            Draft = true,
            Prerelease = options.Prerelease,
            Name = string.IsNullOrWhiteSpace(options.ReleaseName) ? semVer.ToString() : options.ReleaseName,
        };

        Log.Info($"Creating draft release titled '{newReleaseReq.Name}'");

        var existingReleases = await client.Repository.Release.GetAll(repoOwner, repoName);
        if (existingReleases.Any(r => r.TagName == semVer.ToString())) {
            throw new Exception($"There is already an existing release tagged '{semVer}'. Please delete this release or provide a new version number / release name.");
        }

        // create github release
        var release = await client.Repository.Release.Create(repoOwner, repoName, newReleaseReq);

        // upload all assets (incl packages)
        foreach (var a in assets.Files) {
            await RetryAsync(() => UploadFileAsAsset(client, release, a.FullName), $"Uploading asset '{a.Name}'..");
        }

        MemoryStream releasesFileToUpload = new MemoryStream();
        ReleaseEntry.WriteReleaseFile(assets.Releases, releasesFileToUpload);
        var releasesBytes = releasesFileToUpload.ToArray();
        var data = new ReleaseAssetUpload(assets.ReleasesFileName, "application/octet-stream", new MemoryStream(releasesBytes), TimeSpan.FromMinutes(1));
        await client.Repository.Release.UploadAsset(release, data, CancellationToken.None);

        // convert draft to full release
        if (options.Publish) {
            Log.Info("Converting draft to full published release.");
            var upd = release.ToUpdate();
            upd.Draft = false;
            release = await client.Repository.Release.Edit(repoOwner, repoName, release.Id, upd);
        }
    }

    private async Task UploadFileAsAsset(GitHubClient client, Release release, string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var data = new ReleaseAssetUpload(Path.GetFileName(filePath), "application/octet-stream", stream, TimeSpan.FromMinutes(30));
        await client.Repository.Release.UploadAsset(release, data, CancellationToken.None);
    }
}