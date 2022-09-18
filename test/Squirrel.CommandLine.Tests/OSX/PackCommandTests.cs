using Xunit;
using Squirrel.CommandLine.OSX;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Squirrel.CommandLine.Tests.OSX
{
    public class PackCommandTests : TempFileTestBase
    {
        [Fact]
        public void PackageContent_CanSpecifyMultipleValues()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            FileInfo testFile1 = CreateTempFile(packDir);
            FileInfo testFile2 = CreateTempFile(packDir);
            PackCommand command = new PackCommand();
            string cli = $"-u clowd.squirrel -v 1.0.0 -p \"{packDir.FullName}\"";
            cli += $" --pkgContent welcome={testFile1.FullName}";
            cli += $" --pkgContent license={testFile2.FullName}";
            ParseResult parseResult = command.Parse(cli);

            Assert.Empty(parseResult.Errors);
            var packageContent = parseResult.GetValueForOption(command.PackageContent);
            Assert.Equal(2, packageContent?.Length);
            
            Assert.Equal("welcome", packageContent![0].Key);
            Assert.Equal(testFile1.FullName, packageContent![0].Value.FullName);

            Assert.Equal("license", packageContent![1].Key);
            Assert.Equal(testFile2.FullName, packageContent![1].Value.FullName);
        }

        [Fact]
        public void PackageContent_WihtInvalidKey_DisplaysError()
        {
            DirectoryInfo packDir = CreateTempDirectory();
            FileInfo testFile1 = CreateTempFile(packDir);
            PackCommand command = new PackCommand();
            string cli = $"-u clowd.squirrel -v 1.0.0 -p \"{packDir.FullName}\"";
            cli += $" --pkgContent unknown={testFile1.FullName}";
            ParseResult parseResult = command.Parse(cli);

            ParseError error = parseResult.Errors.Single();
            Assert.Equal("Invalid pkgContent key: unknown. Must be one of: welcome, readme, license, conclusion", error.Message);
        }
    }
}
