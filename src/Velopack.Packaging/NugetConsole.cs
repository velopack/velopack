using System.Security;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging;

public class NugetConsole
{
    private readonly ILogger Log;

    public NugetConsole(ILogger logger)
    {
        Log = logger;
    }

    public static string CreateNuspec(
        string packId, string packTitle, string packAuthors,
        string packVersion, string releaseNotes)
    {
        var releaseNotesText = String.IsNullOrEmpty(releaseNotes)
            ? "" // no releaseNotes
            : $"<releaseNotes>{SecurityElement.Escape(File.ReadAllText(releaseNotes))}</releaseNotes>";

        string nuspec = $@"
<?xml version=""1.0"" encoding=""utf-8""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
  <metadata>
    <id>{packId}</id>
    <title>{packTitle ?? packId}</title>
    <description>{packTitle ?? packId}</description>
    <authors>{packAuthors ?? packId}</authors>
    <version>{packVersion}</version>
    {releaseNotesText}
  </metadata>
</package>
".Trim();

        return nuspec;
    }
}