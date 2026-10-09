using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Util;

namespace Velopack.Packaging.Unix.Commands;

/// <summary>
/// Builds macOS releases. On macOS it uses Apple's own tools throughout. Off macOS (Linux, Windows), it builds everything except
/// the .pkg installer (pkgbuild and productbuild only exist on macOS), zipping the portable bundle with
/// <see cref="OsxPortableZip"/> in place of ditto. On any OS, a certificate file (--signP12File) signs and notarizes with
/// rcodesign (<see cref="RcodesignTools"/>) instead of codesign and notarytool.
/// </summary>
public class OsxPackCommandRunner : PackageBuilder<OsxPackOptions, OsxPackOptionsValidator>
{
    private const string UpdateMacFileName = "UpdateMac";

    // Set when signing with rcodesign (--signP12File), which is located before any packaging work starts.
    private RcodesignTools _rcodesign;

    public OsxPackCommandRunner(ILogger logger, IFancyConsole console)
        : base(RuntimeOs.OSX, logger, console)
    {
    }

    protected override Task RunCoreAsync(OsxPackOptions options)
    {
        _rcodesign = String.IsNullOrEmpty(options.SignP12File)
            ? null
            : RcodesignTools.Create(Log, options.SignP12File, options.SignP12PasswordFile);

        return base.RunCoreAsync(options);
    }

    private static string GetUpdateMacPath(string appBundlePath)
    {
        return Path.Combine(new OsxStructureBuilder(appBundlePath).MacosDirectory, UpdateMacFileName);
    }

    protected override string ExtractPackDir(string packDirectory)
    {
        // The options validator only accepts a .pkg on macOS, where pkgutil exists.
        if (VelopackRuntimeInfo.IsOSX && packDirectory.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)) {
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
        File.Copy(HelperFile.GetUpdatePath(Options.TargetRuntime, Log), Path.Combine(macosdir, UpdateMacFileName), true);

        // Symlinked directories are not descended into, so a link cycle cannot recurse forever.
        foreach (var f in FileUtil.EnumerateFilesWithoutFollowingSymlinks(new DirectoryInfo(macosdir))) {
            if (BinDetect.IsMachOImage(f.FullName)) {
                Log.Debug(f.FullName + " is a mach-o binary, chmod as executable.");
                Chmod.ChmodFileAsExecutable(f.FullName);
            }
        }

        // Files in the MacOS directory need to be signed, but text files are signed via xattrs, which we don't yet preserve
        // in nupkg releases. Instead we can put it in the Resources dir and symlink to it. Symlinks don't need to be signed.
        var resourcesdir = structure.ResourcesDirectory;
        File.WriteAllText(Path.Combine(resourcesdir, "sq.version"), GenerateNuspecContent());
        // An .app packed by vpk before already has this link; Resources/sq.version was just rewritten, so it is recreated.
        FileUtil.CreateRelativeSymlink(Path.Combine(macosdir, "sq.version"), Path.Combine(resourcesdir, "sq.version"));

        progress(100);
        return Task.FromResult(dir.FullName);
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
        var hasCodesignIdentity = VelopackRuntimeInfo.IsOSX && !String.IsNullOrEmpty(Options.SignAppIdentity);
        if (_rcodesign == null && !hasCodesignIdentity) {
            if (VelopackRuntimeInfo.IsOSX) {
                Log.Warn("Package will not be signed or notarized. Missing the --signAppIdentity and --notaryProfile options.");
            } else {
                Log.Warn("Package will not be signed or notarized, and Apple Silicon Macs will not run it unsigned. " +
                         "Off macOS, sign and notarize with rcodesign: --signP12File, --signP12PasswordFile and --notaryApiKeyFile.");
            }

            return Task.CompletedTask;
        }

        if (Options.SignDisableDeep) {
            Log.Warn("Code signing with --signDisableDeep means that Velopack will only sign binaries it adds, " +
                     "along with the final .app bundle. Please ensure all other binaries and frameworks are signed " +
                     "properly before calling velopack.");
        }

        if (_rcodesign != null) {
            CodeSignWithRcodesign(progress, packDir);
        } else if (VelopackRuntimeInfo.IsOSX) {
            CodeSignWithCodesign(progress, packDir);
        }

        return Task.CompletedTask;
    }

    private string GetEntitlements()
    {
        string entitlements = Options.SignEntitlements;
        if (String.IsNullOrEmpty(entitlements)) {
            Log.Info("No entitlements specified, using default: " +
                     "https://docs.microsoft.com/dotnet/core/install/macos-notarization-issues");
            entitlements = HelperFile.VelopackEntitlements;
        }

        return entitlements;
    }

