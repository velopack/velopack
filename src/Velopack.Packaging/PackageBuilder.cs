using System.Diagnostics;
using System.Security;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Spectre.Console;
using Velopack.Compression;
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

            if (options.TargetRuntime?.HasArchitecture == true && options.TargetRuntime.Architecture == RuntimeCpu.x86)
                throw new UserInfoException("Velopack does not support building releases for x86 platforms.");

            Log.Info("Beginning to package release.");
            Log.Info("Releases Directory: " + options.ReleaseDir.FullName);

            var releaseDir = options.ReleaseDir;
            var channel = options.Channel?.ToLower() ?? ReleaseEntryHelper.GetDefaultChannel(SupportedTargetOs);

            var entryHelper = new ReleaseEntryHelper(releaseDir.FullName, Log);
            entryHelper.ValidateChannelForPackaging(SemanticVersion.Parse(options.PackVersion), channel, options.TargetRuntime);

            var packId = options.PackId;
            var packTitle = options.PackTitle ?? options.PackId;
            var packAuthors = options.PackAuthors ?? options.PackId;
            var packDirectory = options.PackDirectory;
            var packVersion = options.PackVersion;

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

            // verify that the main executable is a valid velopack app
            try {
                var psi = new ProcessStartInfo(mainExePath);
                psi.AppendArgumentListSafe(new[] { "--veloapp-version" }, out var _);
                var output = psi.Output(5000);
                if (String.IsNullOrWhiteSpace(output)) {
                    throw new VelopackAppVerificationException("Exited with no output");
                }
                if (SemanticVersion.TryParse(output.Trim(), out var version)) {
                    if (version != VelopackRuntimeInfo.VelopackNugetVersion) {
                        Log.Warn($"VelopackApp version '{version}' does not match CLI version '{VelopackRuntimeInfo.VelopackNugetVersion}'.");
                    } else {
                        Log.Info($"VelopackApp version verified ({version}).");
                    }
                } else {
                    throw new VelopackAppVerificationException($"Failed to parse version: {output.Trim()}");
                }
            } catch (TimeoutException) {
                throw new VelopackAppVerificationException("Timed out");
            }

            var suffix = ReleaseEntryHelper.GetPkgSuffix(SupportedTargetOs, channel);
            if (!String.IsNullOrWhiteSpace(suffix)) {
                packVersion += suffix;
            }

            using var _1 = Utility.GetTempDirectory(out var pkgTempDir);
            TempDir = new DirectoryInfo(pkgTempDir);
            Options = options;

            List<(string from, string to)> filesToCopy = new();

            try {
                await Progress.ExecuteAsync(Log, async (ctx) => {
                    string nuspecText = null;
                    ReleasePackage prev = null;
                    await ctx.RunTask("Pre-process steps", async (progress) => {
                        prev = entryHelper.GetPreviousFullRelease(NuGetVersion.Parse(packVersion), channel);
                        nuspecText = GenerateNuspecContent(
                            packId, packTitle, packAuthors, packVersion, options.ReleaseNotes, packDirectory, mainExeName);
                        packDirectory = await PreprocessPackDir(progress, packDirectory, nuspecText);
                    });

                    if (VelopackRuntimeInfo.IsWindows || VelopackRuntimeInfo.IsOSX) {
                        await ctx.RunTask("Code-sign application", async (progress) => {
                            await CodeSign(progress, packDirectory);
                        });
                    }

                    var portableTask = ctx.RunTask("Building portable package", async (progress) => {
                        var suggestedPortable = entryHelper.GetSuggestedPortablePath(packId, channel, options.TargetRuntime);
                        var incomplete = Path.Combine(pkgTempDir, Path.GetFileName(suggestedPortable));
                        if (File.Exists(incomplete)) File.Delete(incomplete);
                        filesToCopy.Add((incomplete, suggestedPortable));
                        await CreatePortablePackage(progress, packDirectory, incomplete);
                    });

                    // this is a prerequisite for building full package but only on linux
                    if (VelopackRuntimeInfo.IsLinux) await portableTask;

                    string releasePath = null;
                    await ctx.RunTask($"Building release {packVersion}", async (progress) => {
                        var releaseName = new ReleaseEntryName(packId, SemanticVersion.Parse(packVersion), false, Options.TargetRuntime);
                        releasePath = Path.Combine(releaseDir.FullName, releaseName.ToFileName());
                        if (File.Exists(releasePath)) File.Delete(releasePath);
                        await CreateReleasePackage(progress, packDirectory, nuspecText, releasePath);
                        entryHelper.AddNewRelease(releasePath, channel);
                    });

                    Task setupTask = null;
                    if (VelopackRuntimeInfo.IsWindows || VelopackRuntimeInfo.IsOSX) {
                        setupTask = ctx.RunTask("Building setup package", async (progress) => {
                            var suggestedSetup = entryHelper.GetSuggestedSetupPath(packId, channel, options.TargetRuntime);
                            var incomplete = Path.Combine(pkgTempDir, Path.GetFileName(suggestedSetup));
                            if (File.Exists(incomplete)) File.Delete(incomplete);
                            filesToCopy.Add((incomplete, suggestedSetup));
                            await CreateSetupPackage(progress, releasePath, packDirectory, incomplete);
                        });
                    }

                    if (prev != null && options.DeltaMode != DeltaMode.None) {
                        await ctx.RunTask($"Building delta {prev.Version} -> {packVersion}", async (progress) => {
                            var deltaPkg = await CreateDeltaPackage(progress, releasePath, prev.PackageFile, options.DeltaMode);
                            entryHelper.AddNewRelease(deltaPkg, channel);
                        });
                    }

                    if (!VelopackRuntimeInfo.IsLinux) await portableTask;
                    if (setupTask != null) await setupTask;

                    await ctx.RunTask("Post-process steps", (progress) => {
                        entryHelper.SaveReleasesFiles();
                        foreach (var f in filesToCopy) {
                            File.Move(f.from, f.to, true);
                        }
                        progress(100);
                        return Task.CompletedTask;
                    });
                });
            } catch {
                try {
                    entryHelper.RollbackNewReleases();
                } catch (Exception ex) {
                    Log.Warn("Failed to remove incomplete releases: " + ex.Message);
                }
                throw;
            }
        }

        protected virtual string GenerateNuspecContent(string packId, string packTitle, string packAuthors, string packVersion, string releaseNotes, string packDir, string mainExeName)
        {
            var releaseNotesText = String.IsNullOrEmpty(releaseNotes)
                       ? "" // no releaseNotes
                       : $"<releaseNotes>{SecurityElement.Escape(File.ReadAllText(releaseNotes))}</releaseNotes>";

            string nuspec = $@"
<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
  <metadata>
    <id>{packId}</id>
    <title>{packTitle ?? packId}</title>
    <description>{packTitle ?? packId}</description>
    <authors>{packAuthors ?? packId}</authors>
    <version>{packVersion}</version>
    {releaseNotesText}
  </metadata>
</package>
".Trim();

            return nuspec;
        }

        protected abstract Task<string> PreprocessPackDir(Action<int> progress, string packDir, string nuspecText);

        protected virtual Task CodeSign(Action<int> progress, string packDir)
        {
            return Task.CompletedTask;
        }

        protected virtual Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
        {
            return Task.CompletedTask;
        }

        protected virtual Task<string> CreateDeltaPackage(Action<int> progress, string releasePkg, string prevReleasePkg, DeltaMode mode)
        {
            var deltaBuilder = new DeltaPackageBuilder(Log);
            var deltaOutputPath = releasePkg.Replace("-full", "-delta");
            var (dp, stats) = deltaBuilder.CreateDeltaPackage(new ReleasePackage(prevReleasePkg), new ReleasePackage(releasePkg), deltaOutputPath, mode, progress);
            return Task.FromResult(dp.PackageFile);
        }

        protected virtual Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string outputPath)
        {
            return Task.CompletedTask;
        }

        protected virtual async Task CreateReleasePackage(Action<int> progress, string packDir, string nuspecText, string outputPath)
        {
            var stagingDir = TempDir.CreateSubdirectory("CreateReleasePackage");

            var nuspecPath = Path.Combine(stagingDir.FullName, Options.PackId + ".nuspec");
            File.WriteAllText(nuspecPath, nuspecText);

            var appDir = stagingDir.CreateSubdirectory("lib").CreateSubdirectory("app");
            CopyFiles(new DirectoryInfo(packDir), appDir, Utility.CreateProgressDelegate(progress, 0, 30));

            var metadataFiles = GetReleaseMetadataFiles();
            foreach (var kvp in metadataFiles) {
                File.Copy(kvp.Value, Path.Combine(stagingDir.FullName, kvp.Key), true);
            }

            AddContentTypesAndRel(nuspecPath);
            RenderReleaseNotesMarkdown(nuspecPath);

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

        protected virtual void RenderReleaseNotesMarkdown(string specPath)
        {
            var doc = new XmlDocument();
            doc.Load(specPath);

            var metadata = doc.DocumentElement.ChildNodes
                .OfType<XmlElement>()
                .First(x => x.Name.ToLowerInvariant() == "metadata");

            var releaseNotes = metadata.ChildNodes
                .OfType<XmlElement>()
                .FirstOrDefault(x => x.Name.ToLowerInvariant() == "releasenotes");

            if (releaseNotes == null || String.IsNullOrWhiteSpace(releaseNotes.InnerText)) {
                Log.Debug($"No release notes found in {specPath}");
                return;
            }

            var releaseNotesHtml = doc.CreateElement("releaseNotesHtml");
            releaseNotesHtml.InnerText = String.Format("<![CDATA[\n" + "{0}\n" + "]]>",
                new Markdown().Transform(releaseNotes.InnerText));
            metadata.AppendChild(releaseNotesHtml);

            doc.Save(specPath);
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

            File.WriteAllText(Path.Combine(rootDirectory, "[Content_Types].xml"), contentType);

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
