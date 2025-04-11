#nullable disable
using System;
using System.IO;
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
        /// Convert a <see cref="ZipPackage"/> to a <see cref="VelopackAsset"/>.
        /// </summary>
        public static VelopackAsset FromZipPackage(ZipPackage zip)
        {
            var filePath = zip.LoadedFromPath;
            return new VelopackAsset {
                PackageId = zip.Id,
                Version = zip.Version,
                NotesMarkdown = zip.ReleaseNotes,
                NotesHTML = zip.ReleaseNotesHtml,
                Size = new FileInfo(filePath).Length,
                SHA1 = IoUtil.CalculateFileSHA1(filePath),
                SHA256 = IoUtil.CalculateFileSHA256(filePath),
                FileName = Path.GetFileName(filePath),
                Type = IsDeltaFile(filePath) ? VelopackAssetType.Delta : VelopackAssetType.Full,
            };
        }

        /// <summary>
        /// Load a <see cref="VelopackAsset"/> from a .nupkg file on disk.
        /// </summary>
        public static VelopackAsset FromNupkg(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
            if (!filePath.EndsWith(NugetUtil.PackageExtension)) throw new ArgumentException("Must be a .nupkg file", nameof(filePath));
            return FromZipPackage(new ZipPackage(filePath));
        }

        internal static bool IsDeltaFile(string filePath)
        {
            return Path.GetFileNameWithoutExtension(filePath).EndsWith("-delta", StringComparison.OrdinalIgnoreCase);
        }
    }
}
