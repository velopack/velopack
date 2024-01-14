namespace Velopack.Packaging
{
    public interface IPackOptions : INugetPackCommand
    {
        RID TargetRuntime { get; }
        DirectoryInfo ReleaseDir { get; }
        string Channel { get; }
        DeltaMode DeltaMode { get; }
        string EntryExecutableName { get; }
    }
}
