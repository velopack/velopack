#nullable disable
using System;
using System.IO;
using System.Threading.Tasks;
using NuGet.Versioning;
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

        /// <summary> A delta update package. </summary>
        Delta,
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

        /// <summary> The SHA256 checksum (if availible) of the update package containing this release. </summary>
        public string SHA256 { get; set; }

        /// <summary> The size in bytes of the update package containing this release. </summary>
        public long Size { get; set; }

        /// <summary> The release notes in markdown format, as passed to Velopack when packaging the release. </summary>
        public string NotesMarkdown { get; set; }

        /// <summary> The release notes in HTML format, transformed from Markdown when packaging the release. </summary>
        public string NotesHTML { get; set; }

        // /// <summary>
        // /// Convert a <see cref="ZipPackage"/> to a <see cref="VelopackAsset"/>.
        // /// </summary>
        // public static async Task<VelopackAsset> FromZipPackage(ZipPackage zip)
        // {
        //     var filePath = zip.LoadedFromPath;
        //     return new VelopackAsset {
        //         PackageId = zip.Id,
        //         Version = zip.Version,
        //         NotesMarkdown = zip.ReleaseNotes,
        //         NotesHTML = zip.ReleaseNotesHtml,
        //         Size = new FileInfo(filePath).Length,
        //         SHA1 = await IoUtil.CalculateFileSHA1(filePath).ConfigureAwait(false),
        //         SHA256 = await IoUtil.CalculateFileSHA256(filePath).ConfigureAwait(false),
        //         FileName = Path.GetFileName(filePath),
        //         Type = IsDeltaFile(filePath) ? VelopackAssetType.Delta : VelopackAssetType.Full,
        //     };
        // }

        // /// <summary>
        // /// Load a <see cref="VelopackAsset"/> from a .nupkg file on disk.
        // /// </summary>
        // public static async Task<VelopackAsset> FromNupkg(string filePath)
        // {
        //     if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
        //     if (!filePath.EndsWith(NugetUtil.PackageExtension)) throw new ArgumentException("Must be a .nupkg file", nameof(filePath));
        //
        //     var zip = await ZipPackage.ReadManifestAsync(filePath).ConfigureAwait(false);
        //
        //     return new VelopackAsset {
        //         PackageId = zip.Id,
        //         Version = zip.Version,
        //         NotesMarkdown = zip.ReleaseNotes,
        //         NotesHTML = zip.ReleaseNotesHtml,
        //         Size = new FileInfo(filePath).Length,
        //         SHA1 = await IoUtil.CalculateFileSHA1(filePath).ConfigureAwait(false),
        //         SHA256 = await IoUtil.CalculateFileSHA256(filePath).ConfigureAwait(false),
        //         FileName = Path.GetFileName(filePath),
        //         Type = IsDeltaFile(filePath) ? VelopackAssetType.Delta : VelopackAssetType.Full,
        //     };
        // }
        
        /// <summary>
        /// Load a <see cref="VelopackAsset"/> from a file on disk plus an already loaded manifest.
        /// </summary>
        public static async Task<VelopackAsset> FromManifest(string filePath, PackageManifest manifest, bool computeChecksums)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
            if (!filePath.EndsWith(NugetUtil.PackageExtension)) throw new ArgumentException("Must be a .nupkg file", nameof(filePath));

            var asset = new VelopackAsset {
                PackageId = manifest.Id,
                Version = manifest.Version,
                NotesMarkdown = manifest.ReleaseNotes,
                NotesHTML = manifest.ReleaseNotesHtml,
                Size = new FileInfo(filePath).Length,
                FileName = Path.GetFileName(filePath),
                Type = IsDeltaFile(filePath) ? VelopackAssetType.Delta : VelopackAssetType.Full,
            };

            if (computeChecksums) {
                asset.SHA1 = await IoUtil.CalculateFileSHA1(filePath).ConfigureAwait(false);
                asset.SHA256 = await IoUtil.CalculateFileSHA256(filePath).ConfigureAwait(false);
            }

            return asset;
        }

        /// <summary>
        /// Load a <see cref="VelopackAsset"/> from a .nupkg file on disk.
        /// </summary>
        public static async Task<VelopackAsset> FromZip(string filePath, bool computeChecksums)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
            if (!filePath.EndsWith(NugetUtil.PackageExtension)) throw new ArgumentException("Must be a .nupkg file", nameof(filePath));

            var zip = await ZipPackage.ReadManifestAsync(filePath).ConfigureAwait(false);
            return await FromManifest(filePath, zip, computeChecksums).ConfigureAwait(false);
        }

        internal static bool IsDeltaFile(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath).EndsWith("-delta", StringComparison.OrdinalIgnoreCase);
        }
    }
}