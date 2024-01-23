using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Spectre.Console;
using Velopack.Compression;
using Velopack.NuGet;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging
{
    public abstract class PackageBuilder<T> : ICommand<T>
        where T : class, IPackOptions
    {
        protected RuntimeOs SupportedTargetOs { get; }

        protected ILogger Log { get; }

        protected DirectoryInfo TempDir { get; private set; }

        protected T Options { get; private set; }

        protected string MainExeName { get; private set; }

        protected string MainExePath { get; private set; }

        protected string Channel { get; private set; }

        private readonly Regex REGEX_EXCLUDES = new Regex(@".*[\\\/]createdump.*|.*\.vshost\..*|.*\.nupkg$|.*\.pdb$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public PackageBuilder(RuntimeOs supportedOs, ILogger logger)
        {
            SupportedTargetOs = supportedOs;
            Log = logger;
        }

        public async Task Run(T options)
        {
            if (options.TargetRuntime?.BaseRID != SupportedTargetOs)
                throw new UserInfoException($"To build packages for {SupportedTargetOs.GetOsLongName()}, " +
                    $"the target rid must be {SupportedTargetOs} (actually was {options.TargetRuntime?.BaseRID}).");

            Log.Info("Beginning to package release.");
            Log.Info("Releases Directory: " + options.ReleaseDir.FullName);

            var releaseDir = options.ReleaseDir;
            var channel = options.Channel?.ToLower() ?? ReleaseEntryHelper.GetDefaultChannel(SupportedTargetOs);

            var entryHelper = new ReleaseEntryHelper(releaseDir.FullName, channel, Log);
            entryHelper.ValidateForPackaging(SemanticVersion.Parse(options.PackVersion));
            Channel = channel;

            var packId = options.PackId;
            var packDirectory = options.PackDirectory;
            var packVersion = options.PackVersion;
            var semVer = SemanticVersion.Parse(packVersion);

            // check that entry exe exists
            var mainExt = options.TargetRuntime.BaseRID == RuntimeOs.Windows ? ".exe" : "";
            var mainExeName = options.EntryExecutableName ?? (options.PackId + mainExt);
            var mainExePath = Path.Combine(packDirectory, mainExeName);

            // TODO: this is a hack, fix this.
            if (!File.Exists(mainExePath) && VelopackRuntimeInfo.IsLinux)
                mainExePath = Path.Combine(packDirectory, "usr", "bin", mainExeName);

            if (!File.Exists(mainExePath)) {
                throw new UserInfoException(
                    $"Could not find main application executable (the one that runs 'VelopackApp.Build().Run()'). " + Environment.NewLine +
                    $"I searched for '{mainExeName}' in {packDirectory}." + Environment.NewLine +
                    $"If your main binary is not named '{mainExeName}', please specify the name with the argument: --exeName {{yourBinary.exe}}");
            }
            MainExeName = mainExeName;
            MainExePath = mainExePath;

            // verify that the main executable is a valid velopack app
            VerifyMainBinary(mainExePath);

            using var _1 = Utility.GetTempDirectory(out var pkgTempDir);
            TempDir = new DirectoryInfo(pkgTempDir);
            Options = options;

            ConcurrentBag<(string from, string to)> filesToCopy = new();

            string getIncompletePath(string fileName)
            {
                var incomplete = Path.Combine(pkgTempDir, fileName);
                var final = Path.Combine(releaseDir.FullName, fileName);
                try { File.Delete(incomplete); } catch { }
                filesToCopy.Add((incomplete, final));
                return incomplete;
            }

            await Progress.ExecuteAsync(Log, async (ctx) => {
                ReleasePackage prev = null;
                await ctx.RunTask("Pre-process steps", async (progress) => {
                    prev = entryHelper.GetPreviousFullRelease(NuGetVersion.Parse(packVersion));
                    packDirectory = await PreprocessPackDir(progress, packDirectory);
                });

                if (VelopackRuntimeInfo.IsWindows || VelopackRuntimeInfo.IsOSX) {
                    await ctx.RunTask("Code-sign application", async (progress) => {
                        await CodeSign(progress, packDirectory);
                    });
                }

                var portableTask = ctx.RunTask("Building portable package", async (progress) => {
                    var suggestedName = ReleaseEntryHelper.GetSuggestedPortableName(packId, channel);
                    var path = getIncompletePath(suggestedName);
                    await CreatePortablePackage(progress, packDirectory, path);
                });

                // TODO: hack, this is a prerequisite for building full package but only on linux
                if (VelopackRuntimeInfo.IsLinux) await portableTask;

                string releasePath = null;
                await ctx.RunTask($"Building release {packVersion}", async (progress) => {
                    var suggestedName = ReleaseEntryHelper.GetSuggestedReleaseName(packId, packVersion, channel, false);
                    releasePath = getIncompletePath(suggestedName);
                    await CreateReleasePackage(progress, packDirectory, releasePath);
                });

                Task setupTask = null;
                if (VelopackRuntimeInfo.IsWindows || VelopackRuntimeInfo.IsOSX) {
                    setupTask = ctx.RunTask("Building setup package", async (progress) => {
                        var suggestedName = ReleaseEntryHelper.GetSuggestedSetupName(packId, channel);
                        var path = getIncompletePath(suggestedName);
                        await CreateSetupPackage(progress, releasePath, packDirectory, path);
                    });
                }

                if (prev != null && options.DeltaMode != DeltaMode.None) {
                    await ctx.RunTask($"Building delta {prev.Version} -> {packVersion}", async (progress) => {
                        var suggestedName = ReleaseEntryHelper.GetSuggestedReleaseName(packId, packVersion, channel, true);
                        var deltaPkg = await CreateDeltaPackage(progress, releasePath, prev.PackageFile, getIncompletePath(suggestedName), options.DeltaMode);
                    });
                }

                if (!VelopackRuntimeInfo.IsLinux) await portableTask;
                if (setupTask != null) await setupTask;

                await ctx.RunTask("Post-process steps", (progress) => {
                    var expectedAssets = VelopackRuntimeInfo.IsLinux ? 2 : 3;
                    if (prev != null && options.DeltaMode != DeltaMode.None) expectedAssets += 1;
                    if (filesToCopy.Count != expectedAssets) {
                        throw new Exception($"Expected {expectedAssets} assets to be created, but only {filesToCopy.Count} were.");
                    }

                    foreach (var f in filesToCopy) {
                        File.Move(f.from, f.to, true);
                    }

                    ReleaseEntryHelper.UpdateReleaseFiles(releaseDir.FullName, Log);
                    BuildAssets.Write(releaseDir.FullName, channel, filesToCopy.Select(x => x.to));
                    progress(100);
                    return Task.CompletedTask;
                });
            });
        }

        protected virtual string GetRuntimeDependencies()
        {
            return null;
        }

        protected virtual void VerifyMainBinary(string mainExePath)
        {
            try {
                var psi = new ProcessStartInfo(mainExePath);
                psi.AppendArgumentListSafe(new[] { "--veloapp-version" }, out var _);
                var output = psi.Output(5000);
                if (String.IsNullOrWhiteSpace(output)) {
                    throw new VelopackAppVerificationException("Exited with no output");
                }
                AssertVelopackVersionCompatible(output);
            } catch (TimeoutException) {
                throw new VelopackAppVerificationException("Timed out");
            }
        }

        protected virtual void AssertVelopackVersionCompatible(string velopackVersion)
        {
            if (SemanticVersion.TryParse(velopackVersion.Trim(), out var version)) {
                if (version != VelopackRuntimeInfo.VelopackNugetVersion) {
                    Log.Warn($"VelopackApp version '{version}' does not match CLI version '{VelopackRuntimeInfo.VelopackNugetVersion}'.");
                } else {
                    Log.Info($"VelopackApp version verified ({version}).");
                }
            } else {
                throw new VelopackAppVerificationException($"Failed to parse version: {velopackVersion.Trim()}");
            }
        }

        protected virtual string GenerateNuspecContent()
        {
            var packId = Options.PackId;
            var packTitle = Options.PackTitle ?? Options.PackId;
            var packAuthors = Options.PackAuthors ?? Options.PackId;
            var packVersion = Options.PackVersion;
            var releaseNotes = Options.ReleaseNotes;
            var rid = Options.TargetRuntime;

            string releaseNotesText = "";
            if (!String.IsNullOrEmpty(releaseNotes)) {
                var markdown = File.ReadAllText(releaseNotes);
                releaseNotesText = $"""
<releaseNotes>{SecurityElement.Escape(markdown)}</releaseNotes>
<releaseNotesHtml><![CDATA[{"\n"}{new Markdown().Transform(markdown)}{"\n"}]]></releaseNotesHtml>
""";
            }

            string osMinVersionText = "";
            if (rid?.HasVersion == true) {
                osMinVersionText = $"<osMinVersion>{rid.Version}</osMinVersion>";
            }

            string machineArchitectureText = "";
            if (rid?.HasArchitecture == true) {
                machineArchitectureText = $"<machineArchitecture>{rid.Architecture}</machineArchitecture>";
            }

            var runtimeDeps = GetRuntimeDependencies();
            string runtimeDependenciesText = "";
            if (!String.IsNullOrWhiteSpace(runtimeDeps)) {
                runtimeDependenciesText = $"<runtimeDependencies>{runtimeDeps}</runtimeDependencies>";
            }

            string nuspec = $"""
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
<metadata>
<id>{packId}</id>
<title>{packTitle ?? packId}</title>
<description>{packTitle ?? packId}</description>
<authors>{packAuthors ?? packId}</authors>
<version>{packVersion}</version>
<channel>{Channel}</channel>
<mainExe>{MainExeName}</mainExe>
<os>{rid.BaseRID.GetOsShortName()}</os>
<rid>{rid.ToDisplay(RidDisplayType.NoVersion)}</rid>
{osMinVersionText}
{machineArchitectureText}
{releaseNotesText}
{runtimeDependenciesText}
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

        protected virtual Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string outputPath)
        {
            return Task.CompletedTask;
        }

        protected virtual async Task CreateReleasePackage(Action<int> progress, string packDir, string outputPath)
        {
            var stagingDir = TempDir.CreateSubdirectory("CreateReleasePackage");

            var nuspecPath = Path.Combine(stagingDir.FullName, Options.PackId + ".nuspec");
            File.WriteAllText(nuspecPath, GenerateNuspecContent());

            var appDir = stagingDir.CreateSubdirectory("lib").CreateSubdirectory("app");
            CopyFiles(new DirectoryInfo(packDir), appDir, Utility.CreateProgressDelegate(progress, 0, 30));

            var metadataFiles = GetReleaseMetadataFiles();
            foreach (var kvp in metadataFiles) {
                File.Copy(kvp.Value, Path.Combine(stagingDir.FullName, kvp.Key), true);
            }

            AddContentTypesAndRel(nuspecPath);

            await EasyZip.CreateZipFromDirectoryAsync(Log, outputPath, stagingDir.FullName, Utility.CreateProgressDelegate(progress, 30, 100));
            progress(100);
        }

        protected virtual Dictionary<string, string> GetReleaseMetadataFiles()
        {
            return new Dictionary<string, string>();
        }

        protected virtual void CopyFiles(DirectoryInfo source, DirectoryInfo target, Action<int> progress, bool excludeAnnoyances = false)
        {
            var numFiles = source.EnumerateFiles("*", SearchOption.AllDirectories).Count();
            int currentFile = 0;

            void CopyFilesInternal(DirectoryInfo source, DirectoryInfo target)
            {
                foreach (var fileInfo in source.GetFiles()) {
                    var path = Path.Combine(target.FullName, fileInfo.Name);
                    currentFile++;
                    progress((int) ((double) currentFile / numFiles * 100));
                    if (excludeAnnoyances && REGEX_EXCLUDES.IsMatch(path)) {
                        Log.Debug("Skipping because matched exclude pattern: " + path);
                        continue;
                    }
                    fileInfo.CopyTo(path, true);
                }

                foreach (var sourceSubDir in source.GetDirectories()) {
                    var targetSubDir = target.CreateSubdirectory(sourceSubDir.Name);
                    CopyFilesInternal(sourceSubDir, targetSubDir);
                }
            }

            CopyFilesInternal(source, target);
        }

        protected virtual void AddContentTypesAndRel(string nuspecPath)
        {
            var rootDirectory = Path.GetDirectoryName(nuspecPath);
            var extensions = Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetExtension(p).TrimStart('.').ToLower())
                .Distinct()
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
    }
}
