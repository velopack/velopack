using Velopack.Packaging;

namespace Velopack.Vpk.Commands;

public abstract class OutputCommand : BaseCommand
{
    public string ReleaseDir { get; private set; }

    public string Channel { get; private set; }

    public bool UpdateReleasesFile { get; set; }

    protected CliOption<DirectoryInfo> ReleaseDirectoryOption { get; private set; }

    protected CliOption<string> ChannelOption { get; private set; }

    protected OutputCommand(string name, string description, RuntimeOs targetOs = RuntimeOs.Unknown)
        : base(name, description)
    {
        ReleaseDirectoryOption = AddOption<DirectoryInfo>((v) => ReleaseDir = v.ToFullNameOrNull(), "-o", "--outputDir")
             .SetDescription("Output directory for created packages.")
             .SetArgumentHelpName("DIR")
             .SetDefault(new DirectoryInfo("Releases"));

        ChannelOption = AddOption<string>((v) => Channel = v, "-c", "--channel")
            .SetDescription("The channel to use for this release.")
            .RequiresValidNuGetId()
            .SetArgumentHelpName("NAME")
            .SetDefault(ReleaseEntryHelper.GetDefaultChannel(targetOs == RuntimeOs.Unknown ? VelopackRuntimeInfo.SystemOs : targetOs));

        AddOption<bool>((v) => UpdateReleasesFile = v, "--update-releases-file", "-u")
            .SetDescription("Create or update the local releases files.")
            .SetDefault(false);
    }

    public DirectoryInfo GetReleaseDirectory()
    {
        var di = new DirectoryInfo(ReleaseDir);
        if (!di.Exists) di.Create();
        return di;
    }
}
