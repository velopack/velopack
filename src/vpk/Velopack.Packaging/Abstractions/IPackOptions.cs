namespace Velopack.Packaging.Abstractions;

public interface IPackOptions : INugetPackCommand, IPlatformOptions
{
    string Channel { get; set; }
    DeltaMode DeltaMode { get; set; }
    string EntryExecutableName { get; set; }
    string Icon { get; set; }
    string Exclude { get; set; }
    bool NoPortable { get; set; }
    bool NoInst { get; set; }
    bool BuildMsi { get; }
}
