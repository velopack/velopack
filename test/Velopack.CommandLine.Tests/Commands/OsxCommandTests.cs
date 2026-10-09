using System.CommandLine;
using Velopack.Core.Validation;
using Velopack.Packaging.Unix.Commands;
using Velopack.Vpk;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Commands.Packaging;

namespace Velopack.CommandLine.Tests.Commands;

public class OsxPackCommandTests : BaseCommandTests<OsxPackCommand>
{
    // Apple's codesign/notarytool/pkgbuild options: registered on macOS only.
    private static readonly string[] AppleToolOptions = [
        "--instWelcome", "--instReadme", "--instLicense", "--instConclusion",
        "--signAppIdentity", "--signInstallIdentity", "--notaryProfile", "--keychain",
        "--noInst", "--noPortable",
    ];

    // rcodesign and the shared signing options: registered on every OS.
    private static readonly string[] CrossPlatformOptions = [
        "--signEntitlements", "--signDisableDeep", "--signP12File", "--signP12PasswordFile", "--notaryApiKeyFile",
    ];

    private static bool HasOption(OsxPackCommand command, string name) => command.Options.Any(o => o.Name == name);

    private OsxPackOptions ParseAndMap(string cli)
    {
        var command = new OsxPackCommand();
        ParseResult parseResult = command.ParseAndApply(cli);
        Assert.Empty(parseResult.Errors);
        return command.ToOptions();
    }

    private static FluentValidation.Results.ValidationResult Validate(OsxPackOptions options)
        => new OsxPackOptionsValidator().Validate(options);

    [Fact]
    public void Command_WithValidRequiredArguments_Parses()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        var command = new OsxPackCommand();

        ParseResult parseResult = command.ParseAndApply($"-u Clowd.Squirrel -v 1.2.3 -p \"{packDir.FullName}\" -e MyApp");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("Clowd.Squirrel", command.PackId);
        Assert.Equal("1.2.3", command.PackVersion);
        Assert.Equal(packDir.FullName, command.PackDirectory);
    }

    [Fact]
    public void RcodesignOptions_WithFiles_ParseAndMap()
    {
        FileInfo p12 = CreateTempFile(name: "id.p12");
        FileInfo password = CreateTempFile(name: "password.txt");
        FileInfo apiKey = CreateTempFile(name: "key.json");
        var command = new OsxPackCommand();

        string cli = GetRequiredDefaultOptions() +
                     $"--signP12File \"{p12.FullName}\" --signP12PasswordFile \"{password.FullName}\" " +
                     $"--notaryApiKeyFile \"{apiKey.FullName}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(p12.FullName, command.SignP12File);
        Assert.Equal(password.FullName, command.SignP12PasswordFile);
        Assert.Equal(apiKey.FullName, command.NotaryApiKeyFile);

        var options = command.ToOptions();
        Assert.Equal(p12.FullName, options.SignP12File);
        Assert.Equal(password.FullName, options.SignP12PasswordFile);
        Assert.Equal(apiKey.FullName, options.NotaryApiKeyFile);
        Assert.Empty(Validate(options).Errors);
    }

    [Fact]
    public void RcodesignOptions_WithRelativePaths_MapToFullPaths()
    {
        var command = new OsxPackCommand();

        ParseResult parseResult = command.ParseAndApply(
            GetRequiredDefaultOptions() + "--signP12File id.p12 --signP12PasswordFile pw.txt --notaryApiKeyFile key.json");

        Assert.Empty(parseResult.Errors);
        Assert.Equal(Path.GetFullPath("id.p12"), command.SignP12File);
        Assert.Equal(Path.GetFullPath("pw.txt"), command.SignP12PasswordFile);
        Assert.Equal(Path.GetFullPath("key.json"), command.NotaryApiKeyFile);
    }

    [Fact]
    public void CrossPlatformOptions_AreRegisteredOnEveryOS()
    {
        var command = new OsxPackCommand();

        Assert.All(CrossPlatformOptions, name => Assert.True(HasOption(command, name), name));
    }

    [Fact]
    public void AppleToolOptions_AreRegisteredOnlyOnMacOS()
    {
        var command = new OsxPackCommand();

        Assert.All(AppleToolOptions, name => Assert.Equal(VelopackRuntimeInfo.IsOSX, HasOption(command, name)));
    }

    [Fact]
    public void AppleToolOption_OffMacOS_IsAParseError()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "the option is registered on macOS");
        var command = new OsxPackCommand();

        ParseResult parseResult = command.ParseAndApply(GetRequiredDefaultOptions() + "--signAppIdentity \"Developer ID\"");

        Assert.NotEmpty(parseResult.Errors);
    }

    // The installer and validation rules themselves are covered by Velopack.Packaging.Tests' OsxPackValidatorTests.
    [Fact]
    public void NoInst_OnMacOS_MapsFromFlag()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "--noInst is only registered on macOS");

        Assert.False(ParseAndMap(GetRequiredDefaultOptions()).NoInst);
        Assert.True(ParseAndMap(GetRequiredDefaultOptions() + "--noInst").NoInst);
    }

    [Fact]
    public void RequiredOptions_AreMarkedRequiredInHelp()
    {
        var command = new OsxPackCommand();
        command.ApplyRequiredHints(new OsxPackOptionsValidator().GetRequiredProperties());

        Option Find(string name) => command.Options.First(o => o.Name == name);

        Assert.True(Find("--packId").IsRequiredHint());
        Assert.True(Find("--packVersion").IsRequiredHint());
        Assert.True(Find("--packDir").IsRequiredHint());

        // only required together with --signP12File, which the help text cannot express
        Assert.False(Find("--signP12PasswordFile").IsRequiredHint());
        Assert.False(Find("--signP12File").IsRequiredHint());
        Assert.False(Find("--notaryApiKeyFile").IsRequiredHint());
    }

    protected override string GetRequiredDefaultOptions()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);

        return $"-u Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" -e MyApp ";
    }
}
