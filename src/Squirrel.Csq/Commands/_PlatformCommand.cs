using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squirrel.Csq.Commands
{
    public class PlatformCommand : OutputCommand
    {
        public string TargetRuntime { get; set; }

        public FileSystemInfo SolutionDir { get; set; }

        protected PlatformCommand(string name, string description) : base(name, description)
        {
            TargetRuntime = SquirrelRuntimeInfo.SystemOs.GetOsShortName();

            AddOption<string>((v) => TargetRuntime = v, "-r", "--runtime")
                .SetDescription("The target runtime to build packages for.")
                .SetArgumentHelpName("RID")
                .MustBeSupportedRid();

            AddOption<FileSystemInfo>((v) => SolutionDir = v, "--sln")
                .SetDescription("Explicit path to project solution (.sln)")
                .AcceptExistingOnly();
        }

        public RID GetRid() => RID.Parse(TargetRuntime ?? SquirrelRuntimeInfo.SystemOs.GetOsShortName());

        public RuntimeOs GetRuntimeOs() => GetRid().BaseRID;
    }
}
