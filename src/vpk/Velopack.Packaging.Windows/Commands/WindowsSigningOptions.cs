namespace Velopack.Packaging.Windows.Commands;

public class WindowsSigningOptions
{
    public string SignParameters { get; set; }

    public string SignExclude { get; set; }

    /// <summary>
    /// For --signParams / --signTemplate, the number of files passed to each (sequential) signing command. For Azure
    /// Trusted Signing, the maximum number of files signed concurrently.
    /// </summary>
    public int SignParallel { get; set; } = 10;

    public string SignTemplate { get; set; }

    public string AzureTrustedSignFile { get; set; }
}