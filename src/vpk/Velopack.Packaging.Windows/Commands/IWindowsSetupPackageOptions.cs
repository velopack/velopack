using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Windows.Commands;
public interface IWindowsSetupPackageOptions : IWindowsCodeSigningOptions
{
    public string Icon { get; set; }
}
