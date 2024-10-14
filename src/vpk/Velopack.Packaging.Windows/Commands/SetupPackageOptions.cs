namespace Velopack.Packaging.Commands;
public class SetupPackageOptions
{
    public string NugetPackagePath { get; set; }
    public string OutputPath { get; set; }
    public string Icon { get; set; }
    public string SignParameters { get; set; }
    public int SignParallel { get; set; }
    public string SignTemplate { get; set; }
}
