using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Commands
{
    public class DeltaPatchOptions
    {
        public string BasePackage { get; set; }

        public FileInfo[] PatchFiles { get; set; }

        public string OutputFile { get; set; }
    }
}
