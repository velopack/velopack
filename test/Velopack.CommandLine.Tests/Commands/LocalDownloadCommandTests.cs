using System.CommandLine;
using Velopack.Deployment;
using Velopack.Util;
using Velopack.Vpk;
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
    public void Path_WithEmptyPath_FailsValidation()
    {
        var command = new LocalDownloadCommand();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        command.ParseAndApply($"--path {releaseDir}");
        var options = OptionMapper.Map<LocalDownloadOptions>(command);

        var result = new LocalDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must be a non-empty directory"));
    }

    [Fact]
    public void Path_WithNonExistingDirectory_FailsValidation()
    {
        var command = new LocalDownloadCommand();

        // Parse with a fake path
        command.ParseAndApply($"--path \"E:\\releases\"");
        var options = OptionMapper.Map<LocalDownloadOptions>(command);

        var result = new LocalDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must be a non-empty directory"));
    }

    [Fact]
    public void Path_Missing_FailsValidation()
    {
        var command = new LocalDownloadCommand();
        ParseResult parseResult = command.ParseAndApply("");

        Assert.Empty(parseResult.Errors);

        var options = OptionMapper.Map<LocalDownloadOptions>(command);
        var result = new LocalDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.PropertyName == "TargetPath");
    }
}

public class LocalUploadCommandTests : BaseCommandTests<LocalUploadCommand>
{
    [Fact]
    public void Path_WithExistingFile_FailsValidation()
    {
        var command = new LocalUploadCommand();

        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        var filePath = Path.Combine(tempDir, "shipPath");
        File.Create(filePath).Close();

        command.ParseAndApply($"--path \"{filePath}\"");
        var options = OptionMapper.Map<LocalUploadOptions>(command);

        var result = new LocalUploadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must be a directory, but a file already exists"));
    }

    [Fact]
    public void Path_WithExistingDirectory_PassesValidation()
    {
        var command = new LocalUploadCommand();

        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        using var _2 = TempUtil.GetTempDirectory(out var releaseDir);
        File.Create(Path.Combine(releaseDir, "test.txt")).Close();

        command.ParseAndApply($"--path \"{tempDir}\" --outputDir \"{releaseDir}\"");
        var options = OptionMapper.Map<LocalUploadOptions>(command);

        var result = new LocalUploadOptionsValidator().Validate(options);

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
    }
}
