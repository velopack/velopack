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

        public string? LoadedFromPath { get; private set; }

        public ZipPackage(string filePath, bool loadUpdateExe = false)
        {
            LoadedFromPath = filePath;
            using var zipStream = File.OpenRead(filePath);
            Init(zipStream, loadUpdateExe, false);
        }
        public ZipPackage(Stream zipStream, bool loadUpdateExe = false)
        {
            Init(zipStream, loadUpdateExe, true);
        }
        private void Init(Stream zipStream, bool loadUpdateExe = false, bool leaveOpen = false)
        {
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen);
            using var manifest = GetManifestEntry(zip).Open();
            ReadManifest(manifest);

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

        private static ZipArchiveEntry GetManifestEntry(ZipArchive zip)
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