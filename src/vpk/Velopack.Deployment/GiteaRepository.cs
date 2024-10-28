using System.Net;
using System.Text;
using Gitea.Net.Api;
using Gitea.Net.Client;
using Gitea.Net.Model;
using Microsoft.Extensions.Logging;
using Velopack.NuGet;
using Velopack.Packaging;
using Velopack.Packaging.Exceptions;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Deployment;

public class GiteaBaseOptions : RepositoryOptions
{
    public bool Prerelease { get; set; }

    public string RepoUrl { get; set; }

    public string Token { get; set; }

    ///// <summary>
    ///// Example https://gitea.com
    ///// </summary>
    //public string ServerUrl { get; set; }

    //public int ServerPort { get; set; }
}

public class GiteaDownloadOptions : GiteaBaseOptions, IObjectDownloadOptions
{
    public bool UpdateReleasesFile { get; set; }
}

public class GiteaUploadOptions : GiteaBaseOptions
{
    public bool Publish { get; set; }

    public string ReleaseName { get; set; }

    public string TagName { get; set; }

    public string TargetCommitish { get; set; }

    public bool Merge { get; set; }
}
public class GiteaRepository : SourceRepository<GiteaDownloadOptions, GiteaBaseOptions, GiteaSource>, IRepositoryCanUpload<GiteaUploadOptions>
{
    public GiteaRepository(ILogger logger) : base(logger)
    {
    }
    public override GiteaSource CreateSource(GiteaBaseOptions options)
    {
        return new GiteaSource(options.RepoUrl, options.Token, options.Prerelease);
    }

    public static (string owner, string repo) GetOwnerAndRepo(string repoUrl)
    {
        var repoUri = new Uri(repoUrl);
        var repoParts = repoUri.AbsolutePath.Trim('/').Split('/');
        if (repoParts.Length != 2)
            throw new Exception($"Invalid Gitea URL, '{repoUri.AbsolutePath}' should be in the format 'owner/repo'");

        var repoOwner = repoParts[0];
        var repoName = repoParts[1];
        return (repoOwner, repoName);
    }

