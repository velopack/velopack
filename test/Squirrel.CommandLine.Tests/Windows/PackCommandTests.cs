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
        public void BaseUrl_WithUrl_ParsesValue()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--baseUrl \"https://clowd.squirell.com\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("https://clowd.squirell.com/", parseResult.GetValueForOption(command.BaseUrl)?.AbsoluteUri);
        }

        [Fact]
        public void BaseUrl_WithNonHttpValue_ShowsError()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--baseUrl \"file://clowd.squirrel.com\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.BaseUrl, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--baseUrl must contain a Uri with one of the following schems: http, https.", parseResult.Errors[0].Message);
        }

        [Fact]
        public void BaseUrl_WithRelativeUrl_ShowsError()
        {
            var command = new PackCommand();
            string cli = GetRequiredDefaultOptions() + $"--baseUrl \"clowd.squirrel.com\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.BaseUrl, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--baseUrl must contain an absolute Uri.", parseResult.Errors[0].Message);
        }

        [Fact]
        public void AddSearchPath_WithValue_ParsesValue()
        {
            string searchPath = CreateTempDirectory().FullName;
            var command = new PackCommand();
            string cli = GetRequiredDefaultOptions() + $"--addSearchPath \"{searchPath}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(searchPath, parseResult.GetValueForOption(command.AddSearchPath));
        }

        [Fact]
        public void DebugSetupExe_WithFilePath_ParsesValue()
        {
            string debugExe = CreateTempFile().FullName;
            var command = new PackCommand();
            string cli = GetRequiredDefaultOptions() + $"--debugSetupExe \"{debugExe}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(debugExe, parseResult.GetValueForOption(command.DebugSetupExe)?.FullName);
        }

        [Fact]
        public void NoDelta_BareOption_SetsFlag()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + "--noDelta";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.NoDelta));
        }

        [Fact]
        public void Runtime_WithValue_ParsesValue()
        {
            var command = new PackCommand();
            string cli = GetRequiredDefaultOptions() + $"--framework \"net6,vcredist143\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("net6,vcredist143", parseResult.GetValueForOption(command.Runtimes));
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
        public void SquirrelAwareExecutable_WithFileName_ParsesValue()
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--mainExe \"MyApp.exe\"";
            ParseResult parseResult = command.Parse(cli);
            
            Assert.Equal("MyApp.exe", parseResult.GetValueForOption(command.SquirrelAwareExecutable));
        }

        [Fact]
        public void AppIcon_WithValidFile_ParsesValue()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--appIcon \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(fileInfo.FullName, parseResult.GetValueForOption(command.AppIcon)?.FullName);
        }

        [Fact]
        public void AppIcon_WithBadFileExtension_ShowsError()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".wrong"));
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--appIcon \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal($"{fileInfo.FullName} for --appIcon does not have an .ico extension", parseResult.Errors[0].Message);
        }

        [Fact]
        public void AppIcon_WithoutFile_ShowsError()
        {
            string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--appIcon \"{file}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.AppIcon, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
            Assert.Contains(file, parseResult.Errors[0].Message);
        }

        [WindowsOnlyTheory]
        [InlineData(Bitness.x86)]
        [InlineData(Bitness.x64)]
        public void BuildMsi_WithBitness_ParsesValue(Bitness bitness)
        {
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--msi \"{bitness}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(bitness, parseResult.GetValueForOption(command.BuildMsi));
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

        [Fact]
        public void ReleaseDirectory_WithDirectory_ParsesValue()
        {
            string releaseDirectory = CreateTempDirectory().FullName;
            var command = new PackCommand();

            string cli = GetRequiredDefaultOptions() + $"--releaseDir \"{releaseDirectory}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(releaseDirectory, parseResult.GetValueForOption(command.ReleaseDirectory)?.FullName);
        }

        private string GetRequiredDefaultOptions()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            CreateTempFile(packDir);

            return $"-u Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" ";
        }
    }
}
