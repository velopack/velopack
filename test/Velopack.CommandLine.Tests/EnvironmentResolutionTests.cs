using System.ComponentModel;
using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Velopack.Core;
using Velopack.Core.Json;
using Velopack.Deployment;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Commands.Deployment;
using Velopack.Vpk.Commands.Packaging;
using Velopack.Vpk.Converters;

namespace Velopack.CommandLine.Tests;

// Tests for the BaseCommand.AddOption setter chain: explicit cli value > IConfiguration (env vars
// with the VPK_ prefix, simulated here via AddInMemoryCollection) > cli default value.
public class EnvironmentResolutionTests : TempFileTestBase
{
    static EnvironmentResolutionTests()
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

    // resolve the IConfiguration key for an option from the command itself, so these tests do not
    // depend on hardcoded knowledge of the option-name -> env-var humanization rules.
    // Program.cs registers env vars with builder.Configuration.AddEnvironmentVariables("VPK_"),
    // which strips the prefix, so the config key is the env var name without "VPK_".
    private static string EnvKey(BaseCommand command, string optionName)
    {
        var option = command.Options.First(o => o.Name == optionName);
        string envName = command.GetEnvVariableName(option);
        Assert.StartsWith("VPK_", envName);
        return envName.Substring("VPK_".Length);
    }

    [Fact]
    public void BoolOption_FromEnv_SetsProperty()
    {
        var command = new WindowsPackCommand();
        string key = EnvKey(command, "--noPortable");
        Assert.Equal("NO_PORTABLE", key); // documents the generated env var name (VPK_NO_PORTABLE)

        command.ParseAndApply("", Config(new Dictionary<string, string?> { [key] = "true" }));

        Assert.True(command.NoPortable);
    }

    [Fact]
    public void BoolOption_WithoutEnv_RemainsFalse()
    {
        var command = new WindowsPackCommand();
        command.ParseAndApply("", Config(new Dictionary<string, string?>()));

        Assert.False(command.NoPortable);
    }

    [Fact]
    public void DefaultedOption_EnvOverridesDefault()
    {
        var command = new WindowsPackCommand();
        string key = EnvKey(command, "--channel");

        command.ParseAndApply("", Config(new Dictionary<string, string?> { [key] = "staging" }));

        Assert.Equal("staging", command.Channel);
    }

    [Fact]
    public void DefaultedOption_WithoutEnv_UsesCliDefault()
    {
        var command = new WindowsPackCommand();
        command.ParseAndApply("", Config(new Dictionary<string, string?>()));

        Assert.Equal("win", command.Channel); // WindowsPackCommand default channel
        Assert.Equal(@".*\.pdb", command.Exclude);
    }

    [Fact]
    public void DefaultedOption_ExplicitCliBeatsEnv()
    {
        var command = new WindowsPackCommand();
        string key = EnvKey(command, "--channel");

        command.ParseAndApply("--channel clichan", Config(new Dictionary<string, string?> { [key] = "envchan" }));

        Assert.Equal("clichan", command.Channel);
    }

    [Fact]
    public void FileInfoOption_FromEnv_Converts()
    {
        FileInfo icon = CreateTempFile(name: "icon.ico");
        var command = new WindowsPackCommand();
        string key = EnvKey(command, "--icon");

        command.ParseAndApply("", Config(new Dictionary<string, string?> { [key] = icon.FullName }));

        Assert.Equal(icon.FullName, command.Icon);
    }

    [Fact]
    public void DirectoryInfoOption_FromEnv_Converts()
    {
        DirectoryInfo outDir = CreateTempDirectory();
        var command = new WindowsPackCommand();
        string key = EnvKey(command, "--outputDir");

        command.ParseAndApply("", Config(new Dictionary<string, string?> { [key] = outDir.FullName }));

        Assert.Equal(outDir.FullName, command.ReleaseDir);
    }

    [Fact]
    public void ArrayOption_FromIndexedEnv_BindsAllValues()
    {
        // multi-value options are bound from indexed env vars (VPK_HEADER__0, VPK_HEADER__1);
        // the env provider maps '__' to ':', so the config keys are 'HEADER:0', 'HEADER:1'.
        var command = new HttpDownloadCommand();
        string key = EnvKey(command, "--header");
        Assert.Equal("HEADER", key);

        command.ParseAndApply(
            "",
            Config(
                new Dictionary<string, string?> {
                    [$"{key}:0"] = "Authorization: Bearer test",
                    [$"{key}:1"] = "X-API-Key: Bleh",
                }));

        Assert.Equal(new[] { "Authorization: Bearer test", "X-API-Key: Bleh" }, command.Headers);
    }

    [Fact]
    public void ArrayOption_FromScalarEnv_ThrowsHelpfulError()
    {
        // a single (non-indexed) env value can not be bound to an array option, and must produce
        // an error which explains the indexed syntax instead of being silently ignored.
        var command = new HttpDownloadCommand();
        string key = EnvKey(command, "--header");

        var ex = Assert.Throws<UserInfoException>(
            () => command.ParseAndApply("", Config(new Dictionary<string, string?> { [key] = "Authorization: Bearer test" })));

        Assert.Contains("VPK_HEADER__0", ex.Message);
    }

    [Fact]
    public void EnvAndJson_Combined_BothApplyToFinalOptions()
    {
        // json mode: env sets packId, json sets packVersion (and omits packId) - after the json
        // overlay both must be present on the final options object.
        var outDir = CreateTempDirectory();
        var jsonFile = CreateTempFile(name: "config.json");
        File.WriteAllText(jsonFile.FullName, """{ "packVersion": "2.0.0" }""");

        var command = new WindowsPackCommand();
        command.ParseAndApply(
            "",
            Config(
                new Dictionary<string, string?> {
                    ["PACK_ID"] = "Env.App",
                    ["OUTPUT_DIR"] = outDir.FullName,
                }));

        var options = OptionMapper.Map<WindowsPackOptions>(command);
        JsonConfigLoader.Populate(jsonFile.FullName, options);

        Assert.Equal("Env.App", options.PackId); // from env, untouched by json
        Assert.Equal("2.0.0", options.PackVersion); // from json
    }

    [Fact]
    public void HttpDownloadCommand_EnvOnly_MapsAndValidates()
    {
        // deployment commands must also be fully drivable from env vars alone
        DirectoryInfo outDir = CreateTempDirectory();
        var command = new HttpDownloadCommand();

        ParseResult parseResult = command.ParseAndApply(
            "",
            Config(
                new Dictionary<string, string?> {
                    [EnvKey(command, "--url")] = "https://example.com/releases",
                    [EnvKey(command, "--timeout")] = "10",
                    [EnvKey(command, "--channel")] = "stable",
                    [EnvKey(command, "--outputDir")] = outDir.FullName,
                    [EnvKey(command, "--allowEmptyChannel")] = "true",
                }));
        Assert.Empty(parseResult.Errors);

        var options = OptionMapper.Map<HttpDownloadOptions>(command);
        var result = new HttpDownloadOptionsValidator().Validate(options);

        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
        Assert.Equal("https://example.com/releases", options.Url);
        Assert.Equal(10d, options.Timeout);
        Assert.Equal("stable", options.Channel);
        Assert.Equal(outDir.FullName, options.ReleaseDir.FullName);
        Assert.True(options.AllowEmptyChannel);
    }
}
