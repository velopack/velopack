using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Squirrel.CommandLine.Commands;
using Squirrel.NuGet;
using Squirrel.SimpleSplat;
using FileMode = System.IO.FileMode;

namespace Squirrel.CommandLine.Windows
{
    class Commands : IEnableLogger
    {
        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Commands));

        public static void Pack(PackWindowsCommand options)
        {
            using (Utility.GetTempDirectory(out var tmp)) {
                var nupkgPath = NugetConsole.CreatePackageFromOptions(tmp, options);
                options.Package = nupkgPath;
                Releasify(options);
            }
        }

        public static void Releasify(ReleasifyWindowsCommand options)
        {
            var targetDir = options.ReleaseDirectory;
            var package = options.Package;
            var baseUrl = options.BaseUrl;
            var generateDeltas = !options.NoDelta;
            var backgroundGif = options.SplashImage;
            var setupIcon = options.Icon ?? options.AppIcon;

            // normalize and validate that the provided frameworks are supported 
            var requiredFrameworks = Runtimes.ParseDependencyString(options.Runtimes);
            if (requiredFrameworks.Any())
                Log.Info("Package dependencies (from '--framework' argument) resolved as: " + String.Join(", ", requiredFrameworks.Select(r => r.Id)));

            using var ud = Utility.GetTempDirectory(out var tempDir);

            // update icon for Update.exe if requested
            var bundledUpdatePath = HelperExe.UpdatePath;
            var updatePath = Path.Combine(tempDir, "Update.exe");
            if (setupIcon != null && SquirrelRuntimeInfo.IsWindows) {
                DotnetUtil.UpdateSingleFileBundleIcon(bundledUpdatePath, updatePath, setupIcon);
            } else {
                if (setupIcon != null) {
                    Log.Warn("Unable to set icon for Update.exe (only supported on windows).");
                }

                File.Copy(bundledUpdatePath, updatePath, true);
            }

            if (!DotnetUtil.IsSingleFileBundle(updatePath))
                throw new InvalidOperationException("Update.exe is corrupt. Broken Squirrel install?");

            // copy input package to target output directory
            File.Copy(package, Path.Combine(targetDir, Path.GetFileName(package)), true);

            var allNuGetFiles = Directory.EnumerateFiles(targetDir)
                .Where(x => x.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase));

            var toProcess = allNuGetFiles.Select(p => new FileInfo(p)).Where(x => !x.Name.Contains("-delta") && !x.Name.Contains("-full"));
            var processed = new List<string>();

            var releaseFilePath = Path.Combine(targetDir, "RELEASES");
            var previousReleases = new List<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                previousReleases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));
            }

            foreach (var file in toProcess) {
                Log.Info("Creating release for package: " + file.FullName);

                var rp = new ReleasePackageBuilder(file.FullName);
                rp.CreateReleasePackage(contentsPostProcessHook: (pkgPath, zpkg) => {
                    var nuspecPath = Directory.GetFiles(pkgPath, "*.nuspec", SearchOption.TopDirectoryOnly)
                        .ContextualSingle("package", "*.nuspec", "top level directory");
                    var libDir = Directory.GetDirectories(Path.Combine(pkgPath, "lib"))
                        .ContextualSingle("package", "'lib' folder");

                    var spec = NuspecManifest.ParseFromFile(nuspecPath);

                    foreach (var exename in options.SquirrelAwareExecutableNames) {
                        var exepath = Path.GetFullPath(Path.Combine(libDir, exename));
                        if (!File.Exists(exepath)) {
                            throw new Exception($"Could not find main exe '{exename}' in package.");
                        }
                        File.WriteAllText(exepath + ".squirrel", "1");
                    }

                    var awareExes = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(libDir);

                    // do not allow the creation of packages without a SquirrelAwareApp inside
                    if (!awareExes.Any()) {
                        throw new ArgumentException(
                            "There are no SquirreAwareApps in the provided package. Please mark an exe " +
                            "as aware using the '-e' argument, or the assembly manifest.");
                    }

                    // warning if there are long paths (>200 char) in this package. 260 is max path
                    // but with the %localappdata% + user name + app name this can add up quickly.
                    // eg. 'C:\Users\SamanthaJones\AppData\Local\Application\app-1.0.1\' is 60 characters.
                    Directory.EnumerateFiles(libDir, "*", SearchOption.AllDirectories)
                        .Select(f => f.Substring(libDir.Length).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                        .Where(f => f.Length >= 200)
                        .ForEach(f => Log.Warn($"File path in package exceeds 200 characters ({f.Length}) and may cause issues on Windows: '{f}'."));

                    // fail the release if this is a clickonce application
                    if (Directory.EnumerateFiles(libDir, "*.application").Any(f => File.ReadAllText(f).Contains("clickonce"))) {
                        throw new ArgumentException(
                            "Squirrel does not support building releases for ClickOnce applications. " +
                            "Please publish your application to a folder without ClickOnce.");
                    }

                    // parse the PE header of every squirrel aware app
                    var peparsed = awareExes.ToDictionary(path => path, path => new PeNet.PeFile(path));

                    // record architecture of squirrel aware binaries so setup can fast fail if unsupported
                    RuntimeCpu parseMachine(PeNet.Header.Pe.MachineType machine)
                    {
                        Utility.TryParseEnumU16<RuntimeCpu>((ushort) machine, out var cpu);
                        return cpu;
                    }

                    var peArch = from pe in peparsed
                                 let machine = pe.Value?.ImageNtHeaders?.FileHeader?.Machine ?? 0
                                 let arch = parseMachine(machine)
                                 select new { Name = Path.GetFileName(pe.Key), Architecture = arch };

                    if (awareExes.Count > 0) {
                        Log.Info($"There are {awareExes.Count} SquirrelAwareApps. Binaries will be executed during install/update/uninstall hooks.");
                        foreach (var pe in peArch) {
                            Log.Info($"  Detected SquirrelAwareApp '{pe.Name}' (arch: {pe.Architecture})");
                        }
                    } else {
                        Log.Warn("There are no SquirrelAwareApps. No hooks will be executed during install/update/uninstall. " +
                                 "Shortcuts will be created for every binary in package.");
                    }

                    ZipPackage.SetMetadata(nuspecPath, requiredFrameworks.Select(r => r.Id), options.TargetRuntime);

                    // create stub executable for all exe's in this package (except Squirrel!)
                    var exesToCreateStubFor = new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                        .Where(x => x.Name.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase))
                        .Where(x => !x.Name.Equals("squirrel.exe", StringComparison.InvariantCultureIgnoreCase))
                        .Where(x => !x.Name.Equals("createdump.exe", StringComparison.InvariantCultureIgnoreCase))
                        .Where(x => Utility.IsFileTopLevelInPackage(x.FullName, pkgPath))
                        .ToArray(); // materialize the IEnumerable so we never end up creating stubs for stubs

                    Log.Info($"Creating {exesToCreateStubFor.Length} stub executables");
                    exesToCreateStubFor.ForEach(x => createExecutableStubForExe(x.FullName));

                    // copy Update.exe into package, so it can also be updated in both full/delta packages
                    // and do it before signing so that Update.exe will also be signed. It is renamed to
                    // 'Squirrel.exe' only because Squirrel.Windows expects it to be called this.
                    File.Copy(updatePath, Path.Combine(libDir, "Squirrel.exe"), true);

                    // sign all exe's in this package
                    var filesToSign = new DirectoryInfo(libDir).GetAllFilesRecursively()
                        .Where(x => options.SignSkipDll ? Utility.PathPartEndsWith(x.Name, ".exe") : Utility.FileIsLikelyPEImage(x.Name))
                        .Select(x => x.FullName)
                        .ToArray();

                    signFiles(options, libDir, filesToSign);

                    // copy app icon to 'lib/fx/app.ico'
                    var iconTarget = Path.Combine(libDir, "app.ico");
                    if (options.AppIcon != null) {
                        // icon was specified on the command line
                        Log.Info("Using app icon from command line arguments");
                        File.Copy(options.AppIcon, iconTarget, true);
                    } else if (!File.Exists(iconTarget) && zpkg.IconUrl != null) {
                        // icon was provided in the nuspec. download it and possibly convert it from a different image format
                        Log.Info($"Downloading app icon from '{zpkg.IconUrl}'.");
                        var fd = Utility.CreateDefaultDownloader();
                        var imgBytes = fd.DownloadBytes(zpkg.IconUrl.ToString()).Result;
                        if (zpkg.IconUrl.AbsolutePath.EndsWith(".ico")) {
                            File.WriteAllBytes(iconTarget, imgBytes);
                        } else {
                            if (SquirrelRuntimeInfo.IsWindows) {
                                using var imgStream = new MemoryStream(imgBytes);
                                using var bmp = (Bitmap) Image.FromStream(imgStream);
                                using var ico = Icon.FromHandle(bmp.GetHicon());
                                using var fs = File.Open(iconTarget, FileMode.Create, FileAccess.Write);
                                ico.Save(fs);
                            } else {
                                Log.Warn($"App icon is currently {Path.GetExtension(zpkg.IconUrl.AbsolutePath)} and can not be automatically " +
                                         $"converted to .ico (only supported on windows). Supply a .ico image instead.");
                            }
                        }
                    }

                    // copy other images to root (used by setup)
                    if (setupIcon != null) File.Copy(setupIcon, Path.Combine(pkgPath, "setup.ico"), true);
                    if (backgroundGif != null) File.Copy(backgroundGif, Path.Combine(pkgPath, "splashimage" + Path.GetExtension(backgroundGif)));

                    return Path.Combine(targetDir, ReleasePackageBuilder.GetSuggestedFileName(spec.Id, spec.Version.ToString(), options.TargetRuntime.StringWithNoVersion));
                });

                processed.Add(rp.ReleasePackageFile);

                var prev = ReleasePackageBuilder.GetPreviousRelease(previousReleases, rp, targetDir, options.TargetRuntime);
                if (prev != null && generateDeltas) {
                    var deltaBuilder = new DeltaPackageBuilder();
                    var deltaOutputPath = rp.ReleasePackageFile.Replace("-full", "-delta");
                    var dp = deltaBuilder.CreateDeltaPackage(prev, rp, deltaOutputPath);
                    processed.Insert(0, dp.InputPackageFile);
                }
            }

            foreach (var file in toProcess) {
                File.Delete(file.FullName);
            }

            var newReleaseEntries = processed
                .Select(packageFilename => ReleaseEntry.GenerateFromFile(packageFilename, baseUrl))
                .ToList();
            var distinctPreviousReleases = previousReleases
                .Where(x => !newReleaseEntries.Select(e => e.Version).Contains(x.Version));
            var releaseEntries = distinctPreviousReleases.Concat(newReleaseEntries).ToList();

            ReleaseEntry.WriteReleaseFile(releaseEntries, releaseFilePath);

            var bundledzp = new ZipPackage(package);
            var targetSetupExe = Path.Combine(targetDir, $"{bundledzp.Id}Setup-{options.TargetRuntime.StringWithNoVersion}.exe");
            File.Copy(options.DebugSetupExe ?? HelperExe.SetupPath, targetSetupExe, true);

            if (SquirrelRuntimeInfo.IsWindows) {
                HelperExe.SetPEVersionBlockFromPackageInfo(targetSetupExe, bundledzp, setupIcon);
            } else {
                Log.Warn("Unable to set Setup.exe icon (only supported on windows)");
            }

            var newestFullRelease = Squirrel.EnumerableExtensions.MaxBy(releaseEntries, x => x.Version).Where(x => !x.IsDelta).First();
            var newestReleasePath = Path.Combine(targetDir, newestFullRelease.Filename);

            Log.Info($"Creating Setup bundle");
            var bundleOffset = SetupBundle.CreatePackageBundle(targetSetupExe, newestReleasePath);
            Log.Info("Signing Setup bundle");
            signFiles(options, targetDir, targetSetupExe);
            Log.Info("Bundle package offset is " + bundleOffset);

            Log.Info($"Setup bundle created at '{targetSetupExe}'.");

            // this option is used for debugging a local Setup.exe
            if (options.DebugSetupExe != null) {
                File.Copy(targetSetupExe, options.DebugSetupExe, true);
                Log.Warn($"DEBUG OPTION: Setup bundle copied on top of '{options.DebugSetupExe}'. Recompile before creating a new bundle.");
            }

            if (options.BuildMsi) {
                if (SquirrelRuntimeInfo.IsWindows) {
                    var msiPath = createMsiPackage(targetSetupExe, bundledzp, options.TargetRuntime.Architecture == RuntimeCpu.x64, options.MsiVersion);
                    Log.Info("Signing MSI package");
                    signFiles(options, targetDir, msiPath);
                } else {
                    Log.Warn("Unable to create MSI (only supported on windows).");
                }
            }

            Log.Info("Done");
        }

        private static void signFiles(SigningCommand options, string rootDir, params string[] filePaths)
        {
            var signParams = options.SignParameters;
            var signTemplate = options.SignTemplate;
            var signParallel = options.SignParallel;

            if (String.IsNullOrEmpty(signParams) && String.IsNullOrEmpty(signTemplate)) {
                Log.Debug($"No signing paramaters provided, {filePaths.Length} file(s) will not be signed.");
                return;
            }

            if (!String.IsNullOrEmpty(signTemplate)) {
                Log.Info($"Preparing to sign {filePaths.Length} files with custom signing template");
                foreach (var f in filePaths) {
                    HelperExe.SignPEFileWithTemplate(f, signTemplate);
                }
                return;
            }

            // signtool.exe does not work if we're not on windows.
            if (!SquirrelRuntimeInfo.IsWindows) return;

            if (!String.IsNullOrEmpty(signParams)) {
                Log.Info($"Preparing to sign {filePaths.Length} files with embedded signtool.exe with parallelism of {signParallel}");
                HelperExe.SignPEFilesWithSignTool(rootDir, filePaths, signParams, signParallel);
            }
        }

        [SupportedOSPlatform("windows")]
        static string createMsiPackage(string setupExe, IPackage package, bool packageAs64Bit, string msiVersionOverride)
        {
            Log.Info($"Compiling machine-wide msi deployment tool in {(packageAs64Bit ? "64-bit" : "32-bit")} mode");

            var setupExeDir = Path.GetDirectoryName(setupExe);
            var setupName = Path.GetFileNameWithoutExtension(setupExe);
            var culture = CultureInfo.GetCultureInfo(package.Language ?? "").TextInfo.ANSICodePage;

            // WiX Identifiers may contain ASCII characters A-Z, a-z, digits, underscores (_), or
            // periods(.). Every identifier must begin with either a letter or an underscore.
            var wixId = Regex.Replace(package.Id, @"[^\w\.]", "_");
            if (Char.GetUnicodeCategory(wixId[0]) == UnicodeCategory.DecimalDigitNumber)
                wixId = "_" + wixId;

            var templateData = new Dictionary<string, string> {
                { "Id", wixId },
                { "Title", package.ProductName },
                { "Author", package.ProductCompany },
                { "Version", msiVersionOverride ?? $"{package.Version.Major}.{package.Version.Minor}.{package.Version.Patch}.0" },
                { "Summary", package.ProductDescription },
                { "Codepage", $"{culture}" },
                { "Platform", packageAs64Bit ? "x64" : "x86" },
                { "ProgramFilesFolder", packageAs64Bit ? "ProgramFiles64Folder" : "ProgramFilesFolder" },
                { "Win64YesNo", packageAs64Bit ? "yes" : "no" },
                { "SetupName", setupName }
            };

            // NB: We need some GUIDs that are based on the package ID, but unique (i.e.
            // "Unique but consistent").
            for (int i = 1; i <= 10; i++) {
                templateData[String.Format("IdAsGuid{0}", i)] = Utility.CreateGuidFromHash(String.Format("{0}:{1}", package.Id, i)).ToString();
            }

            return HelperExe.CompileWixTemplateToMsi(templateData, setupExeDir, setupName);
        }

        static void createExecutableStubForExe(string exeToCopy)
        {
            try {
                var targetName = Path.GetFileNameWithoutExtension(exeToCopy) + "_ExecutionStub.exe";
                var target = Path.Combine(Path.GetDirectoryName(exeToCopy), targetName);

                Utility.Retry(() => File.Copy(HelperExe.StubExecutablePath, target, true));
                Utility.Retry(() => {
                    if (SquirrelRuntimeInfo.IsWindows) {
                        using var writer = new Microsoft.NET.HostModel.ResourceUpdater(target, true);
                        writer.AddResourcesFromPEImage(exeToCopy);
                        writer.Update();
                    } else {
                        Log.Warn($"Cannot set resources/icon for {target} (only supported on windows).");
                    }
                });
            } catch (Exception ex) {
                Log.ErrorException($"Error creating StubExecutable and copying resources for '{exeToCopy}'. This stub may or may not work properly.", ex);
            }
        }
    }
}