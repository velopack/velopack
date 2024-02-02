namespace Velopack.Vpk.Commands;

public class DeltaPatchCommand : BaseCommand
{
    public string BasePackage { get; set; }

    public FileInfo[] PatchFiles { get; set; }

    public string OutputFile { get; set; }

    public DeltaPatchCommand()
        : base("patch", "Patch a base package and retrieve the original new package.")

    {
        AddOption<FileInfo>((v) => BasePackage = v.ToFullNameOrNull(), "--base", "-b")
            .SetDescription("The base package for the created patch.")
            .SetArgumentHelpName("PATH")
            .RequiresExtension(".nupkg")
            .MustExist()
            .SetRequired();

        AddOption<FileInfo[]>((v) => PatchFiles = v, "--patch", "-p")
            .SetDescription("The resulting package for the created patch.")
            .AllowMultiple()
            .SetArgumentHelpName("PATH");

        AddOption<FileInfo>((v) => OutputFile = v.ToFullNameOrNull(), "--output", "-o")
            .SetDescription("The output file path for the created patch.")
            .SetArgumentHelpName("PATH")
            .SetRequired();
    }
}
