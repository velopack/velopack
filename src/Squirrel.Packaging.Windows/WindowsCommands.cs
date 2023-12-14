using System.Drawing;
using System.Text;
using Microsoft.Extensions.Logging;
using Squirrel.NuGet;
using FileMode = System.IO.FileMode;

namespace Squirrel.Packaging.Windows;

public class SigningOptions
{
    public string SignParameters { get; set; }

    public bool SignSkipDll { get; set; }

    public int SignParallel { get; set; }

    public string SignTemplate { get; set; }
}

public class ReleasifyWindowsOptions : SigningOptions
{
    public DirectoryInfo ReleaseDir { get; set; }

    public RID TargetRuntime { get; set; }

    public string Package { get; set; }

    public string BaseUrl { get; set; }

    public string DebugSetupExe { get; set; }

    public bool NoDelta { get; set; }

    public string Runtimes { get; set; }

    public string SplashImage { get; set; }

    public string Icon { get; set; }

    public string[] MainExe { get; set; }

    public string AppIcon { get; set; }
}

public class PackWindowsOptions : ReleasifyWindowsOptions, INugetPackCommand
{
    public string PackId { get; set; }

    public string PackVersion { get; set; }

    public string PackDirectory { get; set; }

    public string PackAuthors { get; set; }

    public string PackTitle { get; set; }

    public bool IncludePdb { get; set; }

    public string ReleaseNotes { get; set; }
}

public class WindowsCommands
{
    private readonly ILogger Log;

    public WindowsCommands(ILogger logger)
    {
        Log = logger;
    }

    public void Pack(PackWindowsOptions options)
    {
        using (Utility.GetTempDirectory(out var tmp)) {
            var nupkgPath = new NugetConsole(Log).CreatePackageFromOptions(tmp, options);
            options.Package = nupkgPath;
            Releasify(options);
        }
    }

    public void Releasify(ReleasifyWindowsOptions options)
    {
        var targetDir = options.ReleaseDir.FullName;
        var package = options.Package;
        var baseUrl = options.BaseUrl;
        var generateDeltas = !options.NoDelta;
        var backgroundGif = options.SplashImage;
        var setupIcon = options.Icon ?? options.AppIcon;

        // normalize and validate that the provided frameworks are supported 
        var requiredFrameworks = options.Runtimes
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(Runtimes.GetRuntimeByName);

        if (requiredFrameworks.Where(f => f == null).Any())
            throw new ArgumentException("Invalid target frameworks string.");

        using var ud = Utility.GetTempDirectory(out var tempDir);

        // update icon for Update.exe if requested
        var helper = new HelperExe(Log);
        var updatePath = Path.Combine(tempDir, "Update.exe");
        File.Copy(HelperExe.UpdatePath, updatePath, true);

        if (setupIcon != null && SquirrelRuntimeInfo.IsWindows) {
            helper.SetExeIcon(updatePath, setupIcon);
        } else if (setupIcon != null) {
            Log.Warn("Unable to set icon for Update.exe (only supported on windows).");
        }

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

            var rp = new ReleasePackageBuilder(Log, file.FullName);
            rp.CreateReleasePackage(contentsPostProcessHook: (pkgPath, zpkg) => {
                var nuspecPath = Directory.GetFiles(pkgPath, "*.nuspec", SearchOption.TopDirectoryOnly)
                    .ContextualSingle("package", "*.nuspec", "top level directory");
                var libDir = Directory.GetDirectories(Path.Combine(pkgPath, "lib"))
                    .ContextualSingle("package", "'lib' folder");

                var spec = NuspecManifest.ParseFromFile(nuspecPath);

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

                ZipPackage.SetMetadata(nuspecPath, requiredFrameworks.Select(r => r.Id), options.TargetRuntime);

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

            var prev = ReleasePackageBuilder.GetPreviousRelease(Log, previousReleases, rp, targetDir, options.TargetRuntime);
            if (prev != null && generateDeltas) {
                var deltaBuilder = new DeltaPackageBuilder(Log);
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
            helper.SetPEVersionBlockFromPackageInfo(targetSetupExe, bundledzp, setupIcon);
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

        Log.Info("Done");
    }

    private void signFiles(SigningOptions options, string rootDir, params string[] filePaths)
    {
        var signParams = options.SignParameters;
        var signTemplate = options.SignTemplate;
        var signParallel = options.SignParallel;
        var helper = new HelperExe(Log);

        if (String.IsNullOrEmpty(signParams) && String.IsNullOrEmpty(signTemplate)) {
            Log.Debug($"No signing paramaters provided, {filePaths.Length} file(s) will not be signed.");
            return;
        }

        if (!String.IsNullOrEmpty(signTemplate)) {
            Log.Info($"Preparing to sign {filePaths.Length} files with custom signing template");
            foreach (var f in filePaths) {
                helper.SignPEFileWithTemplate(f, signTemplate);
            }
            return;
        }

        // signtool.exe does not work if we're not on windows.
        if (!SquirrelRuntimeInfo.IsWindows) return;

        if (!String.IsNullOrEmpty(signParams)) {
            Log.Info($"Preparing to sign {filePaths.Length} files with embedded signtool.exe with parallelism of {signParallel}");
            helper.SignPEFilesWithSignTool(rootDir, filePaths, signParams, signParallel);
        }
    }
}