namespace Velopack.Vpk.Commands.Deployment;

public class LocalUploadCommand : LocalBaseCommand
{
    public int KeepMaxReleases { get; private set; }
    public int KeepMaxDeltaReleases { get; private set; }

    public bool ForceRegenerate { get; private set; }

    public LocalUploadCommand()
        : base("local", "Upload releases to a local path or network share.")
    {
        AddOption<int>((x) => KeepMaxReleases = x, "--keepMaxReleases")
         .SetDescription("The maximum number of full releases to keep in the target directory, anything older will be deleted.")
         .SetArgumentHelpName("COUNT");

        AddOption<int>((x) => KeepMaxDeltaReleases = x, "--keepMaxDeltaReleases")
         .SetDescription("The maximum number of delta releases to keep in the target directory, anything older will be deleted.");

        AddOption<bool>((x) => ForceRegenerate = x, "--regenerate")
            .SetDescription("Force regenerate the releases.{channel}.json file in the target directory.");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