    public async Task UploadMissingAssetsAsync(GiteaUploadOptions options)
    {
        var (repoOwner, repoName) = GetOwnerAndRepo(options.RepoUrl);
        var helper = new ReleaseEntryHelper(options.ReleaseDir.FullName, options.Channel, Log, options.TargetOs);
        var build = BuildAssets.Read(options.ReleaseDir.FullName, options.Channel);
        var latest = helper.GetLatestFullRelease();
        var latestPath = Path.Combine(options.ReleaseDir.FullName, latest.FileName);
        var releaseNotes = new ZipPackage(latestPath).ReleaseNotes;
        var semVer = options.TagName ?? latest.Version.ToString();
        var releaseName = string.IsNullOrWhiteSpace(options.ReleaseName) ? semVer.ToString() : options.ReleaseName;

        // Setup Gitea config
        Configuration config = new Configuration();
        // Example: http://www.Gitea.com/api/v1
        var uri = new Uri(options.RepoUrl);
        var baseUri = uri.GetLeftPart(System.UriPartial.Authority);
        config.BasePath = baseUri + "/api/v1";

        Log.Info($"Preparing to upload {build.Files.Count} asset(s) to Gitea");

        // Set token if provided
        if (!string.IsNullOrWhiteSpace(options.Token)) {
            config.ApiKey.Add("token", options.Token);
        }
        var apiInstance = new RepositoryApi(config);
        // Get all releases
        // Get repository info for total releases
        List<Release> existingReleases = null;
        ApiResponse<Repository> repositoryInfo = await apiInstance.RepoGetWithHttpInfoAsync(repoOwner, repoName);
        if (repositoryInfo != null && repositoryInfo.StatusCode == HttpStatusCode.OK) {
            // Get all releases
            var allReleases = await apiInstance.RepoListReleasesWithHttpInfoAsync(repoOwner, repoName, page: 1, limit: (int) repositoryInfo.Data.ReleaseCounter);
            existingReleases = allReleases.Data;
            if (allReleases != null && allReleases.StatusCode == HttpStatusCode.OK && allReleases.Data.Any(r => r.Name == releaseName)) {
                throw new UserInfoException($"There is already an existing release named '{releaseName}'. Please delete this release or provide a new release name.");
            }
        } else {
            throw new UserInfoException("Could not get all releases from server");
        }

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
            ?? existingReleases.FirstOrDefault(r => r.Name == releaseName); ;

        if (release != null) {
            if (release.TagName != semVer.ToString())
                throw new UserInfoException($"Found existing release matched by name ({release.Name} [{release.TagName}]), but tag name does not match ({semVer}).");
            Log.Info($"Found existing release ({release.Name} [{release.TagName}]). Merge flag is enabled.");
        } else {
            var newReleaseReq = new CreateReleaseOption(
                body: releaseNotes,
                draft: true,
                prerelease: options.Prerelease,
                name: string.IsNullOrWhiteSpace(options.ReleaseName) ? semVer.ToString() : options.ReleaseName,
                targetCommitish: options.TargetCommitish,
                tagName: semVer.ToString()
            );
            Log.Info($"Creating draft release titled '{newReleaseReq.Name}'");
            release = await apiInstance.RepoCreateReleaseAsync(repoOwner, repoName, newReleaseReq);
        }

        // check if there is an existing releasesFile to merge
        var releasesFileName = CoreUtil.GetVeloReleaseIndexName(options.Channel);
        var releaseAsset = release.Assets.FirstOrDefault(a => a.Name == releasesFileName);
        if (releaseAsset != null) {
            throw new UserInfoException($"There is already a remote asset named '{releasesFileName}', and merging release files on Gitea is not supported.");
        }

        // upload all assets (incl packages)
        foreach (var a in build.Files) {
            await RetryAsync(() => UploadFileAsAsset(apiInstance, release, repoOwner, repoName, a), $"Uploading asset '{Path.GetFileName(a)}'..");
        }

        var feed = new VelopackAssetFeed {
            Assets = build.GetReleaseEntries().ToArray(),
        };
        var json = ReleaseEntryHelper.GetAssetFeedJson(feed);

        await RetryAsync(async () => {
            await apiInstance.RepoCreateReleaseAttachmentAsync(repoOwner, repoName, release.Id, releasesFileName, new MemoryStream(Encoding.UTF8.GetBytes(json)));
        }, "Uploading " + releasesFileName);

        if (options.Channel == ReleaseEntryHelper.GetDefaultChannel(RuntimeOs.Windows)) {
            var legacyReleasesContent = ReleaseEntryHelper.GetLegacyMigrationReleaseFeedString(feed);
            var legacyReleasesBytes = Encoding.UTF8.GetBytes(legacyReleasesContent);
            await RetryAsync(async () => {
                await apiInstance.RepoCreateReleaseAttachmentAsync(repoOwner, repoName, release.Id, "RELEASES", new MemoryStream(legacyReleasesBytes));
            }, "Uploading legacy RELEASES (compatibility)");
        }

        // convert draft to full release
        if (options.Publish) {
            if (release.Draft) {
                Log.Info("Converting draft to full published release.");
                var body = new EditReleaseOption(
                    release.Body,
                    false, // Draft
                    release.Name,
                    release.Prerelease,
                    release.TagName,
                    release.TargetCommitish
                    );
                Release result = await apiInstance.RepoEditReleaseAsync(repoOwner, repoName, release.Id, body);
            } else {
                Log.Info("Skipping publish, release is already not a draft.");
            }
        }
    }

    private async Task UploadFileAsAsset(RepositoryApi client, Release release, string repoOwner, string repoName, string filePath)
    {
        using var stream = File.OpenRead(filePath);
        // Create a release attachment
        await client.RepoCreateReleaseAttachmentAsync(repoOwner, repoName, release.Id, Path.GetFileName(filePath), stream);
    }
}