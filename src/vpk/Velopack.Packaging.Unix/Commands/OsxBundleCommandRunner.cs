using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Exceptions;
using Velopack.Util;

namespace Velopack.Packaging.Unix.Commands;

[SupportedOSPlatform("osx")]
public class OsxBundleCommandRunner : ICommand<OsxBundleOptions>
{
    private readonly ILogger _logger;

    public OsxBundleCommandRunner(ILogger logger)
    {
        _logger = logger;
    }

    public Task Run(OsxBundleOptions options)
    {
        Bundle(options);
        return Task.CompletedTask;
    }

    public string Bundle(OsxBundleOptions options)
    {
        var icon = options.Icon ?? HelperFile.GetDefaultAppIcon(RuntimeOs.OSX);
        var packId = options.PackId;
        var packDirectory = options.PackDirectory;
        var packVersion = options.PackVersion;
        var exeName = options.EntryExecutableName ?? options.PackId;
        var packAuthors = options.PackAuthors;
        var packTitle = options.PackTitle;
        var releaseDir = options.ReleaseDir;

        _logger.Info("Generating new '.app' bundle from a directory of application files.");

        var mainExePath = Path.Combine(packDirectory, exeName);
        if (!File.Exists(mainExePath)) {
            throw new UserInfoException($"--exeName '{mainExePath}' does not exist.");
        }

        if (!BinDetect.IsMachOImage(mainExePath)) {
            throw new UserInfoException($"--exeName '{mainExePath}' is not a mach-o executable.");
        }

        var appleId = $"com.{packAuthors ?? packId}.{packId}";
        var escapedAppleId = Regex.Replace(appleId, @"[^\w\.]", "_");
        var appleSafeVersion = NuGetVersion.Parse(packVersion).Version.ToString();

        var info = new OsxAppInfo {
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

        _logger.Debug("Creating '.app' directory structure");
        var builder = new OsxStructureBuilder(packId, releaseDir.FullName);
        if (Directory.Exists(builder.AppDirectory)) {
            _logger.Warn(builder.AppDirectory + " already exists, deleting...");
            IoUtil.DeleteFileOrDirectoryHard(builder.AppDirectory);
        }

        builder.Build();

        if (options.InfoPlistPath != null) {
            _logger.Info("Bundle using provided Info.plist: " + options.InfoPlistPath);
            File.Copy(options.InfoPlistPath, Path.Combine(builder.ContentsDirectory, "Info.plist"));
        } else {
            _logger.Debug("Writing generic Info.plist");
            var plist = new PlistWriter(_logger, info, builder.ContentsDirectory);
            plist.Write();
        }

        _logger.Debug("Copying resources into new '.app' bundle");
        File.Copy(icon, Path.Combine(builder.ResourcesDirectory, Path.GetFileName(icon)));

        _logger.Debug("Copying application files into new '.app' bundle");
        new OsxBuildTools(_logger).CopyPreserveSymlinks(packDirectory, builder.MacosDirectory);

        _logger.Debug("Bundle created successfully: " + builder.AppDirectory);

        return builder.AppDirectory;
    }
}