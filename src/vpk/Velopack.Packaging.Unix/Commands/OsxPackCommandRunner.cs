using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Abstractions;
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
        var monoBundlePath = Path.Combine(packDir, "Contents", "MonoBundle");

        string entitlements = Options.SignEntitlements;
        if (String.IsNullOrEmpty(entitlements)) {
            Log.Info("No entitlements specified, using default: " +
                     "https://docs.microsoft.com/dotnet/core/install/macos-notarization-issues");
            entitlements = HelperFile.VelopackEntitlements;
        }

        void InnerSign()
        {
            if (Directory.Exists(monoBundlePath)) {
                Log.Warn("Detected invalid Xamarin MonoBundle, fixing code signing...");
                var files = Directory.EnumerateFiles(monoBundlePath).ToArray();
                int processed = 0;
                Parallel.ForEach(files, new ParallelOptions() { MaxDegreeOfParallelism = 4}, (file) => {
                    helper.CodeSign(Options.SignAppIdentity, entitlements, file, false, keychainPath);
                    Interlocked.Increment(ref processed);
                    progress(Math.Min((int)(processed * 100d / files.Length), 95));
                });
                Thread.Sleep(100);
            }
            Log.Info("Code signing application bundle...");
            progress(-1); // indeterminate
            helper.CodeSign(Options.SignAppIdentity, entitlements, packDir, true, keychainPath);
        }
        
        // code signing all mach-o binaries
        if (!string.IsNullOrEmpty(Options.SignAppIdentity) && !string.IsNullOrEmpty(Options.NotaryProfile)) {
            var zipPath = Path.Combine(TempDir.FullName, "notarize.zip");
            InnerSign();
            helper.CreateDittoZip(packDir, zipPath);
            helper.Notarize(zipPath, Options.NotaryProfile, keychainPath);
            helper.Staple(packDir);
            helper.SpctlAssessCode(packDir);
            File.Delete(zipPath);
        } else if (!string.IsNullOrEmpty(Options.SignAppIdentity)) {
            Log.Warn("Package will be signed but not notarized. Missing the --notaryProfile option.");
            InnerSign();
        } else {
            Log.Warn("Package will not be signed or notarized. Missing the --signAppIdentity and --notaryProfile options.");
        }
        progress(100);
        return Task.CompletedTask;
    }

    protected override Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string pkgPath)
    {
        // create installer package, sign and notarize
        if (!Options.NoInst) {
            CreateSetupPackageImpl(progress, Options, Log, packDir, pkgPath);
        }
        progress(100);
        return Task.CompletedTask;
    }

    internal static void CreateSetupPackageImpl(Action<int> progress, IOsxSetupPackageOptions options, ILogger log, string packDir, string pkgPath)
    {
        var helper = new OsxBuildTools(log);
        Dictionary<string, string> pkgContent = new() {
                {"welcome", options.InstWelcome },
                {"license", options.InstLicense },
                {"readme", options.InstReadme },
                {"conclusion", options.InstConclusion },
            };

        var packTitle = options.PackTitle ?? options.PackId;
        var packId = options.PackId;

        if (!string.IsNullOrEmpty(options.SignInstallIdentity) && !string.IsNullOrEmpty(options.NotaryProfile)) {
            helper.CreateInstallerPkg(packDir, packTitle, packId, pkgContent, pkgPath, options.SignInstallIdentity, CoreUtil.CreateProgressDelegate(progress, 0, 60));
            progress(-1); // indeterminate
            helper.Notarize(pkgPath, options.NotaryProfile, options.Keychain);
            progress(80);
            helper.Staple(pkgPath);
            progress(90);
            helper.SpctlAssessInstaller(pkgPath);
        } else {
            log.Warn("Package installer (.pkg) will not be Notarized. " +
                     "This is supported with the --signInstallIdentity and --notaryProfile arguments.");
            helper.CreateInstallerPkg(packDir, packTitle, packId, pkgContent, pkgPath, options.SignInstallIdentity, progress);
        }
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