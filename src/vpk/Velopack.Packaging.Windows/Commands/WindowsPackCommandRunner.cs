using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.NuGet;
using Velopack.Packaging.Windows.Msi;
using Velopack.Util;
using Velopack.Windows;

namespace Velopack.Packaging.Windows.Commands;

public class WindowsPackCommandRunner : PackageBuilder<WindowsPackOptions>
{
    public WindowsPackCommandRunner(ILogger logger, IFancyConsole console)
        : base(RuntimeOs.Windows, logger, console)
    {
    }

    protected override Task CodeSign(Action<int> progress, string packDir)
    {
        Regex fileExcludeRegex = Options.SignExclude != null ? new Regex(Options.SignExclude) : null;
        var filesToSign = new DirectoryInfo(packDir).GetAllFilesRecursively()
            .Where(x => PathUtil.FileIsLikelyPEImage(x.Name))
            .Where(x => fileExcludeRegex == null || !fileExcludeRegex.IsMatch(x.FullName))
            .Select(x => x.FullName)
            .ToArray();

        SignFilesImpl(progress, filesToSign);

        return Task.CompletedTask;
    }

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir)
    {
        if (!Options.SkipVelopackAppCheck) {
            var compat = new CompatUtil(Log, Console);
            compat.Verify(MainExePath);
        } else {
            Log.Info("Skipping VelopackApp.Build.Run() check.");
        }

        // add nuspec metadata
        ExtraNuspecMetadata["runtimeDependencies"] = GetRuntimeDependencies();
        ExtraNuspecMetadata["shortcutLocations"] = GetShortcutLocations();
        ExtraNuspecMetadata["shortcutAmuid"] = CoreUtil.GetAppUserModelId(Options.PackId);

        // copy files to temp dir, so we can modify them
        var dir = TempDir.CreateSubdirectory("PreprocessPackDirWin");
        CopyFiles(new DirectoryInfo(packDir), dir, progress, true);
        File.WriteAllText(Path.Combine(dir.FullName, CoreUtil.SpecVersionFileName), GenerateNuspecContent());
        packDir = dir.FullName;

        var updatePath = Path.Combine(TempDir.FullName, "Update.exe");
        File.Copy(HelperFile.GetUpdatePath(Options.TargetRuntime, Log), updatePath, true);

        // check for and delete clickonce manifest
        var clickonceManifests = Directory.EnumerateFiles(packDir, "*.application")
            .Where(f => File.ReadAllText(f).Contains("clickonce"))
            .ToArray();
        if (clickonceManifests.Any()) {
            foreach (var manifest in clickonceManifests) {
                Log.Warn(
                    $"ClickOnce manifest found in pack directory: '{Path.GetFileName(manifest)}'. " +
                    $"Velopack does not support building ClickOnce applications, and so will delete this file automatically. " +
                    $"It is recommended that you remove ClickOnce from your .csproj to avoid this warning.");
                File.Delete(manifest);
            }
        }

        // update icon for Update.exe if requested
        if (Options.Icon != null) {
            var editor = new ResourceEdit(updatePath, Log);
            editor.SetExeIcon(Options.Icon);
            editor.Commit();
        }

        File.Copy(updatePath, Path.Combine(packDir, "Squirrel.exe"), true);

        // create a stub for portable packages
        var mainExeName = Options.EntryExecutableName;
        var mainPath = Path.Combine(packDir, mainExeName);
        var stubPath = Path.Combine(packDir, Path.GetFileNameWithoutExtension(mainExeName) + "_ExecutionStub.exe");
        CreateExecutableStubForExe(mainPath, stubPath);

        return Task.FromResult(packDir);
    }

    protected string GetShortcutLocations()
    {
        var flags = GetShortcuts();
        var names = Enum.GetValues(typeof(ShortcutLocation))
            .Cast<ShortcutLocation>()
            .Where(f => f != ShortcutLocation.None && flags.HasFlag(f))
            .Select(f => f.ToString())
            .ToList();

        var shortcutStr = names.Count > 0 ? string.Join(",", names) : "None";
        Log.Info($"Shortcuts: {shortcutStr}");
        return shortcutStr;
    }

    protected string GetRuntimeDependencies()
    {
        if (string.IsNullOrWhiteSpace(Options.Runtimes))
            return "";

        var providedRuntimes = Options.Runtimes.ToLower()
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);

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
            "vcredist144-x86",
            "vcredist144-x64",
            "vcredist144-arm64",
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

        List<string> validated = [];

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

            throw new UserInfoException(
                $"The framework/runtime dependency '{str}' is not valid. See https://docs.velopack.io/packaging/bootstrapping");
        }

        foreach (var str in validated) {
            Log.Info("Runtime Dependency: " + str);
        }

        return String.Join(",", validated);
    }

    protected override Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string targetSetupExe,
        Func<string, VelopackAssetType, string> createAsset)
    {
        var setupExeProgress = Options.BuildMsi
            ? CoreUtil.CreateProgressDelegate(progress, 0, 33)
            : CoreUtil.CreateProgressDelegate(progress, 0, 66);
        var msiProgress = CoreUtil.CreateProgressDelegate(progress, 33, 66);
        var signingProgress = CoreUtil.CreateProgressDelegate(progress, 66, 100);

        List<string> filesToSign = new();

        var bundledZip = new ZipPackage(releasePkg);
        IoUtil.Retry(() => File.Copy(HelperFile.SetupPath, targetSetupExe, true));
        setupExeProgress(10);

        var editor = new ResourceEdit(targetSetupExe, Log);
        editor.SetVersionInfo(bundledZip);
        if (Options.Icon != null) {
            editor.SetExeIcon(Options.Icon);
        }

        editor.Commit();

        setupExeProgress(25);
        Log.Debug("Creating Setup bundle");
        SetupBundle.CreatePackageBundle(targetSetupExe, releasePkg);
        filesToSign.Add(targetSetupExe);
        Log.Info($"Setup bundle created '{Path.GetFileName(targetSetupExe)}'.");
        setupExeProgress(100);

        if (Options.BuildMsi && VelopackRuntimeInfo.IsWindows) {
            var msiName = DefaultName.GetSuggestedMsiName(Options.PackId, Options.Channel, TargetOs);
            var msiPath = createAsset(msiName, VelopackAssetType.Msi);
            var portablePackage = new DirectoryInfo(Path.Combine(TempDir.FullName, "CreatePortablePackage"));
            if (portablePackage.Exists) {
                CompileWixTemplateToMsi(msiProgress, portablePackage, msiPath);
                Log.Info($"MSI created '{Path.GetFileName(msiPath)}'.");
                filesToSign.Add(msiPath);
                msiProgress(100);
            } else {
                Log.Warn("Portable package not found, skipping MSI creation.");
            }
        }

        Log.Debug("Signing Setup files");
        SignFilesImpl(signingProgress, filesToSign.ToArray());
        progress(100);
        return Task.CompletedTask;
    }

    protected override async Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
    {
        var dir = TempDir.CreateSubdirectory("CreatePortablePackage");
        File.Copy(Path.Combine(packDir, "Squirrel.exe"), Path.Combine(dir.FullName, "Update.exe"), true);
        var current = dir.CreateSubdirectory("current");

        CopyFiles(new DirectoryInfo(packDir), current, CoreUtil.CreateProgressDelegate(progress, 0, 30));

        File.Delete(Path.Combine(current.FullName, "Squirrel.exe"));

        // move the stub to the root of the portable package
        var stubPath = Path.Combine(
            current.FullName,
            Path.GetFileNameWithoutExtension(Options.EntryExecutableName) + "_ExecutionStub.exe");
        File.Move(stubPath, Path.Combine(dir.FullName, GetPortableStubFileName()));

        // create a .portable file to indicate this is a portable package
        File.Create(Path.Combine(dir.FullName, ".portable")).Close();

        await EasyZip.CreateZipFromDirectoryAsync(
            Log.ToVelopackLogger(),
            outputPath,
            dir.FullName,
            CoreUtil.CreateProgressDelegate(progress, 40, 100));
        progress(100);
    }

    protected override Dictionary<string, string> GetReleaseMetadataFiles()
    {
        var dict = new Dictionary<string, string>();
        if (Options.Icon != null) dict["setup.ico"] = Options.Icon;
        if (Options.SplashImage != null) dict["splashimage" + Path.GetExtension(Options.SplashImage)] = Options.SplashImage;
        return dict;
    }

    private void CreateExecutableStubForExe(string exeToCopy, string targetStubPath)
    {
        if (!File.Exists(exeToCopy)) {
            throw new ArgumentException($"Cannot create StubExecutable for '{exeToCopy}' because it does not exist.");
        }

        try {
            IoUtil.Retry(() => File.Copy(HelperFile.StubExecutablePath, targetStubPath, true));
            var edit = new ResourceEdit(targetStubPath, Log);
            edit.CopyResourcesFrom(exeToCopy);
            edit.Commit();
        } catch (Exception ex) {
            Log.Error(ex, $"Error creating StubExecutable and copying resources for '{exeToCopy}'. This stub may or may not work properly.");
        }
    }

    private void SignFilesImpl(Action<int> progress, params string[] filePaths)
    {
        var signParams = Options.SignParameters;
        var signTemplate = Options.SignTemplate;
        var signParallel = Options.SignParallel;
        var trustedSignMetadataPath = Options.AzureTrustedSignFile;
        var helper = new CodeSign(Log);

        if (string.IsNullOrEmpty(signParams) && string.IsNullOrEmpty(signTemplate) && string.IsNullOrEmpty(trustedSignMetadataPath)) {
            Log.Warn($"No signing parameters provided, {filePaths.Length} file(s) will not be signed.");
            return;
        }

        if (!string.IsNullOrEmpty(signTemplate)) {
            helper.Sign(filePaths, signTemplate, signParallel, progress, true);
        }

        // signtool.exe does not work if we're not on windows.
        if (!VelopackRuntimeInfo.IsWindows) return;

        if (!string.IsNullOrEmpty(trustedSignMetadataPath)) {
            Log.Info($"Use Azure Trusted Signing service for code signing. Metadata file path: {trustedSignMetadataPath}");

            string dlibPath = GetDlibPath(CancellationToken.None);
            signParams =
                $"/fd SHA256 /tr http://timestamp.acs.microsoft.com /v /debug /td SHA256 /dlib {HelperFile.AzureDlibFileName} /dmdf \"{trustedSignMetadataPath}\"";
            helper.Sign(filePaths, signParams, signParallel, progress, false);
        } else if (!string.IsNullOrEmpty(signParams)) {
            helper.Sign(filePaths, signParams, signParallel, progress, false);
        }
    }

    [SupportedOSPlatform("windows")]
    private string GetDlibPath(CancellationToken cancellationToken)
    {
        // DLib library is required for Azure Trusted Signing. It must be in the same directory as SignTool.exe.
        // https://learn.microsoft.com/azure/trusted-signing/how-to-signing-integrations#download-and-install-the-trusted-signing-dlib-package
        var signToolPath = HelperFile.SignToolPath;
        var signToolDirectory = Path.GetDirectoryName(signToolPath);
        var dlibPath = Path.Combine(signToolDirectory, HelperFile.AzureDlibFileName);
        if (File.Exists(dlibPath)) {
            return dlibPath;
        }

        throw new NotSupportedException("Azure Trusted Signing is not supported in this version of Velopack.");

        // Log.Info($"Downloading Azure Trusted Signing dlib to '{dlibPath}'");
        // var dl = new NuGetDownloader();
        //
        // using MemoryStream nupkgStream = new();
        // await dl.DownloadPackageToStream("Microsoft.Trusted.Signing.Client", "1.*", nupkgStream, cancellationToken);
        //
        // nupkgStream.Position = 0;
        //
        // string parentDir = NugetUtil.BinDirectory + Path.AltDirectorySeparatorChar + "x64" + Path.AltDirectorySeparatorChar;
        //
        // ZipArchive zipPackage = new(nupkgStream);
        // var entries = zipPackage.Entries.Where(x => x.FullName.StartsWith(parentDir, StringComparison.OrdinalIgnoreCase));
        // foreach (var entry in entries) {
        //     var relativePath = entry.FullName.Substring(parentDir.Length);
        //     entry.ExtractToFile(Path.Combine(signToolDirectory, relativePath), true);
        // }
        // return dlibPath;
    }

    [SupportedOSPlatform("windows")]
    private void CompileWixTemplateToMsi(Action<int> progress, DirectoryInfo portableDirectory, string msiFilePath)
    {
        var templateData = MsiBuilder.ConvertOptionsToTemplateData(
            portableDirectory,
            GetShortcuts(),
            GetRuntimeDependencies(),
            Options);
        MsiBuilder.CompileWixMsi(Log, templateData, progress, msiFilePath);
    }

    protected override string[] GetMainExeSearchPaths(string packDirectory, string mainExeName)
    {
        return [
            Path.Combine(packDirectory, mainExeName),
            Path.Combine(packDirectory, mainExeName) + ".exe",
        ];
    }

    private string GetPortableStubFileName() => (Options.PackTitle ?? Options.PackId) + ".exe";

    private ShortcutLocation GetShortcuts()
    {
        var items = Options.Shortcuts
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim());

        ShortcutLocation result = ShortcutLocation.None;

        foreach (var item in items) {
            if (Enum.TryParse<ShortcutLocation>(item, true, out var loc)) {
                result |= loc;
            } else {
                throw new UserInfoException(
                    $"Invalid shortcut locations '{Options.Shortcuts}'. " +
                    $"Valid values for comma delimited list are: {string.Join(", ", Enum.GetNames(typeof(ShortcutLocation)))}.");
            }
        }

        return result;
    }
}