using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

/// <summary>
/// Builds the nested <c>Contents/Helpers/{Name}.app</c> bundle which owns the app's CFBundleURLTypes /
/// CFBundleDocumentTypes on macOS. The helper (the <c>assochelper</c> rust binary) receives the open-URL /
/// open-file events and launches the main app with <c>--veloapp-urlproto</c> / <c>--veloapp-fileassoc</c> argv pairs.
/// Cross-platform (no mac tools) so it is testable on Windows.
/// </summary>
public static class OsxAssociationHelper
{
    public const string ExecutableName = "AssocHelper";

    public const string LaunchModePlistKey = "VelopackAssociationLaunchMode";

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
        string[] extensions, string[] schemes, AssociationLaunchMode mode)
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
            VelopackAssociationLaunchMode = mode.ToString(),
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
