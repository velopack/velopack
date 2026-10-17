using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Sources
{
    /// <summary>
    /// Base class to provide some shared implementation between sources which download releases from a Git repository.
    /// </summary>
    public abstract class GitBase<T> : IUpdateSource, IRangeReadableUpdateSource
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
        /// The Bearer or other type of Authorization header used to authenticate against the Api.
        /// </summary>
        protected abstract KeyValuePair<string, string>? Authorization { get; }

        /// <summary>
        /// Base constructor.
        /// </summary>
        protected GitBase(string repoUrl, string? accessToken, bool prerelease, IFileDownloader? downloader = null)
        {
            RepoUri = new Uri(repoUrl.TrimEnd('/'));
            AccessToken = accessToken;
            Prerelease = prerelease;
            Downloader = downloader ?? HttpUtil.CreateDefaultDownloader();
        }

        /// <inheritdoc />
        public virtual Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress,
            CancellationToken cancelToken)
        {
            if (releaseEntry is GitBaseAsset githubEntry) {
                // this might be a browser url or an api url (depending on whether we have a AccessToken or not)
                // https://docs.github.com/en/rest/reference/releases#get-a-release-asset
                var assetUrl = GetAssetUrlFromName(githubEntry.Release, releaseEntry.FileName);
                return Downloader.DownloadFile(
                    assetUrl,
                    localFile,
                    progress,
                    GetRequestHeaders("application/octet-stream"),
                    cancelToken: cancelToken);
            }

            throw new ArgumentException($"Expected releaseEntry to be {nameof(GitBaseAsset)} but got {releaseEntry.GetType().Name}.");
        }

        /// <inheritdoc />
        public virtual Task<IRangeReader?> OpenRangeReaderAsync(IVelopackLogger logger, VelopackAsset asset, CancellationToken cancelToken)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            if (asset is not GitBaseAsset gitAsset) {
                logger.Debug($"Asset is not a {nameof(GitBaseAsset)}, ranged reads are not available.");
                return Task.FromResult<IRangeReader?>(null);
            }

            if (Downloader is not IRangeFileDownloader rangeDownloader) {
                logger.Debug("The downloader does not support ranged reads.");
                return Task.FromResult<IRangeReader?>(null);
            }

            // the same url + headers DownloadReleaseEntry uses
            var assetUrl = GetAssetUrlFromName(gitAsset.Release, asset.FileName);
            logger.Info($"Opening ranged reader for '{asset.FileName}' at '{assetUrl}'.");
            return Task.FromResult<IRangeReader?>(rangeDownloader.OpenRangeReader(assetUrl, GetRequestHeaders("application/octet-stream"), 30));
        }

        /// <inheritdoc />
        public virtual async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null,
            VelopackAsset? latestLocalRelease = null)
        {
            var releases = await GetReleases(Prerelease).ConfigureAwait(false);
            if (releases == null || releases.Length == 0) {
                logger.Warn($"No releases found at '{RepoUri}'.");
                return new VelopackAssetFeed();
            }

            var releasesFileName = CoreUtil.GetVeloReleaseIndexName(channel);
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

                var releaseBytes = await Downloader.DownloadBytes(
                    assetUrl,
                    GetRequestHeaders("application/octet-stream")
                ).ConfigureAwait(false);
                var txt = CoreUtil.RemoveByteOrderMarkerIfPresent(releaseBytes);
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
        /// Constructs a dictionary containing HTTP request headers.
        /// </summary>
        /// <param name="accept">The value for the "Accept" header; defaults to "application/json".</param>
        /// <returns>A dictionary of headers including "Accept" and, if available, authorization headers.</returns>
        protected virtual Dictionary<string, string> GetRequestHeaders(string accept = "application/json")
        {
            var headers = new Dictionary<string, string> {
                ["Accept"] = accept,
            };
            if (Authorization.HasValue) {
                headers.Add(Authorization.Value.Key, Authorization.Value.Value);
            }

            return headers;
        }

        /// <summary>
        /// Provides a wrapper around <see cref="VelopackAsset"/> which also contains a Git Release.
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
                SHA256 = entry.SHA256;
                Size = entry.Size;
                NotesMarkdown = entry.NotesMarkdown;
                NotesHTML = entry.NotesHTML;
                ChunkIndexOffset = entry.ChunkIndexOffset;
                ChunkIndexLength = entry.ChunkIndexLength;
                ChunkIndexSHA256 = entry.ChunkIndexSHA256;
            }
        }
    }
}