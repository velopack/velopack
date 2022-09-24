using Xunit;
using Squirrel.CommandLine.Windows;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Squirrel.CommandLine.Tests.Windows
{
    public class PackCommandTests : TempFileTestBase
    {
        [Fact]
        public void Command_WithValidRequiredArguments_Parses()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            CreateTempFile(packDir);
            var command = new PackCommand();

            ParseResult parseResult = command.Parse($"-u Clowd.Squirrel -v 1.2.3 -p \"{packDir.FullName}\"");

            Assert.Empty(parseResult.Errors);
            Assert.Equal("Clowd.Squirrel", parseResult.GetValueForOption(command.PackId));
            Assert.Equal("1.2.3", parseResult.GetValueForOption(command.PackVersion));
            Assert.Equal(packDir.FullName, parseResult.GetValueForOption(command.PackDirectory)?.FullName);
        }

        [Fact]
        public void PackId_WithInvalidNuGetId_ShowsError()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            CreateTempFile(packDir);
            var command = new PackCommand();

            ParseResult parseResult = command.Parse($"--packId $42@ -v 1.0.0 -p \"{packDir.FullName}\"");

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.StartsWith("--packId is an invalid NuGet package id.", parseResult.Errors[0].Message);
            Assert.Contains("$42@", parseResult.Errors[0].Message);
        }

        [Fact]
        public void PackName_WithValue_ParsesValue()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            CreateTempFile(packDir);
            var command = new PackCommand();

            ParseResult parseResult = command.Parse($"--packName Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\"");

            Assert.Equal("Clowd.Squirrel", parseResult.GetValueForOption(command.PackName));
        }

        [Fact]
        public void PackVersion_WithInvalidVersion_ShowsError()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            CreateTempFile(packDir);
            var command = new PackCommand();

            ParseResult parseResult = command.Parse($"-u Clowd.Squirrel --packVersion 1.a.c -p \"{packDir.FullName}\"");

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.StartsWith("--packVersion contains an invalid package version", parseResult.Errors[0].Message);
            Assert.Contains("1.a.c", parseResult.Errors[0].Message);
        }

        [Fact]
        public void PackDirectory_WithEmptyFolder_ShowsError()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            var command = new PackCommand();

            ParseResult parseResult = command.Parse($"-u Clowd.Squirrel -v 1.0.0 --packDir \"{packDir.FullName}\"");

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.StartsWith("--packDir must a non-empty directory", parseResult.Errors[0].Message);
            Assert.Contains(packDir.FullName, parseResult.Errors[0].Message);
        }

        [Fact]
        public void PackAuthors_WithMultipleAuthors_ParsesValue()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--packAuthors Me,mysel,I";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("Me,mysel,I", parseResult.GetValueForOption(command.PackAuthors));
        }

        [Fact]
        public void PackTitle_WithTitle_ParsesValue()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--packTitle \"My Awesome Title\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("My Awesome Title", parseResult.GetValueForOption(command.PackTitle));
        }

        [Fact]
        public void IncludePdb_BareOption_SetsFlag()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--includePdb";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.IncludePdb));
        }

        [Fact]
        public void ReleaseNode_WithExistingFile_ParsesValue()
        {
            FileInfo releaseNotes = CreateTempFile();
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--releaseNotes \"{releaseNotes.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(releaseNotes.FullName, parseResult.GetValueForOption(command.ReleaseNotes)?.FullName);
        }

        [Fact]
        public void ReleaseNode_WithoutFile_ShowsError()
        {
            string releaseNotes = Path.GetFullPath(Path.GetRandomFileName());
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--releaseNotes \"{releaseNotes}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.ReleaseNotes, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
            Assert.Contains(releaseNotes, parseResult.Errors[0].Message);
        }

        [Fact]
        public void SquirrelAwareExecutable_WithFileName_ParsesValue()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--mainExe \"MyApp.exe\"";
            ParseResult parseResult = command.Parse(cli);
            
            Assert.Equal("MyApp.exe", parseResult.GetValueForOption(command.SquirrelAwareExecutable));
        }

        [Fact]
        public void Icon_WithValidFile_ParsesValue()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--icon \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(fileInfo.FullName, parseResult.GetValueForOption(command.Icon)?.FullName);
        }

        [Fact]
        public void Icon_WithBadFileExtension_ShowsError()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".wrong"));
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--icon \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal($"{fileInfo.FullName} for --icon does not have an .ico extension", parseResult.Errors[0].Message);
        }

        [Fact]
        public void Icon_WithoutFile_ShowsError()
        {
            string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--icon \"{file}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.Icon, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
            Assert.Contains(file, parseResult.Errors[0].Message);
        }

        [Fact]
        public void NoDelta_BareOption_SetsFlag()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--noDelta";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.NoDelta));
        }
        
        private string GetRequiredDefaultOptions()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            CreateTempFile(packDir);

            return $"-u Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" ";
        }
    }
}
