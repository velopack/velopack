using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Velopack.Packaging.OSX.Commands;

public class OsxPackCommandRunner : PackageBuilder<OsxPackOptions>
{

    public OsxPackCommandRunner(ILogger logger)
        : base(RuntimeOs.OSX, logger)
    {
    }

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir, string nuspecText)
    {
        var dir = TempDir.CreateSubdirectory(Options.PackId + ".app");
        bool deleteAppBundle = false;
        string appBundlePath = Options.PackDirectory;
        if (!Options.PackDirectory.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) {
            appBundlePath = new OsxBundleCommandRunner(Log).Bundle(Options);
            deleteAppBundle = true;
        }

        CopyFiles(new DirectoryInfo(appBundlePath), dir, progress, true);
        var structure = new StructureBuilder(dir.FullName);

        if (deleteAppBundle) {
            Log.Debug("Removing temporary .app bundle.");
            Utility.DeleteFileOrDirectoryHard(appBundlePath);
        }

        var helper = new HelperExe(Log);
        var macosdir = structure.MacosDirectory;
        File.WriteAllText(Path.Combine(macosdir, "sq.version"), nuspecText);
        File.Copy(helper.UpdateMacPath, Path.Combine(macosdir, "UpdateMac"), true);

        foreach (var f in Directory.GetFiles(macosdir, "*", SearchOption.AllDirectories)) {
            if (MachO.IsMachOImage(f)) {
                Log.Debug(f + " is a mach-o binary, chmod as executable.");
                Chmod.ChmodFileAsExecutable(f);
            }
        }

        progress(100);
        return Task.FromResult(dir.FullName);
    }

    protected override Task CodeSign(Action<int> progress, string packDir)
    {
        var zipPath = Path.Combine(TempDir.FullName, "app.zip");
        var helper = new HelperExe(Log);

        // code signing all mach-o binaries
        if (!string.IsNullOrEmpty(Options.SigningAppIdentity) && !string.IsNullOrEmpty(Options.NotaryProfile)) {
            helper.CodeSign(Options.SigningAppIdentity, Options.SigningEntitlements, packDir);
            helper.CreateDittoZip(packDir, zipPath);
            helper.Notarize(zipPath, Options.NotaryProfile);
            helper.Staple(packDir);
            helper.SpctlAssessCode(packDir);
            File.Delete(zipPath);
        } else {
            Log.Warn("Package will not be signed or notarized. Requires the --signAppIdentity and --notaryProfile options.");
        }
        return Task.CompletedTask;
    }

    protected override Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string pkgPath)
    {
        // create installer package, sign and notarize
        if (!Options.NoPackage) {
            var helper = new HelperExe(Log);
            Dictionary<string, string> pkgContent = new() {
                {"welcome", Options.PackageWelcome },
                {"license", Options.PackageLicense },
                {"readme", Options.PackageReadme },
                {"conclusion", Options.PackageConclusion },
            };

            var packTitle = Options.PackTitle ?? Options.PackId;

            helper.CreateInstallerPkg(packDir, packTitle, pkgContent, pkgPath, Options.SigningInstallIdentity);
            if (!string.IsNullOrEmpty(Options.SigningInstallIdentity) && !string.IsNullOrEmpty(Options.NotaryProfile)) {
                helper.Notarize(pkgPath, Options.NotaryProfile);
                helper.Staple(pkgPath);
                helper.SpctlAssessInstaller(pkgPath);
            } else {
                Log.Warn("Package installer (.pkg) will not be Notarized. " +
                         "This is supported with the --signInstallIdentity and --notaryProfile arguments.");
            }
        }
        return Task.CompletedTask;
    }

    protected override Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
    {
        var helper = new HelperExe(Log);
        helper.CreateDittoZip(packDir, outputPath);
        return Task.CompletedTask;
    }
}