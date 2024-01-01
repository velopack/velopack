#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Velopack.NuGet
{
    public class ZipPackage : NuspecManifest, IZipPackage
    {
        public IEnumerable<string> Frameworks { get; private set; } = Enumerable.Empty<string>();

        public IEnumerable<ZipPackageFile> Files { get; private set; } = Enumerable.Empty<ZipPackageFile>();

        public byte[] UpdateExeBytes { get; private set; }

        public ZipPackage(string filePath) : this(File.OpenRead(filePath))
        {
        }

        public ZipPackage(Stream zipStream, bool leaveOpen = false)
        {
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen);
            using var manifest = GetManifestEntry(zip).Open();
            ReadManifest(manifest);
            Files = GetPackageFiles(zip).ToArray();
            Frameworks = GetFrameworks(Files);
            UpdateExeBytes = ReadFileToBytes(zip, f => f.FullName.EndsWith("Squirrel.exe"));
        }

        protected byte[] ReadFileToBytes(ZipArchive archive, Func<ZipArchiveEntry, bool> predicate)
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

        private string[] GetFrameworks(IEnumerable<ZipPackageFile> files)
        {
            return FrameworkAssemblies
                .SelectMany(f => f.SupportedFrameworks)
                .Concat(files.Select(z => z.TargetFramework))
                .Where(f => f != null)
                .Distinct()
                .ToArray();
        }
    }
}