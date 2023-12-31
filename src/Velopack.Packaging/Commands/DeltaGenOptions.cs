using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Commands
{
    public class DeltaGenOptions
    {
        public DeltaMode DeltaMode { get; set; }

        public string BasePackage { get; set; }

        public string NewPackage { get; set; }

        public string OutputFile { get; set; }
    }
}
