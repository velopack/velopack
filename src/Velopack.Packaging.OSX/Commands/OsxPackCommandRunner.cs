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

        // parse releases in curent channel, and if there are any that don't match the current rid we should bail
        var releaseFilePath = Path.Combine(releaseDir.FullName, "RELEASES");
        if (!String.IsNullOrWhiteSpace(options.Channel))
            releaseFilePath = Path.Combine(releaseDir.FullName, $"RELEASES-{options.Channel}");

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
                $"than the current one. Please use the same runtime for all releases in a channel.";
            throw new ArgumentException(message);
        }

        string appBundlePath = options.PackDirectory;
        if (!options.PackDirectory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) {
            appBundlePath = new OsxBundleCommandRunner(_logger).Bundle(options);
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
        var nuspecPath = Path.Combine(structure.ContentsDirectory, Utility.SpecVersionFileName);

        var helper = new HelperExe(_logger);

        // nuspec and UpdateMac need to be in contents dir or this package can't update
        File.WriteAllText(nuspecPath, nuspecText);
        File.Copy(helper.UpdateMacPath, Path.Combine(structure.ContentsDirectory, "UpdateMac"), true);

        var zipPath = Path.Combine(releaseDir.FullName, $"{options.PackId}-{options.TargetRuntime.ToDisplay(RidDisplayType.NoVersion)}.zip");
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

        var releases = new Dictionary<string, ReleaseEntry>();

        ReleaseEntry.BuildReleasesFile(releaseDir.FullName);
        foreach (var rel in ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8))) {
            releases[rel.OriginalFilename] = rel;
        }

        var rp = new ReleasePackageBuilder(_logger, nupkgPath);
        var suggestedName = new ReleaseEntryName(packId, SemanticVersion.Parse(packVersion), false, options.TargetRuntime).ToFileName();
        var newPkgPath = rp.CreateReleasePackage((i, pkg) => Path.Combine(releaseDir.FullName, suggestedName));

        _logger.Info("Creating Delta Packages");
        var prev = ReleasePackageBuilder.GetPreviousRelease(_logger, releases.Values, rp, releaseDir.FullName);
        if (prev != null && options.DeltaMode != DeltaMode.None) {
            var deltaBuilder = new DeltaPackageBuilder(_logger);
            var deltaFile = rp.ReleasePackageFile.Replace("-full", "-delta");
            var dp = deltaBuilder.CreateDeltaPackage(prev, rp, deltaFile, options.DeltaMode);
            var deltaEntry = ReleaseEntry.GenerateFromFile(deltaFile);
            releases[deltaEntry.OriginalFilename] = deltaEntry;
        }

        var fullEntry = ReleaseEntry.GenerateFromFile(newPkgPath);
        releases[fullEntry.OriginalFilename] = fullEntry;

        ReleaseEntry.WriteReleaseFile(releases.Values, releaseFilePath);

        // create installer package, sign and notarize
        if (!options.NoPackage) {
            var pkgPath = Path.Combine(releaseDir.FullName, $"{packId}-Setup-[{options.TargetRuntime.ToDisplay(RidDisplayType.NoVersion)}].pkg");

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

        _logger.Info("Done.");
    }
}