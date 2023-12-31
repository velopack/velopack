using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Octokit;
using Squirrel.Packaging;

namespace Squirrel.Csq.Commands
{
    public class DeltaGenCommand : BaseCommand
    {
        public DeltaMode Delta { get; set; }

        public string BasePackage { get; set; }

        public string NewPackage { get; set; }

        public string OutputFile { get; set; }

        public DeltaGenCommand()
            : base("generate", "Generate a delta patch from two full releases.")

        {
            AddOption<DeltaMode>((v) => Delta = v, "--mode")
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
}
