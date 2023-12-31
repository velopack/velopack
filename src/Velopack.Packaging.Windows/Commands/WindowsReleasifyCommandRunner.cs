using System.Text;
using Microsoft.Extensions.Logging;
using Velopack.NuGet;
using Velopack.Windows;

namespace Velopack.Packaging.Windows.Commands;

public class WindowsReleasifyCommandRunner
{
    private readonly ILogger _logger;

    public WindowsReleasifyCommandRunner(ILogger logger)
    {
        _logger = logger;
    }

    public void Releasify(WindowsReleasifyOptions options)
    {
        var targetDir = options.ReleaseDir.FullName;
        var package = options.Package;
        var backgroundGif = options.SplashImage;
        var setupIcon = options.Icon;

        // normalize and validate that the provided frameworks are supported 
        IEnumerable<Runtimes.RuntimeInfo> requiredFrameworks = Enumerable.Empty<Runtimes.RuntimeInfo>();
        if (!string.IsNullOrWhiteSpace(options.Runtimes)) {
            requiredFrameworks = options.Runtimes
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Runtimes.GetRuntimeByName);
        }

        if (requiredFrameworks.Where(f => f == null).Any())
            throw new ArgumentException("Invalid target frameworks string.");

        using var ud = Utility.GetTempDirectory(out var tempDir);

        // parse releases in curent channel, and if there are any that don't match the current rid we should bail
        var releaseFilePath = Path.Combine(targetDir, "RELEASES");
        if (!String.IsNullOrWhiteSpace(options.Channel))
            releaseFilePath = Path.Combine(targetDir, $"RELEASES-{options.Channel}");

