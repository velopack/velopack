using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Velopack.Util;

#if NET8_0_OR_GREATER
using JsonName = System.Text.Json.Serialization.JsonPropertyNameAttribute;
#else
using JsonName = Newtonsoft.Json.JsonPropertyAttribute;
#endif

namespace Velopack.Sources
{
    /// <summary>
    /// Describes a Gitlab release, plus any assets that are attached.
    /// </summary>
    public class GitlabRelease
    {
        /// <summary>
        /// The name of the release.
        /// </summary>
        [JsonName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// True if this is intended for an upcoming release.
        /// </summary>
        [JsonName("upcoming_release")]
        public bool UpcomingRelease { get; set; }

        /// <summary>
        /// The date which this release was published publicly.
        /// </summary>
        [JsonName("released_at")]
        public DateTime? ReleasedAt { get; set; }

        /// <summary>
        /// A container for the assets (files) uploaded to this release.
        /// </summary>
        [JsonName("assets")]
        public GitlabReleaseAsset? Assets { get; set; }
    }

    /// <summary>
    /// Describes a container for the assets attached to a release.
    /// </summary>
    public class GitlabReleaseAsset
    {
        /// <summary>
        /// The amount of assets linked to the release.
        /// </summary>
        [JsonName("count")]
        public int Count { get; set; }

        /// <summary>
        /// A list of asset (file) links.
        /// </summary>
        [JsonName("links")]
        public GitlabReleaseLink[] Links { get; set; } = new GitlabReleaseLink[0];
    }

    /// <summary>
    /// Describes a container for the links of assets attached to a release.
    /// </summary>
    public class GitlabReleaseLink
    {
        /// <summary>
        /// Name of the asset (file) linked.
        /// </summary>
        [JsonName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// The url for the asset. This make use of the Gitlab API.
        /// </summary>
        [JsonName("url")]
        public string? Url { get; set; }

        /// <summary>
        /// A direct url to the asset, via a traditional URl. 
        /// As a posed to using the API.
        /// This links directly to the raw asset (file).
        /// </summary>
        [JsonName("direct_asset_url")]
        public string? DirectAssetUrl { get; set; }

        /// <summary>
        /// The category type that the asset is listed under.
        /// Options: 'Package', 'Image', 'Runbook', 'Other'
        /// </summary>
        [JsonName("link_type")]
        public string? Type { get; set; }
    }

    /// <summary>
    /// Retrieves available releases from a GitLab repository. This class only
    /// downloads assets from the very latest GitLab release.
    /// </summary>
    public class GitlabSource : GitBase<GitlabRelease>
    {
        /// <inheritdoc cref="Authorization"/>
        protected override (string Name, string Value) Authorization => ("PRIVATE-TOKEN", AccessToken ?? string.Empty);
        
        /// <inheritdoc cref="GitlabSource" />
        /// <param name="repoUrl">
        /// The URL of the GitLab repository to download releases from 
        /// (e.g. https://gitlab.com/api/v4/projects/ProjectId)
        /// </param>
        /// <param name="accessToken">
        /// The GitLab access token to use with the request to download releases.
        /// </param>
        /// <param name="upcomingRelease">
        /// If true, the latest upcoming release will be downloaded. If false, the latest 
        /// stable release will be downloaded.
        /// </param>
        /// <param name="downloader">
        /// The file downloader used to perform HTTP requests. 
        /// </param>
        public GitlabSource(string repoUrl, string accessToken, bool upcomingRelease, IFileDownloader? downloader = null)
            : base(repoUrl, accessToken, upcomingRelease, downloader)
        {
        }

        /// <summary>
        /// Given a <see cref="GitlabRelease"/> and an asset filename (eg. 'RELEASES') this 
        /// function will return either <see cref="GitlabReleaseLink.DirectAssetUrl"/> or
        /// <see cref="GitlabReleaseLink.Url"/>, depending whether an access token is available
        /// or not. Throws if the specified release has no matching assets.
        /// </summary>
        protected override string GetAssetUrlFromName(GitlabRelease release, string assetName)
        {
            if (release.Assets == null || release.Assets.Count == 0) {
                throw new ArgumentException($"No assets found in Gitlab Release '{release.Name}'.");
            }

            GitlabReleaseLink? packageFile =
                release.Assets.Links.FirstOrDefault(a => a.Name?.Equals(assetName, StringComparison.InvariantCultureIgnoreCase) == true);
            if (packageFile == null) {
                throw new ArgumentException($"Could not find asset called '{assetName}' in GitLab Release '{release.Name}'.");
            }

            if (String.IsNullOrWhiteSpace(AccessToken) && packageFile.DirectAssetUrl != null) {
                return packageFile.DirectAssetUrl;
            } else if (packageFile.Url != null) {
                return packageFile.Url;
            } else {
                throw new Exception($"Could not find a valid URL for asset '{assetName}' in GitLab Release '{release.Name}'.");
            }
        }

        /// <summary>
        /// Retrieves a list of <see cref="GitlabRelease"/> from the current repository.
        /// </summary>
        protected override async Task<GitlabRelease[]> GetReleases(bool includePrereleases)
        {
            const int perPage = 10;
            const int page = 1;
            // https://docs.gitlab.com/ee/api/releases/
            var releasesPath = $"{RepoUri.AbsolutePath}/releases?per_page={perPage}&page={page}";
            var baseUri = new Uri("https://gitlab.com");
            var getReleasesUri = new Uri(baseUri, releasesPath);
            var response = await Downloader.DownloadString(getReleasesUri.ToString(),
                new Dictionary<string, string> {
                    [Authorization.Name] = Authorization.Value,
                    ["Accept"] = "application/json"
                }).ConfigureAwait(false);
            var releases = CompiledJson.DeserializeGitlabReleaseList(response);
            if (releases == null) return new GitlabRelease[0];
            return releases.OrderByDescending(d => d.ReleasedAt).Where(x => includePrereleases || !x.UpcomingRelease).ToArray();
        }
    }
}
