using System;
using System.Collections.Generic;
using System.IO;
using NuGet.Versioning;
using Velopack.Json;
using Velopack.NuGet;

namespace Velopack
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public enum VelopackAssetType
    {
        FullPackage = 1,
        DeltaPackage,
        Portable,
        Setup,
    }

    public class VelopackAssetFeed
    {
        public List<VelopackAsset> Assets { get; set; } = new();

        public static VelopackAssetFeed FromJson(string json)
        {
            return SimpleJson.DeserializeObject<VelopackAssetFeed>(json);
        }
    }

    public record VelopackAsset
    {
        public string PackageId { get; init; }
        public SemanticVersion Version { get; init; }
        public VelopackAssetType Type { get; init; }
        public string FileName { get; init; }
        public string SHA1 { get; init; }
        public long? Size { get; init; }
        public string NotesMarkdown { get; init; }
        public string NotesHTML { get; init; }

        public static VelopackAsset FromZipPackage(ZipPackage zip)
        {
            var filePath = zip.LoadedFromPath;
            return new VelopackAsset {
                PackageId = zip.Id,
                Version = zip.Version,
                NotesMarkdown = zip.ReleaseNotes,
                NotesHTML = zip.ReleaseNotesHtml,
                Size = new FileInfo(filePath).Length,
                SHA1 = Utility.CalculateFileSHA1(filePath),
                FileName = Path.GetFileName(filePath),
                Type = IsDeltaFile(filePath) ? VelopackAssetType.DeltaPackage : VelopackAssetType.FullPackage,
            };
        }

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
