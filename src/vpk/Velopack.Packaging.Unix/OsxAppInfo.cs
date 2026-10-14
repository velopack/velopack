namespace Velopack.Packaging.Unix;

public class OsxAppInfo
{
    public string CFBundleName { get; set; }

    public string CFBundleDisplayName { get; set; }

    public string CFBundleIdentifier { get; set; }

    public string CFBundleVersion { get; set; }

    public string CFBundlePackageType { get; set; }

    public string CFBundleSignature { get; set; }

    public string CFBundleExecutable { get; set; }

    public string CFBundleIconFile { get; set; }

    public string CFBundleShortVersionString { get; set; }

    public string NSPrincipalClass { get; set; }

    public bool NSHighResolutionCapable { get; set; }

    public bool? NSRequiresAquaSystemAppearance { get; private set; }

    /// <summary>Hides the app from the Dock and the app switcher (used by the association helper).</summary>
    public bool? LSUIElement { get; set; }

    public string LSMinimumSystemVersion { get; set; }

    /// <summary>
    /// Normalized file extensions (with leading dot, eg. ".myproj") emitted as CFBundleDocumentTypes.
    /// </summary>
    public string[] DocumentExtensions { get; set; } = [];

    /// <summary>
    /// Normalized URL schemes (eg. "myapp") emitted as CFBundleURLTypes.
    /// </summary>
    public string[] UrlSchemes { get; set; } = [];
}
