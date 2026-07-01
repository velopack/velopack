using System.Net.Http;
using System.Text.Json;
using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Sources;
using Velopack.TestCommon;
using Velopack.Util;
using Xunit;

namespace Velopack.Deployment.Tests;

/// <summary> A provider-neutral view of a git release, used for suite assertions across Gitea and GitHub. </summary>
public sealed record RemoteRelease(
    string Name,
    string TagName,
    bool Draft,
    bool Prerelease,
    string Body,
    string TargetCommitish,
    string[] AssetNames);

/// <summary> Parameters for one in-process upload runner invocation against a git-release destination. </summary>
public sealed record GitReleaseUpload(string ReleaseDir, string Channel)
{
    public string? ReleaseName { get; init; }
    public string? TagName { get; init; }
    public string? TargetCommitish { get; init; }
    public bool Publish { get; init; }
    public bool Prerelease { get; init; }
    public bool Merge { get; init; }
}

/// <summary>
/// One test's exclusive window onto a git-release destination: a pristine repository (fresh Gitea repo, or a
/// leased+reset live GitHub repo) plus provider-specific upload/download runners and release inspection.
/// </summary>
public interface IGitReleaseScope : IAsyncDisposable
{
    /// <summary> The https://host/owner/repo URL used by the runners and sources. </summary>
    string RepoUrl { get; }

    /// <summary> The API token used by the runners and sources. </summary>
    string Token { get; }

    /// <summary> Runs the provider's upload command runner in-process. </summary>
    Task UploadAsync(GitReleaseUpload upload, ILogger log);

    /// <summary> Runs the provider's download command runner in-process into <paramref name="downloadDir"/>. </summary>
    Task DownloadAsync(string downloadDir, string channel, ILogger log);

    /// <summary> Creates the lib-csharp update source for this repo (always authenticated). </summary>
    IUpdateSource CreateSource(bool prerelease);

    /// <summary> Lists all releases (including drafts, which require the authenticated API). </summary>
    Task<IReadOnlyList<RemoteRelease>> ListReleasesAsync();

    /// <summary> The name of the repository's default branch (e.g. "main"). </summary>
    Task<string> GetDefaultBranchNameAsync();

    /// <summary> The commit sha at the head of the default branch. </summary>
    Task<string> GetDefaultBranchHeadShaAsync();
}

/// <summary> <see cref="IGitReleaseScope"/> over a fresh local docker Gitea repository (deleted on dispose). </summary>
public sealed class GiteaGitReleaseScope(GiteaTestRepo repo) : IGitReleaseScope
{
    public string RepoUrl => repo.HttpUrl;

    public string Token => repo.Token;

    private string ApiRepoBase => $"{repo.Context.Server.BaseUrl}/api/v1/repos/{repo.Repo.Owner}/{repo.Repo.Name}";

    public Task UploadAsync(GitReleaseUpload upload, ILogger log)
        => new GiteaUploadCommandRunner(log).Run(new GiteaUploadOptions {
            RepoUrl = RepoUrl,
            Token = Token,
            ReleaseDir = new DirectoryInfo(upload.ReleaseDir),
            Channel = upload.Channel,
            ReleaseName = upload.ReleaseName,
            TagName = upload.TagName,
            TargetCommitish = upload.TargetCommitish,
            Publish = upload.Publish,
            Prerelease = upload.Prerelease,
            Merge = upload.Merge,
        });

    public Task DownloadAsync(string downloadDir, string channel, ILogger log)
        => new GiteaDownloadCommandRunner(log).Run(new GiteaDownloadOptions {
            RepoUrl = RepoUrl,
            Token = Token,
            ReleaseDir = new DirectoryInfo(downloadDir),
            Channel = channel,
        });

    public IUpdateSource CreateSource(bool prerelease) => new GiteaSource(RepoUrl, Token, prerelease);

    public async Task<IReadOnlyList<RemoteRelease>> ListReleasesAsync()
    {
        using var doc = await GetJsonAsync($"{ApiRepoBase}/releases?limit=50&page=1");
        var result = new List<RemoteRelease>();
        foreach (var r in doc.RootElement.EnumerateArray()) {
            var assets = r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? "").ToArray()
                : Array.Empty<string>();
            result.Add(new RemoteRelease(
                GetString(r, "name"),
                GetString(r, "tag_name"),
                r.GetProperty("draft").GetBoolean(),
                r.GetProperty("prerelease").GetBoolean(),
                GetString(r, "body"),
                GetString(r, "target_commitish"),
                assets));
        }

