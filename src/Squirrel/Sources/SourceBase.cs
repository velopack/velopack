using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Squirrel.Sources
{
    /// <summary>
    /// A base class to provide some common functionality for classes that implement <see cref="IUpdateSource"/>.
    /// </summary>
    public abstract class SourceBase : IUpdateSource
    {
        /// <summary> The release channel to search for packages for. Can be null (default channel). </summary>
        public string Channel { get; }

        /// <summary> Microsoft logging instance to use for diagnostic messages. </summary>
        protected ILogger Log { get; }

        /// <inheritdoc cref="SourceBase" />
        public SourceBase(string channel, ILogger logger)
        {
            Log = logger ?? NullLogger.Instance;
            Channel = channel;
        }

        /// <summary> Get the RELEASES file name for the specified Channel </summary>
        protected virtual string GetReleasesFileName() => String.IsNullOrWhiteSpace(Channel) ? "RELEASES" : $"RELEASES-{Channel}";

        /// <inheritdoc/>
        public abstract Task<ReleaseEntry[]> GetReleaseFeed(Guid? stagingId = null, ReleaseEntry latestLocalRelease = null);

        /// <inheritdoc/>
        public abstract Task DownloadReleaseEntry(ReleaseEntry releaseEntry, string localFile, Action<int> progress);
    }
}
