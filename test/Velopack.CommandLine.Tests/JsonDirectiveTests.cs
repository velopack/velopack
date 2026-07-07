using System.ComponentModel;
using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Velopack.Core;
using Velopack.Core.Json;
using Velopack.Packaging.Compression;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk;
using Velopack.Vpk.Commands.Packaging;
using Velopack.Vpk.Converters;

namespace Velopack.CommandLine.Tests;

// Tests for the [json] directive pipeline. Program.cs cannot be invoked directly here, so these
// tests exercise the same seams Program.cs uses (JsonConfigArgument, GetExplicitOptionNames,
// SetProperties via ParseAndApply, OptionMapper.Map, JsonConfigLoader.Populate) in the same order,
// mirroring the json-mode behavior of ProgramCommandExtensions.Add.
public class JsonDirectiveTests : TempFileTestBase
{
    static JsonDirectiveTests()
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

    private FileInfo WriteJsonFile(string json)
    {
        var file = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".json"));
        File.WriteAllText(file.FullName, json);
        return file;
    }

    // serialize a string into a valid JSON string literal (escapes backslashes in windows paths)
    private static string J(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public void JsonConfigArgument_CapturesPositionalPath()
    {
        var command = new WindowsPackCommand();
        ParseResult parseResult = command.Parse("myconfig.json");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("myconfig.json", parseResult.GetValue(command.JsonConfigArgument));
    }

    [Fact]
    public void JsonConfigArgument_WhenAbsent_IsNull()
    {
        var command = new WindowsPackCommand();
        ParseResult parseResult = command.Parse("--packId My.App");

        Assert.Empty(parseResult.Errors);
        Assert.Null(parseResult.GetValue(command.JsonConfigArgument));
    }

    [Fact]
    public void JsonConfigArgument_IsHiddenAndOptional()
    {
        var command = new WindowsPackCommand();

        Assert.True(command.JsonConfigArgument.Hidden);
        Assert.Equal(ArgumentArity.ZeroOrOne, command.JsonConfigArgument.Arity);
    }

    [Fact]
    public void StrayJsonFile_WithoutDirective_IsDetectableForDidYouMeanError()
    {
        // without the [json] directive, Program.cs raises "Did you mean 'vpk [json] pack myconfig.json'?"
        // whenever the jsonConfig argument was supplied. The parser must accept the stray token without
        // errors so that Program.cs (not the parser) can produce that friendly message.
        var command = new WindowsPackCommand();
        ParseResult parseResult = command.Parse("myconfig.json");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("myconfig.json", parseResult.GetValue(command.JsonConfigArgument));
    }

    [Fact]
    public void ExplicitOptions_MixedWithJsonFile_AreReportedForJsonModeBan()
    {
        // mirrors Program.cs: in json mode, GetExplicitOptionNames must be empty or the command is
        // rejected ("the following command line options are not allowed").
        var command = new WindowsPackCommand();
        ParseResult parseResult = command.ParseAndApply("--packId My.App myconfig.json");

        Assert.Equal("myconfig.json", parseResult.GetValue(command.JsonConfigArgument));
        var names = command.GetExplicitOptionNames(parseResult);
        var name = Assert.Single(names);
        Assert.Equal("--packId", name);
    }

    [Fact]
    public void JsonFileOnly_HasNoExplicitOptions_SoJsonModeIsAllowed()
    {
        var command = new WindowsPackCommand();
        ParseResult parseResult = command.ParseAndApply("myconfig.json");

        Assert.Equal("myconfig.json", parseResult.GetValue(command.JsonConfigArgument));
        Assert.Empty(command.GetExplicitOptionNames(parseResult));
    }

    [Fact]
    public void JsonOverlay_TakesPrecedenceOverEnv_AndPreservesEnvAndDefaults()
    {
        var packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        var outDir = CreateTempDirectory();

        var jsonFile = WriteJsonFile($$"""{ "packId": "Json.App", "packDirectory": {{J(packDir.FullName)}} }""");

        var command = new WindowsPackCommand();
        ParseResult parseResult = command.ParseAndApply(
            "",
            Config(
                new Dictionary<string, string?> {
                    ["PACK_ID"] = "Env.App",
                    ["PACK_VERSION"] = "1.0.0",
                    ["PACK_AUTHORS"] = "EnvAuthor",
                    ["OUTPUT_DIR"] = outDir.FullName,
                }));
        Assert.Empty(parseResult.Errors);

        var options = OptionMapper.Map<WindowsPackOptions>(command);
        JsonConfigLoader.Populate(jsonFile.FullName, options);

        Assert.Equal("Json.App", options.PackId); // json wins over env
        Assert.Equal(packDir.FullName, options.PackDirectory); // json wins (env did not set it)
        Assert.Equal("1.0.0", options.PackVersion); // env value preserved when absent from json
        Assert.Equal("EnvAuthor", options.PackAuthors); // env value preserved when absent from json
        Assert.Equal(@".*\.pdb", options.Exclude); // cli default preserved when in neither
        Assert.Equal("win", options.Channel); // cli default preserved when in neither

        // the final overlaid options object must pass validation, as it would in Program.cs
        var result = new WindowsPackOptionsValidator().Validate(options);
        Assert.True(result.IsValid, string.Join(", ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public void JsonOverlay_InvalidValue_IsRejectedByValidatorAfterOverlay()
    {
        var packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        var outDir = CreateTempDirectory();

        // env vars provide a fully valid set of options, then json overrides packId with garbage -
        // proving that validation runs against the final overlaid values.
        var jsonFile = WriteJsonFile("""{ "packId": "$invalid!!id$" }""");

        var command = new WindowsPackCommand();
        command.ParseAndApply(
            "",
            Config(
                new Dictionary<string, string?> {
                    ["PACK_ID"] = "My.App",
                    ["PACK_VERSION"] = "1.0.0",
                    ["PACK_DIR"] = packDir.FullName,
                    ["OUTPUT_DIR"] = outDir.FullName,
                }));

        var options = OptionMapper.Map<WindowsPackOptions>(command);
        JsonConfigLoader.Populate(jsonFile.FullName, options);
        var result = new WindowsPackOptionsValidator().Validate(options);

        var error = Assert.Single(result.Errors, e => e.PropertyName == "PackId");
        Assert.Contains("$invalid!!id$", error.ErrorMessage);
    }

    [Fact]
    public void JsonOverlay_UnknownKey_ThrowsUserInfoException()
    {
        var jsonFile = WriteJsonFile("""{ "packIdd": "My.App" }""");
        var options = MapEmptyCommand();

        var ex = Assert.Throws<UserInfoException>(() => JsonConfigLoader.Populate(jsonFile.FullName, options));
        Assert.Contains("packIdd", ex.Message);
    }

    [Fact]
    public void JsonOverlay_InvalidSyntax_ThrowsUserInfoException()
    {
        var jsonFile = WriteJsonFile("{ this is not valid json");
        var options = MapEmptyCommand();

        var ex = Assert.Throws<UserInfoException>(() => JsonConfigLoader.Populate(jsonFile.FullName, options));
        Assert.Contains("Invalid JSON config", ex.Message);
    }

    [Fact]
    public void JsonOverlay_MissingFile_ThrowsUserInfoException()
    {
        var missing = Path.Combine(TempDirectory.FullName, Path.GetRandomFileName());
        var options = MapEmptyCommand();

        var ex = Assert.Throws<UserInfoException>(() => JsonConfigLoader.Populate(missing, options));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void JsonOverlay_ConvertsTypedValues()
    {
        var newReleaseDir = Path.Combine(TempDirectory.FullName, "sub", "releases");
        var jsonFile = WriteJsonFile(
            $$"""
              {
                  "releaseDir": {{J(newReleaseDir)}},
                  "deltaMode": "None",
                  "targetRuntime": "win-x64"
              }
              """);

        var options = MapEmptyCommand();
        Assert.Equal(DeltaMode.BestSpeed, options.DeltaMode); // cli default before overlay

        JsonConfigLoader.Populate(jsonFile.FullName, options);

        Assert.Equal(newReleaseDir, options.ReleaseDir.FullName); // DirectoryInfo converted
        Assert.False(Directory.Exists(newReleaseDir)); // created at point of use, not during parsing
        Assert.Equal(DeltaMode.None, options.DeltaMode); // enum converted from string
        Assert.Equal(RID.Parse("win-x64"), options.TargetRuntime); // RID converted from string
    }

    private WindowsPackOptions MapEmptyCommand()
    {
        // OUTPUT_DIR is provided so the mapped options do not point at a ./Releases directory
        // relative to the test working directory.
        var outDir = CreateTempDirectory();
        var command = new WindowsPackCommand();
        command.ParseAndApply("", Config(new Dictionary<string, string?> { ["OUTPUT_DIR"] = outDir.FullName }));
        return OptionMapper.Map<WindowsPackOptions>(command);
    }
}
