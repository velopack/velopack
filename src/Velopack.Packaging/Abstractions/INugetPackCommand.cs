namespace Velopack.Packaging.Abstractions;

public interface INugetPackCommand
{
    string PackId { get; }
    string PackVersion { get; }
    string PackDirectory { get; }
    string PackAuthors { get; }
    string PackTitle { get; }
    string ReleaseNotes { get; }
}
