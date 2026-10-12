using System.CommandLine;
using Velopack.Packaging;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Vpk;
using Velopack.Vpk.Commands.Packaging;

namespace Velopack.CommandLine.Tests.Commands;

/// <summary>
/// Parsing + mapping + validation of the shared --fileAssociations / --urlProtocols / --associationLaunchMode
/// options across the Windows and macOS pack/bundle commands, and their absence from the Linux pack command.
/// </summary>
public class AssociationOptionTests : TempFileTestBase
{
    private string RequiredOptions()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        return $"-u Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" -e main.exe ";
    }

    [Fact]
    public void WindowsPack_ParsesAndMapsBothOptions()
    {
        var command = new WindowsPackCommand();
        var result = command.ParseAndApply(RequiredOptions() + "--fileAssociations \".myproj,.mytpl\" --urlProtocols \"myapp,myapp-beta\"");

        Assert.Empty(result.Errors);
        Assert.Equal(".myproj,.mytpl", command.FileAssociations);
        Assert.Equal("myapp,myapp-beta", command.UrlProtocols);

        var options = OptionMapper.Map<WindowsPackOptions>(command);
        Assert.Equal(".myproj,.mytpl", options.FileAssociations);
        Assert.Equal("myapp,myapp-beta", options.UrlProtocols);
        Assert.Empty(new WindowsPackOptionsValidator().Validate(options).Errors);
    }

    [Fact]
    public void WindowsPack_WithoutOptions_MapsNull()
    {
        var command = new WindowsPackCommand();
        command.ParseAndApply(RequiredOptions());

        var options = OptionMapper.Map<WindowsPackOptions>(command);
        Assert.Null(options.FileAssociations);
        Assert.Null(options.UrlProtocols);
    }

    [Theory]
    [InlineData("--fileAssociations \".my proj\"", "FileAssociations", "Invalid file association")]
    [InlineData("--urlProtocols http", "UrlProtocols", "reserved")]
    [InlineData("--urlProtocols ms-foo", "UrlProtocols", "reserved")]
    [InlineData("--urlProtocols 1app", "UrlProtocols", "Invalid URL protocol")]
    public void WindowsPack_InvalidValues_FailValidation(string cli, string property, string messagePart)
    {
        var command = new WindowsPackCommand();
        Assert.Empty(command.ParseAndApply(RequiredOptions() + cli).Errors);

        var result = new WindowsPackOptionsValidator().Validate(OptionMapper.Map<WindowsPackOptions>(command));

        var error = Assert.Single(result.Errors, e => e.PropertyName == property);
        Assert.Contains(messagePart, error.ErrorMessage);
    }

    [Fact]
    public void OsxPack_ParsesAndMapsBothOptions()
    {
        var command = new OsxPackCommand();
        var result = command.ParseAndApply(RequiredOptions() + "--fileAssociations .myproj --urlProtocols myapp");

        Assert.Empty(result.Errors);
        var options = OptionMapper.Map<OsxPackOptions>(command);
        Assert.Equal(".myproj", options.FileAssociations);
        Assert.Equal("myapp", options.UrlProtocols);
        Assert.Empty(new OsxPackOptionsValidator().Validate(options).Errors);
    }

    [Fact]
    public void OsxPack_InvalidValues_FailValidation()
    {
        var command = new OsxPackCommand();
        Assert.Empty(command.ParseAndApply(RequiredOptions() + "--fileAssociations ..bad --urlProtocols mailto").Errors);

        var result = new OsxPackOptionsValidator().Validate(OptionMapper.Map<OsxPackOptions>(command));

        Assert.Contains(result.Errors, e => e.PropertyName == "FileAssociations");
        Assert.Contains(result.Errors, e => e.PropertyName == "UrlProtocols");
    }

    [Fact]
    public void OsxBundle_ParsesAndMapsBothOptions()
    {
        var command = new OsxBundleCommand();
        var result = command.ParseAndApply(RequiredOptions() + "--fileAssociations .myproj --urlProtocols myapp");

        Assert.Empty(result.Errors);
        var options = OptionMapper.Map<OsxBundleOptions>(command);
        Assert.Equal(".myproj", options.FileAssociations);
        Assert.Equal("myapp", options.UrlProtocols);
        Assert.Empty(new OsxBundleOptionsValidator().Validate(options).Errors);
    }

    [Fact]
    public void OsxBundle_InvalidValues_FailValidation()
    {
        var command = new OsxBundleCommand();
        Assert.Empty(command.ParseAndApply(RequiredOptions() + "--urlProtocols https://").Errors);

        var result = new OsxBundleOptionsValidator().Validate(OptionMapper.Map<OsxBundleOptions>(command));

        var error = Assert.Single(result.Errors, e => e.PropertyName == "UrlProtocols");
        Assert.Contains("reserved", error.ErrorMessage);
    }

    [Theory]
    [InlineData(typeof(WindowsPackCommand))]
    [InlineData(typeof(OsxPackCommand))]
    [InlineData(typeof(OsxBundleCommand))]
    public void AssociationLaunchMode_DefaultsToNewInstance(Type commandType)
    {
        var command = (PackCommand) Activator.CreateInstance(commandType)!;

        Assert.Empty(command.ParseAndApply(RequiredOptions()).Errors);
        Assert.Equal(AssociationLaunchMode.NewInstance, command.AssociationLaunchMode);
    }

    [Theory]
    [InlineData(typeof(WindowsPackCommand))]
    [InlineData(typeof(OsxPackCommand))]
    [InlineData(typeof(OsxBundleCommand))]
    public void AssociationLaunchMode_InvalidValue_IsParseError(Type commandType)
    {
        var command = (PackCommand) Activator.CreateInstance(commandType)!;

        Assert.NotEmpty(command.ParseAndApply(RequiredOptions() + "--associationLaunchMode Bogus").Errors);
    }

    [Fact]
    public void WindowsPack_ParsesAndMapsAssociationLaunchMode()
    {
        var command = new WindowsPackCommand();
        var result = command.ParseAndApply(RequiredOptions() + "--urlProtocols myapp --associationLaunchMode SingleInstance");

        Assert.Empty(result.Errors);
        Assert.Equal(AssociationLaunchMode.SingleInstance, command.AssociationLaunchMode);
        Assert.Equal(AssociationLaunchMode.SingleInstance, OptionMapper.Map<WindowsPackOptions>(command).AssociationLaunchMode);
    }

    [Fact]
    public void OsxPack_ParsesAndMapsAssociationLaunchMode()
    {
        var command = new OsxPackCommand();
        var result = command.ParseAndApply(RequiredOptions() + "--fileAssociations .myproj --associationLaunchMode SingleInstance");

        Assert.Empty(result.Errors);
        Assert.Equal(AssociationLaunchMode.SingleInstance, OptionMapper.Map<OsxPackOptions>(command).AssociationLaunchMode);
    }

    [Fact]
    public void OsxBundle_ParsesAndMapsAssociationLaunchMode()
    {
        var command = new OsxBundleCommand();
        var result = command.ParseAndApply(RequiredOptions() + "--urlProtocols myapp --associationLaunchMode SingleInstance");

        Assert.Empty(result.Errors);
        Assert.Equal(AssociationLaunchMode.SingleInstance, OptionMapper.Map<OsxBundleOptions>(command).AssociationLaunchMode);
    }

    [Fact]
    public void LinuxPack_DoesNotOfferAssociationOptions()
    {
        var command = new LinuxPackCommand();

        Assert.DoesNotContain(command.Options, o => o.Name == "--fileAssociations");
        Assert.DoesNotContain(command.Options, o => o.Name == "--urlProtocols");
        Assert.DoesNotContain(command.Options, o => o.Name == "--associationLaunchMode");

        var result = command.ParseAndApply(RequiredOptions() + "--fileAssociations .myproj");
        Assert.NotEmpty(result.Errors);
        Assert.Null(command.FileAssociations);

        Assert.NotEmpty(command.ParseAndApply(RequiredOptions() + "--associationLaunchMode SingleInstance").Errors);
    }
}
