using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
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
        if (options.TargetRuntime?.BaseRID != RuntimeOs.Windows)
            throw new ArgumentException("Target runtime must be Windows.", nameof(options.TargetRuntime));

        var targetDir = options.ReleaseDir.FullName;
        var package = options.Package;
        var backgroundGif = options.SplashImage;
        var setupIcon = options.Icon;
        var channel = options.Channel?.ToLower() ?? ReleaseEntryHelper.GetDefaultChannel(RuntimeOs.Windows);

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
        var fileToProcess = Path.Combine(tempDir, Path.GetFileName(package));
        File.Copy(package, fileToProcess, true);

        _logger.Info("Creating release for package: " + fileToProcess);

        var rp = new ReleasePackageBuilder(_logger, fileToProcess);

        var entryHelper = new ReleaseEntryHelper(targetDir, _logger);
        entryHelper.ValidateChannelForPackaging(rp.Version, channel, options.TargetRuntime);

        rp.CreateReleasePackage(contentsPostProcessHook: (pkgPath, zpkg) => {
            var nuspecPath = Directory.GetFiles(pkgPath, "*.nuspec", SearchOption.TopDirectoryOnly)
                .ContextualSingle("package", "*.nuspec", "top level directory");
            var libDir = Directory.GetDirectories(Path.Combine(pkgPath, "lib"))
                .ContextualSingle("package", "'lib' folder");

            var mainExeName = options.EntryExecutableName ?? zpkg.Id + ".exe";
            var mainExe = Path.Combine(libDir, mainExeName);
            if (!File.Exists(mainExe))
                throw new ArgumentException($"--exeName '{mainExeName}' does not exist in package. Searched at: '{mainExe}'");

            try {
                var psi = new ProcessStartInfo(mainExe);
                psi.AppendArgumentListSafe(new[] { "--veloapp-version" }, out var _);
                var output = psi.Output(3000);
                if (String.IsNullOrWhiteSpace(output)) {
                    throw new Exception("Process exited with no output.");
                }
                var version = SemanticVersion.Parse(output.Trim());
                if (version != VelopackRuntimeInfo.VelopackNugetVersion) {
                    _logger.Warn($"VelopackApp version '{version}' does not match CLI version '{VelopackRuntimeInfo.VelopackNugetVersion}'.");
                }
            } catch {
                _logger.Error("Failed to verify VelopackApp. Ensure you have added the startup code to your Program.Main(): VelopackApp.Build().Run();");
                throw;
            }

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

            var versionSuffix = ReleaseEntryHelper.GetPkgSuffix(RuntimeOs.Windows, options.Channel);
            var versionOverride = String.IsNullOrWhiteSpace(versionSuffix)
                ? zpkg.Version : SemanticVersion.Parse(zpkg.Version.ToFullString() + versionSuffix);
            NuspecManifest.SetMetadata(nuspecPath, mainExeName, requiredFrameworks.Select(r => r.Id), options.TargetRuntime, versionOverride.ToFullString());

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

            var releaseName = new ReleaseEntryName(spec.Id, versionOverride, false, options.TargetRuntime);
            return Path.Combine(targetDir, releaseName.ToFileName());
        });

        File.Delete(fileToProcess);
        entryHelper.AddNewRelease(rp.ReleasePackageFile, channel);

        var prev = entryHelper.GetPreviousFullRelease(rp.Version, channel);
        if (prev != null && options.DeltaMode != DeltaMode.None) {
            _logger.Info($"Creating delta package between {prev.Version} and {rp.Version}");
            var deltaBuilder = new DeltaPackageBuilder(_logger);
            var deltaOutputPath = rp.ReleasePackageFile.Replace("-full", "-delta");
            var dp = deltaBuilder.CreateDeltaPackage(prev, rp, deltaOutputPath, options.DeltaMode);
            entryHelper.AddNewRelease(dp.InputPackageFile, channel);
        }

        _logger.Info("Updating RELEASES files");
        entryHelper.SaveReleasesFiles();

        var bundledzp = new ZipPackage(package);
        var targetSetupExe = entryHelper.GetSuggestedSetupPath(bundledzp.Id, channel, options.TargetRuntime);
        File.Copy(helper.SetupPath, targetSetupExe, true);

        if (VelopackRuntimeInfo.IsWindows) {
            helper.SetPEVersionBlockFromPackageInfo(targetSetupExe, bundledzp, setupIcon);
        } else {
            _logger.Warn("Unable to set Setup.exe icon (only supported on windows)");
        }

        _logger.Info($"Creating Setup bundle");
        var bundleOffset = SetupBundle.CreatePackageBundle(targetSetupExe, rp.ReleasePackageFile);
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