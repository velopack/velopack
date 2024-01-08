namespace Velopack.Packaging;

public interface INugetPackCommand
{
    string PackId { get; }
    string PackVersion { get; }
    string PackDirectory { get; }
    string PackAuthors { get; }
    string PackTitle { get; }
    bool IncludePdb { get; }
    string ReleaseNotes { get; }
}
