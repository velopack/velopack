using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Velopack.Json;

namespace Velopack.Sources
{
    /// <summary> Describes a GitHub release, including attached assets. </summary>
    public class GithubRelease
    {
        /// <summary> The name of this release. </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary> True if this release is a prerelease. </summary>
        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        /// <summary> The date which this release was published publically. </summary>
        [JsonPropertyName("published_at")]
        public DateTime? PublishedAt { get; set; }

        /// <summary> A list of assets (files) uploaded to this release. </summary>
        [JsonPropertyName("assets")]
        public GithubReleaseAsset[] Assets { get; set; } = new GithubReleaseAsset[0];
    }

    /// <summary> Describes a asset (file) uploaded to a GitHub release. </summary>
    public class GithubReleaseAsset
    {
        /// <summary> 
        /// The asset URL for this release asset. Requests to this URL will use API
        /// quota and return JSON unless the 'Accept' header is "application/octet-stream". 
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>  
        /// The browser URL for this release asset. This does not use API quota,
        /// however this URL only works for public repositories. If downloading
        /// assets from a private repository, the <see cref="Url"/> property must
        /// be used with an appropriate access token.
        /// </summary>
        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        /// <summary> The name of this release asset. </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary> The mime type of this release asset (as detected by GitHub). </summary>
        [JsonPropertyName("content_type")]
        public string? ContentType { get; set; }
    }

    /// <summary>
    /// Retrieves available releases from a GitHub repository.
    /// </summary>
    public class GithubSource : GitBase<GithubRelease>
    {
        /// <inheritdoc cref="GithubSource" />
        /// <param name="repoUrl">
        /// The URL of the GitHub repository to download releases from 
        /// (e.g. https://github.com/myuser/myrepo)
        /// </param>
        /// <param name="accessToken">
        /// The GitHub access token to use with the request to download releases. 
        /// If left empty, the GitHub rate limit for unauthenticated requests allows 
        /// for up to 60 requests per hour, limited by IP address.
        /// </param>
        /// <param name="prerelease">
        /// If true, pre-releases will be also be searched / downloaded. If false, only
        /// stable releases will be considered.
        /// </param>
        /// <param name="downloader">
        /// The file downloader used to perform HTTP requests. 
        /// </param>
        public GithubSource(string repoUrl, string? accessToken, bool prerelease, IFileDownloader? downloader = null)
            : base(repoUrl, accessToken, prerelease, downloader)
        {
        }

        /// <inheritdoc />
        protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
        {
            // https://docs.github.com/en/rest/reference/releases
            const int perPage = 10;
            const int page = 1;
            var releasesPath = $"repos{RepoUri.AbsolutePath}/releases?per_page={perPage}&page={page}";
            var baseUri = GetApiBaseUrl(RepoUri);
            var getReleasesUri = new Uri(baseUri, releasesPath);
            var response = await Downloader.DownloadString(getReleasesUri.ToString(), Authorization, "application/vnd.github.v3+json").ConfigureAwait(false);
            var releases = SimpleJson.DeserializeObject<List<GithubRelease>>(response);
            if (releases == null) return new GithubRelease[0];
            return releases.OrderByDescending(d => d.PublishedAt).Where(x => includePrereleases || !x.Prerelease).ToArray();
        }

        /// <inheritdoc />
        protected override string GetAssetUrlFromName(GithubRelease release, string assetName)
        {
            if (release.Assets == null || release.Assets.Count() == 0) {
                throw new ArgumentException($"No assets found in Github Release '{release.Name}'.");
            }

            IEnumerable<GithubReleaseAsset> allReleasesFiles = release.Assets.Where(a => a.Name?.Equals(assetName, StringComparison.InvariantCultureIgnoreCase) == true);
            if (allReleasesFiles == null || allReleasesFiles.Count() == 0) {
                throw new ArgumentException($"Could not find asset called '{assetName}' in Github Release '{release.Name}'.");
            }

            var asset = allReleasesFiles.First();

            if (String.IsNullOrWhiteSpace(AccessToken) && asset.BrowserDownloadUrl != null) {
                // if no AccessToken provided, we use the BrowserDownloadUrl which does not 
                // count towards the "unauthenticated api request" limit of 60 per hour per IP.
                return asset.BrowserDownloadUrl;
            } else if (asset.Url != null) {
                // otherwise, we use the regular asset url, which will allow us to retrieve
                // assets from private repositories
                // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
                return asset.Url;
            } else {
                throw new ArgumentException("Could not find a valid asset url for the specified asset.");
            }
        }

        /// <summary>
        /// Given a repository URL (e.g. https://github.com/myuser/myrepo) this function
        /// returns the API base for performing requests. (eg. "https://api.github.com/" 
        /// or http://internal.github.server.local/api/v3)
        /// </summary>
        /// <param name="repoUrl"></param>
        /// <returns></returns>
        protected virtual Uri GetApiBaseUrl(Uri repoUrl)
        {
            Uri baseAddress;
            if (repoUrl.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase)) {
                baseAddress = new Uri("https://api.github.com/");
            } else {
                // if it's not github.com, it's probably an Enterprise server
                // now the problem with Enterprise is that the API doesn't come prefixed
                // it comes suffixed so the API path of http://internal.github.server.local
                // API location is http://internal.github.server.local/api/v3
                baseAddress = new Uri(string.Format("{0}{1}{2}/api/v3/", repoUrl.Scheme, Uri.SchemeDelimiter, repoUrl.Host));
            }
            // above ^^ notice the end slashes for the baseAddress, explained here: http://stackoverflow.com/a/23438417/162694
            return baseAddress;
        }
    }
}
