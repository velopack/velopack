using System.Text;
using Microsoft.Extensions.Logging;
using Octokit;
using Velopack.NuGet;
using Velopack.Packaging;
using Velopack.Packaging.Exceptions;
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

    public string TagName { get; set; }

    public bool Merge { get; set; }
}

public class GitHubRepository : SourceRepository<GitHubDownloadOptions, GithubSource>, IRepositoryCanUpload<GitHubUploadOptions>
{
    public GitHubRepository(ILogger logger) : base(logger)
    {
    }

    public override GithubSource CreateSource(GitHubDownloadOptions options)
    {
        return new GithubSource(options.RepoUrl, options.Token, options.Prerelease);
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
        var helper = new ReleaseEntryHelper(options.ReleaseDir.FullName, options.Channel, Log);
        var build = BuildAssets.Read(options.ReleaseDir.FullName, options.Channel);
        var latest = helper.GetLatestFullRelease();
        var latestPath = Path.Combine(options.ReleaseDir.FullName, latest.FileName);
        var releaseNotes = new ZipPackage(latestPath).ReleaseNotes;
        var semVer = options.TagName ?? latest.Version.ToString();
        var releaseName = string.IsNullOrWhiteSpace(options.ReleaseName) ? semVer.ToString() : options.ReleaseName;

        Log.Info($"Preparing to upload {build.Files.Count} asset(s) to GitHub");

        var client = new GitHubClient(new ProductHeaderValue("Velopack")) {
            Credentials = new Credentials(options.Token)
        };

        var existingReleases = await client.Repository.Release.GetAll(repoOwner, repoName);
        if (!options.Merge) {
            if (existingReleases.Any(r => r.TagName == semVer.ToString())) {
                throw new UserInfoException($"There is already an existing release tagged '{semVer}'. Please delete this release or provide a new version number.");
            }
            if (existingReleases.Any(r => r.Name == releaseName)) {
                throw new UserInfoException($"There is already an existing release named '{releaseName}'. Please delete this release or provide a new release name.");
            }
        }

        // create or retrieve github release
        var release = existingReleases.FirstOrDefault(r => r.TagName == semVer.ToString())
            ?? existingReleases.FirstOrDefault(r => r.Name == releaseName);

        if (release != null) {
            if (release.TagName != semVer.ToString())
                throw new UserInfoException($"Found existing release matched by name ({release.Name} [{release.TagName}]), but tag name does not match ({semVer}).");
            Log.Info($"Found existing release ({release.Name} [{release.TagName}]). Merge flag is enabled.");
        } else {
            var newReleaseReq = new NewRelease(semVer.ToString()) {
                Body = releaseNotes,
                Draft = true,
                Prerelease = options.Prerelease,
                Name = string.IsNullOrWhiteSpace(options.ReleaseName) ? semVer.ToString() : options.ReleaseName,
            };
            Log.Info($"Creating draft release titled '{newReleaseReq.Name}'");
            release = await client.Repository.Release.Create(repoOwner, repoName, newReleaseReq);
        }

        // check if there is an existing releasesFile to merge
        var releasesFileName = Utility.GetVeloReleaseIndexName(options.Channel);
        var releaseAsset = release.Assets.FirstOrDefault(a => a.Name == releasesFileName);
        if (releaseAsset != null) {
            throw new UserInfoException($"There is already a remote asset named '{releasesFileName}', and merging release files on GitHub is not supported.");
        }

        // upload all assets (incl packages)
        foreach (var a in build.Files) {
            await RetryAsync(() => UploadFileAsAsset(client, release, a), $"Uploading asset '{Path.GetFileName(a)}'..");
        }

        var feed = new VelopackAssetFeed {
            Assets = build.GetReleaseEntries().ToArray(),
        };
        var entries = build.GetReleaseEntries();
        var json = ReleaseEntryHelper.GetAssetFeedJson(feed);

        await RetryAsync(async () => {
            var data = new ReleaseAssetUpload(releasesFileName, "application/json", new MemoryStream(Encoding.UTF8.GetBytes(json)), TimeSpan.FromMinutes(1));
            await client.Repository.Release.UploadAsset(release, data, CancellationToken.None);
        }, "Uploading " + releasesFileName);

        if (options.Channel == ReleaseEntryHelper.GetDefaultChannel(RuntimeOs.Windows)) {
            var ms = new MemoryStream();
#pragma warning disable CS0618 // Type or member is obsolete
            ReleaseEntry.WriteReleaseFile(entries.Select(ReleaseEntry.FromVelopackAsset), ms);
#pragma warning restore CS0618 // Type or member is obsolete
            await RetryAsync(async () => {
                var data = new ReleaseAssetUpload("RELEASES", "application/octet-stream", new MemoryStream(ms.ToArray()), TimeSpan.FromMinutes(1));
                await client.Repository.Release.UploadAsset(release, data, CancellationToken.None);
            }, "Uploading legacy RELEASES (compatibility)");
        }

        // convert draft to full release
        if (options.Publish) {
            if (release.Draft) {
                Log.Info("Converting draft to full published release.");
                var upd = release.ToUpdate();
                upd.Draft = false;
                release = await client.Repository.Release.Edit(repoOwner, repoName, release.Id, upd);
            } else {
                Log.Info("Skipping publish, release is already not a draft.");
            }
        }
    }

    private async Task UploadFileAsAsset(GitHubClient client, Release release, string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var data = new ReleaseAssetUpload(Path.GetFileName(filePath), "application/octet-stream", stream, TimeSpan.FromMinutes(30));
        await client.Repository.Release.UploadAsset(release, data, CancellationToken.None);
    }
}