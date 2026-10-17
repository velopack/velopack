#nullable disable
namespace Velopack
{
    /// <summary>
    /// The completion marker (<c>ready.json</c>) of a staged update tree assembled by the chunk update path.
    /// A staged directory without a valid marker is never applied.
    /// </summary>
    public sealed class StagedReadyMarker
    {
        /// <summary> The marker format version written by this library. </summary>
        public const int CurrentFormat = 1;

        /// <summary> The marker format version. </summary>
        public int Format { get; set; }

        /// <summary>
        /// The staged tree identity: the first 32 hex characters of <see cref="IndexSHA256"/>, lower-case. Equals the directory name.
        /// </summary>
        public string StagedId { get; set; }

        /// <summary>
        /// The SHA-256 (upper-case hex) of the chunk index trailer the tree was assembled from; equals the asset's ChunkIndexSHA256.
        /// </summary>
        public string IndexSHA256 { get; set; }

        /// <summary> The SHA-256 (upper-case hex) of the staged <c>Update.exe</c> sidecar. </summary>
        public string UpdaterSHA256 { get; set; }

        /// <summary> The release asset the tree was assembled for, including its chunk index pointer. </summary>
        public VelopackAsset Asset { get; set; }

        /// <summary> When the marker was written, as an ISO 8601 UTC timestamp (eg. <c>2026-10-17T12:00:00Z</c>). </summary>
        public string CreatedUtc { get; set; }
    }
}