        return result;
    }

    public async Task<string> GetDefaultBranchNameAsync()
    {
        using var doc = await GetJsonAsync(ApiRepoBase);
        return GetString(doc.RootElement, "default_branch");
    }

    public async Task<string> GetDefaultBranchHeadShaAsync()
    {
        var branch = await GetDefaultBranchNameAsync();
        using var doc = await GetJsonAsync($"{ApiRepoBase}/branches/{Uri.EscapeDataString(branch)}");
        return doc.RootElement.GetProperty("commit").GetProperty("id").GetString()!;
    }

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"token {Token}");
        using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"GET {url} failed: {(int) resp.StatusCode} {body}");
        return JsonDocument.Parse(body);
    }

    public ValueTask DisposeAsync() => repo.DisposeAsync();
}

/// <summary> <see cref="IGitReleaseScope"/> over a leased live GitHub test repo (reset on acquire, lock released on dispose). </summary>
public sealed class GitHubGitReleaseScope(GitHubRepoLease lease) : IGitReleaseScope
{
    public string RepoUrl => lease.RepoUrl;

    public string Token => lease.Token;

    public Task UploadAsync(GitReleaseUpload upload, ILogger log)
        => new GitHubUploadCommandRunner(log).Run(new GitHubUploadOptions {
            RepoUrl = RepoUrl,
            Token = Token,
            ReleaseDir = new DirectoryInfo(upload.ReleaseDir),
            Channel = upload.Channel,
            ReleaseName = upload.ReleaseName,
            TagName = upload.TagName,
            TargetCommitish = upload.TargetCommitish,
            Publish = upload.Publish,
            Prerelease = upload.Prerelease,
            Merge = upload.Merge,
        });

    public Task DownloadAsync(string downloadDir, string channel, ILogger log)
        => new GitHubDownloadCommandRunner(log).Run(new GitHubDownloadOptions {
            RepoUrl = RepoUrl,
            Token = Token,
            ReleaseDir = new DirectoryInfo(downloadDir),
            Channel = channel,
        });

    public IUpdateSource CreateSource(bool prerelease) => new GithubSource(RepoUrl, Token, prerelease);

    public async Task<IReadOnlyList<RemoteRelease>> ListReleasesAsync()
    {
        var releases = await lease.Client.Repository.Release.GetAll(lease.Owner, lease.Name).ConfigureAwait(false);
        return releases.Select(r => new RemoteRelease(
            r.Name ?? "",
            r.TagName ?? "",
            r.Draft,
            r.Prerelease,
            r.Body ?? "",
            r.TargetCommitish ?? "",
            r.Assets?.Select(a => a.Name).ToArray() ?? Array.Empty<string>())).ToArray();
    }

    public async Task<string> GetDefaultBranchNameAsync()
    {
        var repo = await lease.Client.Repository.Get(lease.Owner, lease.Name).ConfigureAwait(false);
        return repo.DefaultBranch;
    }

    public async Task<string> GetDefaultBranchHeadShaAsync()
    {
        var branch = await GetDefaultBranchNameAsync();
        var reference = await lease.Client.Git.Reference.Get(lease.Owner, lease.Name, $"heads/{branch}").ConfigureAwait(false);
        return reference.Object.Sha;
    }

    public ValueTask DisposeAsync() => lease.DisposeAsync();
}

/// <summary>
/// Shared destination-test suite for the git-release style vpk deployment targets (Gitea, GitHub). Each inherited
/// [Fact] creates a pristine scope, drives the in-process upload/download runners through one branch of
/// <c>GitReleaseUploadCommandRunner</c>, and asserts on the provider-neutral release listing. Channels are distinct
/// per test (and always explicit, never OS-defaulted) so cached packs stay reusable and repos never collide.
/// </summary>
public abstract class GitReleaseDeploymentSuite
{
    /// <summary> The packed app id, shared by every concrete suite so pack caching is reused across providers. </summary>
    protected const string AppId = "GitRelApp";

    /// <summary> Deterministic release-notes markdown packed into the "with notes" fixture. </summary>
    protected const string NotesContent = "# GitRelApp Release\nThis is just a _test_ release body.";

    protected readonly ITestOutputHelper Output;

    protected GitReleaseDeploymentSuite(ITestOutputHelper output)
    {
        Output = output;
    }

    /// <summary> Skips the test (never fails) when the backing service/token is unavailable. </summary>
    protected abstract Task SkipUnlessReadyAsync();

    /// <summary> Creates a pristine repository scope for the current test. </summary>
    protected abstract Task<IGitReleaseScope> CreateScopeAsync(ILogger log);

