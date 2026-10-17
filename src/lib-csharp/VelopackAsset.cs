#nullable disable
using System;
using System.IO;
using System.Threading.Tasks;
using Velopack.Chunks;
using Velopack.Util;
using Velopack.NuGet;

namespace Velopack
{
    /// <summary>
    /// Represents a Velopack Asset Type.
    /// </summary>
    public enum VelopackAssetType
    {
        /// <summary> A full update package. </summary>
        Full = 1,
        /// <summary> A legacy delta update package. Still recognised in existing feeds, but ignored by update clients. </summary>
        Delta = 2,
        /// <summary> A portable application zip archive. </summary>
        Portable = 3,
        /// <summary> An application installer archive. </summary>
        Installer = 4,
        /// <summary> A Windows Installer package (.msi).</summary>
        Msi = 5,
    }

    /// <summary>
    /// A feed of Velopack assets, usually returned from an <see cref="Velopack.Sources.IUpdateSource"/>.
    /// </summary>
    public record VelopackAssetFeed
    {
        /// <summary>
        /// A list of assets available in this feed.
        /// </summary>
        public VelopackAsset[] Assets { get; set; } = Array.Empty<VelopackAsset>();

        /// <summary>
        /// Parse a json string into a <see cref="VelopackAssetFeed"/>.
        /// </summary>
        public static VelopackAssetFeed FromJson(string json)
        {
            return CompiledJson.DeserializeVelopackAssetFeed(json) ?? new VelopackAssetFeed();
        }
    }

    /// <summary>
    /// An individual Velopack asset, could refer to an asset on-disk or in a remote package feed.
    /// </summary>
    public record VelopackAsset
    {
        /// <summary> The name or Id of the package containing this release. </summary>
        public string PackageId { get; set; }

        /// <summary> The version of this release. </summary>
        public SemanticVersion Version { get; set; }

        /// <summary> The type of asset (eg. full or delta). </summary>
        public VelopackAssetType Type { get; set; }

        /// <summary> The filename of the update package containing this release. </summary>
        public string FileName { get; set; }

        /// <summary> The SHA1 checksum of the update package containing this release. </summary>
        public string SHA1 { get; set; }

        /// <summary> The SHA256 checksum (if available) of the update package containing this release. </summary>
        public string SHA256 { get; set; }

        /// <summary> The size in bytes of the update package containing this release. </summary>
        public long Size { get; set; }

        /// <summary> The release notes in markdown format, as passed to Velopack when packaging the release. </summary>
        public string NotesMarkdown { get; set; }

        /// <summary> The release notes in HTML format, transformed from Markdown when packaging the release. </summary>
        public string NotesHTML { get; set; }

        /// <summary>
        /// For packages carrying a chunk index (used for chunk-based updates): the byte offset of the index trailer
        /// inside the package file. Null for packages without one.
        /// </summary>
        public long? ChunkIndexOffset { get; set; }

        /// <summary> The byte length of the chunk index trailer, or null for packages without one. </summary>
        public long? ChunkIndexLength { get; set; }

        /// <summary> The SHA256 checksum (upper-case hex) of the chunk index trailer bytes, or null for packages without one. </summary>
        public string ChunkIndexSHA256 { get; set; }

        /// <summary> True when this asset carries a chunk index pointer. </summary>
        internal bool HasChunkIndex => ChunkIndexLength > 0 && !string.IsNullOrEmpty(ChunkIndexSHA256);

        /// <summary>
        /// Returns a plain <see cref="VelopackAsset"/> carrying exactly the feed fields of this asset, so that derived
        /// assets (eg. git release wrappers) never serialise their extra payload into <c>asset.json</c> / <c>ready.json</c>.
        /// </summary>
        internal VelopackAsset ToPlainAsset()
        {
            return new VelopackAsset {
                PackageId = PackageId,
                Version = Version,
                Type = Type,
                FileName = FileName,
                SHA1 = SHA1,
                SHA256 = SHA256,
                Size = Size,
                NotesMarkdown = NotesMarkdown,
                NotesHTML = NotesHTML,
                ChunkIndexOffset = ChunkIndexOffset,
                ChunkIndexLength = ChunkIndexLength,
                ChunkIndexSHA256 = ChunkIndexSHA256,
            };
        }

