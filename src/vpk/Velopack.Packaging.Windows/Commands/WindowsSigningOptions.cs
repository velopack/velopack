namespace Velopack.Packaging.Windows.Commands;

public class WindowsSigningOptions : IWindowsCodeSigningOptions
{
    public string SignParameters { get; set; }

    public bool SignSkipDll { get; set; }

    public int SignParallel { get; set; }

    public string SignTemplate { get; set; }
}
