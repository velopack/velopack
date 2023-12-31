using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Vpk.Commands
{
    public class OutputCommand : BaseCommand
    {
        public string ReleaseDirectory { get; private set; }

        protected CliOption<DirectoryInfo> ReleaseDirectoryOption { get; private set; }

        protected OutputCommand(string name, string description) 
            : base(name, description)
        {
            ReleaseDirectoryOption = AddOption<DirectoryInfo>((v) => ReleaseDirectory = v.ToFullNameOrNull(), "-o", "--outputDir")
                 .SetDescription("Output directory for Squirrel packages.")
                 .SetArgumentHelpName("DIR")
                 .SetDefault(new DirectoryInfo(".\\Releases"));
        }

        public DirectoryInfo GetReleaseDirectory()
        {
            var di = new DirectoryInfo(ReleaseDirectory);
            if (!di.Exists) di.Create();
            return di;
        }
    }
}
