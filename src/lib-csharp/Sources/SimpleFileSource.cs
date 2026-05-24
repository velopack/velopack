using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;
using Velopack.NuGet;
using Velopack.Util;

namespace Velopack.Sources
{
    /// <summary>
    /// Retrieves available updates from a local or network-attached disk. The directory
    /// must contain one or more valid packages, as well as a 'releases.{channel}.json' index file.
    /// </summary>
    public class SimpleFileSource : IUpdateSource
    {
        /// <summary> The local directory containing packages to update to. </summary>
        public virtual DirectoryInfo BaseDirectory { get; }

        /// <inheritdoc cref="SimpleFileSource" />
        public SimpleFileSource(DirectoryInfo baseDirectory)
        {
            BaseDirectory = baseDirectory;
        }

        /// <inheritdoc />
        public virtual async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null,
            VelopackAsset? latestLocalRelease = null)
        {
            if (!BaseDirectory.Exists) {
                logger.Error($"The local update directory '{BaseDirectory.FullName}' does not exist.");
                return new VelopackAssetFeed();
            }

            // if a feed exists in the folder, let's use that.
            var feedLoc = Path.Combine(BaseDirectory.FullName, CoreUtil.GetVeloReleaseIndexName(channel));
            if (File.Exists(feedLoc)) {
                logger.Debug($"Found local file feed at '{feedLoc}'.");
                return VelopackAssetFeed.FromJson(File.ReadAllText(feedLoc));
            }

            logger.Warn($"No local feed found at '{feedLoc}', will search for *.nupkg in directory '{BaseDirectory.FullName}'.");

            // if not, we can try to iterate the packages, hopefully this is not a remote/network folder!
            var list = new List<VelopackAsset>();
            foreach (var pkg in Directory.EnumerateFiles(BaseDirectory.FullName, "*.nupkg")) {
                try {
                    var zip = new ZipPackage(pkg);
                    var asset = await VelopackAsset.FromZipPackageGenerateChecksumAsync(zip).ConfigureAwait(false);
                    if (asset?.Version != null) {
                        if (channel != null) {
                            // If requesting a specific channel, only include packages explicitly in that channel.
                            // Packages without channel metadata (e.g. legacy Squirrel releases) are excluded.
                            if (zip?.Channel == channel) {
                                logger.Debug($"Read package '{pkg}' with version '{asset.Version}' in channel '{zip?.Channel}'.");
                                list.Add(asset);
                            } else {
                                logger.Warn($"Skipping local package '{pkg}' because it is not in the '{channel}' channel.");
                            }
                        } else {
                            // If no specific channel requested, include all packages regardless of channel.
                            logger.Debug($"Read package '{pkg}' with version '{asset.Version}' in channel '{zip?.Channel}'.");
                            list.Add(asset);
                        }
                    }
                } catch (Exception ex) {
                    logger.Warn(ex, $"Error while reading local package '{pkg}'.");
                }
            }
            return new VelopackAssetFeed { Assets = list.ToArray() };
        }

        /// <inheritdoc />
        public virtual Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
        {
            var releasePath = Path.Combine(BaseDirectory.FullName, releaseEntry.FileName);
            if (!File.Exists(releasePath))
                throw new Exception($"The file '{releasePath}' does not exist. The packages directory is invalid.");

            File.Copy(releasePath, localFile, true);
            progress?.Invoke(100);
            return Task.CompletedTask;
        }
    }
}
