using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Squirrel.Packaging.OSX.Commands;

public class OsxReleasifyCommandRunner
{
    private readonly ILogger _logger;

    public OsxReleasifyCommandRunner(ILogger logger)
    {
        _logger = logger;
    }

    public void Releasify(OsxReleasifyOptions options)
    {
        var releaseDir = options.ReleaseDir;

        var appBundlePath = options.BundleDirectory;
        _logger.Info("Creating Squirrel application from app bundle at: " + appBundlePath);

        _logger.Info("Parsing app Info.plist");
        var contentsDir = Path.Combine(appBundlePath, "Contents");

        if (!Directory.Exists(contentsDir))
            throw new Exception("Invalid bundle structure (missing Contents dir)");

        var plistPath = Path.Combine(contentsDir, "Info.plist");
        if (!File.Exists(plistPath))
            throw new Exception("Invalid bundle structure (missing Info.plist)");

        var rootDict = (NSDictionary) PropertyListParser.Parse(plistPath);
        var packId = rootDict.ObjectForKey(nameof(AppInfo.SQPackId))?.ToString();
        if (string.IsNullOrWhiteSpace(packId))
            packId = rootDict.ObjectForKey(nameof(AppInfo.CFBundleIdentifier))?.ToString();

        var packAuthors = rootDict.ObjectForKey(nameof(AppInfo.SQPackAuthors))?.ToString();
        if (string.IsNullOrWhiteSpace(packAuthors))
            packAuthors = packId;

        var packTitle = rootDict.ObjectForKey(nameof(AppInfo.CFBundleName))?.ToString();
        var packVersion = rootDict.ObjectForKey(nameof(AppInfo.CFBundleVersion))?.ToString();

        if (string.IsNullOrWhiteSpace(packId))
            throw new InvalidOperationException($"Invalid CFBundleIdentifier in Info.plist: '{packId}'");

        if (string.IsNullOrWhiteSpace(packTitle))
            throw new InvalidOperationException($"Invalid CFBundleName in Info.plist: '{packTitle}'");

        if (string.IsNullOrWhiteSpace(packVersion) || !NuGetVersion.TryParse(packVersion, out var _))
            throw new InvalidOperationException($"Invalid CFBundleVersion in Info.plist: '{packVersion}'");

        _logger.Info($"Package valid: '{packId}', Name: '{packTitle}', Version: {packVersion}");

        _logger.Info("Adding Squirrel resources to bundle.");
        var nuspecText = NugetConsole.CreateNuspec(
            packId, packTitle, packAuthors, packVersion, options.ReleaseNotes, options.IncludePdb);
        var nuspecPath = Path.Combine(contentsDir, Utility.SpecVersionFileName);

        var helper = new HelperExe(_logger);

        // nuspec and UpdateMac need to be in contents dir or this package can't update
        File.WriteAllText(nuspecPath, nuspecText);
        File.Copy(helper.UpdateMacPath, Path.Combine(contentsDir, "UpdateMac"), true);

        var zipPath = Path.Combine(releaseDir.FullName, $"{packId}-{options.TargetRuntime.ToDisplay(RidDisplayType.NoVersion)}.zip");
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
        _logger.Info("Creating final application artifact (zip)");
        helper.CreateDittoZip(appBundlePath, zipPath);

        // create release / delta from notarized .app
        _logger.Info("Creating Squirrel Release");
        using var _ = Utility.GetTempDirectory(out var tmp);
        var nuget = new NugetConsole(_logger);
        var nupkgPath = nuget.CreatePackageFromNuspecPath(tmp, appBundlePath, nuspecPath);

        var releaseFilePath = Path.Combine(releaseDir.FullName, "RELEASES");
        var releases = new Dictionary<string, ReleaseEntry>();

        ReleaseEntry.BuildReleasesFile(releaseDir.FullName);
        foreach (var rel in ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8))) {
            releases[rel.Filename] = rel;
        }

        var rp = new ReleasePackageBuilder(_logger, nupkgPath);
        var suggestedName = ReleasePackageBuilder.GetSuggestedFileName(packId, packVersion, options.TargetRuntime.StringWithNoVersion);
        var newPkgPath = rp.CreateReleasePackage((i, pkg) => Path.Combine(releaseDir.FullName, suggestedName));

        _logger.Info("Creating Delta Packages");
        var prev = ReleasePackageBuilder.GetPreviousRelease(_logger, releases.Values, rp, releaseDir.FullName, options.TargetRuntime);
        if (prev != null && !options.NoDelta) {
            var deltaBuilder = new DeltaPackageBuilder(_logger);
            var deltaFile = rp.ReleasePackageFile.Replace("-full", "-delta");
            var dp = deltaBuilder.CreateDeltaPackage(prev, rp, deltaFile);
            var deltaEntry = ReleaseEntry.GenerateFromFile(deltaFile);
            releases[deltaEntry.Filename] = deltaEntry;
        }

        var fullEntry = ReleaseEntry.GenerateFromFile(newPkgPath);
        releases[fullEntry.Filename] = fullEntry;

        ReleaseEntry.WriteReleaseFile(releases.Values, releaseFilePath);

        // create installer package, sign and notarize
        if (!options.NoPackage) {
            if (SquirrelRuntimeInfo.IsOSX) {
                var pkgPath = Path.Combine(releaseDir.FullName, $"{packId}-{options.TargetRuntime.StringWithNoVersion}.pkg");

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
            } else {
                _logger.Warn("Package installer (.pkg) will not be created - this is only supported on OSX.");
            }
        }

        _logger.Info("Done.");
    }
}