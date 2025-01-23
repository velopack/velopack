namespace Velopack.Vpk.Commands.Packaging;

public class OsxBundleCommand : PackCommand
{
    public string BundleId { get; private set; }

    public string InfoPlistPath { get; private set; }

    public OsxBundleCommand()
        : this("bundle", "Create's an OSX .app bundle from a folder containing application files.")
    {
        RemoveOption(NoPortableOption);
        RemoveOption(NoInstOption);
        RemoveOption(ReleaseNotesOption);
        RemoveOption(DeltaModeOption);
        RemoveOption(CustomUrlProtocolsOption);
    }

    public OsxBundleCommand(string name, string description)
        : base(name, description, RuntimeOs.OSX)
    {
        IconOption.RequiresExtension(".icns");

        var bundleId = AddOption<string>((v) => BundleId = v, "--bundleId")
            .SetDescription("Optional Apple bundle Id.")
            .SetArgumentHelpName("ID");

        var infoPlist = AddOption<FileInfo>((v) => InfoPlistPath = v.ToFullNameOrNull(), "--plist")
            .SetDescription("A custom Info.plist to use in the app bundle.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        this.AreMutuallyExclusive(bundleId, infoPlist);
    }
}
