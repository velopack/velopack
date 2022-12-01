using Xunit;
using Squirrel.CommandLine.Windows;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Squirrel.CommandLine.Tests.Windows
{
    public class ReleasifyCommandTests : ReleaseCommandTests<ReleasifyCommand>
    {
        [Fact]
        public void Command_WithValidRequiredArguments_Parses()
        {
            FileInfo package = CreateTempFile(name:Path.ChangeExtension(Path.GetRandomFileName(), ".nupkg"));
            var command = new ReleasifyCommand();

            ParseResult parseResult = command.Parse($"--package \"{package.FullName}\"");

            Assert.Empty(parseResult.Errors);
            Assert.Equal(package.FullName, parseResult.GetValueForOption(command.Package)?.FullName);
        }

        [Fact]
        public void Package_WithoutNupkgExtension_ShowsError()
        {
            FileInfo package = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".notpkg"));
            var command = new ReleasifyCommand();

            ParseResult parseResult = command.Parse($"--package \"{package.FullName}\"");

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.Package, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--package does not have an .nupkg extension", parseResult.Errors[0].Message);
        }

        [Fact]
        public void Package_WithoutExistingFile_ShowsError()
        {
            string package = Path.ChangeExtension(Path.GetRandomFileName(), ".nupkg");
            var command = new ReleasifyCommand();

            ParseResult parseResult = command.Parse($"--package \"{package}\"");

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.Package, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
            Assert.StartsWith($"File does not exist: '{package}'", parseResult.Errors[0].Message);
        }

        [Fact]
        public void SignTemplate_WithTemplate_ParsesValue()
        {
            var command = new ReleasifyCommand();

            string cli = GetRequiredDefaultOptions() + "--signTemplate \"signtool {{file}}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("signtool {{file}}", parseResult.GetValueForOption(command.SignTemplate));
        }

        [Fact]
        public void SignTemplate_WithoutFileParameter_ShowsError()
        {
            var command = new ReleasifyCommand();

            string cli = GetRequiredDefaultOptions() + "--signTemplate \"signtool file\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.SignTemplate, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--signTemplate must contain '{{file}}'", parseResult.Errors[0].Message);
        }

        [WindowsOnlyFact]
        public void SignParameters_WithParameters_ParsesValue()
        {
            var command = new ReleasifyCommand();

            string cli = GetRequiredDefaultOptions() + "--signParams \"param1 param2\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("param1 param2", parseResult.GetValueForOption(command.SignParameters));
        }

        [WindowsOnlyFact]
        public void SignParameters_WithSignTemplate_ShowsError()
        {
            var command = new ReleasifyCommand();

            string cli = GetRequiredDefaultOptions() + "--signTemplate \"signtool {{file}}\" --signParams \"param1 param2\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("Cannot use '--signParams' and '--signTemplate' options together", parseResult.Errors[0].Message);
        }

        [WindowsOnlyFact]
        public void SignSkipDll_BareOption_SetsFlag()
        {
            var command = new ReleasifyCommand();

            string cli = GetRequiredDefaultOptions() + "--signSkipDll";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.SignSkipDll));
        }

        [WindowsOnlyFact]
        public void SignParallel_WithValue_SetsFlag()
        {
            var command = new ReleasifyCommand();

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
            var command = new ReleasifyCommand();

            string cli = GetRequiredDefaultOptions() + $"--signParallel {value}";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.SignParallel, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.Equal($"The value for --signParallel must be greater than 1 and less than 1000", parseResult.Errors[0].Message);
        }

        [WindowsOnlyFact]
        public void SignParallel_WithNonNumericValue_ShowsError()
        {
            var command = new ReleasifyCommand();

            string cli = GetRequiredDefaultOptions() + $"--signParallel abc";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.SignParallel, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.Equal($"abc is not a valid integer for --signParallel", parseResult.Errors[0].Message);
        }

        protected override string GetRequiredDefaultOptions()
        {
            FileInfo package = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".nupkg"));

            return $"-p \"{package.FullName}\" ";
        }
    }
}
