#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

// https://dev.to/emrahsungu/how-to-compare-two-files-using-net-really-really-fast-2pd9
// https://github.com/SnowflakePowered/vcdiff

namespace Squirrel.Compression
{
    internal class DeltaPackage
    {
        private readonly ILogger _log;
        private readonly string _baseTempDir;

        public DeltaPackage(ILogger logger, string baseTempDir = null)
        {
            _log = logger;
            _baseTempDir = baseTempDir ?? Utility.GetDefaultTempBaseDirectory();
        }

        public string ApplyDeltaPackage(string basePackageZip, string deltaPackageZip, string outputFile, Action<int> progress = null)
        {
            progress = progress ?? (x => { });

            if (deltaPackageZip is null) throw new ArgumentNullException(nameof(deltaPackageZip));
            if (String.IsNullOrEmpty(outputFile)) throw new ArgumentNullException(nameof(outputFile));
            if (File.Exists(outputFile)) throw new ArgumentException("File already exists", nameof(outputFile));

            using (Utility.GetTempDirectory(out var deltaPath, _baseTempDir))
            using (Utility.GetTempDirectory(out var workingPath, _baseTempDir)) {
                EasyZip.ExtractZipToDirectory(_log, deltaPackageZip, deltaPath);
                progress(25);

                EasyZip.ExtractZipToDirectory(_log, basePackageZip, workingPath);
                progress(50);

                var pathsVisited = new List<string>();

                var deltaPathRelativePaths = new DirectoryInfo(deltaPath).GetAllFilesRecursively()
                    .Select(x => x.FullName.Replace(deltaPath + Path.DirectorySeparatorChar, ""))
                    .ToArray();

                // Apply all of the .diff files
                deltaPathRelativePaths
                    .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                    .Where(x => !x.EndsWith(".shasum", StringComparison.InvariantCultureIgnoreCase))
                    .Where(x => !x.EndsWith(".diff", StringComparison.InvariantCultureIgnoreCase) ||
                                !deltaPathRelativePaths.Contains(x.Replace(".diff", ".bsdiff")))
                    .ForEach(file => {
                        pathsVisited.Add(Regex.Replace(file, @"\.(bs)?diff$", "").ToLowerInvariant());
                        applyDiffToFile(deltaPath, file, workingPath);
                    });

                progress(75);

                // Delete all of the files that were in the old package but
                // not in the new one.
                new DirectoryInfo(workingPath).GetAllFilesRecursively()
                    .Select(x => x.FullName.Replace(workingPath + Path.DirectorySeparatorChar, "").ToLowerInvariant())
                    .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase) && !pathsVisited.Contains(x))
                    .ForEach(x => {
                        _log.Info($"{x} was in old package but not in new one, deleting");
                        File.Delete(Path.Combine(workingPath, x));
                    });

                progress(80);

                // Update all the files that aren't in 'lib' with the delta
                // package's versions (i.e. the nuspec file, etc etc).
                deltaPathRelativePaths
                    .Where(x => !x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                    .ForEach(x => {
                        _log.Info($"Updating metadata file: {x}");
                        File.Copy(Path.Combine(deltaPath, x), Path.Combine(workingPath, x), true);
                    });

                _log.Info($"Repacking into full package: {outputFile}");

                EasyZip.CreateZipFromDirectory(_log, outputFile, workingPath);

                progress(100);
            }

            return outputFile;
        }

