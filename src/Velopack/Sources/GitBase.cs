using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Velopack.Sources
{
    /// <summary>
    /// Base class to provide some shared implementation between sources which download releases from a Git repository.
    /// </summary>
    public abstract class GitBase<T> : IUpdateSource
    {
        /// <summary> 
        /// The URL of the repository to download releases from.
        /// </summary>
        public virtual Uri RepoUri { get; }

        /// <summary>  
        /// If true, the latest upcoming/prerelease release will be downloaded. If false, the latest 
        /// stable release will be downloaded.
        /// </summary>
        public virtual bool Prerelease { get; }

        /// <summary> 
        /// The file downloader used to perform HTTP requests. 
        /// </summary>
        public virtual IFileDownloader Downloader { get; }

        /// <summary>
        /// The GitLab access token to use with the request to download releases.
        /// </summary>
        protected virtual string? AccessToken { get; }

        /// <summary>
        /// The Bearer token used in the request.
        /// </summary>
        protected virtual string? Authorization => string.IsNullOrWhiteSpace(AccessToken) ? null : "Bearer " + AccessToken;

        /// <inheritdoc />
        public GitBase(string repoUrl, string? accessToken, bool prerelease, IFileDownloader? downloader = null)
        {
            RepoUri = new Uri(repoUrl);
            AccessToken = accessToken;
            Prerelease = prerelease;
            Downloader = downloader ?? Utility.CreateDefaultDownloader();
        }

        /// <inheritdoc />
        public virtual Task DownloadReleaseEntry(ILogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
        {
            if (releaseEntry is GitBaseAsset githubEntry) {
                // this might be a browser url or an api url (depending on whether we have a AccessToken or not)
                // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
                var assetUrl = GetAssetUrlFromName(githubEntry.Release, releaseEntry.FileName);
                return Downloader.DownloadFile(assetUrl, localFile, progress, Authorization, "application/octet-stream", cancelToken);
            }

            throw new ArgumentException($"Expected releaseEntry to be {nameof(GitBaseAsset)} but got {releaseEntry.GetType().Name}.");
        }

        /// <inheritdoc />
        public virtual async Task<VelopackAssetFeed> GetReleaseFeed(ILogger logger, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            var releases = await GetReleases(Prerelease).ConfigureAwait(false);
            if (releases == null || releases.Count() == 0) {
                logger.Warn($"No releases found at '{RepoUri}'.");
                return new VelopackAssetFeed();
            }

            var releasesFileName = Utility.GetVeloReleaseIndexName(channel);
            List<GitBaseAsset> entries = new List<GitBaseAsset>();

            foreach (var r in releases) {
                // this might be a browser url or an api url (depending on whether we have a AccessToken or not)
                // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
                string assetUrl;
                try {
                    assetUrl = GetAssetUrlFromName(r, releasesFileName);
                } catch (Exception ex) {
                    logger.Trace(ex.ToString());
                    continue;
                }
                var releaseBytes = await Downloader.DownloadBytes(assetUrl, Authorization, "application/octet-stream").ConfigureAwait(false);
                var txt = Utility.RemoveByteOrderMarkerIfPresent(releaseBytes);
                var feed = VelopackAssetFeed.FromJson(txt);
                foreach (var f in feed.Assets) {
                    entries.Add(new GitBaseAsset(f, r));
                }
            }

            return new VelopackAssetFeed {
                Assets = entries.Cast<VelopackAsset>().ToArray(),
            };
        }

        /// <summary>
        /// Retrieves a list of <see cref="GithubRelease"/> from the current repository.
        /// </summary>
        protected abstract Task<T[]> GetReleases(bool includePrereleases);

        /// <summary>
        /// Given a <see cref="GithubRelease"/> and an asset filename (eg. 'RELEASES') this 
        /// function will return either <see cref="GithubReleaseAsset.BrowserDownloadUrl"/> or
        /// <see cref="GithubReleaseAsset.Url"/>, depending whether an access token is available
        /// or not. Throws if the specified release has no matching assets.
        /// </summary>
        protected abstract string GetAssetUrlFromName(T release, string assetName);

        /// <summary>
        /// Provides a wrapper around <see cref="ReleaseEntry"/> which also contains a Git Release.
        /// </summary>
        protected internal record GitBaseAsset : VelopackAsset
        {
            /// <summary> The Github release which contains this release package. </summary>
            public T Release { get; init; }

            /// <inheritdoc cref="GitBaseAsset"/>
            public GitBaseAsset(VelopackAsset entry, T release)
            {
                Release = release;
                PackageId = entry.PackageId;
                Version = entry.Version;
                Type = entry.Type;
                FileName = entry.FileName;
                SHA1 = entry.SHA1;
                Size = entry.Size;
                NotesMarkdown = entry.NotesMarkdown;
                NotesHTML = entry.NotesHTML;
            }
        }
    }
}
