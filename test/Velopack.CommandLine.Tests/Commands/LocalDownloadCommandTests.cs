using System.CommandLine;
using Velopack.Util;
using Velopack.Vpk.Commands.Deployment;

namespace Velopack.CommandLine.Tests.Commands;
public class LocalDownloadCommandTests : BaseCommandTests<LocalDownloadCommand>
{
    [Fact]
    public void Path_WithPath_ParsesValue()
    {
        var command = new LocalDownloadCommand();

        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        File.Create(Path.Combine(releaseDir, "test.txt")).Close();

        ParseResult parseResult = command.ParseAndApply($"--path {releaseDir}");

        Assert.Empty(parseResult.Errors);
        Assert.Equal(releaseDir, command.TargetPath.FullName);
    }

    [Fact]
    public void Path_WithEmptyPath_ParsesValue()
    {
        var command = new LocalDownloadCommand();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        ParseResult parseResult = command.ParseAndApply($"--path {releaseDir}");

        Assert.True(parseResult.Errors.Count > 0);
        Assert.Contains("must be a non-empty directory", parseResult.Errors[0].Message);
    }

    [Fact]
    public void Path_WithNonExistingDirectory_ShowsError()
    {
        var command = new LocalDownloadCommand();

        // Parse with a fake path
        ParseResult parseResult = command.ParseAndApply($"--path \"E:\\releases\"");

        Assert.True(parseResult.Errors.Count > 0);
        Assert.Contains("must be a non-empty directory", parseResult.Errors[0].Message);
    }
}
