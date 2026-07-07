
using System.CommandLine;
using Velopack.Core.Validation;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Commands.Packaging;

namespace Velopack.CommandLine.Tests.Commands;

public abstract class ReleaseCommandTests<T> : BaseCommandTests<T>
    where T : WindowsPackCommand, new()
{
    protected WindowsPackOptions ParseAndMap(string cli)
    {
        var command = new T();
        ParseResult parseResult = command.ParseAndApply(cli);
        Assert.Empty(parseResult.Errors);
        return OptionMapper.Map<WindowsPackOptions>(command);
    }

    protected static FluentValidation.Results.ValidationResult Validate(WindowsPackOptions options)
        => new WindowsPackOptionsValidator().Validate(options);

    [Fact]
    public void NoDelta_BareOption_SetsFlag()
    {
        var command = new T();

        string cli = GetRequiredDefaultOptions() + "--delta none";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.True(command.DeltaMode == Packaging.Compression.DeltaMode.None);
    }

    [Fact]
    public void Runtime_WithValue_ParsesValue()
    {
        var command = new T();
        string cli = GetRequiredDefaultOptions() + $"--framework \"net6,vcredist143\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("net6,vcredist143", command.Runtimes);
    }

    [Fact]
    public void SplashImage_WithValidFile_ParsesValue()
    {
        FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
        var command = new T();

        string cli = GetRequiredDefaultOptions() + $"--splashImage \"{fileInfo.FullName}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(fileInfo.FullName, command.SplashImage);
    }

    [Fact]
    public void SplashImage_WithoutFile_FailsValidation()
    {
        string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));

        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--splashImage \"{file}\"");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("splashImage file is not found"));
    }

    [Fact]
    public void Icon_WithValidFile_ParsesValue()
    {
        FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
        var command = new T();

        string cli = GetRequiredDefaultOptions() + $"--icon \"{fileInfo.FullName}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(fileInfo.FullName, command.Icon);
    }

    [Fact]
    public void Icon_WithBadFileExtension_FailsValidation()
    {
        FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".wrong"));

        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--icon \"{fileInfo.FullName}\"");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("icon must have a '.ico' extension"));
    }

    [Fact]
    public void Icon_WithoutFile_FailsValidation()
    {
        string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));

        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--icon \"{file}\"");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("icon file is not found"));
    }
}

public class PackWindowsCommandTests : ReleaseCommandTests<WindowsPackCommand>
{
    [Fact]
    public void Command_WithValidRequiredArguments_Parses()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        var command = new WindowsPackCommand();

