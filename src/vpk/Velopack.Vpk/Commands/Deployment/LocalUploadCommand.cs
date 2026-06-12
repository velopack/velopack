namespace Velopack.Vpk.Commands.Deployment;

public class LocalUploadCommand : LocalBaseCommand
{
    public int KeepMaxReleases { get; private set; }

    public bool ForceRegenerate { get; private set; }

    public LocalUploadCommand()
        : base("local", "Upload releases to a local path or network share.")
    {
        AddOption<int>((x) => KeepMaxReleases = x, ["--keepMaxReleases"])
         .SetDescription("The maximum number of releases to keep, older releases are deleted.")
         .SetArgumentHelpName("COUNT");

        AddOption<bool>((x) => ForceRegenerate = x, ["--regenerate"])
            .SetDescription("Force regenerate the releases.{channel}.json file.");
    }
}
