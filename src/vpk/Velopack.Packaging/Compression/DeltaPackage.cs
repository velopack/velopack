#nullable enable
using System.Text.RegularExpressions;
using Velopack.Core;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Packaging.Compression
{
    internal abstract class DeltaPackage
    {
        protected readonly IVelopackLogger Log;
        protected readonly string BaseTempDir;

        private static Regex DIFF_SUFFIX = new Regex(@"\.(bs|zs)?diff$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public DeltaPackage(IVelopackLogger logger, string baseTmpDir)
        {
            Log = logger;
            BaseTempDir = baseTmpDir;
        }

        public void ApplyDeltaPackageFast(string workingPath, string deltaPackageZip, Action<int>? progress = null)
        {
            progress = progress ?? (x => { });

            if (deltaPackageZip is null) throw new ArgumentNullException(nameof(deltaPackageZip));

            Log.Info($"Applying delta package from {deltaPackageZip} to delta staging directory.");

            using var _1 = TempUtil.GetTempDirectory(out var deltaPath, BaseTempDir);
            EasyZip.ExtractZipToDirectory(Log, deltaPackageZip, deltaPath);
            progress(10);

            var pathsVisited = new List<string>();

            var deltaPathRelativePaths = new DirectoryInfo(deltaPath).GetAllFilesRecursively()
                .Select(x => x.FullName.Replace(deltaPath + Path.DirectorySeparatorChar, ""))
                .ToArray();

            // Apply all of the .diff files
            var files = deltaPathRelativePaths
                .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                .Where(x => !x.EndsWith(".shasum", StringComparison.InvariantCultureIgnoreCase))
                .Where(x => DIFF_SUFFIX.IsMatch(x))
                .ToArray();

            for (var index = 0; index < files.Length; index++) {
                var file = files[index];
                pathsVisited.Add(DIFF_SUFFIX.Replace(file, "").ToLowerInvariant());
                applyDiffToFile(deltaPath, file, workingPath);
                var perc = (index + 1) / (double) files.Length * 100;
                progress(CoreUtil.CalculateProgress((int) perc, 10, 90));
            }

            progress(80);

            // Delete all of the files that were in the old package but
            // not in the new one.
            new DirectoryInfo(workingPath).GetAllFilesRecursively()
                .Select(x => x.FullName.Replace(workingPath + Path.DirectorySeparatorChar, "").ToLowerInvariant())
                .Where(x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase) && !pathsVisited.Contains(x))
                .ForEach(
                    x => {
                        Log.Trace($"{x} was in old package but not in new one, deleting");
                        File.Delete(Path.Combine(workingPath, x));
                    });

            progress(85);

            // Add all of the files that are in the new package but
            // not in the old one.
            deltaPathRelativePaths
                .Where(
                    x => x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase)
                         && !x.EndsWith(".shasum", StringComparison.InvariantCultureIgnoreCase)
                         && !pathsVisited.Contains(DIFF_SUFFIX.Replace(x, ""), StringComparer.InvariantCultureIgnoreCase))
                .ForEach(
                    x => {
                        Log.Trace($"{x} was in new package but not in old one, adding");

                        string outputFile = Path.Combine(workingPath, x);
                        string outputDirectory = Path.GetDirectoryName(outputFile)!;

                        if (!Directory.Exists(outputDirectory)) {
                            Directory.CreateDirectory(outputDirectory);
                        }

                        File.Copy(Path.Combine(deltaPath, x), outputFile);
                    });

            progress(95);

            // Update all the files that aren't in 'lib' with the delta
            // package's versions (i.e. the nuspec file, etc etc).
            deltaPathRelativePaths
                .Where(x => !x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase))
                .ForEach(
                    x => {
                        Log.Trace($"Writing metadata file: {x}");
                        File.Copy(Path.Combine(deltaPath, x), Path.Combine(workingPath, x), true);
                    });

            // delete all metadata files that are not in the new package
            new DirectoryInfo(workingPath).GetAllFilesRecursively()
                .Select(x => x.FullName.Replace(workingPath + Path.DirectorySeparatorChar, "").ToLowerInvariant())
                .Where(
                    x => !x.StartsWith("lib", StringComparison.InvariantCultureIgnoreCase)
                         && !deltaPathRelativePaths.Contains(x, StringComparer.InvariantCultureIgnoreCase))
                .ForEach(
                    x => {
                        Log.Trace($"Deleting removed metadata file: {x}");
                        File.Delete(Path.Combine(workingPath, x));
                    });

            progress(100);
        }

        protected abstract void ApplyZstdPatch(string baseFile, string patchFile, string outputFile);

        void applyDiffToFile(string deltaPath, string relativeFilePath, string workingDirectory)
        {
            var inputFile = Path.Combine(deltaPath, relativeFilePath);
            var finalTarget = Path.Combine(workingDirectory, DIFF_SUFFIX.Replace(relativeFilePath, ""));

            using var _d = TempUtil.GetTempFileName(out var tempTargetFile, BaseTempDir);

            // NB: Zero-length diffs indicate the file hasn't actually changed
            if (new FileInfo(inputFile).Length == 0) {
                Log.Trace($"{relativeFilePath} exists unchanged, skipping");
                return;
            }

            if (!relativeFilePath.EndsWith(".zsdiff", StringComparison.InvariantCultureIgnoreCase)) {
                throw new NotSupportedException($"Unsupported patch format: {relativeFilePath}");
            }

            Log.Trace($"Applying zstd diff to {relativeFilePath}");
            ApplyZstdPatch(finalTarget, inputFile, tempTargetFile);

            if (File.Exists(finalTarget)) File.Delete(finalTarget);

            var targetPath = Directory.GetParent(finalTarget)!;
            if (!targetPath.Exists) targetPath.Create();

            File.Move(tempTargetFile, finalTarget);
        }
    }
}