    protected ICacheLogger<GitReleaseDeploymentSuite> CreateLogger()
        => Output.BuildLoggerFor<GitReleaseDeploymentSuite>();

    private static async Task<RemoteRelease> GetSingleReleaseAsync(IGitReleaseScope scope)
    {
        var releases = await scope.ListReleasesAsync();
        return Assert.Single(releases);
    }

    private static void TryDeleteDir(string dir) => ReleaseFixtures.TryDeleteDir(dir);

    [Fact]
    public async Task InitialUploadPublishesRelease()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        // "win" is the Windows default channel, set explicitly for determinism — it exercises the extra
        // legacy 'RELEASES' asset branch, which only runs for that exact channel name.
        var channel = "win";
        var pack = ReleaseFixtures.GetCachedPackWithNotes(AppId, channel, log, NotesContent, "1.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(pack.ReleaseDir, channel) {
                ReleaseName = "GitRelApp v1.0.0",
                Publish = true,
            }, log);

            var release = await GetSingleReleaseAsync(scope);
            Assert.False(release.Draft, "release should have been published (not left as a draft)");
            Assert.Equal("GitRelApp v1.0.0", release.Name);
            Assert.Equal("1.0.0", release.TagName);
            Assert.Equal(
                NotesContent.Trim().ReplaceLineEndings("\n"),
                release.Body.Trim().ReplaceLineEndings("\n"));
            Assert.Contains(release.AssetNames, a => a.StartsWith($"{AppId}-1.0.0") && a.EndsWith("-full.nupkg"));
            Assert.Contains($"releases.{channel}.json", release.AssetNames);
            Assert.Contains("RELEASES", release.AssetNames);
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }

    [Fact]
    public async Task UploadWithoutPublishLeavesDraft()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var channel = "draftup";
        var pack = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(pack.ReleaseDir, channel) { Publish = false }, log);

            var release = await GetSingleReleaseAsync(scope);
            Assert.True(release.Draft, "release should have been left as a draft");
            Assert.Equal("1.0.0", release.TagName);
            Assert.Contains($"releases.{channel}.json", release.AssetNames);
            // the legacy RELEASES feed is only uploaded for the windows default channel
            Assert.DoesNotContain("RELEASES", release.AssetNames);
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }

    [Fact]
    public async Task RefusesSecondUploadWithoutMerge()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var channel = "nomerge";
        var p1 = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        var p2 = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(p1.ReleaseDir, channel) { Publish = true }, log);

            // same tag (1.0.0) again without --merge must refuse
            await Assert.ThrowsAnyAsync<UserInfoException>(
                () => scope.UploadAsync(new GitReleaseUpload(p2.ReleaseDir, channel) { Publish = true }, log));
        } finally {
            TryDeleteDir(p1.ReleaseDir);
            TryDeleteDir(p2.ReleaseDir);
        }
    }

    [Fact]
    public async Task RefusesMergeWithMismatchedTag()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var channel = "mismatch";
        var p1 = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        var p2 = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0", "2.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(p1.ReleaseDir, channel) {
                ReleaseName = "mismatch-rel",
                Publish = true,
                Merge = true,
            }, log);

            // matched by release name, but the existing release is tagged 1.0.0 and this upload wants 2.0.0
            var ex = await Assert.ThrowsAnyAsync<UserInfoException>(
                () => scope.UploadAsync(new GitReleaseUpload(p2.ReleaseDir, channel) {
                    ReleaseName = "mismatch-rel",
                    Publish = true,
                    Merge = true,
                }, log));
            Assert.Contains("tag name does not match", ex.Message);
        } finally {
            TryDeleteDir(p1.ReleaseDir);
            TryDeleteDir(p2.ReleaseDir);
        }
    }

    [Fact]
    public async Task MergesTwoChannelsIntoOneRelease()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var pa = ReleaseFixtures.GetCachedPack(AppId, "stablea", log, "1.0.0");
        var pb = ReleaseFixtures.GetCachedPack(AppId, "stableb", log, "1.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(pa.ReleaseDir, "stablea") {
                TagName = "1.0.0",
                Publish = true,
            }, log);
            await scope.UploadAsync(new GitReleaseUpload(pb.ReleaseDir, "stableb") {
                TagName = "1.0.0",
                Publish = true,
                Merge = true,
            }, log);

            var release = await GetSingleReleaseAsync(scope);
            Assert.Equal("1.0.0", release.TagName);
            Assert.Contains($"{AppId}-1.0.0-stablea-full.nupkg", release.AssetNames);
            Assert.Contains($"{AppId}-1.0.0-stableb-full.nupkg", release.AssetNames);
            Assert.Contains("releases.stablea.json", release.AssetNames);
            Assert.Contains("releases.stableb.json", release.AssetNames);
        } finally {
            TryDeleteDir(pa.ReleaseDir);
            TryDeleteDir(pb.ReleaseDir);
        }
    }

    [Fact]
    public async Task RefusesMergeWhenChannelIndexExists()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var channel = "chidx";
        var p1 = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        var p2 = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(p1.ReleaseDir, channel) {
                TagName = "1.0.0",
                Publish = true,
                Merge = true,
            }, log);

            // merging into the same release is fine, but not when releases.{channel}.json is already there
            var ex = await Assert.ThrowsAnyAsync<UserInfoException>(
                () => scope.UploadAsync(new GitReleaseUpload(p2.ReleaseDir, channel) {
                    TagName = "1.0.0",
                    Publish = true,
                    Merge = true,
                }, log));
            Assert.Contains("merging release files", ex.Message);
        } finally {
            TryDeleteDir(p1.ReleaseDir);
            TryDeleteDir(p2.ReleaseDir);
        }
    }

    [Fact]
    public async Task TagOnDefaultBranchWhenTargetCommitishNotSet()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var channel = "tagdef";
        var pack = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(pack.ReleaseDir, channel) { Publish = true }, log);

            var defaultBranch = await scope.GetDefaultBranchNameAsync();
            var headSha = await scope.GetDefaultBranchHeadShaAsync();
            var release = await GetSingleReleaseAsync(scope);
            // providers report either the branch name or the resolved head sha once the tag exists
            Assert.True(
                release.TargetCommitish == defaultBranch || release.TargetCommitish == headSha,
                $"expected target commitish '{defaultBranch}' or '{headSha}' but found '{release.TargetCommitish}'");
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }

    [Fact]
    public Task TagUsesExplicitTargetCommitishSha() => AssertExplicitTargetCommitishAsync("tagsha", useBranchName: false);

    [Fact]
    public Task TagUsesExplicitTargetCommitishBranchName() => AssertExplicitTargetCommitishAsync("tagbranch", useBranchName: true);

    private async Task AssertExplicitTargetCommitishAsync(string channel, bool useBranchName)
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var pack = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        try {
            var commitish = useBranchName
                ? await scope.GetDefaultBranchNameAsync()
                : await scope.GetDefaultBranchHeadShaAsync();
            await scope.UploadAsync(new GitReleaseUpload(pack.ReleaseDir, channel) {
                Publish = true,
                TargetCommitish = commitish,
            }, log);

            var release = await GetSingleReleaseAsync(scope);
            if (useBranchName) {
                // providers report either the branch name or the resolved head sha once the tag exists
                var headSha = await scope.GetDefaultBranchHeadShaAsync();
                Assert.True(
                    release.TargetCommitish == commitish || release.TargetCommitish == headSha,
                    $"expected target commitish '{commitish}' or '{headSha}' but found '{release.TargetCommitish}'");
            } else {
                Assert.Equal(commitish, release.TargetCommitish);
            }
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }

    [Fact]
    public async Task PrereleaseFlagRespected()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var channel = "prerel";
        var pack = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(pack.ReleaseDir, channel) {
                Publish = true,
                Prerelease = true,
            }, log);

            var release = await GetSingleReleaseAsync(scope);
            Assert.True(release.Prerelease, "release should have been marked as a prerelease");

            var stableFeed = await scope.CreateSource(prerelease: false)
                .GetReleaseFeed(log.ToVelopackLogger(), null, channel);
            Assert.Empty(stableFeed.Assets);

            var prereleaseFeed = await scope.CreateSource(prerelease: true)
                .GetReleaseFeed(log.ToVelopackLogger(), null, channel);
            var full = prereleaseFeed.Assets.Where(a => a.Type == VelopackAssetType.Full).ToArray();
            Assert.Single(full);
            Assert.Equal("1.0.0", full[0].Version.ToString());
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }

    [Fact]
    public async Task DownloadRoundTrip()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var channel = "dlround";
        var pack = ReleaseFixtures.GetCachedPack(AppId, channel, log, "1.0.0");
        try {
            await scope.UploadAsync(new GitReleaseUpload(pack.ReleaseDir, channel) { Publish = true }, log);

            using var _1 = TempUtil.GetTempDirectory(out var downloadDir);
            await scope.DownloadAsync(downloadDir, channel, log);
            var downloaded = Path.Combine(downloadDir, $"{AppId}-1.0.0-{channel}-full.nupkg");
            Assert.True(File.Exists(downloaded), $"Expected downloaded package at {downloaded}");
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }
}
