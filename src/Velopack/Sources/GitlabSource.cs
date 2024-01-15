using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack.Json;

namespace Velopack.Sources
{
    /// <summary>
    /// Describes a Gitlab release, plus any assets that are attached.
    /// </summary>
    [DataContract]
    public class GitlabRelease
    {
        /// <summary>
        /// The name of the release.
        /// </summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>
        /// True if this is intended for an upcoming release.
        /// </summary>
        [DataMember(Name = "upcoming_release")]
        public bool UpcomingRelease { get; set; }

        /// <summary>
        /// The date which this release was published publically.
        /// </summary>
        [DataMember(Name = "released_at")]
        public DateTime ReleasedAt { get; set; }

        /// <summary>
        /// A container for the assets (files) uploaded to this release.
        /// </summary>
        [DataMember(Name = "assets")]
        public GitlabReleaseAsset Assets { get; set; }
    }

    /// <summary>
    /// Describes a container for the assets attached to a release.
    /// </summary>
    [DataContract]
    public class GitlabReleaseAsset
    {
        /// <summary>
        /// The amount of assets linked to the release.
        /// </summary>
        [DataMember(Name = "count")]
        public int Count { get; set; }

        /// <summary>
        /// A list of asset (file) links.
        /// </summary>
        [DataMember(Name = "links")]
        public GitlabReleaseLink[] Links { get; set; }
    }

    /// <summary>
    /// Describes a container for the links of assets attached to a release.
    /// </summary>
    [DataContract]
    public class GitlabReleaseLink
    {
        /// <summary>
        /// Name of the asset (file) linked.
        /// </summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>
        /// The url for the asset. This make use of the Gitlab API.
        /// </summary>
        [DataMember(Name = "url")]
        public string Url { get; set; }

        /// <summary>
        /// A direct url to the asset, via a traditional URl. 
        /// As a posed to using the API.
        /// This links directly to the raw asset (file).
        /// </summary>
        [DataMember(Name = "direct_asset_url")]
        public string DirectAssetUrl { get; set; }

        /// <summary>
        /// The category type that the asset is listed under.
        /// Options: 'Package', 'Image', 'Runbook', 'Other'
        /// </summary>
        [DataMember(Name = "link_type")]
        public string Type { get; set; }
    }

    /// <summary>
    /// Provides a wrapper around <see cref="ReleaseEntry"/> which also contains a <see cref="GitlabRelease"/>.
    /// </summary>
    public class GitlabReleaseEntry : ReleaseEntry
    {
        /// <summary> The Github release which contains this release package. </summary>
        public GitlabRelease Release { get; }

        /// <inheritdoc cref="GitlabReleaseEntry"/>
        public GitlabReleaseEntry(ReleaseEntry entry, GitlabRelease release)
            : base(entry.SHA1, entry.OriginalFilename, entry.Filesize, entry.BaseUrl, entry.Query, entry.StagingPercentage)
        {
            Release = release;
        }
    }

    /// <summary>
    /// Retrieves available releases from a GitLab repository. This class only
    /// downloads assets from the very latest GitLab release.
    /// </summary>
    public class GitlabSource : IUpdateSource
    {
        /// <summary> 
        /// The URL of the GitLab repository to download releases from 
        /// (e.g. https://gitlab.com/api/v4/projects/ProjectId)
        /// </summary>
        public virtual Uri RepoUri { get; }

        /// <summary>  
        /// If true, the latest upcoming release will be downloaded. If false, the latest 
        /// stable release will be downloaded.
        /// </summary>
        public virtual bool UpcomingRelease { get; }

        /// <summary> 
        /// The file downloader used to perform HTTP requests. 
        /// </summary>
        public virtual IFileDownloader Downloader { get; }

        /// <summary>
        /// The GitLab access token to use with the request to download releases.
        /// </summary>
        protected virtual string AccessToken { get; }

        /// <summary>
        /// The Bearer token used in the request.
        /// </summary>
        protected virtual string Authorization => string.IsNullOrWhiteSpace(AccessToken) ? null : "Bearer " + AccessToken;

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
        public GitlabSource(string repoUrl, string accessToken, bool upcomingRelease, IFileDownloader downloader = null)
        {
            RepoUri = new Uri(repoUrl);
            AccessToken = accessToken;
            UpcomingRelease = upcomingRelease;
            Downloader = downloader ?? Utility.CreateDefaultDownloader();
        }

