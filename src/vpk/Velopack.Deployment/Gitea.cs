using System.Net;
using Gitea.Net.Api;
using Gitea.Net.Client;
using Gitea.Net.Model;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Sources;

namespace Velopack.Deployment;

public class GiteaDownloadOptions : GitReleaseDownloadOptions;

public class GiteaUploadOptions : GitReleaseUploadOptions;

public sealed class GiteaDownloadOptionsValidator : GitReleaseDownloadOptionsValidator<GiteaDownloadOptions>;

public sealed class GiteaUploadOptionsValidator : GitReleaseUploadOptionsValidator<GiteaUploadOptions>
{
    public GiteaUploadOptionsValidator() : base("Gitea")
    {
    }
}

public class GiteaDownloadCommandRunner(ILogger logger)
    : SourceDownloadCommandRunner<GiteaDownloadOptions, GiteaDownloadOptionsValidator>(logger)
{
    protected override IUpdateSource CreateSource(GiteaDownloadOptions options)
    {
        return new GiteaSource(options.RepoUrl, options.Token, options.Prerelease);
    }
}

public class GiteaReleaseClient : IGitReleaseClient
{
    private readonly RepositoryApi _client;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly Dictionary<long, Release> _nativeReleases = new();

    public string ProviderName => "Gitea";

    public GiteaReleaseClient(string repoUrl, string token, double timeoutMinutes)
    {
        (_repoOwner, _repoName) = GitUrl.GetOwnerAndRepo(repoUrl);

        // Setup Gitea config
        Configuration config = new Configuration();
        // Example: http://www.Gitea.com/api/v1
        var uri = new Uri(repoUrl);
        var baseUri = uri.GetLeftPart(System.UriPartial.Authority);
        config.BasePath = baseUri + "/api/v1";
        config.Timeout = (int) TimeSpan.FromMinutes(timeoutMinutes).TotalMilliseconds;

        // Set token if provided
        if (!string.IsNullOrWhiteSpace(token)) {
            config.ApiKey.Add("token", token);
        }

        _client = new RepositoryApi(config);
    }

    public async Task<IReadOnlyList<GitRelease>> GetReleasesAsync()
    {
        // Get repository info for total releases
        List<Release> existingReleases;
        ApiResponse<Repository> repositoryInfo = await _client.RepoGetWithHttpInfoAsync(_repoOwner, _repoName).ConfigureAwait(false);
        if (repositoryInfo != null && repositoryInfo.StatusCode == HttpStatusCode.OK) {
            // Get all releases
            var allReleases = await _client.RepoListReleasesWithHttpInfoAsync(
                _repoOwner,
                _repoName,
                page: 1,
                limit: (int) repositoryInfo.Data.ReleaseCounter).ConfigureAwait(false);
            existingReleases = allReleases.Data;
        } else {
            throw new UserInfoException("Could not get all releases from server");
        }

        return existingReleases.Select(ToGitRelease).ToArray();
    }

    public async Task<GitRelease> CreateDraftReleaseAsync(string tagName, string name, string body, bool prerelease, string targetCommitish)
    {
        var newReleaseReq = new CreateReleaseOption(
            body: body,
            draft: true,
            prerelease: prerelease,
            name: name,
            targetCommitish: targetCommitish,
            tagName: tagName
        );
        var release = await _client.RepoCreateReleaseAsync(_repoOwner, _repoName, newReleaseReq).ConfigureAwait(false);
        return ToGitRelease(release);
    }

    public async Task UploadAssetAsync(GitRelease release, string assetName, Stream content, string contentType, TimeSpan? timeout = null)
    {
        await _client.RepoCreateReleaseAttachmentAsync(_repoOwner, _repoName, release.Id, assetName, content).ConfigureAwait(false);
    }

    public async Task PublishReleaseAsync(GitRelease release)
    {
        // edit from the native release object so no other fields are cleared by the update
        var native = _nativeReleases[release.Id];
        var body = new EditReleaseOption(
            native.Body,
            false, // Draft
            native.Name,
            native.Prerelease,
            native.TagName,
            native.TargetCommitish
        );
        await _client.RepoEditReleaseAsync(_repoOwner, _repoName, native.Id, body).ConfigureAwait(false);
    }

    private GitRelease ToGitRelease(Release release)
    {
        _nativeReleases[release.Id] = release;
        return new GitRelease {
            Id = release.Id,
            Name = release.Name,
            TagName = release.TagName,
            Draft = release.Draft,
            AssetNames = release.Assets?.Select(a => a.Name).ToArray() ?? [],
        };
    }
}

public class GiteaUploadCommandRunner(ILogger logger)
    : GitReleaseUploadCommandRunner<GiteaUploadOptions, GiteaUploadOptionsValidator>(logger)
{
    protected override IGitReleaseClient CreateClient(GiteaUploadOptions options)
    {
        return new GiteaReleaseClient(options.RepoUrl, options.Token, options.Timeout);
    }
}
