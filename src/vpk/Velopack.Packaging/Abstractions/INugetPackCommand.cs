namespace Velopack.Packaging.Abstractions;

public interface INugetPackCommand
{
    string PackId { get; set; }
    string PackVersion { get; set; }
    string PackDirectory { get; set; }
    string PackAuthors { get; set; }
    string PackTitle { get; set; }
    string ReleaseNotes { get; set; }
}
