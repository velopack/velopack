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
    public class SimpleFileSource : IUpdateSource, IRangeReadableUpdateSource
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

        /// <inheritdoc />
        public virtual Task<IRangeReader?> OpenRangeReaderAsync(IVelopackLogger logger, VelopackAsset asset, CancellationToken cancelToken)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            var releasePath = Path.Combine(BaseDirectory.FullName, asset.FileName);
            if (!File.Exists(releasePath)) {
                logger.Warn($"The file '{releasePath}' does not exist, ranged reads are not available.");
                return Task.FromResult<IRangeReader?>(null);
            }

            return Task.FromResult<IRangeReader?>(new FileRangeReader(releasePath));
        }

        /// <summary>Byte-range access to a local file.</summary>
        private sealed class FileRangeReader : IRangeReader
        {
            private readonly string _path;
            private long _length;

            public FileRangeReader(string path)
            {
                _path = path;
                _length = -1;
            }

            public long Length => Interlocked.Read(ref _length);

            public async Task ReadExactAsync(long offset, byte[] buffer, int bufferOffset, int count, CancellationToken cancelToken)
            {
                if (buffer == null) throw new ArgumentNullException(nameof(buffer));
                if (bufferOffset < 0 || count < 0 || buffer.Length - bufferOffset < count) throw new ArgumentOutOfRangeException(nameof(count));
                cancelToken.ThrowIfCancellationRequested();
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
                Interlocked.Exchange(ref _length, fs.Length);
                if (offset < 0 || offset + count > fs.Length) {
                    throw new RangeUnavailableException($"Range {offset}+{count} is outside the file ({fs.Length} bytes).");
                }

                fs.Seek(offset, SeekOrigin.Begin);
                int read = 0;
                while (read < count) {
                    int n = await fs.ReadAsync(buffer, bufferOffset + read, count - read, cancelToken).ConfigureAwait(false);
                    if (n <= 0) throw new IOException("The file ended before the requested range was read.");
                    read += n;
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
