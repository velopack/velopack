using Microsoft.Extensions.Logging;
using Octokit;
using Velopack.Sources;

namespace Velopack.Deployment;

public class GitHubDownloadOptions : GitReleaseDownloadOptions;

public class GitHubUploadOptions : GitReleaseUploadOptions;

public sealed class GitHubDownloadOptionsValidator : GitReleaseDownloadOptionsValidator<GitHubDownloadOptions>;

public sealed class GitHubUploadOptionsValidator : GitReleaseUploadOptionsValidator<GitHubUploadOptions>
{
    public GitHubUploadOptionsValidator() : base("GitHub")
    {
    }
}

public class GitHubDownloadCommandRunner(ILogger logger)
    : SourceDownloadCommandRunner<GitHubDownloadOptions, GitHubDownloadOptionsValidator>(logger)
{
    protected override IUpdateSource CreateSource(GitHubDownloadOptions options)
    {
        return new GithubSource(options.RepoUrl, options.Token, options.Prerelease);
    }
}

public class GitHubReleaseClient : GitReleaseClient<Release>
{
    private readonly GitHubClient _client;

    public override string ProviderName => "GitHub";

    public GitHubReleaseClient(string repoUrl, string token, double timeoutMinutes)
        : base(repoUrl)
    {
        var connection = new Connection(new ProductHeaderValue("Velopack"), new GitHubHttpClient(TimeSpan.FromMinutes(timeoutMinutes)));
        _client = new GitHubClient(connection) {
            Credentials = new Credentials(token)
        };
    }

    public override async Task<IReadOnlyList<GitRelease>> GetReleasesAsync()
    {
        var releases = await _client.Repository.Release.GetAll(RepoOwner, RepoName).ConfigureAwait(false);
        return releases.Select(RegisterNativeRelease).ToArray();
    }

    public override async Task<GitRelease> CreateDraftReleaseAsync(string tagName, string name, string body, bool prerelease, string targetCommitish)
    {
        var newReleaseReq = new NewRelease(tagName) {
            Body = body,
            Draft = true,
            Prerelease = prerelease,
            Name = name,
            TargetCommitish = targetCommitish,
        };
        var release = await _client.Repository.Release.Create(RepoOwner, RepoName, newReleaseReq).ConfigureAwait(false);
        return RegisterNativeRelease(release);
    }

    public override async Task UploadAssetAsync(GitRelease release, string assetName, Stream content, string contentType, TimeSpan? timeout = null)
    {
        var native = GetNativeRelease(release);
        var data = new ReleaseAssetUpload(assetName, contentType, content, timeout);
        await _client.Repository.Release.UploadAsset(native, data, CancellationToken.None).ConfigureAwait(false);
    }

    public override async Task PublishReleaseAsync(GitRelease release)
    {
        // edit from the native release object so no other fields are cleared by the update
        var native = GetNativeRelease(release);
        var upd = native.ToUpdate();
        upd.Draft = false;
        await _client.Repository.Release.Edit(RepoOwner, RepoName, native.Id, upd).ConfigureAwait(false);
    }

    protected override GitRelease ToGitRelease(Release release)
    {
        return new GitRelease {
            Id = release.Id,
            Name = release.Name,
            TagName = release.TagName,
            Draft = release.Draft,
            AssetNames = release.Assets?.Select(a => a.Name).ToArray() ?? [],
        };
    }
}

public class GitHubUploadCommandRunner(ILogger logger)
    : GitReleaseUploadCommandRunner<GitHubUploadOptions, GitHubUploadOptionsValidator>(logger)
{
    protected override IGitReleaseClient CreateClient(GitHubUploadOptions options)
    {
        return new GitHubReleaseClient(options.RepoUrl, options.Token, options.Timeout);
    }
}
