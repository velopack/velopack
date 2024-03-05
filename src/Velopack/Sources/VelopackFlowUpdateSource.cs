using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack.Json;
using Velopack.Locators;

namespace Velopack.Sources
{
    /// <summary>
    /// Retrieves updates from the hosted Velopack service. 
    /// </summary>
    public sealed class VelopackFlowUpdateSource : IUpdateSource
    {
        /// <inheritdoc cref="SimpleWebSource" />
        public VelopackFlowUpdateSource(
            string baseUri = "https://api.velopack.io/",
            IFileDownloader? downloader = null)
        {
            BaseUri = new Uri(baseUri);
            Downloader = downloader ?? Utility.CreateDefaultDownloader();
        }

        /// <summary> The URL of the server hosting packages to update to. </summary>
        public Uri BaseUri { get; }

        /// <summary> The <see cref="IFileDownloader"/> to be used for performing http requests. </summary>
        public IFileDownloader Downloader { get; }

        /// <inheritdoc />
        public async Task<VelopackAssetFeed> GetReleaseFeed(ILogger logger, string channel, Guid? stagingId = null, 
            VelopackAsset? latestLocalRelease = null)
        {
            Uri baseUri = new(BaseUri, $"api/v1.0/manifest/");
            var uri = Utility.AppendPathToUri(baseUri, Utility.GetVeloReleaseIndexName(channel));
            var args = new Dictionary<string, string>();

            if (VelopackRuntimeInfo.SystemArch != RuntimeCpu.Unknown) {
                args.Add("arch", VelopackRuntimeInfo.SystemArch.ToString());
            }

            if (VelopackRuntimeInfo.SystemOs != RuntimeOs.Unknown) {
                args.Add("os", VelopackRuntimeInfo.SystemOs.GetOsShortName());
                args.Add("rid", VelopackRuntimeInfo.SystemRid);
            }

            if (latestLocalRelease != null) {
                args.Add("id", latestLocalRelease.PackageId);
                args.Add("localVersion", latestLocalRelease.Version.ToString());
            } else {
                args.Add("id", VelopackLocator.GetDefault(logger).AppId ?? "");
            }

            var uriAndQuery = Utility.AddQueryParamsToUri(uri, args);

            logger.Info($"Downloading releases from '{uriAndQuery}'.");

            var json = await Downloader.DownloadString(uriAndQuery.ToString()).ConfigureAwait(false);

            var releaseAssets = SimpleJson.DeserializeObject<VelopackReleaseAsset[]>(json);
            return new VelopackAssetFeed() {
                Assets = releaseAssets
            };
        }

        /// <inheritdoc />
        public async Task DownloadReleaseEntry(ILogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
        {
            if (releaseEntry is null) throw new ArgumentNullException(nameof(releaseEntry));
            if (releaseEntry is not VelopackReleaseAsset velopackRelease) {
                throw new ArgumentException($"Expected {nameof(releaseEntry)} to be {nameof(VelopackReleaseAsset)} but was {releaseEntry.GetType().FullName}");
            }
            if (localFile is null) throw new ArgumentNullException(nameof(localFile));

            Uri sourceBaseUri = Utility.EnsureTrailingSlash(BaseUri);

            Uri downloadUri = new(sourceBaseUri, $"api/v1.0/download/{velopackRelease.Id}");

            logger.Info($"Downloading '{releaseEntry.FileName}' from '{downloadUri}'.");
            await Downloader.DownloadFile(downloadUri.AbsoluteUri, localFile, progress, cancelToken: cancelToken).ConfigureAwait(false);
        }
    }

    internal record VelopackReleaseAsset : VelopackAsset
    {
        public string? Id { get; init; }
    }
}