        ParseResult parseResult = command.ParseAndApply($"-u Clowd.Squirrel -v 1.2.3 -p \"{packDir.FullName}\" -e main.exe");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("Clowd.Squirrel", command.PackId);
        Assert.Equal("1.2.3", command.PackVersion);
        Assert.Equal(packDir.FullName, command.PackDirectory);
    }

    [Fact]
    public void Command_WithMissingRequiredArguments_FailsValidation()
    {
        // missing required options are no longer a parse error (they may be provided via
        // env vars or json config), but they are rejected by the validator.
        var command = new WindowsPackCommand();
        ParseResult parseResult = command.ParseAndApply("");

        Assert.Empty(parseResult.Errors);

        var options = OptionMapper.Map<WindowsPackOptions>(command);
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.PropertyName == "PackId");
        Assert.Contains(result.Errors, e => e.PropertyName == "PackVersion");
        Assert.Contains(result.Errors, e => e.PropertyName == "PackDirectory");
    }

    [Fact]
    public void RequiredOptions_AreMarkedRequiredInHelp()
    {
        var command = new WindowsPackCommand();
        command.ApplyRequiredHints(new WindowsPackOptionsValidator().GetRequiredProperties());

        Option Find(string name) => command.Options.First(o => o.Name == name);

        Assert.True(Find("--packId").IsRequiredHint());
        Assert.True(Find("--packVersion").IsRequiredHint());
        Assert.True(Find("--packDir").IsRequiredHint());
        Assert.False(Find("--packAuthors").IsRequiredHint());

        // Option.Required must remain false so env vars / json config can satisfy them after parsing
        Assert.False(Find("--packId").Required);
    }

    [Fact]
    public void PackId_WithInvalidNuGetId_FailsValidation()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);

        var options = ParseAndMap($"--packId $42@ -v 1.0.0 -p \"{packDir.FullName}\" -e main.exe");
        var result = Validate(options);

        var error = Assert.Single(result.Errors, e => e.PropertyName == "PackId");
        Assert.StartsWith("packId is an invalid NuGet package id", error.ErrorMessage);
        Assert.Contains("$42@", error.ErrorMessage);
    }

    [Fact]
    public void PackName_WithValue_ParsesValue()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        var command = new WindowsPackCommand();

        ParseResult parseResult = command.ParseAndApply($"-u Clowd.Squirrel --packTitle Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" -e main.exe");

        Assert.Equal("Clowd.Squirrel", command.PackTitle);
    }

    [Fact]
    public void PackVersion_WithInvalidVersion_FailsValidation()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);

        var options = ParseAndMap($"-u Clowd.Squirrel --packVersion 1.a.c -p \"{packDir.FullName}\" -e main.exe");
        var result = Validate(options);

        var error = Assert.Single(result.Errors, e => e.PropertyName == "PackVersion");
        Assert.StartsWith("packVersion contains an invalid package version", error.ErrorMessage);
        Assert.Contains("1.a.c", error.ErrorMessage);
    }

    [Fact]
    public void PackTitle_WithTitle_ParsesValue()
    {
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + "--packTitle \"My Awesome Title\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("My Awesome Title", command.PackTitle);
    }

    [Fact]
    public void PackAuthors_WithMultipleAuthors_ParsesValue()
    {
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + "--packAuthors Me,mysel,I";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("Me,mysel,I", command.PackAuthors);
    }

    [Fact]
    public void ReleaseNotes_WithExistingFile_ParsesValue()
    {
        FileInfo releaseNotes = CreateTempFile();
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + $"--releaseNotes \"{releaseNotes.FullName}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(releaseNotes.FullName, command.ReleaseNotes);
    }

    [Fact]
    public void ReleaseNotes_WithoutFile_FailsValidation()
    {
        string releaseNotes = Path.GetFullPath(Path.GetRandomFileName());

        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--releaseNotes \"{releaseNotes}\"");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("releaseNotes file is not found"));
    }

    [Fact]
    public void SignTemplate_WithTemplate_ParsesValue()
    {
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + "--signTemplate \"signtool {{file}}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("signtool {{file}}", command.SignTemplate);
    }

    [WindowsOnlyFact]
    public void SignParameters_WithParameters_ParsesValue()
    {
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + "--signParams \"param1 param2\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("param1 param2", command.SignParameters);
    }

    [WindowsOnlyFact]
    public void SignParameters_WithSignTemplate_FailsValidation()
    {
        var options = ParseAndMap(GetRequiredDefaultOptions() + "--signTemplate \"signtool {{file}}\" --signParams \"param1 param2\"");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Cannot use more than one of 'signTemplate', 'signParams' and 'azureTrustedSignFile'"));
    }

    [WindowsOnlyFact]
    public void SignExclude_WithPattern_SetsOption()
    {
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + @"--signExclude \.dll$";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(@"\.dll$", command.SignExclude);
    }

    [WindowsOnlyFact]
    public void SignParallel_WithValue_SetsFlag()
    {
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + "--signParallel 42";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(42, command.SignParallel);
    }

    [WindowsOnlyTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1001)]
    public void SignParallel_WithBadNumericValue_FailsValidation(int value)
    {
        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--signParallel {value}");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.PropertyName == "SignParallel");
    }

    [WindowsOnlyTheory]
    [InlineData("999.0.0")] // major out of range
    [InlineData("1.2.3.4")] // there is no fourth field in an MSI ProductVersion
    public void MsiVersion_WithInvalidVersion_FailsValidation(string version)
    {
        var options = ParseAndMap(GetRequiredDefaultOptions() + $"--msiVersion {version}");
        var result = Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("invalid MSI ProductVersion"));
    }

    [WindowsOnlyFact]
    public void MsiVersion_WithZeroRevision_PassesValidation()
    {
        // a zero revision is tolerated because the default msi version is generated as 'major.minor.patch.0'
        var options = ParseAndMap(GetRequiredDefaultOptions() + "--msiVersion 1.2.3.0");
        var result = Validate(options);

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == "MsiVersionOverride");
    }

    protected override string GetRequiredDefaultOptions()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);

        return $"-u Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" -e main.exe ";
    }
}
