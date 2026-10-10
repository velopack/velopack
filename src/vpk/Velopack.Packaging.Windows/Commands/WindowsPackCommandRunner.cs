using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using AsmResolver.PE;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.NuGet;
using Velopack.Packaging.Windows.Msi;
using Velopack.Packaging.Windows.Signing;
using Velopack.Util;
using Velopack.Windows;

namespace Velopack.Packaging.Windows.Commands;

public class WindowsPackCommandRunner : PackageBuilder<WindowsPackOptions, WindowsPackOptionsValidator>
{
    private AzureTrustedSigner _azureTrustedSigner;

    public WindowsPackCommandRunner(ILogger logger, IFancyConsole console)
        : base(RuntimeOs.Windows, logger, console)
    {
    }

    protected override async Task RunCoreAsync(WindowsPackOptions options)
    {
        try {
            await base.RunCoreAsync(options).ConfigureAwait(false);
        } finally {
            // the Azure session (credential + certificate chain) is shared by every signing phase of one pack
            _azureTrustedSigner?.Dispose();
            _azureTrustedSigner = null;
        }
    }

    protected override Task CodeSign(Action<int> progress, string packDir)
    {
        Regex fileExcludeRegex = Options.SignExclude != null ? new Regex(Options.SignExclude) : null;
        var filesToSign = new DirectoryInfo(packDir).GetAllFilesRecursively()
            .Where(x => PathUtil.FileIsLikelyPEImage(x.Name))
            .Where(x => fileExcludeRegex == null || !fileExcludeRegex.IsMatch(x.FullName))
            .Select(x => x.FullName)
            .ToArray();

        return SignFilesImplAsync(progress, filesToSign);
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
        ExtraNuspecMetadata["shortcutAumid"] = !string.IsNullOrEmpty(Options.Aumid)
            ? Options.Aumid
            : CoreUtil.GetAppUserModelId(Options.PackId);
        if (!string.IsNullOrEmpty(Options.SplashProgressColor)) {
            ExtraNuspecMetadata["splashProgressColor"] = Options.SplashProgressColor;
        }

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

        // create a stub for portable / MSI packages. The stub is named after the
        // final launcher name (packTitle) rather than the main exe, so that when the
        // updater re-extracts it on update (stripping the "_ExecutionStub" suffix) it
        // produces the same launcher name that was created at pack time. See #982.
        //
        // --noStub skips it here rather than later, so the stub is absent from the
        // .nupkg as well as from the portable package. That is the part that makes it
        // stick: the updater syncs stubs out of the package on every apply
        // (Bundle.extract_stubs_to_dir), so a stub that was never packed cannot be
        // restored, and updaters already in the field need no flag to honour it. See #1060.
        if (Options.NoStub) {
            Log.Info("Skipping launcher stub, --noStub was specified.");
        } else {
            var mainExeName = Options.EntryExecutableName;
            var mainPath = Path.Combine(packDir, mainExeName);
            var stubPath = Path.Combine(packDir, GetStubBaseName() + "_ExecutionStub.exe");
            CreateExecutableStubForExe(mainPath, stubPath);
        }

        Options.TargetRuntime.Architecture = Options.TargetRuntime.HasArchitecture
            ? Options.TargetRuntime.Architecture
            : GetMachineForBinary(MainExePath);

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
        var validated = ParseRuntimeDependencies(Options.Runtimes);

        foreach (var str in validated) {
            Log.Info("Runtime Dependency: " + str);
        }

        return String.Join(",", validated);
    }

    /// <summary>
    /// Parses and validates a comma/semicolon delimited list of runtime dependency names,
    /// throwing a <see cref="UserInfoException"/> if any name is invalid. Also used by
    /// <see cref="WindowsPackOptionsValidator"/> to validate the option up-front.
    /// </summary>
    public static List<string> ParseRuntimeDependencies(string runtimes)
    {
        if (string.IsNullOrWhiteSpace(runtimes))
            return [];

        var providedRuntimes = runtimes.ToLower()
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
            "vcredist145-x86",
            "vcredist145-x64",
            "vcredist145-arm64",
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

        return validated;
    }

