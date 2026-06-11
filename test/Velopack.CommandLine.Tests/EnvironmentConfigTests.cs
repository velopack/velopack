using System.ComponentModel;
using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Commands.Packaging;
using Velopack.Vpk.Converters;

namespace Velopack.CommandLine.Tests;

public class EnvironmentConfigTests : TempFileTestBase
{
    static EnvironmentConfigTests()
    {
        // mirror Program.SetupConfig - required for IConfiguration to convert string values
        TypeDescriptor.AddAttributes(typeof(FileInfo), new TypeConverterAttribute(typeof(FileInfoConverter)));
        TypeDescriptor.AddAttributes(typeof(DirectoryInfo), new TypeConverterAttribute(typeof(DirectoryInfoConverter)));
        TypeDescriptor.AddAttributes(typeof(FileSystemInfo), new TypeConverterAttribute(typeof(FileSystemInfoConverter)));
    }

    private static IConfiguration Config(Dictionary<string, string?> values)
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(values);
        return config;
    }

    [Fact]
    public void RequiredOptions_CanBeSatisfiedByConfigValues()
    {
        var packDir = CreateTempDirectory();
        CreateTempFile(packDir);

        var command = new WindowsPackCommand();
        var parseResult = command.ParseAndApply(
            "",
            Config(
                new Dictionary<string, string?> {
                    ["PACK_ID"] = "My.App",
                    ["PACK_VERSION"] = "1.0.0",
                    ["PACK_DIR"] = packDir.FullName,
                }));

        // a missing required option must not be a parse error, because the value can come from env vars
        Assert.Empty(parseResult.Errors);

        var options = OptionMapper.Map<WindowsPackOptions>(command);
        var result = new WindowsPackOptionsValidator().Validate(options);

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
        Assert.Equal("My.App", options.PackId);
        Assert.Equal("1.0.0", options.PackVersion);
        Assert.Equal(packDir.FullName, options.PackDirectory);
    }

    [Fact]
    public void InvalidConfigValues_AreRejectedByValidator()
    {
        var packDir = CreateTempDirectory();
        CreateTempFile(packDir);

        var command = new WindowsPackCommand();
        var parseResult = command.ParseAndApply(
            "",
            Config(
                new Dictionary<string, string?> {
                    ["PACK_ID"] = "$invalid!!id$",
                    ["PACK_VERSION"] = "1.0.0",
                    ["PACK_DIR"] = packDir.FullName,
                }));

        // env values bypass the cli parser entirely, so this can only be caught by the validator
        Assert.Empty(parseResult.Errors);

        var options = OptionMapper.Map<WindowsPackOptions>(command);
        var result = new WindowsPackOptionsValidator().Validate(options);

        var error = Assert.Single(result.Errors, e => e.PropertyName == "PackId");
        Assert.Contains("$invalid!!id$", error.ErrorMessage);
    }

    [Fact]
    public void ExplicitCliValue_TakesPrecedenceOverConfig()
    {
        var command = new WindowsPackCommand();
        command.ParseAndApply("--packId Cli.App", Config(new Dictionary<string, string?> { ["PACK_ID"] = "Env.App" }));

        Assert.Equal("Cli.App", command.PackId);
    }

    [Fact]
    public void GetExplicitOptionNames_ReturnsOnlyUserProvidedOptions()
    {
        var command = new WindowsPackCommand();
        ParseResult parseResult = command.ParseAndApply("--packId My.App --packAuthors Bob");

        var names = command.GetExplicitOptionNames(parseResult);

        Assert.Contains("--packId", names);
        Assert.Contains("--packAuthors", names);
        // options with default values (eg. --channel, --exclude) must not be reported as explicit
        Assert.DoesNotContain("--channel", names);
        Assert.DoesNotContain("--exclude", names);
        Assert.Equal(2, names.Count);
    }
}
