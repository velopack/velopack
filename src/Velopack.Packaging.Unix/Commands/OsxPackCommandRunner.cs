using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Abstractions;

namespace Velopack.Packaging.Unix.Commands;

[SupportedOSPlatform("osx")]
public class OsxPackCommandRunner : PackageBuilder<OsxPackOptions>
{
    public OsxPackCommandRunner(ILogger logger, IFancyConsole console)
        : base(RuntimeOs.OSX, logger, console)
    {
    }

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir)
    {
        var dir = TempDir.CreateSubdirectory(Options.PackId + ".app");
        bool deleteAppBundle = false;
        string appBundlePath = Options.PackDirectory;
        if (!Options.PackDirectory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) {
            appBundlePath = new OsxBundleCommandRunner(Log).Bundle(Options);
            deleteAppBundle = true;
        }

        CopyFiles(new DirectoryInfo(appBundlePath), dir, progress, true);

        if (deleteAppBundle) {
            Log.Debug("Removing temporary .app bundle.");
            Utility.DeleteFileOrDirectoryHard(appBundlePath);
        }

        var structure = new OsxStructureBuilder(dir.FullName);
        var macosdir = structure.MacosDirectory;
        File.WriteAllText(Path.Combine(macosdir, "sq.version"), GenerateNuspecContent());
        File.Copy(HelperFile.GetUpdatePath(), Path.Combine(macosdir, "UpdateMac"), true);

        foreach (var f in Directory.GetFiles(macosdir, "*", SearchOption.AllDirectories)) {
            if (MachO.IsMachOImage(f)) {
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
        return base.GetMainExeSearchPaths(packDirectory, mainExeName);
    }

    protected override Task CodeSign(Action<int> progress, string packDir)
    {
        var helper = new OsxBuildTools(Log);
        // code signing all mach-o binaries
        if (!string.IsNullOrEmpty(Options.SigningAppIdentity) && !string.IsNullOrEmpty(Options.NotaryProfile)) {
            progress(-1); // indeterminate
            var zipPath = Path.Combine(TempDir.FullName, "notarize.zip");
            helper.CodeSign(Options.SigningAppIdentity, Options.SigningEntitlements, packDir);
            helper.CreateDittoZip(packDir, zipPath);
            helper.Notarize(zipPath, Options.NotaryProfile);
            helper.Staple(packDir);
            helper.SpctlAssessCode(packDir);
            File.Delete(zipPath);
            progress(100);
        } else if (!string.IsNullOrEmpty(Options.SigningAppIdentity)) {
            progress(-1); // indeterminate
            Log.Warn("Package will be signed, but [underline]not notarized[/]. Missing the --notaryProfile option.");
            helper.CodeSign(Options.SigningAppIdentity, Options.SigningEntitlements, packDir);
            progress(100);
        } else {
            Log.Warn("Package will not be signed or notarized. Missing the --signAppIdentity and --notaryProfile options.");
        }
        return Task.CompletedTask;
    }

    protected override Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string pkgPath)
    {
        // create installer package, sign and notarize
        if (!Options.NoPackage) {
            var helper = new OsxBuildTools(Log);
            Dictionary<string, string> pkgContent = new() {
                {"welcome", Options.PackageWelcome },
                {"license", Options.PackageLicense },
                {"readme", Options.PackageReadme },
                {"conclusion", Options.PackageConclusion },
            };

            var packTitle = Options.PackTitle ?? Options.PackId;
            var packId = Options.PackId;

            if (!string.IsNullOrEmpty(Options.SigningInstallIdentity) && !string.IsNullOrEmpty(Options.NotaryProfile)) {
                helper.CreateInstallerPkg(packDir, packTitle, packId, pkgContent, pkgPath, Options.SigningInstallIdentity, Utility.CreateProgressDelegate(progress, 0, 60));
                progress(-1); // indeterminate
                helper.Notarize(pkgPath, Options.NotaryProfile);
                progress(80);
                helper.Staple(pkgPath);
                progress(90);
                helper.SpctlAssessInstaller(pkgPath);
            } else {
                Log.Warn("Package installer (.pkg) will not be Notarized. " +
                         "This is supported with the --signInstallIdentity and --notaryProfile arguments.");
                helper.CreateInstallerPkg(packDir, packTitle, packId, pkgContent, pkgPath, Options.SigningInstallIdentity, progress);
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