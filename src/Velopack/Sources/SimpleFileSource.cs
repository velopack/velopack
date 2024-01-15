using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Velopack.NuGet;

namespace Velopack.Sources
{
    /// <summary>
    /// Retrieves available updates from a local or network-attached disk. The directory
    /// must contain one or more valid packages, as well as a 'RELEASES' index file.
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
        public Task<VelopackAssetFeed> GetReleaseFeed(ILogger logger, string channel, Guid? stagingId = null, VelopackAsset latestLocalRelease = null)
        {
            if (!BaseDirectory.Exists) {
                logger.Error($"The local update directory '{BaseDirectory.FullName}' does not exist.");
                return Task.FromResult(new VelopackAssetFeed());
            }

            var assets = Directory.EnumerateFiles(BaseDirectory.FullName, "*.nupkg")
               .Select(x => new ZipPackage(x))
               .Where(x => x?.Version != null)
               .Where(x => x.Channel == null || x.Channel == channel)
               .Select(x => VelopackAsset.FromZipPackage(x))
               .ToList();

            return Task.FromResult(new VelopackAssetFeed { Assets = assets });
        }

        /// <inheritdoc />
        public Task DownloadReleaseEntry(ILogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress)
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