        /// <inheritdoc />
        public async Task DownloadReleaseEntry(ILogger logger, ReleaseEntry releaseEntry, string localFile, Action<int> progress)
        {
            if (releaseEntry is GitlabReleaseEntry githubEntry) {
                // this might be a browser url or an api url (depending on whether we have a AccessToken or not)
                // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
                var assetUrl = GetAssetUrlFromName(githubEntry.Release, releaseEntry.OriginalFilename);
                await Downloader.DownloadFile(assetUrl, localFile, progress, Authorization, "application/octet-stream").ConfigureAwait(false);
            }

            throw new ArgumentException($"Expected releaseEntry to be {nameof(GitlabReleaseEntry)} but got {releaseEntry.GetType().Name}.");
        }

        /// <inheritdoc />
        public async Task<ReleaseEntry[]> GetReleaseFeed(ILogger logger, string channel = null, Guid? stagingId = null, ReleaseEntryName latestLocalRelease = null)
        {
            var releases = await GetReleases(UpcomingRelease).ConfigureAwait(false);
            if (releases == null || releases.Count() == 0)
                throw new Exception($"No Gitlab releases found at '{RepoUri}'.");

            // for now, we only search for packages in the latest Github release.
            // in the future, we might want to search through more than one for delta's.
            var release = releases.First();

            var assetUrl = GetAssetUrlFromName(release, Utility.GetReleasesFileName(channel));
            var releaseBytes = await Downloader.DownloadBytes(assetUrl, Authorization, "application/octet-stream").ConfigureAwait(false);
            var txt = Utility.RemoveByteOrderMarkerIfPresent(releaseBytes);
            return ReleaseEntry.ParseReleaseFileAndApplyStaging(txt, stagingId)
                .Select(r => new GitlabReleaseEntry(r, release))
                .ToArray();
        }

        /// <summary>
        /// Given a <see cref="GitlabRelease"/> and an asset filename (eg. 'RELEASES') this 
        /// function will return either <see cref="GitlabReleaseLink.DirectAssetUrl"/> or
        /// <see cref="GitlabReleaseLink.Url"/>, depending whether an access token is available
        /// or not. Throws if the specified release has no matching assets.
        /// </summary>
        protected virtual string GetAssetUrlFromName(GitlabRelease release, string assetName)
        {
            if (release.Assets == null || release.Assets.Count == 0) {
                throw new ArgumentException($"No assets found in Gitlab Release '{release.Name}'.");
            }

            GitlabReleaseLink packageFile =
                release.Assets.Links.FirstOrDefault(a => a.Name.Equals(assetName, StringComparison.InvariantCultureIgnoreCase));
            if (packageFile == null) {
                throw new ArgumentException($"Could not find asset called '{assetName}' in GitLab Release '{release.Name}'.");
            }

            if (String.IsNullOrWhiteSpace(AccessToken)) {
                return packageFile.DirectAssetUrl;
            } else {
                return packageFile.Url;
            }
        }

        /// <summary>
        /// Retrieves a list of <see cref="GitlabRelease"/> from the current repository.
        /// </summary>
        public virtual async Task<GitlabRelease[]> GetReleases(bool includePrereleases, int perPage = 30, int page = 1)
        {
            // https://docs.gitlab.com/ee/api/releases/
            var releasesPath = $"{RepoUri.AbsolutePath}/releases?per_page={perPage}&page={page}";
            var baseUri = new Uri("https://gitlab.com");
            var getReleasesUri = new Uri(baseUri, releasesPath);
            var response = await Downloader.DownloadString(getReleasesUri.ToString(), Authorization).ConfigureAwait(false);
            var releases = SimpleJson.DeserializeObject<List<GitlabRelease>>(response);
            return releases.OrderByDescending(d => d.ReleasedAt).Where(x => includePrereleases || !x.UpcomingRelease).ToArray();
        }
    }
}
