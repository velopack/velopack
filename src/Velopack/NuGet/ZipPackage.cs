#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Velopack.NuGet
{
    public class ZipPackage : PackageManifest
    {
        public IEnumerable<ZipPackageFile> Files { get; private set; } = Enumerable.Empty<ZipPackageFile>();

        public byte[]? UpdateExeBytes { get; private set; }

        public string LoadedFromPath { get; private set; }

        public ZipPackage(string filePath, bool loadUpdateExe = false)
        {
            using var zipStream = File.OpenRead(filePath);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, false);
            using var manifest = GetManifestEntry(zip).Open();
            ReadManifest(manifest);

            LoadedFromPath = filePath;
            Files = GetPackageFiles(zip).ToArray();

            if (loadUpdateExe) {
                UpdateExeBytes = ReadFile(zip, f => f.FullName.EndsWith("Squirrel.exe"));
            }
        }

        protected byte[]? ReadFile(ZipArchive archive, Func<ZipArchiveEntry, bool> predicate)
        {
            var f = archive.Entries.FirstOrDefault(predicate);
            if (f == null)
                return null;

            using var stream = f.Open();
            if (stream == null)
                return null;

            var ms = new MemoryStream();
            stream.CopyTo(ms);

            return ms.ToArray();
        }

        private ZipArchiveEntry GetManifestEntry(ZipArchive zip)
        {
            var manifest = zip.Entries
                .FirstOrDefault(f => f.FullName.EndsWith(NugetUtil.ManifestExtension, StringComparison.OrdinalIgnoreCase));

            if (manifest == null)
                throw new InvalidDataException("Invalid nupkg. Does not contain required '.nuspec' manifest.");

            return manifest;
        }

        private IEnumerable<ZipPackageFile> GetPackageFiles(ZipArchive zip)
        {
            return from entry in zip.Entries
                   where !entry.IsDirectory()
                   let uri = new Uri(entry.FullName, UriKind.Relative)
                   let path = NugetUtil.GetPath(uri)
                   where IsPackageFile(path)
                   select new ZipPackageFile(uri);
        }
    }
}