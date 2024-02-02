using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Abstractions;

public interface IPlatformOptions : IOutputOptions
{
    RID TargetRuntime { get; }
}