        public void ApplyDeltaPackageFast(string workingPath, string deltaPackageZip, Action<int> progress = null)
        {
            progress = progress ?? (x => { });

            if (deltaPackageZip is null) throw new ArgumentNullException(nameof(deltaPackageZip));

            using var _1 = Utility.GetTempDirectory(out var deltaPath, _baseTempDir);
            EasyZip.ExtractZipToDirectory(_log, deltaPackageZip, deltaPath);
            progress(10);

            var pathsVisited = new List<string>();

            var deltaPathRelativePaths = new DirectoryInfo(deltaPath).GetAllFilesRecursively()
                .Select(x => x.FullName.Replace(deltaPath + Path.DirectorySeparatorChar, ""))
                .ToArray();

            // Apply all of the .diff files
            var files = deltaPathRelativePaths
                .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                .Where(x => !x.EndsWith(".shasum", StringComparison.InvariantCultureIgnoreCase))
                .Where(x => !x.EndsWith(".diff", StringComparison.InvariantCultureIgnoreCase) ||
                            !deltaPathRelativePaths.Contains(x.Replace(".diff", ".bsdiff")))
                .ToArray();

            for (var index = 0; index < files.Length; index++) {
                var file = files[index];
                pathsVisited.Add(Regex.Replace(file, @"\.(bs)?diff$", "").ToLowerInvariant());
                applyDiffToFile(deltaPath, file, workingPath);
                var perc = (index + 1) / (double) files.Length * 100;
                Utility.CalculateProgress((int) perc, 10, 90);
            }

            progress(90);

            // Delete all of the files that were in the old package but
            // not in the new one.
            new DirectoryInfo(workingPath).GetAllFilesRecursively()
                .Select(x => x.FullName.Replace(workingPath + Path.DirectorySeparatorChar, "").ToLowerInvariant())
                .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase) && !pathsVisited.Contains(x))
                .ForEach(x => {
                    _log.Info($"{x} was in old package but not in new one, deleting");
                    File.Delete(Path.Combine(workingPath, x));
                });

            progress(95);

            // Update all the files that aren't in 'lib' with the delta
            // package's versions (i.e. the nuspec file, etc etc).
            deltaPathRelativePaths
                .Where(x => !x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                .ForEach(x => {
                    _log.Info($"Updating metadata file: {x}");
                    File.Copy(Path.Combine(deltaPath, x), Path.Combine(workingPath, x), true);
                });

            progress(100);
        }

        void applyDiffToFile(string deltaPath, string relativeFilePath, string workingDirectory)
        {
            var inputFile = Path.Combine(deltaPath, relativeFilePath);
            var finalTarget = Path.Combine(workingDirectory, Regex.Replace(relativeFilePath, @"\.(bs)?diff$", ""));

            using var _d = Utility.GetTempFileName(out var tempTargetFile, _baseTempDir);

            // NB: Zero-length diffs indicate the file hasn't actually changed
            if (new FileInfo(inputFile).Length == 0) {
                _log.Info($"{relativeFilePath} exists unchanged, skipping");
                return;
            }

            if (relativeFilePath.EndsWith(".bsdiff", StringComparison.InvariantCultureIgnoreCase)) {
                using (var of = File.OpenWrite(tempTargetFile))
                using (var inf = File.OpenRead(finalTarget)) {
                    _log.Info($"Applying bsdiff to {relativeFilePath}");
                    BinaryPatchUtility.Apply(inf, () => File.OpenRead(inputFile), of);
                }

                verifyPatchedFile(relativeFilePath, inputFile, tempTargetFile);
            } else if (relativeFilePath.EndsWith(".diff", StringComparison.InvariantCultureIgnoreCase)) {
                _log.Info($"Applying msdiff to {relativeFilePath}");

                if (SquirrelRuntimeInfo.IsWindows) {
                    MsDeltaCompression.ApplyDelta(inputFile, finalTarget, tempTargetFile);
                } else {
                    throw new InvalidOperationException("msdiff is not supported on non-windows platforms.");
                }

                verifyPatchedFile(relativeFilePath, inputFile, tempTargetFile);
            } else {
                using (var of = File.OpenWrite(tempTargetFile))
                using (var inf = File.OpenRead(inputFile)) {
                    _log.Info($"Adding new file: {relativeFilePath}");
                    inf.CopyTo(of);
                }
            }

            if (File.Exists(finalTarget)) File.Delete(finalTarget);

            var targetPath = Directory.GetParent(finalTarget);
            if (!targetPath.Exists) targetPath.Create();

            File.Move(tempTargetFile, finalTarget);
        }

        void verifyPatchedFile(string relativeFilePath, string inputFile, string tempTargetFile)
        {
            var shaFile = Regex.Replace(inputFile, @"\.(bs)?diff$", ".shasum");
            var expectedReleaseEntry = ReleaseEntry.ParseReleaseEntry(File.ReadAllText(shaFile, Encoding.UTF8));
            var actualReleaseEntry = ReleaseEntry.GenerateFromFile(tempTargetFile);

            if (expectedReleaseEntry.Filesize != actualReleaseEntry.Filesize) {
                _log.Warn($"Patched file {relativeFilePath} has incorrect size, expected {expectedReleaseEntry.Filesize}, got {actualReleaseEntry.Filesize}");
                throw new ChecksumFailedException(relativeFilePath);
            }

            if (expectedReleaseEntry.SHA1 != actualReleaseEntry.SHA1) {
                _log.Warn($"Patched file {relativeFilePath} has incorrect SHA1, expected {expectedReleaseEntry.SHA1}, got {actualReleaseEntry.SHA1}");
                throw new ChecksumFailedException(relativeFilePath);
            }
        }
    }
}