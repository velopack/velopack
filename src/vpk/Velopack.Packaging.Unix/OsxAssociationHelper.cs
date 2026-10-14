using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

/// <summary>
/// Builds the nested <c>Contents/Helpers/{Name}.app</c> bundle which owns the app's CFBundleURLTypes /
/// CFBundleDocumentTypes on macOS. The helper (the <c>assochelper</c> rust binary) receives the open-URL /
/// open-file events and forwards them to a running instance that accepts activations, or else launches the main app
/// with <c>--veloapp-urlproto</c> / <c>--veloapp-fileassoc</c> argv pairs.
/// Cross-platform (no mac tools) so it is testable on Windows, except <see cref="BuildForBundle"/>.
/// </summary>
public static class OsxAssociationHelper
{
    public const string ExecutableName = "AssocHelper";

    public const string MinimumSystemVersion = "10.13";

    private static readonly Regex InvalidBundleIdChars = new("[^A-Za-z0-9.-]", RegexOptions.Compiled);

    /// <summary>The helper bundle name shown in Finder "Open With": the main display name made path-safe.</summary>
    public static string SanitizeBundleName(string displayName)
    {
        var name = (displayName ?? "").Replace('/', '_').Replace(':', '_').Trim();
        return name.Length == 0 ? "Associations" : name;
    }

    /// <summary>The helper bundle id: <c>{mainBundleId}.velopack-assoc</c>, unique and stable per app.</summary>
    public static string MakeBundleId(string mainBundleId)
    {
        return InvalidBundleIdChars.Replace(mainBundleId ?? "", "-") + ".velopack-assoc";
    }

    /// <summary>Creates the Info.plist content of a helper for the given main app.</summary>
    public static OsxAppInfo CreateInfo(string displayName, string mainBundleId, string shortVersion, string version,
        string[] extensions, string[] schemes)
    {
        return new OsxAppInfo {
            CFBundleName = displayName,
            CFBundleDisplayName = displayName,
            CFBundleExecutable = ExecutableName,
            CFBundleIdentifier = MakeBundleId(mainBundleId),
            CFBundlePackageType = "APPL",
            CFBundleSignature = "????",
            CFBundleShortVersionString = shortVersion,
            CFBundleVersion = version,
            NSPrincipalClass = "NSApplication",
            NSHighResolutionCapable = true,
            LSUIElement = true,
            LSMinimumSystemVersion = MinimumSystemVersion,
            DocumentExtensions = extensions ?? [],
            UrlSchemes = schemes ?? [],
        };
    }

    /// <summary>
    /// Creates <c>{appBundlePath}/Contents/Helpers/{SanitizeBundleName(info.CFBundleName)}.app</c> (deleting any
    /// existing helper first) with the helper executable, the icon and the Info.plist. Returns the helper .app path.
    /// </summary>
    public static string Build(ILogger log, string appBundlePath, OsxAppInfo info, string helperBinary, string iconPath)
    {
        foreach (var existing in FindAll(appBundlePath)) {
            log.Debug("Removing existing association helper: " + existing);
            IoUtil.DeleteFileOrDirectoryHard(existing);
        }

        var helpersDir = Path.Combine(appBundlePath, "Contents", "Helpers");
        var builder = new OsxStructureBuilder(SanitizeBundleName(info.CFBundleName), helpersDir);
        log.Info("Creating association helper bundle: " + builder.AppDirectory);
        builder.Build();

        var exePath = Path.Combine(builder.MacosDirectory, ExecutableName);
        File.Copy(helperBinary, exePath, true);
        Chmod.ChmodFileAsExecutable(exePath);

        info.CFBundleIconFile = Path.GetFileName(iconPath);
        File.Copy(iconPath, Path.Combine(builder.ResourcesDirectory, info.CFBundleIconFile), true);

        new PlistWriter(log, info, builder.ContentsDirectory).Write();
        return builder.AppDirectory;
    }

    /// <summary>
    /// Builds the helper for an existing app bundle, taking the main app's identity (bundle id, display name, icon)
    /// from its final <c>Contents/Info.plist</c> (read with PlistBuddy, so macOS only) so a prebuilt .app or a user
    /// <c>--plist</c> is matched in Finder "Open With". The fallbacks apply when the plist lacks the key.
    /// </summary>
    public static string BuildForBundle(ILogger log, string appBundlePath, string[] extensions, string[] schemes,
        string packVersion, string iconOption, string fallbackBundleId, string fallbackDisplayName)
    {
        var structure = new OsxStructureBuilder(appBundlePath);
        var plist = Path.Combine(structure.ContentsDirectory, PlistWriter.PlistFileName);
        string Read(string key) => OsxBuildTools.ReadPlistValue(plist, key)?.Trim();

        var mainBundleId = Read("CFBundleIdentifier") ?? fallbackBundleId;
        var displayName = Read("CFBundleDisplayName") ?? Read("CFBundleName") ?? fallbackDisplayName;

        // the helper shows the main app's icon in Finder / Safari: --icon, else the plist's CFBundleIconFile, else
        // CFBundleIconName (an icon in Assets.car cannot be reused, so warn when falling back to the default)
        var icon = iconOption;
        var iconFile = Read("CFBundleIconFile");
        if (icon == null && iconFile != null) {
            icon = new[] { iconFile, iconFile + ".icns" }
                .Select(f => Path.Combine(structure.ResourcesDirectory, f))
                .FirstOrDefault(File.Exists);
        }

        var iconName = Read("CFBundleIconName");
        if (icon == null && iconName != null) {
            var path = Path.Combine(structure.ResourcesDirectory, iconName + ".icns");
            icon = File.Exists(path) ? path : null;
        }

        if (icon == null) {
            icon = HelperFile.GetDefaultAppIcon(RuntimeOs.OSX);
            if (iconName != null) {
                log.Warn("The association helper will use the default Velopack icon in Finder/Safari; pass --icon with the app's .icns to match.");
            }
        }

        if (Read("CFBundleURLTypes") != null || Read("CFBundleDocumentTypes") != null) {
            log.Warn(
                "Info.plist also declares CFBundleURLTypes/CFBundleDocumentTypes. Launch Services may route activations to the " +
                "main bundle, where they arrive as Apple Events for the app's own framework and NOT as Velopack OnFileAssoc/OnUrlProto " +
                "callbacks. Remove those keys from Info.plist or do not pass --fileAssociations/--urlProtocols.");
        }

        var info = CreateInfo(
            displayName,
            mainBundleId,
            SemanticVersion.Parse(packVersion).Version.ToString(),
            packVersion,
            extensions,
            schemes);
        return Build(log, appBundlePath, info, HelperFile.GetAssocHelperPath(), icon);
    }

    /// <summary>An existing helper .app (eg. built by <c>vpk bundle</c>) inside the app bundle, or null.</summary>
    public static string Find(string appBundlePath) => FindAll(appBundlePath).FirstOrDefault();

    /// <summary>Every <c>Contents/Helpers/*.app</c> whose <c>Contents/MacOS/AssocHelper</c> exists.</summary>
    public static string[] FindAll(string appBundlePath)
    {
        var helpersDir = Path.Combine(appBundlePath, "Contents", "Helpers");
        if (!Directory.Exists(helpersDir)) {
            return [];
        }

        return Directory.GetDirectories(helpersDir, "*.app")
            .Where(app => File.Exists(Path.Combine(app, "Contents", "MacOS", ExecutableName)))
            .OrderBy(app => app, StringComparer.Ordinal)
            .ToArray();
    }
}
