using Microsoft.Extensions.Logging;
using Octokit;
using Velopack.Sources;

namespace Velopack.Deployment;

public class GitHubOptions
{
    public DirectoryInfo ReleaseDir { get; set; }
    public string RepoUrl { get; set; }

    public string Token { get; set; }
}

public class GitHubDownloadOptions : GitHubOptions
{
    public bool Pre { get; set; }

}

public class GitHubUploadOptions : GitHubOptions
{
    public bool Publish { get; set; }

    public string ReleaseName { get; set; }

}

public class GitHubRepository
{
    private readonly ILogger _log;

    public GitHubRepository(ILogger logger)
    {
        _log = logger;
    }

    public async Task DownloadRecentPackages(GitHubDownloadOptions options)
    {
        var releaseDirectoryInfo = options.ReleaseDir;
        if (String.IsNullOrWhiteSpace(options.Token))
            _log.Warn("No GitHub access token provided. Unauthenticated requests will be limited to 60 per hour.");

        _log.Info("Fetching RELEASES...");
        var source = new GithubSource(options.RepoUrl, options.Token, options.Pre);
        var latestReleaseEntries = await source.GetReleaseFeed();

        if (latestReleaseEntries == null || latestReleaseEntries.Length == 0) {
            _log.Warn("No github release or assets found.");
            return;
        }

        _log.Info($"Found {latestReleaseEntries.Length} assets in latest RELEASES file.");

        var releasesToDownload = latestReleaseEntries
            .Where(x => !x.IsDelta)
            .OrderByDescending(x => x.Version)
            .Take(1)
            .Select(x => new {
                Obj = x,
                LocalPath = Path.Combine(releaseDirectoryInfo.FullName, x.OriginalFilename),
                Filename = x.OriginalFilename,
            });

        foreach (var entry in releasesToDownload) {
            if (File.Exists(entry.LocalPath)) {
                _log.Warn($"File '{entry.Filename}' exists on disk, skipping download.");
                continue;
            }

            _log.Info($"Downloading {entry.Filename}...");
            await source.DownloadReleaseEntry(entry.Obj, entry.LocalPath, (p) => { });
        }

        ReleaseEntry.BuildReleasesFile(releaseDirectoryInfo.FullName);
        _log.Info("Done.");
    }

    public async Task UploadMissingPackages(GitHubUploadOptions options)
    {
        if (String.IsNullOrWhiteSpace(options.Token))
            throw new InvalidOperationException("Must provide access token to create a GitHub release.");

        var releaseDirectoryInfo = options.ReleaseDir;

        var repoUri = new Uri(options.RepoUrl);
        var repoParts = repoUri.AbsolutePath.Trim('/').Split('/');
        if (repoParts.Length != 2)
            throw new Exception($"Invalid GitHub URL, '{repoUri.AbsolutePath}' should be in the format 'owner/repo'");

        var repoOwner = repoParts[0];
        var repoName = repoParts[1];

        var client = new GitHubClient(new ProductHeaderValue("Clowd.Squirrel")) {
            Credentials = new Credentials(options.Token)
        };

        var releasesPath = Path.Combine(releaseDirectoryInfo.FullName, "RELEASES");
        if (!File.Exists(releasesPath))
            ReleaseEntry.BuildReleasesFile(releaseDirectoryInfo.FullName);

        var releases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesPath)).ToArray();
        if (releases.Length == 0)
            throw new Exception("There are no nupkg's in the releases directory to upload");

        var ver = Enumerable.MaxBy(releases, x => x.Version);
        if (ver == null)
            throw new Exception("There are no nupkg's in the releases directory to upload");
        var semVer = ver.Version;

        _log.Info($"Preparing to upload latest local release to GitHub");

        var newReleaseReq = new NewRelease(semVer.ToString()) {
            //Body = ver.GetReleaseNotes(releaseDirectoryInfo.FullName, ReleaseNotesFormat.Markdown),
            Draft = true,
            Prerelease = semVer.HasMetadata || semVer.IsPrerelease,
            Name = string.IsNullOrWhiteSpace(options.ReleaseName)
                ? semVer.ToString()
                : options.ReleaseName,
        };

        _log.Info($"Creating draft release titled '{semVer.ToString()}'");

        var existingReleases = await client.Repository.Release.GetAll(repoOwner, repoName);
        if (existingReleases.Any(r => r.TagName == semVer.ToString())) {
            throw new Exception($"There is already an existing release tagged '{semVer}'. Please delete this release or choose a new version number.");
        }

        var release = await client.Repository.Release.Create(repoOwner, repoName, newReleaseReq);

        // locate files to upload
        var files = releaseDirectoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly);
        var msiFile = files.SingleOrDefault(f => f.FullName.EndsWith(".msi", StringComparison.InvariantCultureIgnoreCase));
        var setupFile = files.Where(f => f.FullName.EndsWith("Setup.exe", StringComparison.InvariantCultureIgnoreCase))
            .ContextualSingle("release directory", "Setup.exe file");

        var releasesToUpload = releases.Where(x => x.Version == semVer).ToArray();
        MemoryStream releasesFileToUpload = new MemoryStream();
        ReleaseEntry.WriteReleaseFile(releasesToUpload, releasesFileToUpload);
        var releasesBytes = releasesFileToUpload.ToArray();

        // upload nupkg's
        foreach (var r in releasesToUpload) {
            var path = Path.Combine(releaseDirectoryInfo.FullName, r.OriginalFilename);
            await UploadFileAsAsset(client, release, path);
        }

        // other files
        await UploadFileAsAsset(client, release, setupFile.FullName);
        if (msiFile != null) await UploadFileAsAsset(client, release, msiFile.FullName);

        // RELEASES
        _log.Info($"Uploading RELEASES");
        var data = new ReleaseAssetUpload("RELEASES", "application/octet-stream", new MemoryStream(releasesBytes), TimeSpan.FromMinutes(1));
        await client.Repository.Release.UploadAsset(release, data, CancellationToken.None);

        _log.Info($"Done creating draft GitHub release.");

        // convert draft to full release
        if (options.Publish) {
            _log.Info("Converting draft to full published release.");
            var upd = release.ToUpdate();
            upd.Draft = false;
            release = await client.Repository.Release.Edit(repoOwner, repoName, release.Id, upd);
        }

        _log.Info("Release URL: " + release.HtmlUrl);
    }

    private async Task UploadFileAsAsset(GitHubClient client, Release release, string filePath)
    {
        _log.Info($"Uploading asset '{Path.GetFileName(filePath)}'");
        using var stream = File.OpenRead(filePath);
        var data = new ReleaseAssetUpload(Path.GetFileName(filePath), "application/octet-stream", stream, TimeSpan.FromMinutes(30));
        await client.Repository.Release.UploadAsset(release, data, CancellationToken.None);
    }
}