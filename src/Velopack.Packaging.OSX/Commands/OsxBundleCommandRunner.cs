using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Velopack.Packaging.OSX.Commands;

public class OsxBundleCommandRunner
{
    private readonly ILogger _logger;

    public OsxBundleCommandRunner(ILogger logger)
    {
        _logger = logger;
    }

    public string Bundle(OsxBundleOptions options)
    {
        var icon = options.Icon;
        var packId = options.PackId;
        var packDirectory = options.PackDirectory;
        var packVersion = options.PackVersion;
        var exeName = options.EntryExecutableName;
        var packAuthors = options.PackAuthors;
        var packTitle = options.PackTitle;
        var releaseDir = options.ReleaseDir;

        _logger.Info("Generating new '.app' bundle from a directory of application files.");

        var mainExePath = Path.Combine(packDirectory, exeName);
        if (!File.Exists(mainExePath) || !MachO.IsMachOImage(mainExePath))
            throw new ArgumentException($"--exeName '{mainExePath}' does not exist or is not a mach-o executable.");

        var appleId = $"com.{packAuthors ?? packId}.{packId}";
        var escapedAppleId = Regex.Replace(appleId, @"[^\w\.]", "_");
        var appleSafeVersion = NuGetVersion.Parse(packVersion).Version.ToString();

        var info = new AppInfo {
            // SQPackId = packId,
            // SQPackAuthors = packAuthors,
            CFBundleName = packTitle ?? packId,
            //CFBundleDisplayName = packTitle ?? packId,
            CFBundleExecutable = exeName,
            CFBundleIdentifier = options.BundleId ?? escapedAppleId,
            CFBundlePackageType = "APPL",
            CFBundleShortVersionString = appleSafeVersion,
            CFBundleVersion = packVersion,
            CFBundleSignature = "????",
            NSPrincipalClass = "NSApplication",
            NSHighResolutionCapable = true,
            CFBundleIconFile = Path.GetFileName(icon),
        };

        _logger.Info("Creating '.app' directory structure");
        var builder = new StructureBuilder(packId, releaseDir.FullName);
        if (Directory.Exists(builder.AppDirectory)) {
            _logger.Warn(builder.AppDirectory + " already exists, deleting...");
            Utility.DeleteFileOrDirectoryHard(builder.AppDirectory);
        }

        builder.Build();

        _logger.Info("Writing Info.plist");
        var plist = new PlistWriter(_logger, info, builder.ContentsDirectory);
        plist.Write();

        _logger.Info("Copying resources into new '.app' bundle");
        File.Copy(icon, Path.Combine(builder.ResourcesDirectory, Path.GetFileName(icon)));

        _logger.Info("Copying application files into new '.app' bundle");
        Utility.CopyFiles(new DirectoryInfo(packDirectory), new DirectoryInfo(builder.MacosDirectory));

        _logger.Info("Bundle created successfully: " + builder.AppDirectory);

        return builder.AppDirectory;
    }
}