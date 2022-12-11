using Squirrel.CommandLine.Commands;
using Xunit;

namespace Squirrel.CommandLine.Tests
{
    public abstract class BaseCommandTests<T> : TempFileTestBase
        where T : BaseCommand, new()
    {
        public virtual bool ShouldBeNonEmptyReleaseDir => false;

        [Fact]
        public void ReleaseDirectory_WithDirectory_ParsesValue()
        {
            var releaseDirectory = CreateTempDirectory();

            if (ShouldBeNonEmptyReleaseDir)
                CreateTempFile(releaseDirectory, "anything");

            BaseCommand command = new T();

            var cli = GetRequiredDefaultOptions() + $"--outputDir \"{releaseDirectory.FullName}\"";
            var parseResult = command.ParseAndApply(cli);

            Assert.Equal(releaseDirectory.FullName, command.ReleaseDirectory);
        }

        protected virtual string GetRequiredDefaultOptions() => "";
    }
}