        /// <summary>
        /// Convert a <see cref="ZipPackage"/> to a <see cref="VelopackAsset"/> without computing checksums.
        /// </summary>
        public static VelopackAsset FromZipPackageNoChecksum(ZipPackage zip)
        {
            var filePath = zip.LoadedFromPath;
            var asset = new VelopackAsset {
                PackageId = zip.Id,
                Version = zip.Version,
                NotesMarkdown = zip.ReleaseNotes,
                NotesHTML = zip.ReleaseNotesHtml,
                Size = new FileInfo(filePath).Length,
                FileName = Path.GetFileName(filePath),
                Type = IsDeltaFile(filePath) ? VelopackAssetType.Delta : VelopackAssetType.Full,
            };
            FillChunkIndexPointer(asset, filePath);
            return asset;
        }

        /// <summary>
        /// Locates the chunk index trailer entry (<see cref="ChunkIndex.TrailerEntryName"/>) in the package and records
        /// its offset, length and SHA256. Packages without the entry are left untouched.
        /// </summary>
        private static void FillChunkIndexPointer(VelopackAsset asset, string filePath)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
            var entry = ZipCentralDirectory.FindEntryData(stream, ChunkIndex.TrailerEntryName);
            if (entry == null) return;
            if (entry.Length <= 0 || entry.Length > ChunkIndex.MaxTrailerLength) {
                throw new InvalidDataException($"Chunk index trailer in '{filePath}' has an invalid length ({entry.Length}).");
            }
            stream.Seek(entry.Offset, SeekOrigin.Begin);
            asset.ChunkIndexOffset = entry.Offset;
            asset.ChunkIndexLength = entry.Length;
            asset.ChunkIndexSHA256 = ChunkInflate.Sha256(stream, entry.Length).ToHex();
        }

        /// <summary>
        /// Convert a <see cref="ZipPackage"/> to a <see cref="VelopackAsset"/>, computing SHA1 and SHA256 checksums asynchronously.
        /// </summary>
        public static async Task<VelopackAsset> FromZipPackageGenerateChecksumAsync(ZipPackage zip)
        {
            var asset = FromZipPackageNoChecksum(zip);
            var (sha1, sha256) = await IoUtil.CalculateFileSHA1AndSHA256Async(zip.LoadedFromPath).ConfigureAwait(false);
            return asset with { SHA1 = sha1, SHA256 = sha256 };
        }

        /// <summary>
        /// Load a <see cref="VelopackAsset"/> from a .nupkg file on disk without computing checksums.
        /// </summary>
        public static VelopackAsset FromNupkgNoChecksum(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
            if (!filePath.EndsWith(NugetUtil.PackageExtension)) throw new ArgumentException("Must be a .nupkg file", nameof(filePath));
            return FromZipPackageNoChecksum(new ZipPackage(filePath));
        }

        /// <summary>
        /// Load a <see cref="VelopackAsset"/> from a .nupkg file on disk, computing SHA1 and SHA256 checksums asynchronously.
        /// </summary>
        public static async Task<VelopackAsset> FromNupkgGenerateChecksumAsync(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
            if (!filePath.EndsWith(NugetUtil.PackageExtension)) throw new ArgumentException("Must be a .nupkg file", nameof(filePath));
            return await FromZipPackageGenerateChecksumAsync(new ZipPackage(filePath)).ConfigureAwait(false);
        }

        internal static bool IsDeltaFile(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath).EndsWith("-delta", StringComparison.OrdinalIgnoreCase);
        }
    }
}