    protected override async Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string targetSetupExe,
        Func<string, VelopackAssetType, string> createAsset)
    {
        var setupExeProgress = Options.BuildMsi
            ? CoreUtil.CreateProgressDelegate(progress, 0, 33)
            : CoreUtil.CreateProgressDelegate(progress, 0, 66);
        var msiProgress = CoreUtil.CreateProgressDelegate(progress, 33, 66);
        var signingProgress = CoreUtil.CreateProgressDelegate(progress, 66, 100);

        List<string> filesToSign = new();

        var bundledZip = new ZipPackage(releasePkg);
        IoUtil.Retry(() => File.Copy(HelperFile.GetSetupPath(Options.TargetRuntime, Log), targetSetupExe, true));
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
            var dir = TempDir.CreateSubdirectory("MsiPackage");
            File.Copy(Path.Combine(packDir, "Squirrel.exe"), Path.Combine(dir.FullName, "Update.exe"), true);
            var current = dir.CreateSubdirectory("current");
            CopyFiles(new DirectoryInfo(packDir), current, CoreUtil.CreateProgressDelegate(msiProgress, 0, 45));
            File.Delete(Path.Combine(current.FullName, "Squirrel.exe"));

            // move the stub to the root of the MSI package. With --noStub there is none, and
            // the msi template targets current\<main exe> instead.
            if (!Options.NoStub) {
                var msiStubPath = Path.Combine(
                    current.FullName,
                    GetStubBaseName() + "_ExecutionStub.exe");
                File.Move(msiStubPath, Path.Combine(dir.FullName, GetStubFileName()));
            }

            File.Create(Path.Combine(dir.FullName, ".msi-installed")).Close();

            msiProgress(50);
            
            var msiName = DefaultName.GetSuggestedMsiName(Options.PackId, Options.Channel, TargetOs);
            var msiPath = createAsset(msiName, VelopackAssetType.Msi);
            CompileWixTemplateToMsi(msiProgress, dir, msiPath);
            Log.Info($"MSI created '{Path.GetFileName(msiPath)}'.");
            filesToSign.Add(msiPath);
            msiProgress(100);
        }

        Log.Debug("Signing Setup files");
        await SignFilesImplAsync(signingProgress, filesToSign.ToArray()).ConfigureAwait(false);
        progress(100);
    }

    protected override async Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
    {
        var dir = TempDir.CreateSubdirectory("CreatePortablePackage");
        File.Copy(Path.Combine(packDir, "Squirrel.exe"), Path.Combine(dir.FullName, "Update.exe"), true);
        var current = dir.CreateSubdirectory("current");

        CopyFiles(new DirectoryInfo(packDir), current, CoreUtil.CreateProgressDelegate(progress, 0, 30));

        File.Delete(Path.Combine(current.FullName, "Squirrel.exe"));

        // move the stub to the root of the portable package. With --noStub there is
        // none to move, and the package root holds only Update.exe and current/.
        if (!Options.NoStub) {
            var stubPath = Path.Combine(
                current.FullName,
                GetStubBaseName() + "_ExecutionStub.exe");
            File.Move(stubPath, Path.Combine(dir.FullName, GetStubFileName()));
        }

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
            IoUtil.Retry(() => File.Copy(HelperFile.GetStubExecutablePath(Options.TargetRuntime, Log), targetStubPath, true));
            var edit = new ResourceEdit(targetStubPath, Log);
            edit.CopyResourcesFrom(exeToCopy);
            edit.Commit();
        } catch (Exception ex) {
            Log.Error(ex, $"Error creating StubExecutable and copying resources for '{exeToCopy}'. This stub may or may not work properly.");
        }
    }

    private async Task SignFilesImplAsync(Action<int> progress, params string[] filePaths)
    {
        var signParams = Options.SignParameters;
        var signTemplate = Options.SignTemplate;
        var signParallel = Options.SignParallel;
        var trustedSignMetadataPath = Options.AzureTrustedSignFile;
        var helper = new CodeSign(Log, Console);

        if (string.IsNullOrEmpty(signParams) && string.IsNullOrEmpty(signTemplate) && string.IsNullOrEmpty(trustedSignMetadataPath)) {
            Log.Warn($"No signing parameters provided, {filePaths.Length} file(s) will not be signed.");
            return;
        }

        if (!string.IsNullOrEmpty(signTemplate)) {
            helper.Sign(filePaths, signTemplate, signParallel, progress, true);
        } else if (!string.IsNullOrEmpty(trustedSignMetadataPath)) {
            // managed Authenticode signing, works on every OS (MSI files are only produced and signed on Windows).
            Log.Info($"Use Azure Trusted Signing service for code signing. Metadata file path: {trustedSignMetadataPath}");
            var signDescription = Options.PackTitle ?? Options.PackId;
            await SignWithAzureTrustedSigningAsync(filePaths, trustedSignMetadataPath, signDescription, signParallel, progress)
                .ConfigureAwait(false);
        } else if (!string.IsNullOrEmpty(signParams) && VelopackRuntimeInfo.IsWindows) {
            // signtool.exe does not work if we're not on windows.
            helper.Sign(filePaths, signParams, signParallel, progress, false);
        }
    }

    /// <summary>
    /// Signs files with the Azure Trusted Signing certificate profile described by <paramref name="metadataPath"/>.
    /// Virtual so tests can sign with a local key instead of the Azure service.
    /// </summary>
    protected virtual Task SignWithAzureTrustedSigningAsync(string[] filePaths, string metadataPath, string description,
        int parallelism, Action<int> progress)
    {
        _azureTrustedSigner ??= new AzureTrustedSigner(Log);
        return _azureTrustedSigner.SignFilesAsync(filePaths, metadataPath, description, parallelism, progress);
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

    protected virtual RuntimeCpu GetMachineForBinary(string path)
    {
        var image = PEImage.FromFile(path);

        if (image.MachineType.HasFlag(AsmResolver.PE.File.MachineType.Amd64))
            return RuntimeCpu.x64;

        if (image.MachineType.HasFlag(AsmResolver.PE.File.MachineType.Arm64))
            return RuntimeCpu.arm64;

        return RuntimeCpu.x86;
    }

    protected override string[] GetMainExeSearchPaths(string packDirectory, string mainExeName)
    {
        return [
            Path.Combine(packDirectory, mainExeName),
            Path.Combine(packDirectory, mainExeName) + ".exe",
        ];
    }

    // Base name (without extension) for the portable / MSI launcher stub. Kept in sync
    // with the stub file created inside the package so the updater reproduces the same
    // launcher name when it strips the "_ExecutionStub" suffix on update. See #982.
    private string GetStubBaseName() => Options.PackTitle ?? Options.PackId;

    private string GetStubFileName() => GetStubBaseName() + ".exe";

    private ShortcutLocation GetShortcuts()
    {
        var items = Options.Shortcuts?
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()) ?? [];

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