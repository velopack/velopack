using System.CommandLine;
using Velopack.Packaging.Unix.Commands;
using Velopack.Vpk;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Commands.Packaging;

namespace Velopack.CommandLine.Tests.Commands;

public class PackOsxCommandTests : BaseCommandTests<OsxPackCommand>
{
    private OsxPackOptions ParseAndMap(string cli)
    {
        var command = new OsxPackCommand();
        ParseResult parseResult = command.ParseAndApply(cli);
        Assert.Empty(parseResult.Errors);
        return OptionMapper.Map<OsxPackOptions>(command);
    }

    private static FluentValidation.Results.ValidationResult Validate(OsxPackOptions options)
        => new OsxPackOptionsValidator().Validate(options);

    [Fact]
    public void Command_WithValidRequiredArguments_Parses()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        var command = new OsxPackCommand();

        ParseResult parseResult = command.ParseAndApply($"-u Clowd.Squirrel -v 1.2.3 -p \"{packDir.FullName}\" -e main");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("Clowd.Squirrel", command.PackId);
        Assert.Equal("1.2.3", command.PackVersion);
        Assert.Equal(packDir.FullName, command.PackDirectory);
    }

    [Fact]
    public void NotaryProfile_WithName_ParsesValue()
    {
        var command = new OsxPackCommand();

        string cli = GetRequiredDefaultOptions() + "--notaryProfile \"profile name\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("profile name", command.NotaryProfile);
        Assert.Empty(Validate(OptionMapper.Map<OsxPackOptions>(command)).Errors);
    }

    [Fact]
    public void NotaryKey_WithKeyIdAndIssuer_ParsesValues()
    {
        FileInfo key = CreateTempFile(name: "AuthKey_ABC123DEF4.p8");
        var command = new OsxPackCommand();

        string cli = GetRequiredDefaultOptions() + $"--notaryKey \"{key.FullName}\" --notaryKeyId ABC123DEF4 --notaryIssuer 69a6de7e-1234-47e3-e053-5b8c7c11a4d1";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(key.FullName, command.NotaryKey);
        Assert.Equal("ABC123DEF4", command.NotaryKeyId);
        Assert.Equal("69a6de7e-1234-47e3-e053-5b8c7c11a4d1", command.NotaryIssuer);

        var options = OptionMapper.Map<OsxPackOptions>(command);
        Assert.Equal(key.FullName, options.NotaryKey);
        Assert.Equal("ABC123DEF4", options.NotaryKeyId);
        Assert.Equal("69a6de7e-1234-47e3-e053-5b8c7c11a4d1", options.NotaryIssuer);
        Assert.Empty(Validate(options).Errors);
    }

    [Fact]
    public void NotaryKey_WithoutIssuer_PassesValidation()
    {
        // individual API keys do not need an issuer id
        FileInfo key = CreateTempFile(name: "AuthKey_ABC123DEF4.p8");

        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--notaryKey \"{key.FullName}\" --notaryKeyId ABC123DEF4");
        var result = Validate(options);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void NotaryKey_WithoutFile_FailsValidation()
    {
        string key = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".p8"));

        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--notaryKey \"{key}\" --notaryKeyId ABC123DEF4");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("notaryKey file is not found"));
    }

    [Fact]
    public void NotaryKey_WithoutKeyId_FailsValidation()
    {
        FileInfo key = CreateTempFile(name: "AuthKey_ABC123DEF4.p8");

        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--notaryKey \"{key.FullName}\"");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("'notaryKey' and 'notaryKeyId' options must be used together"));
    }

    [Fact]
    public void NotaryKeyId_WithoutKey_FailsValidation()
    {
        var options = ParseAndMap(GetRequiredDefaultOptions() + "--notaryKeyId ABC123DEF4");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("'notaryKey' and 'notaryKeyId' options must be used together"));
    }

    [Fact]
    public void NotaryIssuer_WithoutKey_FailsValidation()
    {
        var options = ParseAndMap(GetRequiredDefaultOptions() + "--notaryIssuer 69a6de7e-1234-47e3-e053-5b8c7c11a4d1");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("'notaryIssuer' option requires 'notaryKey' and 'notaryKeyId'"));
    }

    [Fact]
    public void NotaryKey_WithNotaryProfile_FailsValidation()
    {
        FileInfo key = CreateTempFile(name: "AuthKey_ABC123DEF4.p8");

        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--notaryProfile myprofile --notaryKey \"{key.FullName}\" --notaryKeyId ABC123DEF4");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Cannot use 'notaryProfile' and 'notaryKey' options together"));
    }

    protected override string GetRequiredDefaultOptions()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);

        return $"-u Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" -e main ";
    }
}
