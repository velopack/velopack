using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Vpk.Commands
{
    public abstract class PlatformCommand : OutputCommand
    {
        public string TargetRuntime { get; set; }

        protected CliOption<string> TargetRuntimeOption { get; private set; }

        protected PlatformCommand(string name, string description) : base(name, description)
        {
            TargetRuntimeOption = AddOption<string>((v) => TargetRuntime = v, "-r", "--runtime")
                .SetDescription("The target runtime to build packages for.")
                .SetArgumentHelpName("RID")
                .SetDefault(VelopackRuntimeInfo.SystemOs.GetOsShortName())
                .MustBeSupportedRid();
        }

        public RID GetRid() => RID.Parse(TargetRuntime ?? VelopackRuntimeInfo.SystemOs.GetOsShortName());

        public RuntimeOs GetRuntimeOs() => GetRid().BaseRID;
    }
}
