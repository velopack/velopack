using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Squirrel.Packaging.OSX;

public class BundleOsxOptions
{
    public DirectoryInfo ReleaseDir { get; set; }

    public string PackId { get; set; }

    public string PackVersion { get; set; }

    public string PackDirectory { get; set; }

    public string PackAuthors { get; set; }

    public string PackTitle { get; set; }

    public string EntryExecutableName { get; set; }

    public string Icon { get; set; }

    public string BundleId { get; set; }
}

public class ReleasifyOsxOptions
{
    public DirectoryInfo ReleaseDir { get; set; }

    public RID TargetRuntime { get; set; }

    public string BundleDirectory { get; set; }

    public bool IncludePdb { get; set; }

    public string ReleaseNotes { get; set; }

    public bool NoDelta { get; set; }

    public bool NoPackage { get; set; }

    public string PackageWelcome { get; set; }

    public string PackageReadme { get; set; }

    public string PackageLicense { get; set; }

    public string PackageConclusion { get; set; }

    public string SigningAppIdentity { get; set; }

    public string SigningInstallIdentity { get; set; }

    public string SigningEntitlements { get; set; }

    public string NotaryProfile { get; set; }
}

public class OsxCommands
{
    public ILogger Log { get; }

    public OsxCommands(ILogger logger)
    {
        Log = logger;
    }


    public void Bundle(BundleOsxOptions options)
    {
        var icon = options.Icon;
        var packId = options.PackId;
        var packDirectory = options.PackDirectory;
        var packVersion = options.PackVersion;
        var exeName = options.EntryExecutableName;
        var packAuthors = options.PackAuthors;
        var packTitle = options.PackTitle;
        var releaseDir = options.ReleaseDir;

        Log.Info("Generating new '.app' bundle from a directory of application files.");

        var mainExePath = Path.Combine(packDirectory, exeName);
        if (!File.Exists(mainExePath))// || !PlatformUtil.IsMachOImage(mainExePath))
            throw new ArgumentException($"--exeName '{mainExePath}' does not exist or is not a mach-o executable.");

        var appleId = $"com.{packAuthors ?? packId}.{packId}";
        var escapedAppleId = Regex.Replace(appleId, @"[^\w\.]", "_");
        var appleSafeVersion = NuGetVersion.Parse(packVersion).Version.ToString();

        var info = new AppInfo {
            SQPackId = packId,
            SQPackAuthors = packAuthors,
            CFBundleName = packTitle ?? packId,
            //CFBundleDisplayName = packTitle ?? packId,
            CFBundleExecutable = exeName,
            CFBundleIdentifier = options.BundleId ?? escapedAppleId,
            CFBundlePackageType = "APPL",
            CFBundleShortVersionString = appleSafeVersion,
            CFBundleVersion = packVersion,
            CFBundleSignature = "????",
            NSPrincipalClass = "NSApplication",
            NSHighResolutionCapable = true,
            CFBundleIconFile = Path.GetFileName(icon),
        };

        Log.Info("Creating '.app' directory structure");
        var builder = new StructureBuilder(packId, releaseDir.FullName);
        if (Directory.Exists(builder.AppDirectory)) {
            Log.Warn(builder.AppDirectory + " already exists, deleting...");
            Utility.DeleteFileOrDirectoryHard(builder.AppDirectory);
        }

        builder.Build();

        Log.Info("Writing Info.plist");
        var plist = new PlistWriter(Log, info, builder.ContentsDirectory);
        plist.Write();

        Log.Info("Copying resources into new '.app' bundle");
        File.Copy(icon, Path.Combine(builder.ResourcesDirectory, Path.GetFileName(icon)));

        Log.Info("Copying application files into new '.app' bundle");
        Utility.CopyFiles(new DirectoryInfo(packDirectory), new DirectoryInfo(builder.MacosDirectory));

        Log.Info("Bundle created successfully: " + builder.AppDirectory);
    }

