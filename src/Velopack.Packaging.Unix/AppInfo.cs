namespace Velopack.Packaging.OSX;

internal class AppInfo
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

}
