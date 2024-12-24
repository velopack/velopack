using Velopack.Core;

namespace Velopack.Packaging.Commands;

public class DeltaGenOptions
{
    public DeltaMode DeltaMode { get; set; }

    public string BasePackage { get; set; }

    public string NewPackage { get; set; }

    public string OutputFile { get; set; }
}