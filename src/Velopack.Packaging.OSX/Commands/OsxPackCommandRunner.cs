using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Velopack.Packaging.OSX.Commands;

public class OsxPackCommandRunner
{
    private readonly ILogger _logger;

    public OsxPackCommandRunner(ILogger logger)
    {
        _logger = logger;
    }

    public void Releasify(OsxPackOptions options)
    {
        var releaseDir = options.ReleaseDir;
        var channel = options.Channel?.ToLower() ?? "osx"; // default channel for mac packages.

        var helper = new HelperExe(_logger);
        var entryHelper = new ReleaseEntryHelper(releaseDir.FullName, _logger);
        entryHelper.ValidateEntriesForPackaging(SemanticVersion.Parse(options.PackVersion), channel);

        bool deleteAppBundle = false;
        string appBundlePath = options.PackDirectory;
        if (!options.PackDirectory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) {
            appBundlePath = new OsxBundleCommandRunner(_logger).Bundle(options);
            deleteAppBundle = true;
        }

        _logger.Info("Creating release from app bundle at: " + appBundlePath);

        var structure = new StructureBuilder(appBundlePath);

        var packId = options.PackId;
        var packTitle = options.PackTitle;
        var packAuthors = options.PackAuthors;
        var packVersion = options.PackVersion;

        _logger.Info("Adding Squirrel resources to bundle.");
        var nuspecText = NugetConsole.CreateNuspec(
            packId, packTitle, packAuthors, packVersion, options.ReleaseNotes, options.IncludePdb);
        var nuspecPath = Path.Combine(structure.MacosDirectory, Utility.SpecVersionFileName);

        // nuspec and UpdateMac need to be in contents dir or this package can't update
        File.WriteAllText(nuspecPath, nuspecText);
        File.Copy(helper.UpdateMacPath, Path.Combine(structure.MacosDirectory, "UpdateMac"), true);

        var zipPath = Path.Combine(releaseDir.FullName, $"{options.PackId}-[{options.TargetRuntime.ToDisplay(RidDisplayType.NoVersion)}]-Portable.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        // code signing all mach-o binaries
        if (!string.IsNullOrEmpty(options.SigningAppIdentity) && !string.IsNullOrEmpty(options.NotaryProfile)) {
            helper.CodeSign(options.SigningAppIdentity, options.SigningEntitlements, appBundlePath);
            helper.CreateDittoZip(appBundlePath, zipPath);
            helper.Notarize(zipPath, options.NotaryProfile);
            helper.Staple(appBundlePath);
            helper.SpctlAssessCode(appBundlePath);
            File.Delete(zipPath);
        } else {
            _logger.Warn("Package will not be signed or notarized. Requires the --signAppIdentity and --notaryProfile options.");
        }

        // create a portable zip package from signed/notarized bundle
        _logger.Info("Creating final application artifact (ditto zip)");
        helper.CreateDittoZip(appBundlePath, zipPath);

        // create release / delta from notarized .app
        _logger.Info("Creating Release");
        using var _ = Utility.GetTempDirectory(out var tmp);
        var nuget = new NugetConsole(_logger);
        var nupkgPath = nuget.CreatePackageFromNuspecPath(tmp, appBundlePath, nuspecPath);

        var rp = new ReleasePackageBuilder(_logger, nupkgPath);
        var suggestedName = new ReleaseEntryName(packId, SemanticVersion.Parse(packVersion), false, options.TargetRuntime).ToFileName();
        var newPkgPath = rp.CreateReleasePackage((i, pkg) => Path.Combine(releaseDir.FullName, suggestedName));
        entryHelper.AddNewRelease(newPkgPath, channel);

        var prev = entryHelper.GetPreviousFullRelease(rp.Version, channel);
        if (prev != null && options.DeltaMode != DeltaMode.None) {
            _logger.Info("Creating Delta Packages");
            var deltaBuilder = new DeltaPackageBuilder(_logger);
            var deltaFile = rp.ReleasePackageFile.Replace("-full", "-delta");
            var dp = deltaBuilder.CreateDeltaPackage(prev, rp, deltaFile, options.DeltaMode);
            entryHelper.AddNewRelease(deltaFile, channel);
        }

        _logger.Info("Updating RELEASES files");
        entryHelper.SaveReleasesFiles();

        // create installer package, sign and notarize
        if (!options.NoPackage) {
            var pkgPath = Path.Combine(releaseDir.FullName, $"{packId}-[{options.TargetRuntime.ToDisplay(RidDisplayType.NoVersion)}]-Setup.pkg");

            Dictionary<string, string> pkgContent = new() {
                {"welcome", options.PackageWelcome },
                {"license", options.PackageLicense },
                {"readme", options.PackageReadme },
                {"conclusion", options.PackageConclusion },
            };

            helper.CreateInstallerPkg(appBundlePath, packTitle, pkgContent, pkgPath, options.SigningInstallIdentity);
            if (!string.IsNullOrEmpty(options.SigningInstallIdentity) && !string.IsNullOrEmpty(options.NotaryProfile)) {
                helper.Notarize(pkgPath, options.NotaryProfile);
                helper.Staple(pkgPath);
                helper.SpctlAssessInstaller(pkgPath);
            } else {
                _logger.Warn("Package installer (.pkg) will not be Notarized. " +
                         "This is supported with the --signInstallIdentity and --notaryProfile arguments.");
            }
        }

        if (deleteAppBundle) {
            _logger.Info("Removing temporary .app bundle.");
            Utility.DeleteFileOrDirectoryHard(appBundlePath);
        }

        _logger.Info("Done.");
    }
}