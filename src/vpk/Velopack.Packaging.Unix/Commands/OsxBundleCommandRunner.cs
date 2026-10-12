using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Util;

namespace Velopack.Packaging.Unix.Commands;

public class OsxBundleCommandRunner : ValidatedCommand<OsxBundleOptions, OsxBundleOptionsValidator>
{
    private readonly ILogger _logger;

    public OsxBundleCommandRunner(ILogger logger)
    {
        _logger = logger;
    }

    protected override Task RunCoreAsync(OsxBundleOptions options)
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

        var appleSafeVersion = SemanticVersion.Parse(packVersion).Version.ToString();

        var fileAssociations = AppAssociations.ParseFileAssociations(options.FileAssociations);
        var urlProtocols = AppAssociations.ParseUrlProtocols(options.UrlProtocols);

        // the main Info.plist never declares CFBundleDocumentTypes / CFBundleURLTypes: the nested
        // association helper bundle owns them (see OsxAssociationHelper)
        var info = new OsxAppInfo {
            // SQPackId = packId,
            // SQPackAuthors = packAuthors,
            CFBundleName = packTitle ?? packId,
            //CFBundleDisplayName = packTitle ?? packId,
            CFBundleExecutable = exeName,
            CFBundleIdentifier = options.BundleId ?? GetDefaultBundleId(packId, packAuthors),
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
        FileUtil.CopyDirectoryContents(packDirectory, builder.MacosDirectory, _logger);

        if (fileAssociations.Length + urlProtocols.Length > 0) {
            // not signed here (vpk bundle never signs); the user signs it like the rest of the bundle
            var helperInfo = OsxAssociationHelper.CreateInfo(
                info.CFBundleName,
                info.CFBundleIdentifier,
                appleSafeVersion,
                packVersion,
                fileAssociations,
                urlProtocols,
                options.AssociationLaunchMode);
            OsxAssociationHelper.Build(_logger, builder.AppDirectory, helperInfo, HelperFile.GetAssocHelperPath(), icon);
        }

        _logger.Debug("Bundle created successfully: " + builder.AppDirectory);

        return builder.AppDirectory;
    }

    /// <summary>The bundle id used when neither --bundleId nor a custom Info.plist is given.</summary>
    public static string GetDefaultBundleId(string packId, string packAuthors)
    {
        return Regex.Replace($"com.{packAuthors ?? packId}.{packId}", @"[^\w\.]", "_");
    }
}