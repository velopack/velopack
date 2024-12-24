using Velopack.Core;
using Velopack.Packaging;

namespace Velopack.Vpk.Commands;

public class DeltaGenCommand : BaseCommand
{
    public DeltaMode DeltaMode { get; set; }

    public string BasePackage { get; set; }

    public string NewPackage { get; set; }

    public string OutputFile { get; set; }

    public DeltaGenCommand()
        : base("generate", "Generate a delta patch from two full releases.")

    {
        AddOption<DeltaMode>((v) => DeltaMode = v, "--mode")
            .SetDefault(DeltaMode.BestSpeed)
            .SetDescription("Set the delta generation mode.");

        AddOption<FileInfo>((v) => BasePackage = v.ToFullNameOrNull(), "--base", "-b")
            .SetDescription("The base package for the created patch.")
            .SetArgumentHelpName("PATH")
            .RequiresExtension(".nupkg")
            .MustExist()
            .SetRequired();

        AddOption<FileInfo>((v) => NewPackage = v.ToFullNameOrNull(), "--new", "-n")
            .SetDescription("The resulting package for the created patch.")
            .SetArgumentHelpName("PATH")
            .RequiresExtension(".nupkg")
            .MustExist()
            .SetRequired();

        AddOption<FileInfo>((v) => OutputFile = v.ToFullNameOrNull(), "--output", "-o")
            .SetDescription("The output file path for the created patch.")
            .SetArgumentHelpName("PATH")
            .SetRequired();
    }
}
