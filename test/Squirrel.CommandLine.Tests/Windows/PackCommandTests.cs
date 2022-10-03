using Xunit;
using Squirrel.CommandLine.Windows;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Squirrel.CommandLine.Tests.Windows
{
    public class PackCommandTests : ReleaseCommandTests<PackCommand>
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
        public void ObsoletePackDirectory_WithNonEmptyFolder_ParsesValue()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            CreateTempFile(packDir);
            var command = new PackCommand();

            ParseResult parseResult = command.Parse($"-u Clowd.Squirrel -v 1.0.0 --packDirectory \"{packDir.FullName}\"");

            Assert.Equal(packDir.FullName, parseResult.GetValueForOption(command.PackDirectoryObsolete)?.FullName);
        }

        [Fact]
        public void ObsoletePackDirectory_WithEmptyFolder_ShowsError()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            var command = new PackCommand();

            ParseResult parseResult = command.Parse($"-u Clowd.Squirrel -v 1.0.0 --packDirectory \"{packDir.FullName}\"");

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.StartsWith("--packDirectory must a non-empty directory", parseResult.Errors[0].Message);
            Assert.Contains(packDir.FullName, parseResult.Errors[0].Message);
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
        public void PackTitle_WithTitle_ParsesValue()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--packTitle \"My Awesome Title\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("My Awesome Title", parseResult.GetValueForOption(command.PackTitle));
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
        public void IncludePdb_BareOption_SetsFlag()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--includePdb";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.IncludePdb));
        }

        [Fact]
        public void ReleaseNotes_WithExistingFile_ParsesValue()
        {
            FileInfo releaseNotes = CreateTempFile();
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--releaseNotes \"{releaseNotes.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(releaseNotes.FullName, parseResult.GetValueForOption(command.ReleaseNotes)?.FullName);
        }

        [Fact]
        public void ReleaseNotes_WithoutFile_ShowsError()
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
        public void SignTemplate_WithTemplate_ParsesValue()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--signTemplate \"signtool {{file}}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("signtool {{file}}", parseResult.GetValueForOption(command.SignTemplate));
        }

        [Fact]
        public void SignTemplate_WithoutFileParameter_ShowsError()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--signTemplate \"signtool file\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.SignTemplate, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--signTemplate must contain '{{file}}'", parseResult.Errors[0].Message);
        }

        [WindowsOnlyFact]
        public void SignParameters_WithParameters_ParsesValue()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--signParams \"param1 param2\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("param1 param2", parseResult.GetValueForOption(command.SignParameters));
        }

        [WindowsOnlyFact]
        public void SignParameters_WithSignTemplate_ShowsError()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--signTemplate \"signtool {{file}}\" --signParams \"param1 param2\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("Cannot use '--signParams' and '--signTemplate' options together", parseResult.Errors[0].Message);
        }

        [WindowsOnlyFact]
        public void SignSkipDll_BareOption_SetsFlag()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--signSkipDll";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.SignSkipDll));
        }

        [WindowsOnlyFact]
        public void SignParallel_WithValue_SetsFlag()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--signParallel 42";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(42, parseResult.GetValueForOption(command.SignParallel));
        }

        [WindowsOnlyTheory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(1001)]
        public void SignParallel_WithBadNumericValue_ShowsError(int value)
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--signParallel {value}";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.SignParallel, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.Equal($"The value for --signParallel must be greater than 1 and less than 1000", parseResult.Errors[0].Message);
        }

        [WindowsOnlyFact]
        public void SignParallel_WithNonNumericValue_ShowsError()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--signParallel abc";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.SignParallel, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.Equal($"abc is not a valid integer for --signParallel", parseResult.Errors[0].Message);
        }

        protected override string GetRequiredDefaultOptions()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            CreateTempFile(packDir);

            return $"-u Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" ";
        }
    }
}
