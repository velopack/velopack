using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Spectre.Console;
using Velopack.Compression;
using Velopack.NuGet;
using Velopack.Windows;

namespace Velopack.Packaging.Windows.Commands;

[SupportedOSPlatform("windows")]
public class WindowsPackCommandRunner : PackageBuilder<WindowsPackOptions>
{
    public WindowsPackCommandRunner(ILogger logger)
        : base(RuntimeOs.Windows, logger)
    {
    }

    protected override Task CodeSign(Action<int> progress, string packDir)
    {
        var filesToSign = new DirectoryInfo(packDir).GetAllFilesRecursively()
            .Where(x => Options.SignSkipDll ? Utility.PathPartEndsWith(x.Name, ".exe") : Utility.FileIsLikelyPEImage(x.Name))
            .Select(x => x.FullName)
            .ToArray();

        SignFilesImpl(Options, packDir, progress, filesToSign);
        return Task.CompletedTask;
    }

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir, string nuspecText)
    {
        // fail the release if this is a clickonce application
        if (Directory.EnumerateFiles(packDir, "*.application").Any(f => File.ReadAllText(f).Contains("clickonce"))) {
            throw new ArgumentException(
                "Velopack does not support building releases for ClickOnce applications. " +
                "Please publish your application to a folder without ClickOnce.");
        }

        // copy files to temp dir, so we can modify them
        var dir = TempDir.CreateSubdirectory("PreprocessPackDirWin");
        CopyFiles(new DirectoryInfo(packDir), dir, progress, true);
        File.WriteAllText(Path.Combine(dir.FullName, "sq.version"), nuspecText);
        packDir = dir.FullName;

        var updatePath = Path.Combine(TempDir.FullName, "Update.exe");
        File.Copy(HelperFile.GetUpdatePath(), updatePath, true);

        // update icon for Update.exe if requested
        if (Options.Icon != null && VelopackRuntimeInfo.IsWindows) {
            Rcedit.SetExeIcon(updatePath, Options.Icon);
        } else if (Options.Icon != null) {
            Log.Warn("Unable to set icon for Update.exe (only supported on windows).");
        }

        File.Copy(updatePath, Path.Combine(packDir, "Squirrel.exe"), true);
        return Task.FromResult(packDir);
    }

    protected override string GenerateNuspecContent(string packId, string packTitle, string packAuthors, string packVersion, string releaseNotes, string packDir, string mainExeName)
    {
        // check provided runtimes
        IEnumerable<Runtimes.RuntimeInfo> requiredFrameworks = Enumerable.Empty<Runtimes.RuntimeInfo>();
        if (!string.IsNullOrWhiteSpace(Options.Runtimes)) {
            requiredFrameworks = Options.Runtimes
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Runtimes.GetRuntimeByName);
        }

        if (requiredFrameworks.Where(f => f == null).Any())
            throw new ArgumentException("Invalid target frameworks string.");

        // generate nuspec
        var initial = base.GenerateNuspecContent(packId, packTitle, packAuthors, packVersion, releaseNotes, packDir, mainExeName);
        var tmpPath = Path.Combine(TempDir.FullName, "tmpwin.nuspec");
        File.WriteAllText(tmpPath, initial);
        NuspecManifest.SetMetadata(tmpPath, mainExeName, requiredFrameworks.Select(r => r.Id), Options.TargetRuntime, null);
        return File.ReadAllText(tmpPath);
    }

    protected override Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string targetSetupExe)
    {
        var bundledzp = new ZipPackage(releasePkg);
        Utility.Retry(() => File.Copy(HelperFile.SetupPath, targetSetupExe, true));
        progress(10);
        if (VelopackRuntimeInfo.IsWindows) {
            Rcedit.SetPEVersionBlockFromPackageInfo(targetSetupExe, bundledzp, Options.Icon);
        } else {
            Log.Warn("Unable to set Setup.exe icon (only supported on windows)");
        }
        progress(25);
        Log.Debug($"Creating Setup bundle");
        SetupBundle.CreatePackageBundle(targetSetupExe, releasePkg);
        progress(50);
        Log.Debug("Signing Setup bundle");
        var targetDir = Path.GetDirectoryName(targetSetupExe);
        SignFilesImpl(Options, targetDir, Utility.CreateProgressDelegate(progress, 50, 100), targetSetupExe);
        Log.Debug($"Setup bundle created '{Path.GetFileName(targetSetupExe)}'.");
        progress(100);
        return Task.CompletedTask;
    }

    protected override async Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
    {
        var dir = TempDir.CreateSubdirectory("CreatePortablePackage");
        File.Copy(Path.Combine(packDir, "Squirrel.exe"), Path.Combine(dir.FullName, "Update.exe"), true);
        var current = dir.CreateSubdirectory("current");

        CopyFiles(new DirectoryInfo(packDir), current, Utility.CreateProgressDelegate(progress, 0, 30));

        var mainExeName = Options.EntryExecutableName ?? Options.PackId + ".exe";
        var mainExe = Path.Combine(packDir, mainExeName);
        CreateExecutableStubForExe(mainExe, dir.FullName);

        await EasyZip.CreateZipFromDirectoryAsync(Log, outputPath, dir.FullName, Utility.CreateProgressDelegate(progress, 40, 100));
        progress(100);
    }

    protected override Dictionary<string, string> GetReleaseMetadataFiles()
    {
        var dict = new Dictionary<string, string>();
        if (Options.Icon != null) dict["setup.ico"] = Options.Icon;
        if (Options.SplashImage != null) dict["splashimage" + Path.GetExtension(Options.SplashImage)] = Options.SplashImage;
        return dict;
    }

    private void CreateExecutableStubForExe(string exeToCopy, string targetDir)
    {
        try {
            var target = Path.Combine(targetDir, Path.GetFileName(exeToCopy));
            Utility.Retry(() => File.Copy(HelperFile.StubExecutablePath, target, true));
            Utility.Retry(() => {
                if (VelopackRuntimeInfo.IsWindows) {
                    using var writer = new Microsoft.NET.HostModel.ResourceUpdater(target, true);
                    writer.AddResourcesFromPEImage(exeToCopy);
                    writer.Update();
                } else {
                    Log.Warn($"Cannot set resources/icon for {target} (only supported on windows).");
                }
            });
        } catch (Exception ex) {
            Log.Error(ex, $"Error creating StubExecutable and copying resources for '{exeToCopy}'. This stub may or may not work properly.");
        }
    }

    private void SignFilesImpl(WindowsSigningOptions options, string rootDir, Action<int> progress, params string[] filePaths)
    {
        var signParams = options.SignParameters;
        var signTemplate = options.SignTemplate;
        var signParallel = options.SignParallel;
        var helper = new CodeSign(Log);

        if (string.IsNullOrEmpty(signParams) && string.IsNullOrEmpty(signTemplate)) {
            Log.Warn($"No signing paramaters provided, {filePaths.Length} file(s) will not be signed.");
            return;
        }

        if (!string.IsNullOrEmpty(signTemplate)) {
            Log.Info($"Preparing to sign {filePaths.Length} files with custom signing template");
            for (var i = 0; i < filePaths.Length; i++) {
                var f = filePaths[i];
                helper.SignPEFileWithTemplate(f, signTemplate);
                progress((int) ((double) i / filePaths.Length * 100));
            }
            return;
        }

        // signtool.exe does not work if we're not on windows.
        if (!VelopackRuntimeInfo.IsWindows) return;

        if (!string.IsNullOrEmpty(signParams)) {
            Log.Info($"Preparing to sign {filePaths.Length} files with embedded signtool.exe with parallelism of {signParallel}");
            helper.SignPEFilesWithSignTool(rootDir, filePaths, signParams, signParallel, progress);
        }
    }
}