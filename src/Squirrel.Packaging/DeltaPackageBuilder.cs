using System.Text;
using Squirrel.Compression;
using Microsoft.Extensions.Logging;

namespace Squirrel.Packaging;

public class DeltaPackageBuilder
{
    private readonly ILogger _logger;

    public DeltaPackageBuilder(ILogger logger)
    {
        _logger = logger;
    }

    public ReleasePackageBuilder CreateDeltaPackage(ReleasePackageBuilder basePackage, ReleasePackageBuilder newPackage, string outputFile)
    {
        if (basePackage == null) throw new ArgumentNullException(nameof(basePackage));
        if (newPackage == null) throw new ArgumentNullException(nameof(newPackage));
        if (String.IsNullOrEmpty(outputFile) || File.Exists(outputFile)) throw new ArgumentException("The output file is null or already exists", nameof(outputFile));

        if (basePackage.Version >= newPackage.Version) {
            var message = String.Format(
                "Cannot create a delta package based on version {0} as it is a later or equal to the base version {1}",
                basePackage.Version,
                newPackage.Version);
            throw new InvalidOperationException(message);
        }

        if (basePackage.ReleasePackageFile == null) {
            throw new ArgumentException("The base package's release file is null", "basePackage");
        }

        if (!File.Exists(basePackage.ReleasePackageFile)) {
            throw new FileNotFoundException("The base package release does not exist", basePackage.ReleasePackageFile);
        }

        if (!File.Exists(newPackage.ReleasePackageFile)) {
            throw new FileNotFoundException("The new package release does not exist", newPackage.ReleasePackageFile);
        }

        using (Utility.GetTempDirectory(out var baseTempPath))
        using (Utility.GetTempDirectory(out var tempPath)) {
            var baseTempInfo = new DirectoryInfo(baseTempPath);
            var tempInfo = new DirectoryInfo(tempPath);

            // minThreads = 1, maxThreads = 8
            int numParallel = Math.Min(Math.Max(Environment.ProcessorCount - 1, 1), 8);

            _logger.Info($"Creating delta for {basePackage.Version} -> {newPackage.Version} with {numParallel} parallel threads.");
            _logger.Debug($"Extracting {Path.GetFileName(basePackage.ReleasePackageFile)} and {Path.GetFileName(newPackage.ReleasePackageFile)} into {tempPath}");

            EasyZip.ExtractZipToDirectory(_logger, basePackage.ReleasePackageFile, baseTempInfo.FullName);
            EasyZip.ExtractZipToDirectory(_logger, newPackage.ReleasePackageFile, tempInfo.FullName);

            // Collect a list of relative paths under 'lib' and map them
            // to their full name. We'll use this later to determine in
            // the new version of the package whether the file exists or
            // not.
            var baseLibFiles = baseTempInfo.GetAllFilesRecursively()
                .Where(x => x.FullName.ToLowerInvariant().Contains("lib" + Path.DirectorySeparatorChar))
                .ToDictionary(k => k.FullName.Replace(baseTempInfo.FullName, ""), v => v.FullName);

            var newLibDir = tempInfo.GetDirectories().First(x => x.Name.ToLowerInvariant() == "lib");
            var newLibFiles = newLibDir.GetAllFilesRecursively().ToArray();

            int fNew = 0, fSame = 0, fChanged = 0, fWarnings = 0;

            bool bytesAreIdentical(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
            {
                return a1.SequenceEqual(a2);
            }

            void createDeltaForSingleFile(FileInfo targetFile, DirectoryInfo workingDirectory)
            {
                // NB: There are three cases here that we'll handle:
                //
                // 1. Exists only in new => leave it alone, we'll use it directly.
                // 2. Exists in both old and new => write a dummy file so we know
                //    to keep it.
                // 3. Exists in old but changed in new => create a delta file
                //
                // The fourth case of "Exists only in old => delete it in new"
                // is handled when we apply the delta package
                try {
                    var relativePath = targetFile.FullName.Replace(workingDirectory.FullName, "");

                    // 1. new file, leave it alone
                    if (!baseLibFiles.ContainsKey(relativePath)) {
                        _logger.Debug($"{relativePath} not found in base package, marking as new");
                        fNew++;
                        return;
                    }

                    var oldFilePath = baseLibFiles[relativePath];
                    _logger.Debug($"Delta patching {oldFilePath} => {targetFile.FullName}");

                    var oldData = File.ReadAllBytes(oldFilePath);
                    var newData = File.ReadAllBytes(targetFile.FullName);

                    if (bytesAreIdentical(oldData, newData)) {
                        // 2. exists in both, keep it the same
                        _logger.Debug($"{relativePath} hasn't changed, writing dummy file");
                        File.Create(targetFile.FullName + ".bsdiff").Dispose();
                        File.Create(targetFile.FullName + ".shasum").Dispose();
                        fSame++;
                    } else {
                        // 3. changed, write a delta in new
                        using (FileStream of = File.Create(targetFile.FullName + ".bsdiff")) {
                            BinaryPatchUtility.Create(oldData, newData, of);
                        }
                        var rl = ReleaseEntry.GenerateFromFile(new MemoryStream(newData), targetFile.Name + ".shasum");
                        File.WriteAllText(targetFile.FullName + ".shasum", rl.EntryAsString, Encoding.UTF8);
                        fChanged++;
                    }
                    targetFile.Delete();
                    baseLibFiles.Remove(relativePath);
                } catch (Exception ex) {
                    _logger.Debug(ex, String.Format("Failed to create a delta for {0}", targetFile.Name));
                    Utility.DeleteFileOrDirectoryHard(targetFile.FullName + ".bsdiff", throwOnFailure: false);
                    Utility.DeleteFileOrDirectoryHard(targetFile.FullName + ".diff", throwOnFailure: false);
                    Utility.DeleteFileOrDirectoryHard(targetFile.FullName + ".shasum", throwOnFailure: false);
                    fWarnings++;
                    throw;
                }
            }

            void printProcessed(int cur, int? removed = null)
            {
                string rem = removed.HasValue ? removed.Value.ToString("D4") : "????";
                _logger.Info($"Processed {cur.ToString("D4")}/{newLibFiles.Length.ToString("D4")} files. " +
                    $"{fChanged.ToString("D4")} patched, {fSame.ToString("D4")} unchanged, {fNew.ToString("D4")} new, {rem} removed");
            }

            printProcessed(0);

            var tResult = Task.Run(() => {
                Parallel.ForEach(newLibFiles, new ParallelOptions() { MaxDegreeOfParallelism = numParallel }, (f) => {
                    Utility.Retry(() => createDeltaForSingleFile(f, tempInfo));
                });
            });

            int prevCount = 0;
            while (!tResult.IsCompleted) {
                // sleep for 2 seconds (in 100ms intervals)
                for (int i = 0; i < 20 && !tResult.IsCompleted; i++)
                    Thread.Sleep(100);

                int processed = fNew + fChanged + fSame;
                if (prevCount == processed) {
                    // if there has been no progress, do not print another message
                    continue;
                }

                if (processed < newLibFiles.Length)
                    printProcessed(processed);
                prevCount = processed;
            }

            if (tResult.Exception != null)
                throw new Exception("Unable to create delta package.", tResult.Exception);

            printProcessed(newLibFiles.Length, baseLibFiles.Count);

            ReleasePackageBuilder.addDeltaFilesToContentTypes(tempInfo.FullName);
            EasyZip.CreateZipFromDirectory(_logger, outputFile, tempInfo.FullName);

            _logger.Info(
                $"Successfully created delta package for {basePackage.Version} -> {newPackage.Version}" +
                (fWarnings > 0 ? $" (with {fWarnings} retries)" : "") +
                ".");
        }

        return new ReleasePackageBuilder(_logger, outputFile);
    }
}
