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
    /// <summary> Describes a Gitea release, including attached assets. </summary>
    public class GiteaRelease
    {
        /// <summary> The name of this release. </summary>
        [JsonName("name")]
        public string? Name { get; set; }

        /// <summary> True if this release is a prerelease. </summary>
        [JsonName("prerelease")]
        public bool Prerelease { get; set; }

        /// <summary> The date which this release was published publicly. </summary>
        [JsonName("published_at")]
        public DateTime? PublishedAt { get; set; }

        /// <summary> A list of assets (files) uploaded to this release. </summary>
        [JsonName("assets")]
        public GiteaReleaseAsset[] Assets { get; set; } = new GiteaReleaseAsset[0];
    }
    /// <summary> Describes a asset (file) uploaded to a Gitea release. </summary>
    public class GiteaReleaseAsset
    {
        /// <summary> 
        /// The asset URL for this release asset. Requests to this URL will use API
        /// quota and return JSON unless the 'Accept' header is "application/octet-stream". 
        /// </summary>
        [JsonName("url")]
        public string? Url { get; set; }

        /// <summary>  
        /// The browser URL for this release asset. This does not use API quota,
        /// however this URL only works for public repositories. If downloading
        /// assets from a private repository, the <see cref="Url"/> property must
        /// be used with an appropriate access token.
        /// </summary>
        [JsonName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }

        /// <summary> The name of this release asset. </summary>
        [JsonName("name")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// Retrieves available releases from a Gitea repository.
    /// </summary>
    public class GiteaSource : GitBase<GiteaRelease>
    {
        /// <inheritdoc cref="GiteaSource" />
        /// <param name="repoUrl">
        /// The URL of the Gitea repository to download releases from 
        /// (e.g. https://Gitea.com/myuser/myrepo)
        /// </param>
        /// <param name="accessToken">
        /// The Gitea access token to use with the request to download releases. 
        /// If left empty, the Gitea rate limit for unauthenticated requests allows 
        /// for up to 60 requests per hour, limited by IP address.
        /// </param>
        /// <param name="prerelease">
        /// If true, pre-releases will be also be searched / downloaded. If false, only
        /// stable releases will be considered.
        /// </param>
        /// <param name="downloader">
        /// The file downloader used to perform HTTP requests. 
        /// </param>
        public GiteaSource(string repoUrl, string? accessToken, bool prerelease, IFileDownloader? downloader = null)
            : base(repoUrl, accessToken, prerelease, downloader)
        {
        }
        
        /// <inheritdoc cref="Authorization"/>
        protected override (string Name, string Value)? Authorization => 
            string.IsNullOrEmpty(AccessToken) ? null : ("Authorization", $"token {AccessToken}");

        /// <inheritdoc />
        protected override async Task<GiteaRelease[]> GetReleases(bool includePrereleases)
        {
            // https://gitea.com/api/swagger#/repository/repoListReleases
            // https://docs.gitea.com/api/1.22/#tag/repository/operation/repoListReleases

            const int perPage = 10;
            const int page = 1;
            var releasesPath = $"repos{RepoUri.AbsolutePath}/releases?limit={perPage}&page={page}&draft=false";
            var baseUri = GetApiBaseUrl(RepoUri);
            var getReleasesUri = new Uri(baseUri, releasesPath);
            var response = await Downloader.DownloadString(getReleasesUri.ToString(), GetRequestHeaders()).ConfigureAwait(false);
            var releases = CompiledJson.DeserializeGiteaReleaseList(response);
            if (releases == null) return Array.Empty<GiteaRelease>();
            return releases.OrderByDescending(d => d.PublishedAt).Where(x => includePrereleases || !x.Prerelease).ToArray();
        }

        /// <inheritdoc />
        protected override string GetAssetUrlFromName(GiteaRelease release, string assetName)
        {
            if (release.Assets == null || release.Assets.Count() == 0) {
                throw new ArgumentException($"No assets found in Gitea Release '{release.Name}'.");
            }

            IEnumerable<GiteaReleaseAsset> allReleasesFiles = release.Assets.Where(a => a.Name?.Equals(assetName, StringComparison.InvariantCultureIgnoreCase) == true);
            if (allReleasesFiles == null || allReleasesFiles.Count() == 0) {
                throw new ArgumentException($"Could not find asset called '{assetName}' in Gitea Release '{release.Name}'.");
            }

            var asset = allReleasesFiles.First();

            if (asset.BrowserDownloadUrl != null) {
                return asset.BrowserDownloadUrl;
            } else {
                throw new ArgumentException("Could not find a valid asset url for the specified asset.");
            }
        }

        /// <summary>
        /// Given a repository URL (e.g. https://Gitea.com/myuser/myrepo) this function
        /// returns the API base for performing requests. (eg. "https://api.Gitea.com/" 
        /// or http://internal.Gitea.server.local/api/v1)
        /// or http://localhost:3000/api/v1
        /// </summary>
        /// <param name="repoUrl"></param>
        /// <returns></returns>
        protected virtual Uri GetApiBaseUrl(Uri repoUrl)
        {
            Uri baseAddress;

            if (repoUrl.IsDefaultPort) {
                baseAddress = new Uri(string.Format("{0}{1}{2}/api/v1/", repoUrl.Scheme, Uri.SchemeDelimiter, repoUrl.Host));
                // above ^^ notice the end slashes for the baseAddress, explained here: http://stackoverflow.com/a/23438417/162694
            } else {
                baseAddress = new Uri(string.Format("{0}{1}{2}:{3}/api/v1/", repoUrl.Scheme, Uri.SchemeDelimiter, repoUrl.Host, repoUrl.Port));
            }

            return baseAddress;
        }
    }
}