    public void Releasify(ReleasifyOsxOptions options)
    {
        var releaseDir = options.ReleaseDir;

        var appBundlePath = options.BundleDirectory;
        Log.Info("Creating Squirrel application from app bundle at: " + appBundlePath);

        Log.Info("Parsing app Info.plist");
        var contentsDir = Path.Combine(appBundlePath, "Contents");

        if (!Directory.Exists(contentsDir))
            throw new Exception("Invalid bundle structure (missing Contents dir)");

        var plistPath = Path.Combine(contentsDir, "Info.plist");
        if (!File.Exists(plistPath))
            throw new Exception("Invalid bundle structure (missing Info.plist)");

        NSDictionary rootDict = (NSDictionary) PropertyListParser.Parse(plistPath);
        var packId = rootDict.ObjectForKey(nameof(AppInfo.SQPackId))?.ToString();
        if (String.IsNullOrWhiteSpace(packId))
            packId = rootDict.ObjectForKey(nameof(AppInfo.CFBundleIdentifier))?.ToString();

        var packAuthors = rootDict.ObjectForKey(nameof(AppInfo.SQPackAuthors))?.ToString();
        if (String.IsNullOrWhiteSpace(packAuthors))
            packAuthors = packId;

        var packTitle = rootDict.ObjectForKey(nameof(AppInfo.CFBundleName))?.ToString();
        var packVersion = rootDict.ObjectForKey(nameof(AppInfo.CFBundleVersion))?.ToString();

        if (String.IsNullOrWhiteSpace(packId))
            throw new InvalidOperationException($"Invalid CFBundleIdentifier in Info.plist: '{packId}'");

        if (String.IsNullOrWhiteSpace(packTitle))
            throw new InvalidOperationException($"Invalid CFBundleName in Info.plist: '{packTitle}'");

        if (String.IsNullOrWhiteSpace(packVersion) || !NuGetVersion.TryParse(packVersion, out var _))
            throw new InvalidOperationException($"Invalid CFBundleVersion in Info.plist: '{packVersion}'");

        Log.Info($"Package valid: '{packId}', Name: '{packTitle}', Version: {packVersion}");

        Log.Info("Adding Squirrel resources to bundle.");
        var nuspecText = NugetConsole.CreateNuspec(
            packId, packTitle, packAuthors, packVersion, options.ReleaseNotes, options.IncludePdb);
        var nuspecPath = Path.Combine(contentsDir, Utility.SpecVersionFileName);

        var helper = new HelperExe(Log);

        // nuspec and UpdateMac need to be in contents dir or this package can't update
        File.WriteAllText(nuspecPath, nuspecText);
        File.Copy(helper.UpdateMacPath, Path.Combine(contentsDir, "UpdateMac"), true);

        var zipPath = Path.Combine(releaseDir.FullName, $"{packId}-{options.TargetRuntime.StringWithNoVersion}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        // code signing all mach-o binaries
        if (!String.IsNullOrEmpty(options.SigningAppIdentity) && !String.IsNullOrEmpty(options.NotaryProfile)) {
            helper.CodeSign(options.SigningAppIdentity, options.SigningEntitlements, appBundlePath);
            helper.CreateDittoZip(appBundlePath, zipPath);
            helper.Notarize(zipPath, options.NotaryProfile);
            helper.Staple(appBundlePath);
            helper.SpctlAssessCode(appBundlePath);
            File.Delete(zipPath);
        } else {
            Log.Warn("Package will not be signed or notarized. Requires the --signAppIdentity and --notaryProfile options.");
        }

        // create a portable zip package from signed/notarized bundle
        Log.Info("Creating final application artifact (zip)");
        helper.CreateDittoZip(appBundlePath, zipPath);

        // create release / delta from notarized .app
        Log.Info("Creating Squirrel Release");
        using var _ = Utility.GetTempDirectory(out var tmp);
        var nuget = new NugetConsole(Log);
        var nupkgPath = nuget.CreatePackageFromNuspecPath(tmp, appBundlePath, nuspecPath);

        var releaseFilePath = Path.Combine(releaseDir.FullName, "RELEASES");
        var releases = new Dictionary<string, ReleaseEntry>();

        ReleaseEntry.BuildReleasesFile(releaseDir.FullName);
        foreach (var rel in ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8))) {
            releases[rel.Filename] = rel;
        }

        var rp = new ReleasePackageBuilder(Log, nupkgPath);
        var suggestedName = ReleasePackageBuilder.GetSuggestedFileName(packId, packVersion, options.TargetRuntime.StringWithNoVersion);
        var newPkgPath = rp.CreateReleasePackage((i, pkg) => Path.Combine(releaseDir.FullName, suggestedName));

        Log.Info("Creating Delta Packages");
        var prev = ReleasePackageBuilder.GetPreviousRelease(Log, releases.Values, rp, releaseDir.FullName, options.TargetRuntime);
        if (prev != null && !options.NoDelta) {
            var deltaBuilder = new DeltaPackageBuilder(Log);
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
                if (!String.IsNullOrEmpty(options.SigningInstallIdentity) && !String.IsNullOrEmpty(options.NotaryProfile)) {
                    helper.Notarize(pkgPath, options.NotaryProfile);
                    helper.Staple(pkgPath);
                    helper.SpctlAssessInstaller(pkgPath);
                } else {
                    Log.Warn("Package installer (.pkg) will not be Notarized. " +
                             "This is supported with the --signInstallIdentity and --notaryProfile arguments.");
                }
            } else {
                Log.Warn("Package installer (.pkg) will not be created - this is only supported on OSX.");
            }
        }

        Log.Info("Done.");
    }
}