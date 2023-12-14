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

namespace Squirrel.NuGet
{
    public interface IZipPackage : IPackage
    {
        IEnumerable<string> Frameworks { get; }
        IEnumerable<ZipPackageFile> Files { get; }
    }

    public class ZipPackage : NuspecManifest, IZipPackage
    {
        public IEnumerable<string> Frameworks { get; private set; } = Enumerable.Empty<string>();
        public IEnumerable<ZipPackageFile> Files { get; private set; } = Enumerable.Empty<ZipPackageFile>();

        public byte[] SetupSplashBytes { get; private set; }
        public byte[] SetupIconBytes { get; private set; }
        public byte[] AppIconBytes { get; private set; }

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

            // we pre-load some images so the zip doesn't need to be opened again later
            SetupSplashBytes = ReadFileToBytes(zip, z => Path.GetFileNameWithoutExtension(z.FullName) == "splashimage");
            SetupIconBytes = ReadFileToBytes(zip, z => z.FullName == "setup.ico");
            AppIconBytes = ReadFileToBytes(zip, z => z.FullName == "app.ico") ?? ReadFileToBytes(zip, z => z.FullName.EndsWith("app.ico"));
        }

        private byte[] ReadFileToBytes(ZipArchive archive, Func<ZipArchiveEntry, bool> predicate)
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

        public static Task ExtractZipReleaseForInstall(ILogger logger, string zipFilePath, string outFolder, string rootPackageFolder, Action<int> progress)
        {
            if (SquirrelRuntimeInfo.IsWindows)
                return ExtractZipReleaseForInstallWindows(logger, zipFilePath, outFolder, rootPackageFolder, progress);

            if (SquirrelRuntimeInfo.IsOSX)
                return ExtractZipReleaseForInstallOSX(logger, zipFilePath, outFolder, progress);

            throw new NotSupportedException("Platform not supported.");
        }

        private static readonly Regex libFolderPattern =
            new Regex(@"lib[\\\/][^\\\/]*[\\\/]", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [SupportedOSPlatform("macos")]
        public static Task ExtractZipReleaseForInstallOSX(ILogger logger, string zipFilePath, string outFinalFolder, Action<int> progress)
        {
            if (!File.Exists(zipFilePath)) throw new ArgumentException("zipFilePath must exist");
            progress ??= ((_) => { });

            return Task.Run(() => {
                using (Utility.GetTempDirectory(out var tmp))
                using (var fs = File.OpenRead(zipFilePath))
                using (var za = new ZipArchive(fs)) {
                    var totalItems = za.Entries.Count;
                    var currentItem = 0;

                    foreach (var entry in za.Entries) {
                        // Report progress early since we might be need to continue for non-matches
                        currentItem++;
                        var percentage = (currentItem * 100d) / totalItems;
                        progress((int) percentage);

                        // extract .nuspec to app directory as '.version'
                        if (Utility.FileHasExtension(entry.FullName, NugetUtil.ManifestExtension)) {
                            Utility.Retry(() => entry.ExtractToFile(Path.Combine(tmp, Utility.SpecVersionFileName), true));
                            continue;
                        }

                        var parts = entry.FullName.Split('\\', '/').Select(x => Uri.UnescapeDataString(x));
                        var decoded = String.Join(Path.DirectorySeparatorChar.ToString(), parts);

                        if (!libFolderPattern.IsMatch(decoded)) continue;
                        decoded = libFolderPattern.Replace(decoded, "", 1);

                        var fullTargetFile = Path.Combine(tmp, decoded);
                        var fullTargetDir = Path.GetDirectoryName(fullTargetFile);
                        Directory.CreateDirectory(fullTargetDir);

                        Utility.Retry(() => {
                            if (entry.IsDirectory()) {
                                Directory.CreateDirectory(fullTargetFile);
                            } else {
                                entry.ExtractToFile(fullTargetFile, true);
                            }
                        });

                        if (!entry.IsDirectory() && PlatformUtil.IsMachOImage(fullTargetFile)) {
                            PlatformUtil.ChmodFileAsExecutable(fullTargetFile);
                        }
                    }

                    Utility.DeleteFileOrDirectoryHard(outFinalFolder, renameFirst: true);
                    Directory.Move(tmp, outFinalFolder);
                }

                progress(100);
            });
        }

        [SupportedOSPlatform("windows")]
        public static Task ExtractZipReleaseForInstallWindows(ILogger logger, string zipFilePath, string outFinalFolder, string rootPackageFolder, Action<int> progress)
        {
            if (!File.Exists(zipFilePath)) throw new ArgumentException("zipFilePath must exist");
            progress ??= ((_) => { });

            return Task.Run(() => {
                using (Utility.GetTempDirectory(out var tmp))
                using (var fs = File.OpenRead(zipFilePath))
                using (var za = new ZipArchive(fs)) {
                    var totalItems = za.Entries.Count;
                    var currentItem = 0;

                    foreach (var entry in za.Entries) {
                        // Report progress early since we might be need to continue for non-matches
                        currentItem++;
                        var percentage = (currentItem * 100d) / totalItems;
                        progress((int) percentage);

                        // extract .nuspec to app directory as '.version'
                        if (Utility.FileHasExtension(entry.FullName, NugetUtil.ManifestExtension)) {
                            Utility.Retry(() => entry.ExtractToFile(Path.Combine(tmp, Utility.SpecVersionFileName), true));
                            continue;
                        }

                        var parts = entry.FullName.Split('\\', '/').Select(x => Uri.UnescapeDataString(x));
                        var decoded = String.Join(Path.DirectorySeparatorChar.ToString(), parts);

                        if (!libFolderPattern.IsMatch(decoded)) continue;
                        decoded = libFolderPattern.Replace(decoded, "", 1);

                        var fullTargetFile = Path.Combine(tmp, decoded);
                        var fullTargetDir = Path.GetDirectoryName(fullTargetFile);
                        Directory.CreateDirectory(fullTargetDir);

                        var failureIsOkay = false;
                        if (!entry.IsDirectory() && decoded.Contains("_ExecutionStub.exe")) {
                            // NB: On upgrade, many of these stubs will be in-use, nbd tho.
                            //failureIsOkay = true;

                            //fullTargetFile = Path.Combine(
                            //    rootPackageFolder,
                            //    Path.GetFileName(decoded).Replace("_ExecutionStub.exe", ".exe"));

                            //logger.Info($"Rigging execution stub for {decoded} to {fullTargetFile}");
                            logger.Info($"Skipping obsolete stub {decoded}");
                            continue;
                        }

                        if (Utility.PathPartEquals(parts.Last(), "app.ico")) {
                            failureIsOkay = true;
                            fullTargetFile = Path.Combine(rootPackageFolder, "app.ico");
                        }

                        try {
                            Utility.Retry(() => {
                                if (entry.IsDirectory()) {
                                    Directory.CreateDirectory(fullTargetFile);
                                } else {
                                    entry.ExtractToFile(fullTargetFile, true);
                                }
                            });
                        } catch (Exception e) {
                            if (!failureIsOkay) throw;
                            logger.Warn(e, "Can't write execution stub, probably in use");
                        }
                    }

                    Utility.DeleteFileOrDirectoryHard(outFinalFolder, renameFirst: true);
                    Directory.Move(tmp, outFinalFolder);
                }

                progress(100);
            });
        }
    }
}