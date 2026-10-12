using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Util;

namespace Velopack.Packaging.Unix.Commands;

[SupportedOSPlatform("osx")]
public class OsxPackCommandRunner : PackageBuilder<OsxPackOptions, OsxPackOptionsValidator>
{
    public OsxPackCommandRunner(ILogger logger, IFancyConsole console)
        : base(RuntimeOs.OSX, logger, console)
    {
    }

    protected override string ExtractPackDir(string packDirectory)
    {
        if (packDirectory.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)) {
            Log.Warn("Extracting application bundle from .pkg installer. This is not recommended for production use.");
            var dir = Path.Combine(TempDir.FullName, "pkg_extract");
            var helper = new OsxBuildTools(Log);
            return helper.ExtractPkgToAppBundle(packDirectory, dir);
        }
        
        return packDirectory;
    }

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir)
    {
        var packTitle = Options.PackTitle ?? Options.PackId;
        var dir = TempDir.CreateSubdirectory(packTitle + ".app");
        bool deleteAppBundle = false;
        string appBundlePath = packDir;
        if (!packDir.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) {
            appBundlePath = new OsxBundleCommandRunner(Log).Bundle(Options);
            deleteAppBundle = true;
        }

        CopyFiles(new DirectoryInfo(appBundlePath), dir, progress, true);

        if (deleteAppBundle) {
            Log.Debug("Removing temporary .app bundle.");
            IoUtil.DeleteFileOrDirectoryHard(appBundlePath);
        }

        var structure = new OsxStructureBuilder(dir.FullName);
        var macosdir = structure.MacosDirectory;
        File.Copy(HelperFile.GetUpdatePath(Options.TargetRuntime, Log), Path.Combine(macosdir, "UpdateMac"), true);

        // file / url associations live in a nested helper bundle (never in the main Info.plist). The metadata must be
        // added before sq.version is written below.
        var given = !string.IsNullOrWhiteSpace(Options.FileAssociations) || !string.IsNullOrWhiteSpace(Options.UrlProtocols);
        var inherited = OsxAssociationHelper.Find(dir.FullName);
        if (given) {
            var (extensions, schemes) = AddAssociationMetadata(Options.FileAssociations, Options.UrlProtocols, Options.AssociationLaunchMode);
            BuildAssociationHelper(structure, extensions, schemes);
        } else if (inherited != null) {
            // a helper built by `vpk bundle` (or a previous pack) is kept, and the mode it was built with is carried
            // into the manifest so the pack step needs no association options repeated
            var modeText = OsxBuildTools.ReadPlistValue(Path.Combine(inherited, "Contents", "Info.plist"), OsxAssociationHelper.LaunchModePlistKey);
            var mode = Enum.TryParse<AssociationLaunchMode>(modeText?.Trim(), true, out var parsed) ? parsed : AssociationLaunchMode.NewInstance;
            Log.Info($"Using the association helper found in the bundle ({Path.GetFileName(inherited)}, {mode}).");
            AddAssociationMetadata("", "", mode, hasAssociations: true);
        }

        foreach (var f in Directory.GetFiles(macosdir, "*", SearchOption.AllDirectories)) {
            if (BinDetect.IsMachOImage(f)) {
                Log.Debug(f + " is a mach-o binary, chmod as executable.");
                Chmod.ChmodFileAsExecutable(f);
            }
        }

        // Files in the MacOS directory need to be signed, but text files are signed via xattrs, which we don't yet preserve
        // in nupkg releases. Instead we can put it in the Resources dir and symlink to it. Symlinks don't need to be signed.
        var resourcesdir = structure.ResourcesDirectory;
        File.WriteAllText(Path.Combine(resourcesdir, "sq.version"), GenerateNuspecContent());
        SymbolicLink.Create(Path.Combine(macosdir, "sq.version"), Path.Combine(resourcesdir, "sq.version"), false, true);

        progress(100);
        return Task.FromResult(dir.FullName);
    }

    /// <summary>
    /// Builds the association helper bundle from the pack options, taking the main app's identity (bundle id,
    /// display name, icon) from its final Info.plist so a prebuilt .app is matched in Finder "Open With".
    /// </summary>
    private void BuildAssociationHelper(OsxStructureBuilder structure, string[] extensions, string[] schemes)
    {
        var plist = Path.Combine(structure.ContentsDirectory, PlistWriter.PlistFileName);
        string Read(string key) => OsxBuildTools.ReadPlistValue(plist, key);

        var mainBundleId = Read("CFBundleIdentifier")
                           ?? Options.BundleId
                           ?? OsxBundleCommandRunner.GetDefaultBundleId(Options.PackId, Options.PackAuthors);
        var displayName = Read("CFBundleDisplayName") ?? Read("CFBundleName") ?? Options.PackTitle ?? Options.PackId;

        // the helper shows the main app's icon in Finder / Safari: --icon, else the plist's CFBundleIconFile, else
        // CFBundleIconName (an icon in Assets.car cannot be reused, so warn when falling back to the default)
        var icon = Options.Icon;
        var iconFile = Read("CFBundleIconFile");
        if (icon == null && iconFile != null) {
            icon = new[] { iconFile, iconFile + ".icns" }
                .Select(f => Path.Combine(structure.ResourcesDirectory, f))
                .FirstOrDefault(File.Exists);
        }

        var iconName = Read("CFBundleIconName");
        if (icon == null && iconName != null) {
            var path = Path.Combine(structure.ResourcesDirectory, iconName + ".icns");
            icon = File.Exists(path) ? path : null;
        }

        if (icon == null) {
            icon = HelperFile.GetDefaultAppIcon(RuntimeOs.OSX);
            if (iconName != null) {
                Log.Warn("The association helper will use the default Velopack icon in Finder/Safari; pass --icon with the app's .icns to match.");
            }
        }

        if (Read("CFBundleURLTypes") != null || Read("CFBundleDocumentTypes") != null) {
            Log.Warn(
                "Info.plist also declares CFBundleURLTypes/CFBundleDocumentTypes. Launch Services may route activations to the " +
                "main bundle, where they arrive as Apple Events for the app's own framework and NOT as Velopack OnFileAssoc/OnUrlProto " +
                "callbacks. Remove those keys from Info.plist or do not pass --fileAssociations/--urlProtocols.");
        }

        var info = OsxAssociationHelper.CreateInfo(
            displayName,
            mainBundleId,
            SemanticVersion.Parse(Options.PackVersion).Version.ToString(),
            Options.PackVersion,
            extensions,
            schemes,
            Options.AssociationLaunchMode);
        OsxAssociationHelper.Build(Log, structure.AppDirectory, info, HelperFile.GetAssocHelperPath(), icon);
    }

    protected override string[] GetMainExeSearchPaths(string packDirectory, string mainExeName)
    {
        if (packDirectory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) {
            // if the user pre-bundled the app, we need to look in the Contents/MacOS directory
            return new[] { Path.Combine(packDirectory, "Contents", "MacOS", mainExeName) };
        }

        return new[] { Path.Combine(packDirectory, mainExeName) };
    }

    protected override Task CodeSign(Action<int> progress, string packDir)
    {
        var helper = new OsxBuildTools(Log);
        var keychainPath = Options.Keychain;

        string entitlements = Options.SignEntitlements;
        if (String.IsNullOrEmpty(entitlements)) {
            Log.Info("No entitlements specified, using default: " +
                     "https://docs.microsoft.com/dotnet/core/install/macos-notarization-issues");
            entitlements = HelperFile.VelopackEntitlements;
        }

        // the association helper is always signed with Velopack's entitlements (never the user's: a sandboxed
        // caller cannot pass arguments through NSWorkspace), inside-out before the outer bundle is sealed
        var assocHelpers = OsxAssociationHelper.FindAll(packDir);

        void SignAssociationHelpers()
        {
            foreach (var helperApp in assocHelpers) {
                Log.Info("Code signing association helper bundle...");
                helper.CodeSign(Options.SignAppIdentity, HelperFile.VelopackEntitlements, helperApp, false, keychainPath);
            }
        }

        void InnerSign(Action<int> signProgress)
        {
            if (Options.SignDisableDeep) {
                // when --signDisableDeep is used, we expect the user to have signed everything before calling velopack
                // we only need to sign what we added (UpdateMac, the association helper) and then the final .app bundle

                Log.Warn("Code signing with --signDisableDeep means that Velopack will only sign binaries it adds, " +
                         "along with the final .app bundle. Please ensure all other binaries and frameworks are signed " +
                         "properly before calling velopack.");

                Log.Info("Code signing Velopack binaries...");
                var structure = new OsxStructureBuilder(packDir);
                var updateMacPath = Path.Combine(structure.MacosDirectory, "UpdateMac");
                helper.CodeSign(Options.SignAppIdentity, HelperFile.VelopackEntitlements, updateMacPath, false, keychainPath);
                signProgress(40);
                SignAssociationHelpers();
                signProgress(50);

                Log.Info("Code signing application bundle...");
                helper.CodeSign(Options.SignAppIdentity, entitlements, packDir, false, keychainPath);
                signProgress(100);
            } else {
                // dotnet macos tfm's (xamarin) incorrectly store binaries in "MonoBundle" so are not signed by --deep
                var monoBundlePath = Path.Combine(packDir, "Contents", "MonoBundle");
                if (Directory.Exists(monoBundlePath)) {
                    Log.Warn("Detected invalid Xamarin MonoBundle, fixing code signing...");
                    var files = Directory.EnumerateFiles(monoBundlePath).ToArray();
                    int processed = 0;
                    Parallel.ForEach(
                        files,
                        new ParallelOptions() { MaxDegreeOfParallelism = 4 },
                        (file) => {
                            helper.CodeSign(Options.SignAppIdentity, entitlements, file, false, keychainPath);
                            Interlocked.Increment(ref processed);
                            signProgress(Math.Min((int) (processed * 100d / files.Length), 90));
                        });
                    Thread.Sleep(100); // not sure why but things break without this
                }
                
                // sign the rest of the .app with --deep to recursively sign
                // this does not work 100% of the time, but it does work in a surprising number of cases so it is the default
                // use --signDisableDeep to disable this behavior, which requires you to sign things before calling velopack
                Log.Info("Code signing application bundle recursively (with --deep)...");
                helper.CodeSign(Options.SignAppIdentity, entitlements, packDir, true, keychainPath);
                signProgress(80);

                if (assocHelpers.Length > 0) {
                    // --deep signed the helper with the user's entitlements; re-sign it with Velopack's, then reseal
                    // the outer bundle (non-deep, -f) so its seal covers the helper's new signature
                    SignAssociationHelpers();
                    signProgress(90);
                    Log.Info("Resealing application bundle...");
                    helper.CodeSign(Options.SignAppIdentity, entitlements, packDir, false, keychainPath);
                }

                signProgress(100);
            }
        }
        
        if (!string.IsNullOrEmpty(Options.SignAppIdentity) && !string.IsNullOrEmpty(Options.NotaryProfile)) {
            var zipPath = Path.Combine(TempDir.FullName, "notarize.zip");
            InnerSign(CoreUtil.CreateProgressDelegate(progress, 0, 50));
            helper.CreateDittoZip(packDir, zipPath);
            progress(60);
            helper.Notarize(zipPath, Options.NotaryProfile, keychainPath);
            progress(90);
            helper.Staple(packDir);
            progress(95);
            helper.SpctlAssessCode(packDir);
            File.Delete(zipPath);
            progress(100);
        } else if (!string.IsNullOrEmpty(Options.SignAppIdentity)) {
            Log.Warn("Package will be signed but not notarized. Missing the --notaryProfile option.");
            InnerSign(progress);
            progress(100);
        } else {
            Log.Warn("Package will not be signed or notarized. Missing the --signAppIdentity and --notaryProfile options.");
        }
        return Task.CompletedTask;
    }

    protected override Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string pkgPath, Func<string, VelopackAssetType, string> createAsset)
    {
        // create installer package, sign and notarize
        if (!Options.NoInst) {
            var helper = new OsxBuildTools(Log);
            Dictionary<string, string> pkgContent = new() {
                {"welcome", Options.InstWelcome },
                {"license", Options.InstLicense },
                {"readme", Options.InstReadme },
                {"conclusion", Options.InstConclusion },
            };

            var packTitle = Options.PackTitle ?? Options.PackId;
            var packId = Options.PackId;

            if (!string.IsNullOrEmpty(Options.SignInstallIdentity) && !string.IsNullOrEmpty(Options.NotaryProfile)) {
                helper.CreateInstallerPkg(packDir, packTitle, packId, pkgContent, pkgPath, Options.SignInstallIdentity, CoreUtil.CreateProgressDelegate(progress, 0, 60));
                progress(-1); // indeterminate
                helper.Notarize(pkgPath, Options.NotaryProfile, Options.Keychain);
                progress(80);
                helper.Staple(pkgPath);
                progress(90);
                helper.SpctlAssessInstaller(pkgPath);
            } else {
                Log.Warn("Package installer (.pkg) will not be Notarized. " +
                         "This is supported with the --signInstallIdentity and --notaryProfile arguments.");
                helper.CreateInstallerPkg(packDir, packTitle, packId, pkgContent, pkgPath, Options.SignInstallIdentity, progress);
            }
        }
        progress(100);
        return Task.CompletedTask;
    }

    protected override Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
    {
        progress(-1); // indeterminate
        var helper = new OsxBuildTools(Log);
        helper.CreateDittoZip(packDir, outputPath);
        progress(100);
        return Task.CompletedTask;
    }
}