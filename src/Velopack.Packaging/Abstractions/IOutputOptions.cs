using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Abstractions
{
    public interface IOutputOptions
    {
        DirectoryInfo ReleaseDir { get; }
    }
}
