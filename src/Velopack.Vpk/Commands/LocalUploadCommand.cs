namespace Velopack.Vpk.Commands;

public class LocalUploadCommand : LocalBaseCommand
{
    public DirectoryInfo SecondPath { get; private set; }

    public bool SkipUploadPortable { get; private set; }

    public bool SkipUploadInstaller { get; private set; }

    public LocalUploadCommand()
        : base("local", "Upload releases to a local path source.")
    {
        PathOption.SetDescription("Path to upload releases to.");

        AddOption<DirectoryInfo>((v) => SecondPath = v, "-sp", "--secondPath")
            .SetDescription("Path to upload the portable version of the application and the installer to. They will not be uploaded to the main path if this option is present. If the folder does not exist, it will be created.");

        AddOption<bool>((v) => SkipUploadPortable = v, "--skipPortable")
            .SetDescription("Skip uploading the portable version of the application.");

        AddOption<bool>((v) => SkipUploadInstaller = v, "--skipInstaller")
            .SetDescription("Skip uploading the installer of the application.");
    }
}
