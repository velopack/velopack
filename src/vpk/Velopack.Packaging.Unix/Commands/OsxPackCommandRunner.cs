using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Util;

namespace Velopack.Packaging.Unix.Commands;

[SupportedOSPlatform("osx")]
public class OsxPackCommandRunner : PackageBuilder<OsxPackOptions>
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
        File.WriteAllText(Path.Combine(macosdir, "sq.version"), GenerateNuspecContent());
        File.Copy(HelperFile.GetUpdatePath(Options.TargetRuntime, Log), Path.Combine(macosdir, "UpdateMac"), true);

        foreach (var f in Directory.GetFiles(macosdir, "*", SearchOption.AllDirectories)) {
            if (BinDetect.IsMachOImage(f)) {
                Log.Debug(f + " is a mach-o binary, chmod as executable.");
                Chmod.ChmodFileAsExecutable(f);
            }
        }

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
        var helper = new OsxBuildTools(Log);
        var keychainPath = Options.Keychain;

        string entitlements = Options.SignEntitlements;
        if (String.IsNullOrEmpty(entitlements)) {
            Log.Info("No entitlements specified, using default: " +
                     "https://docs.microsoft.com/dotnet/core/install/macos-notarization-issues");
            entitlements = HelperFile.VelopackEntitlements;
        }

        void InnerSign(Action<int> signProgress)
        {
            if (Options.SignDisableDeep) {
                // when --signDisableDeep is used, we expect the user to have signed everything before calling velopack
                // we only need to sign what we added (UpdateMac) and then the final .app bundle
                
                Log.Warn("Code signing with --signDisableDeep means that Velopack will only sign binaries it adds, " +
                         "along with the final .app bundle. Please ensure all other binaries and frameworks are signed " +
                         "properly before calling velopack.");
                
                Log.Info("Code signing Velopack binaries...");
                var structure = new OsxStructureBuilder(packDir);
                var updateMacPath = Path.Combine(structure.MacosDirectory, "UpdateMac");
                helper.CodeSign(Options.SignAppIdentity, entitlements, updateMacPath, false, keychainPath);
                signProgress(25);
                var versionPath = Path.Combine(structure.MacosDirectory, "sq.version");
                helper.CodeSign(Options.SignAppIdentity, entitlements, versionPath, false, keychainPath);
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