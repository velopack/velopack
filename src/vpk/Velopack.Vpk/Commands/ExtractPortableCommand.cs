using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Vpk.Commands;
public class ExtractPortableCommand : BaseCommand
{
    public string SetupPackagePath { get; set; }
    public string OutputPath { get; set; }

    public ExtractPortableCommand()
        : base("extract", "Extract portable package from a setup package.")
    {
        AddOption<FileInfo>((v) => SetupPackagePath = v.ToFullNameOrNull(), "--input", "-i")
            .SetDescription("The input file path for the setup package.")
//            .SetArgumentHelpName("PATH")
            .SetRequired();

        AddOption<DirectoryInfo>((v) => OutputPath = v.ToFullNameOrNull(), "--output", "-o")
            .SetDescription("The output file path for the extracted package.")
//            .SetArgumentHelpName("PATH")
            .SetRequired();
    }
}
