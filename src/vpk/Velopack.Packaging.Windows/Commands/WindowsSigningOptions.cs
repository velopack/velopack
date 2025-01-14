namespace Velopack.Packaging.Windows.Commands;

public class WindowsSigningOptions
{
    public string SignParameters { get; set; }

    public string SignExclude { get; set; }

    public int SignParallel { get; set; }

    public string SignTemplate { get; set; }

    public string AzureTrustedSignFile { get; set; }
}