using System.Runtime.Versioning;
using AsmResolver.PE;
using AsmResolver.PE.File.Headers;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Velopack.Compression;
using Velopack.NuGet;
using Velopack.Packaging.Exceptions;
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

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir)
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
        File.WriteAllText(Path.Combine(dir.FullName, "sq.version"), GenerateNuspecContent());
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

    protected override void VerifyMainBinary(string mainExePath)
    {
        var binary = PEImage.FromFile(mainExePath);

        var machine = binary.MachineType switch {
            MachineType.Arm64 => RuntimeCpu.arm64,
            MachineType.Amd64 => RuntimeCpu.x64,
            MachineType.I386 => RuntimeCpu.x86,
            _ => throw new Exception($"Unsupported PE machine type '{binary.MachineType}'.")
        };

        if (machine != VelopackRuntimeInfo.SystemArch) {
            Log.Warn($"Skipping VelopackApp verification, because system architecture ({VelopackRuntimeInfo.SystemArch}) does not match main binary architecture ({machine}).");
            return;
        }

        base.VerifyMainBinary(mainExePath);
    }

    protected override string GetRuntimeDependencies()
    {
        if (string.IsNullOrWhiteSpace(Options.Runtimes))
            return "";

        var providedRuntimes = Options.Runtimes.ToLower()
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

        var valid = new string[] {
            "webview2",
            "vcredist100-x86",
            "vcredist100-x64",
            "vcredist110-x86",
            "vcredist110-x64",
            "vcredist120-x86",
            "vcredist120-x64",
            "vcredist140-x86",
            "vcredist140-x64",
            "vcredist141-x86",
            "vcredist141-x64",
            "vcredist142-x86",
            "vcredist142-x64",
            "vcredist143-x86",
            "vcredist143-x64",
            "vcredist143-arm64",
            "net45",
            "net451",
            "net452",
            "net46",
            "net461",
            "net462",
            "net47",
            "net471",
            "net472",
            "net48",
            "net481",
        };

        List<string> validated = new();

        foreach (var str in providedRuntimes) {
            if (valid.Contains(str)) {
                validated.Add(str);
                continue;
            }

#pragma warning disable CS0618 // Type or member is obsolete
            if (Runtimes.DotnetInfo.TryParse(str, out var dotnetInfo)) {
                if (dotnetInfo.MinVersion.Major < 5)
                    throw new UserInfoException($"The framework/runtime dependency '{str}' is not valid. Only .NET 5+ is supported.");
                validated.Add(dotnetInfo.Id);
                continue;
            }
#pragma warning restore CS0618 // Type or member is obsolete

            throw new UserInfoException($"The framework/runtime dependency '{str}' is not valid. See https://github.com/velopack/velopack/blob/master/docs/bootstrapping.md");
        }

        foreach (var str in validated) {
            Log.Info("Runtime Dependency: " + str);
        }

        return String.Join(",", validated);
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