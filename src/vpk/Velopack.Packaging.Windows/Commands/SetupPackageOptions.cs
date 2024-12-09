using Velopack.Packaging.Windows.Commands;

namespace Velopack.Packaging.Commands;
public class SetupPackageOptions : IWindowsSetupPackageOptions
{
    public string NugetPackagePath { get; set; }

    public string OutputPath { get; set; }

    public string Icon { get; set; }

    public string SignParameters { get; set; }

    public bool SignSkipDll { get; set; }

    public int SignParallel { get; set; }

    public string SignTemplate { get; set; }
}
