using System.CommandLine;
using Velopack.Vpk.Commands;

namespace Velopack.CommandLine.Tests.Commands;
public class LocalDownloadCommandTests : BaseCommandTests<LocalDownloadCommand>
{
    [Fact]
    public void Path_WithPath_ParsesValue()
    {
        var command = new LocalDownloadCommand();

        DirectoryInfo directory = Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "releases"));
        ParseResult parseResult = command.ParseAndApply($"--path {directory.FullName}");

        Assert.Empty(parseResult.Errors);
        Assert.Equal(directory.FullName, command.Path.FullName);

        Directory.Delete(directory.FullName);
    }

    [Fact]
    public void Path_WithNonExistingDirectory_ShowsError()
    {
        var command = new LocalDownloadCommand();

        // Parse with a fake path
        ParseResult parseResult = command.ParseAndApply($"--path \"E:\releases\"");

        Assert.Equal(1, parseResult.Errors.Count);
        Assert.StartsWith("--path directory is not found, but must exist", parseResult.Errors[0].Message);
    }
}
