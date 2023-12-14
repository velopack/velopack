using System.Security;
using Microsoft.Extensions.Logging;
using NuGet.Commands;

namespace Squirrel.Packaging;

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

public class NugetConsole
{
    private readonly ILogger Log;

    public NugetConsole(ILogger logger)
    {
        Log = logger;
    }

    public static string CreateNuspec(
        string packId, string packTitle, string packAuthors,
        string packVersion, string releaseNotes, bool includePdb)
    {
        var releaseNotesText = String.IsNullOrEmpty(releaseNotes)
            ? "" // no releaseNotes
            : $"<releaseNotes>{SecurityElement.Escape(File.ReadAllText(releaseNotes))}</releaseNotes>";

        string nuspec = $@"
<?xml version=""1.0"" encoding=""utf-8""?>
<package>
  <metadata>
    <id>{packId}</id>
    <title>{packTitle ?? packId}</title>
    <description>{packTitle ?? packId}</description>
    <authors>{packAuthors ?? packId}</authors>
    <version>{packVersion}</version>
    {releaseNotesText}
  </metadata>
  <files>
    <file src=""**"" target=""lib\squirrel\"" exclude=""{(includePdb ? "" : "*.pdb;")}*.nupkg;*.vshost.*;**\createdump.exe""/>
  </files>
</package>
".Trim();

        return nuspec;
    }

    public string CreatePackageFromNuspecPath(string tempDir, string packDir, string nuspecPath)
    {
        var nup = Path.Combine(tempDir, "squirreltemp.nuspec");
        File.Copy(nuspecPath, nup);

        Pack(nup, packDir, tempDir);

        var nupkgPath = Directory.EnumerateFiles(tempDir).Where(f => f.EndsWith(".nupkg")).FirstOrDefault();
        if (nupkgPath == null)
            throw new Exception($"Failed to generate nupkg, unspecified error");

        return nupkgPath;
    }

    public string CreatePackageFromOptions(string tempDir, INugetPackCommand command)
    {
        return CreatePackageFromMetadata(tempDir, command.PackDirectory, command.PackId, command.PackTitle,
            command.PackAuthors, command.PackVersion, command.ReleaseNotes, command.IncludePdb);
    }

    public string CreatePackageFromMetadata(
        string tempDir, string packDir, string packId, string packTitle, string packAuthors,
        string packVersion, string releaseNotes, bool includePdb)
    {
        string nuspec = CreateNuspec(packId, packTitle, packAuthors, packVersion, releaseNotes, includePdb);
        var nuspecPath = Path.Combine(tempDir, packId + ".nuspec");
        File.WriteAllText(nuspecPath, nuspec);
        return CreatePackageFromNuspecPath(tempDir, packDir, nuspecPath);
    }

    public void Pack(string nuspecPath, string baseDirectory, string outputDirectory)
    {
        Log.Info($"Starting to package '{nuspecPath}'");
        var args = new PackArgs() {
            Deterministic = true,
            BasePath = baseDirectory,
            OutputDirectory = outputDirectory,
            Path = nuspecPath,
            Exclude = Enumerable.Empty<string>(),
            Arguments = Enumerable.Empty<string>(),
            Logger = new NugetLoggingWrapper(Log),
            ExcludeEmptyDirectories = true,
            NoDefaultExcludes = true,
            NoPackageAnalysis = true,
        };

        var c = new PackCommandRunner(args, null);
        if (!c.RunPackageBuild())
            throw new Exception("Error creating nuget package.");
    }
}