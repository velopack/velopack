using System.CommandLine;
using System.CommandLine.Parsing;
using Squirrel.CommandLine.Commands;
using Xunit;

namespace Squirrel.CommandLine.Tests
{
    public abstract class BaseCommandTests<T> : TempFileTestBase
        where T : BaseCommand, new()
    {
        [Fact]
        public void ReleaseDirectory_WithDirectory_ParsesValue()
        {
            var releaseDirectory = CreateTempDirectory().FullName;
            BaseCommand command = new T();

            var cli = GetRequiredDefaultOptions() + $"--releaseDir \"{releaseDirectory}\"";
            var parseResult = command.Parse(cli);

            Assert.Equal(releaseDirectory, parseResult.GetValueForOption(command.ReleaseDirectory)?.FullName);
        }

        protected virtual string GetRequiredDefaultOptions() => "";
    }
}
