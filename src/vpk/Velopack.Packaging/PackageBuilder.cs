using System.IO;
using System.Security;
using System.Text.RegularExpressions;

using Markdig;

using Microsoft.Extensions.Logging;

using NuGet.Versioning;

using Velopack.Compression;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.NuGet;
using Velopack.Packaging.Abstractions;
using Velopack.Util;

namespace Velopack.Packaging;

public abstract class PackageBuilder<T> : ICommand<T>
    where T : class, IPackOptions
{
    protected RuntimeOs TargetOs { get; }

    protected ILogger Log { get; }

    protected IFancyConsole Console { get; }

    protected DirectoryInfo TempDir { get; private set; }

    protected T Options { get; private set; }

    protected string MainExePath { get; private set; }

    protected Dictionary<string, string> ExtraNuspecMetadata { get; } = new();

    private readonly Regex REGEX_EXCLUDES = new(@".*[\\\/]createdump.*|.*\.vshost\..*|.*\.nupkg$|.*\.pdb", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public PackageBuilder(RuntimeOs supportedOs, ILogger logger, IFancyConsole console)
    {
        TargetOs = supportedOs;
        Log = logger;
        Console = console;
    }

    public async Task Run(T options)
    {
        if (options.TargetRuntime == null) {
            options.TargetRuntime = RID.Parse(TargetOs.GetOsShortName());
        }

        if (options.TargetRuntime!.BaseRID != TargetOs) {
            throw new UserInfoException(
                $"To build packages for {TargetOs.GetOsLongName()}, " +
                $"the target rid must be {TargetOs} (actually was {options.TargetRuntime?.BaseRID}). " +
                $"If your real intention was to cross-compile a release for {options.TargetRuntime?.BaseRID} then you " +
                $"should provide an OS directive: eg. 'vpk [{options.TargetRuntime?.BaseRID.GetOsShortName()}] pack ...'");
        }

        Log.Info($"Beginning to package Velopack release {options.PackVersion}.");
        Log.Info("Releases Directory: " + options.ReleaseDir.FullName);

        var releaseDir = options.ReleaseDir;
        var channel = options.Channel?.ToLower() ?? DefaultName.GetDefaultChannel(TargetOs);
        options.Channel = channel;

        var entryHelper = new ReleaseEntryHelper(releaseDir.FullName, channel, Log, TargetOs);
        if (entryHelper.DoesSimilarVersionExist(SemanticVersion.Parse(options.PackVersion))) {
            if (await Console.PromptYesNo(
                    "A release in this channel with the same or greater version already exists. Do you want to continue and potentially overwrite files?") !=
                true) {
                throw new UserInfoException(
                    $"There is a release in channel {channel} which is equal or greater to the current version {options.PackVersion}. Please increase the current package version or remove that release.");
            }
        }

        using var _1 = TempUtil.GetTempDirectory(out var pkgTempDir);
        TempDir = new DirectoryInfo(pkgTempDir);

        var packId = options.PackId;
        var packDirectory = options.PackDirectory;
        var packVersion = options.PackVersion;

        // extract pre-bundled apps as required (eg. .AppImage or .pkg)
        packDirectory = ExtractPackDir(packDirectory);

        // check that entry exe exists
        var mainExeName = options.EntryExecutableName ?? options.PackId;
        var mainSearchPaths = GetMainExeSearchPaths(packDirectory, mainExeName);
        string mainExePath = null;
        foreach (var path in mainSearchPaths) {
            if (File.Exists(path)) {
                mainExePath = path;
                break;
            }
        }

        if (mainExePath == null) {
            throw new UserInfoException(
                $"Could not find main application executable (the one that runs 'VelopackApp.Build().Run()'). " + Environment.NewLine +
                $"If your main binary is not named '{mainExeName}', please specify the name with the argument: --mainExe {{yourBinary.exe}}" +
                Environment.NewLine +
                $"I searched the following paths and none exist: " + Environment.NewLine +
                String.Join(Environment.NewLine, mainSearchPaths)
            );
        }

        MainExePath = mainExePath;
        options.EntryExecutableName = Path.GetFileName(mainExePath);
        Options = options;

        var assetCache = new BuildAssets(pkgTempDir, channel);

        await Console.ExecuteProgressAsync(
            async (ctx) => {
                ReleasePackage prev = null;
                await ctx.RunTask(
                    "Pre-process steps",
                    async (progress) => {
                        prev = entryHelper.GetPreviousFullRelease(NuGetVersion.Parse(packVersion));
                        packDirectory = await PreprocessPackDir(progress, packDirectory);
                    });

                if (TargetOs != RuntimeOs.Linux) {
                    await ctx.RunTask(
                        "Code-sign application",
                        async (progress) => {
                            await CodeSign(progress, packDirectory);
                        });
                }

                Task portableTask = null;
                if (TargetOs == RuntimeOs.Linux || !Options.NoPortable) {
                    portableTask = ctx.RunTask(
                        "Building portable package",
                        async (progress) => {
                            var suggestedName = DefaultName.GetSuggestedPortableName(packId, channel, TargetOs);
                            var path = assetCache.MakeAssetPath(suggestedName, VelopackAssetType.Portable);
                            await CreatePortablePackage(progress, packDirectory, path);
                        });
                }

                // TODO: hack, this is a prerequisite for building full package but only on linux
                if (TargetOs == RuntimeOs.Linux) await portableTask!;

                string releasePath = null;
                await ctx.RunTask(
                    $"Building release {packVersion}",
                    async (progress) => {
                        var suggestedName = DefaultName.GetSuggestedReleaseName(packId, packVersion, channel, false, TargetOs);
                        releasePath = assetCache.MakeAssetPath(suggestedName, VelopackAssetType.Full);
                        await CreateReleasePackage(progress, packDirectory, releasePath);
                    });

                Task setupTask = null;
                if (!Options.NoInst && TargetOs != RuntimeOs.Linux) {
                    setupTask = ctx.RunTask(
                        "Building setup package",
                        async (progress) => {
                            var suggestedName = DefaultName.GetSuggestedSetupName(packId, channel, TargetOs);
                            var path = assetCache.MakeAssetPath(suggestedName, VelopackAssetType.Installer);
                            await CreateSetupPackage(progress, releasePath, packDirectory, path, assetCache.MakeAssetPath);
                        });
                }

                if (prev != null && options.DeltaMode != DeltaMode.None) {
                    await ctx.RunTask(
                        $"Building delta {prev.Version} -> {packVersion}",
                        async (progress) => {
                            var suggestedName = DefaultName.GetSuggestedReleaseName(packId, packVersion, channel, true, TargetOs);
                            var path = assetCache.MakeAssetPath(suggestedName, VelopackAssetType.Delta);
                            var deltaPkg = await CreateDeltaPackage(
                                progress,
                                releasePath,
                                prev.PackageFile,
                                path,
                                options.DeltaMode);
                        });
                }

                if (TargetOs != RuntimeOs.Linux && portableTask != null) await portableTask;
                if (setupTask != null) await setupTask;

                await ctx.RunTask(
                    "Post-process steps",
                    (progress) => {
                        assetCache.MoveBagTo(releaseDir.FullName);
                        assetCache.Write();
                        ReleaseEntryHelper.UpdateReleaseFiles(releaseDir.FullName, Log);
                        progress(100);
                        return Task.CompletedTask;
                    });
            });
    }

    protected virtual string ExtractPackDir(string packDirectory) => packDirectory;

    protected abstract string[] GetMainExeSearchPaths(string packDirectory, string mainExeName);

    protected virtual string GenerateNuspecContent()
    {
        var packId = Options.PackId;
        var packTitle = GetEffectiveTitle();
        var packAuthors = GetEffectiveAuthors();
        var packVersion = Options.PackVersion;
        var releaseNotes = Options.ReleaseNotes;
        var rid = Options.TargetRuntime;

        string extraMetadata = "";

        void addMetadata(string key, string value)
        {
            if (!String.IsNullOrEmpty(key) && !String.IsNullOrEmpty(value)) {
                if (!SecurityElement.IsValidText(value)) {
                    value = $"""<![CDATA[{"\n"}{value}{"\n"}]]>""";
                }

                extraMetadata += $"<{key}>{value}</{key}>{Environment.NewLine}";
            }
        }

        if (ExtraNuspecMetadata.Any()) {
            foreach (var kvp in ExtraNuspecMetadata) {
                addMetadata(kvp.Key, kvp.Value);
            }
        }

        if (!String.IsNullOrEmpty(releaseNotes)) {
            var markdown = File.ReadAllText(releaseNotes);
            addMetadata("releaseNotes", markdown);
            addMetadata("releaseNotesHtml", Markdown.ToHtml(markdown));
        }

        if (rid?.HasVersion == true) {
            addMetadata("osMinVersion", rid.Version.ToString());
        }

        if (rid?.HasArchitecture == true) {
            addMetadata("machineArchitecture", rid.Architecture.ToString());
        }

        string nuspec = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
            <metadata>
            <id>{packId}</id>
            <title>{packTitle}</title>
            <description>{packTitle}</description>
            <authors>{packAuthors ?? packId}</authors>
            <version>{packVersion}</version>
            <channel>{Options.Channel}</channel>
            <mainExe>{Options.EntryExecutableName}</mainExe>
            <os>{rid.BaseRID.GetOsShortName()}</os>
            <rid>{rid.ToDisplay(RidDisplayType.NoVersion)}</rid>
            {extraMetadata.Trim()}
            </metadata>
            </package>
            """.Trim();

        return nuspec;
    }

    protected abstract Task<string> PreprocessPackDir(Action<int> progress, string packDir);

    protected virtual Task CodeSign(Action<int> progress, string packDir)
    {
        return Task.CompletedTask;
    }

    protected abstract Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath);

    protected virtual Task<string> CreateDeltaPackage(Action<int> progress, string releasePkg, string prevReleasePkg, string outputPath, DeltaMode mode)
    {
        var deltaBuilder = new DeltaPackageBuilder(Log);
        var (dp, stats) = deltaBuilder.CreateDeltaPackage(new ReleasePackage(prevReleasePkg), new ReleasePackage(releasePkg), outputPath, mode, progress);
        return Task.FromResult(dp.PackageFile);
    }

    protected virtual Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string outputPath, Func<string, VelopackAssetType, string> createAsset)
    {
        return Task.CompletedTask;
    }

    protected virtual async Task CreateReleasePackage(Action<int> progress, string packDir, string outputPath)
    {
        var stagingDir = TempDir.CreateSubdirectory("CreateReleasePackage");

        var nuspecPath = Path.Combine(stagingDir.FullName, Options.PackId + ".nuspec");
        File.WriteAllText(nuspecPath, GenerateNuspecContent());

        var appDir = stagingDir.CreateSubdirectory("lib").CreateSubdirectory("app");
        CopyFiles(new DirectoryInfo(packDir), appDir, CoreUtil.CreateProgressDelegate(progress, 0, 30));

        var metadataFiles = GetReleaseMetadataFiles();
        foreach (var kvp in metadataFiles) {
            File.Copy(kvp.Value, Path.Combine(stagingDir.FullName, kvp.Key), true);
        }

        AddContentTypesAndRel(nuspecPath);

        await EasyZip.CreateZipFromDirectoryAsync(Log.ToVelopackLogger(), outputPath, stagingDir.FullName, CoreUtil.CreateProgressDelegate(progress, 30, 100));
        progress(100);
    }

    protected virtual Dictionary<string, string> GetReleaseMetadataFiles() => [];

    protected virtual void CopyFiles(DirectoryInfo source, DirectoryInfo target, Action<int> progress, bool excludeAnnoyances = false)
    {
        // On Windows, we can use our custom copy method to avoid annoying files.
        // On OSX, it's a bit tricker because it's common practice to have internal symlinks which this will recursively copy as directories.
        // We need to preserve the internal symlinks, so we will use 'cp -a' and then manually delete annoying files.

        Regex manualExclude = null;
        if (!String.IsNullOrEmpty(Options.Exclude)) {
            manualExclude = new Regex(Options.Exclude, RegexOptions.Compiled);
        }

        if (!source.Exists) {
            throw new ArgumentException("Source directory does not exist: " + source.FullName);
        }

        if (VelopackRuntimeInfo.IsWindows) {
            Log.Debug($"Copying '{source}' to '{target}' (built-in recursive)");
            var numFiles = source.EnumerateFiles("*", SearchOption.AllDirectories).Count();
            int currentFile = 0;

            void CopyFilesInternal(DirectoryInfo source, DirectoryInfo target)
            {
                foreach (var fileInfo in source.GetFiles()) {
                    var path = Path.Combine(target.FullName, fileInfo.Name);
                    currentFile++;
                    progress((int) ((double) currentFile / numFiles * 100));
                    if (excludeAnnoyances) {
                        if (manualExclude != null) {
                            if (manualExclude.IsMatch(path)) {
                                Log.Debug("Skipping because matched exclude pattern: " + path);
                                continue;
                            }
                        } else if (REGEX_EXCLUDES.IsMatch(path)) {
                            Log.Debug("Skipping because matched exclude pattern: " + path);
                            continue;
                        }
                    }

                    fileInfo.CopyTo(path, true);
                }

                foreach (var sourceSubDir in source.GetDirectories()) {
                    var targetSubDir = target.CreateSubdirectory(sourceSubDir.Name);
                    CopyFilesInternal(sourceSubDir, targetSubDir);
                }
            }

            CopyFilesInternal(source, target);
        } else {
            Log.Debug($"Copying '{source}' to '{target}' (preserving symlinks)");
            // copy the contents of the folder, not the folder itself.
            var src = source.FullName.TrimEnd('/') + "/.";
            var dest = target.FullName.TrimEnd('/') + "/";
            Log.Debug(Exe.InvokeAndThrowIfNonZero("cp", ["-a", src, dest], null));

            if (excludeAnnoyances) {
                foreach (var f in target.EnumerateFiles("*", SearchOption.AllDirectories)) {
                    if (manualExclude != null) {
                        if (manualExclude.IsMatch(f.FullName)) {
                            Log.Debug("Deleting because matched exclude pattern: " + f.FullName);
                            f.Delete();
                        }
                    } else if (REGEX_EXCLUDES.IsMatch(f.FullName)) {
                        Log.Debug("Deleting because matched exclude pattern: " + f.FullName);
                        f.Delete();
                    }
                }
            }

            progress(100);
        }
    }

    protected virtual void AddContentTypesAndRel(string nuspecPath)
    {
        var rootDirectory = Path.GetDirectoryName(nuspecPath);
        var extensions = Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetExtension(p).TrimStart('.').ToLower())
            .Distinct()
            .Where(ext => !String.IsNullOrWhiteSpace(ext))
            .Select(ext => $"""  <Default Extension="{ext}" ContentType="application/octet" />""")
            .ToArray();

        var contentType = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
            {String.Join(Environment.NewLine, extensions)}
            </Types>
            """;

        File.WriteAllText(Path.Combine(rootDirectory, NugetUtil.ContentTypeFileName), contentType);

        var relsDir = Path.Combine(rootDirectory, "_rels");
        Directory.CreateDirectory(relsDir);

        var rels = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{Path.GetFileName(nuspecPath)}" Id="R1" />
            </Relationships>
            """;
        File.WriteAllText(Path.Combine(relsDir, ".rels"), rels);
    }

    protected string GetEffectiveTitle() => Options.PackTitle ?? Options.PackId;

    protected string GetEffectiveAuthors() => Options.PackAuthors ?? Options.PackId;
}