    private void CodeSignWithRcodesign(Action<int> progress, string packDir)
    {
        var entitlements = GetEntitlements();
        var notarize = !String.IsNullOrEmpty(Options.NotaryApiKeyFile);

        if (Options.SignDisableDeep) {
            // Sign what Velopack added, then seal the bundle without descending into nested bundles.
            Log.Info("Code signing Velopack binaries (rcodesign)...");
            _rcodesign.Sign(GetUpdateMacPath(packDir), HelperFile.VelopackEntitlements, shallow: false, notarize);
            progress(25);

            Log.Info("Code signing application bundle (rcodesign)...");
            _rcodesign.Sign(packDir, entitlements, shallow: true, notarize);
        } else {
            // One call: rcodesign signs every nested bundle and Mach-O before sealing the bundle. UpdateMac gets the
            // hardened runtime and Velopack's own entitlements, as with --signDisableDeep; the app's go on the main executable.
            // Every other Mach-O outside nested bundles (e.g. createdump, native libraries) gets the hardened runtime, as
            // with codesign --deep --options runtime, so a bundle signed now can still be notarized later.
            var updateMac = $"Contents/MacOS/{UpdateMacFileName}";
            var mainExe = $"Contents/MacOS/{Path.GetFileName(MainExePath)}";
            var nestedCode = new List<(string BundleRelativePath, string Entitlements)> { (updateMac, HelperFile.VelopackEntitlements) };
            nestedCode.AddRange(
                RcodesignTools.FindLooseMachOFiles(packDir)
                    .Where(f => !f.Equals(updateMac, StringComparison.OrdinalIgnoreCase) && !f.Equals(mainExe, StringComparison.OrdinalIgnoreCase))
                    .Select(f => (f, (string) null)));

            Log.Info("Code signing application bundle recursively (rcodesign)...");
            _rcodesign.Sign(packDir, entitlements, shallow: false, notarize, nestedCode);
        }

        progress(50);

        if (notarize) {
            progress(-1); // indeterminate
            _rcodesign.NotarizeAndStaple(packDir, Options.NotaryApiKeyFile);
        } else {
            Log.Warn("Package will be signed but not notarized. Missing the --notaryApiKeyFile option.");
        }

        progress(100);
    }

    [SupportedOSPlatform("osx")]
    private void CodeSignWithCodesign(Action<int> progress, string packDir)
    {
        var helper = new OsxBuildTools(Log);
        var keychainPath = Options.Keychain;
        var entitlements = GetEntitlements();

        void InnerSign(Action<int> signProgress)
        {
            if (Options.SignDisableDeep) {
                // when --signDisableDeep is used, we expect the user to have signed everything before calling velopack
                // we only need to sign what we added (UpdateMac) and then the final .app bundle
                Log.Info("Code signing Velopack binaries...");
                helper.CodeSign(Options.SignAppIdentity, HelperFile.VelopackEntitlements, GetUpdateMacPath(packDir), false, keychainPath);
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
                signProgress(90);
                helper.CodeSign(Options.SignAppIdentity, entitlements, packDir, true, keychainPath);
                signProgress(100);
            }
        }
        
        if (!string.IsNullOrEmpty(Options.NotaryProfile)) {
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
        } else {
            Log.Warn("Package will be signed but not notarized. Missing the --notaryProfile option.");
            InnerSign(progress);
            progress(100);
        }
    }

    protected override Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string pkgPath,
        Func<string, VelopackAssetType, string> createAsset)
    {
        // Create the installer package, sign and notarize it. NoInst is always true off macOS, so this only runs on macOS.
        if (!VelopackRuntimeInfo.IsOSX) {
            return Task.CompletedTask;
        }

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
            helper.CreateInstallerPkg(packDir, packTitle, packId, pkgContent, pkgPath, Options.SignInstallIdentity,
                CoreUtil.CreateProgressDelegate(progress, 0, 60));
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

        progress(100);
        return Task.CompletedTask;
    }

    protected override Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
    {
        progress(-1); // indeterminate
        if (VelopackRuntimeInfo.IsOSX) {
            var helper = new OsxBuildTools(Log);
            helper.CreateDittoZip(packDir, outputPath);
        } else {
            OsxPortableZip.Create(Log, packDir, outputPath);
        }

        progress(100);
        return Task.CompletedTask;
    }
}