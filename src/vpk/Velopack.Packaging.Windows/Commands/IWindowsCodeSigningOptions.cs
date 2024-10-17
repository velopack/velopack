using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Windows.Commands;
public interface IWindowsCodeSigningOptions
{
    public string SignParameters { get; set; }

    public bool SignSkipDll { get; set; }

    public int SignParallel { get; set; }

    public string SignTemplate { get; set; }
}