        var previousReleases = new List<ReleaseEntry>();
        if (File.Exists(releaseFilePath)) {
            previousReleases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));
        }

        var mismatchedRid = previousReleases
            .Select(p => p.Rid)
            .Where(p => p != options.TargetRuntime)
            .Distinct()
            .Select(p => p.ToString())
            .ToArray();
        if (mismatchedRid.Any()) {
            var message = $"Previous releases were built for a different runtime ({String.Join(", ", mismatchedRid)}) " +
                $"than current ({options.TargetRuntime}). Please use the same runtime for all releases in a channel.";
            throw new ArgumentException(message);
        }

        var helper = new HelperExe(_logger);
        var updatePath = Path.Combine(tempDir, "Update.exe");
        File.Copy(helper.UpdatePath, updatePath, true);

        // update icon for Update.exe if requested
        if (setupIcon != null && VelopackRuntimeInfo.IsWindows) {
            helper.SetExeIcon(updatePath, setupIcon);
        } else if (setupIcon != null) {
            _logger.Warn("Unable to set icon for Update.exe (only supported on windows).");
        }

        // copy input package to target output directory
        var fileToProcess = Path.Combine(targetDir, Path.GetFileName(package));
        File.Copy(package, fileToProcess, true);

        _logger.Info("Creating release for package: " + fileToProcess);

        var processed = new List<string>();
        var rp = new ReleasePackageBuilder(_logger, fileToProcess);
        rp.CreateReleasePackage(contentsPostProcessHook: (pkgPath, zpkg) => {
            var nuspecPath = Directory.GetFiles(pkgPath, "*.nuspec", SearchOption.TopDirectoryOnly)
                .ContextualSingle("package", "*.nuspec", "top level directory");
            var libDir = Directory.GetDirectories(Path.Combine(pkgPath, "lib"))
                .ContextualSingle("package", "'lib' folder");

            var mainExe = Path.Combine(libDir, options.EntryExecutableName);
            if (!File.Exists(mainExe))
                throw new ArgumentException($"--exeName '{options.EntryExecutableName}' does not exist in package. Searched at: '{mainExe}'");

            var spec = NuspecManifest.ParseFromFile(nuspecPath);

            // warning if there are long paths (>200 char) in this package. 260 is max path
            // but with the %localappdata% + user name + app name this can add up quickly.
            // eg. 'C:\Users\SamanthaJones\AppData\Local\Application\app-1.0.1\' is 60 characters.
            Directory.EnumerateFiles(libDir, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(libDir.Length).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Where(f => f.Length >= 200)
                .ForEach(f => _logger.Warn($"File path in package exceeds 200 characters ({f.Length}) and may cause issues on Windows: '{f}'."));

            // fail the release if this is a clickonce application
            if (Directory.EnumerateFiles(libDir, "*.application").Any(f => File.ReadAllText(f).Contains("clickonce"))) {
                throw new ArgumentException(
                    "Velopack does not support building releases for ClickOnce applications. " +
                    "Please publish your application to a folder without ClickOnce.");
            }

            NuspecManifest.SetMetadata(nuspecPath, options.EntryExecutableName, requiredFrameworks.Select(r => r.Id), options.TargetRuntime);

            // copy Update.exe into package, so it can also be updated in both full/delta packages
            // and do it before signing so that Update.exe will also be signed. It is renamed to
            // 'Squirrel.exe' only because Squirrel.Windows and Clowd.Squirrel expects it to be called this.
            File.Copy(updatePath, Path.Combine(libDir, "Squirrel.exe"), true);

            // sign all exe's in this package
            var filesToSign = new DirectoryInfo(libDir).GetAllFilesRecursively()
                .Where(x => options.SignSkipDll ? Utility.PathPartEndsWith(x.Name, ".exe") : Utility.FileIsLikelyPEImage(x.Name))
                .Select(x => x.FullName)
                .ToArray();

            signFiles(options, libDir, filesToSign);

            // copy other images to root (used by setup)
            if (setupIcon != null) File.Copy(setupIcon, Path.Combine(pkgPath, "setup.ico"), true);
            if (backgroundGif != null) File.Copy(backgroundGif, Path.Combine(pkgPath, "splashimage" + Path.GetExtension(backgroundGif)));

            var releaseName = new ReleaseEntryName(spec.Id, spec.Version, false, options.TargetRuntime);
            return Path.Combine(targetDir, releaseName.ToFileName());
        });

        processed.Add(rp.ReleasePackageFile);

        var prev = ReleasePackageBuilder.GetPreviousRelease(_logger, previousReleases, rp, targetDir);
        if (prev != null && options.DeltaMode != DeltaMode.None) {
            var deltaBuilder = new DeltaPackageBuilder(_logger);
            var deltaOutputPath = rp.ReleasePackageFile.Replace("-full", "-delta");
            var dp = deltaBuilder.CreateDeltaPackage(prev, rp, deltaOutputPath, options.DeltaMode);
            processed.Insert(0, dp.InputPackageFile);
        }

        File.Delete(fileToProcess);

        var newReleaseEntries = processed
            .Select(packageFilename => ReleaseEntry.GenerateFromFile(packageFilename))
            .ToList();
        var distinctPreviousReleases = previousReleases
            .Where(x => !newReleaseEntries.Select(e => e.Version).Contains(x.Version));
        var releaseEntries = distinctPreviousReleases.Concat(newReleaseEntries).ToList();

        ReleaseEntry.WriteReleaseFile(releaseEntries, releaseFilePath);

        var bundledzp = new ZipPackage(package);
        var targetSetupExe = Path.Combine(targetDir, $"{bundledzp.Id}-Setup-[{options.TargetRuntime.ToDisplay(RidDisplayType.NoVersion)}].exe");
        File.Copy(helper.SetupPath, targetSetupExe, true);

        if (VelopackRuntimeInfo.IsWindows) {
            helper.SetPEVersionBlockFromPackageInfo(targetSetupExe, bundledzp, setupIcon);
        } else {
            _logger.Warn("Unable to set Setup.exe icon (only supported on windows)");
        }

        var newestFullRelease = releaseEntries.MaxBy(x => x.Version).Where(x => !x.IsDelta).First();
        var newestReleasePath = Path.Combine(targetDir, newestFullRelease.OriginalFilename);

        _logger.Info($"Creating Setup bundle");
        var bundleOffset = SetupBundle.CreatePackageBundle(targetSetupExe, newestReleasePath);
        _logger.Info("Signing Setup bundle");
        signFiles(options, targetDir, targetSetupExe);

        _logger.Info($"Setup bundle created at '{targetSetupExe}'.");

        _logger.Info("Done");
    }

    private void signFiles(WindowsSigningOptions options, string rootDir, params string[] filePaths)
    {
        var signParams = options.SignParameters;
        var signTemplate = options.SignTemplate;
        var signParallel = options.SignParallel;
        var helper = new HelperExe(_logger);

        if (string.IsNullOrEmpty(signParams) && string.IsNullOrEmpty(signTemplate)) {
            _logger.Debug($"No signing paramaters provided, {filePaths.Length} file(s) will not be signed.");
            return;
        }

        if (!string.IsNullOrEmpty(signTemplate)) {
            _logger.Info($"Preparing to sign {filePaths.Length} files with custom signing template");
            foreach (var f in filePaths) {
                helper.SignPEFileWithTemplate(f, signTemplate);
            }
            return;
        }

        // signtool.exe does not work if we're not on windows.
        if (!VelopackRuntimeInfo.IsWindows) return;

        if (!string.IsNullOrEmpty(signParams)) {
            _logger.Info($"Preparing to sign {filePaths.Length} files with embedded signtool.exe with parallelism of {signParallel}");
            helper.SignPEFilesWithSignTool(rootDir, filePaths, signParams, signParallel);
        }
    }
}