using System.CommandLine;
using System.CommandLine.Parsing;
using Squirrel.CommandLine.Windows;
using Xunit;

namespace Squirrel.CommandLine.Tests.Windows
{
    public abstract class ReleaseCommandTests<T> : BaseCommandTests<T>
        where T : ReleaseCommand, new()
    {
        [Fact]
        public void BaseUrl_WithUrl_ParsesValue()
        {
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--baseUrl \"https://clowd.squirell.com\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("https://clowd.squirell.com/", parseResult.GetValueForOption(command.BaseUrl)?.AbsoluteUri);
        }

        [Fact]
        public void BaseUrl_WithNonHttpValue_ShowsError()
        {
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--baseUrl \"file://clowd.squirrel.com\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.BaseUrl, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--baseUrl must contain a Uri with one of the following schems: http, https.", parseResult.Errors[0].Message);
        }

        [Fact]
        public void BaseUrl_WithRelativeUrl_ShowsError()
        {
            var command = new T();
            string cli = GetRequiredDefaultOptions() + $"--baseUrl \"clowd.squirrel.com\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.BaseUrl, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--baseUrl must contain an absolute Uri.", parseResult.Errors[0].Message);
        }

        [Fact]
        public void AddSearchPath_WithMultipleValues_ParsesValue()
        {
            string searchPath1 = CreateTempDirectory().FullName;
            string searchPath2 = CreateTempDirectory().FullName;
            var command = new T();
            string cli = GetRequiredDefaultOptions() + $"--addSearchPath \"{searchPath1}\" --addSearchPath \"{searchPath2}\"";
            ParseResult parseResult = command.Parse(cli);

            string[]? searchPaths = parseResult.GetValueForOption(command.AddSearchPath);
            Assert.Equal(2, searchPaths?.Length);
            Assert.Contains(searchPath1, searchPaths);
            Assert.Contains(searchPath2, searchPaths);
        }

        [Fact]
        public void DebugSetupExe_WithFilePath_ParsesValue()
        {
            string debugExe = CreateTempFile().FullName;
            var command = new T();
            string cli = GetRequiredDefaultOptions() + $"--debugSetupExe \"{debugExe}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(debugExe, parseResult.GetValueForOption(command.DebugSetupExe)?.FullName);
        }


        [Fact]
        public void NoDelta_BareOption_SetsFlag()
        {
            var command = new T();

            string cli = GetRequiredDefaultOptions() + "--noDelta";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.NoDelta));
        }

        [Fact]
        public void Runtime_WithValue_ParsesValue()
        {
            var command = new T();
            string cli = GetRequiredDefaultOptions() + $"--framework \"net6,vcredist143\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("net6,vcredist143", parseResult.GetValueForOption(command.Runtimes));
        }

        [Fact]
        public void SplashImage_WithValidFile_ParsesValue()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--splashImage \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(fileInfo.FullName, parseResult.GetValueForOption(command.SplashImage)?.FullName);
        }

        [Fact]
        public void SplashImage_WithoutFile_ShowsError()
        {
            string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--splashImage \"{file}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.SplashImage, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
            Assert.Contains(file, parseResult.Errors[0].Message);
        }

        [Fact]
        public void Icon_WithValidFile_ParsesValue()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--icon \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(fileInfo.FullName, parseResult.GetValueForOption(command.Icon)?.FullName);
        }

        [Fact]
        public void Icon_WithBadFileExtension_ShowsError()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".wrong"));
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--icon \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal($"--icon does not have an .ico extension", parseResult.Errors[0].Message);
        }

        [Fact]
        public void Icon_WithoutFile_ShowsError()
        {
            string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--icon \"{file}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.Icon, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
            Assert.Contains(file, parseResult.Errors[0].Message);
        }

        [Fact]
        public void SquirrelAwareExecutable_WithMultipleValues_ParsesValue()
        {
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--mainExe \"MyApp1.exe\" --mainExe \"MyApp2.exe\"";
            ParseResult parseResult = command.Parse(cli);

            string[]? searchPaths = parseResult.GetValueForOption(command.SquirrelAwareExecutable);
            Assert.Equal(2, searchPaths?.Length);
            Assert.Contains("MyApp1.exe", searchPaths);
            Assert.Contains("MyApp2.exe", searchPaths);
        }

        [Fact]
        public void AppIcon_WithValidFile_ParsesValue()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--appIcon \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(fileInfo.FullName, parseResult.GetValueForOption(command.AppIcon)?.FullName);
        }

        [Fact]
        public void AppIcon_WithBadFileExtension_ShowsError()
        {
            FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".wrong"));
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--appIcon \"{fileInfo.FullName}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal($"--appIcon does not have an .ico extension", parseResult.Errors[0].Message);
        }

        [Fact]
        public void AppIcon_WithoutFile_ShowsError()
        {
            string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
            var command = new T();

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
            var command = new T();

            string cli = GetRequiredDefaultOptions() + $"--msi \"{bitness}\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(bitness, parseResult.GetValueForOption(command.BuildMsi));
        }
    }
}
