﻿
using System.CommandLine;
using Velopack.Vpk.Commands;
using Velopack.Vpk.Commands.Packaging;

namespace Velopack.CommandLine.Tests.Commands;

public abstract class ReleaseCommandTests<T> : BaseCommandTests<T>
    where T : WindowsPackCommand, new()
{
    //[Fact]
    //public void BaseUrl_WithNonHttpValue_ShowsError()
    //{
    //    var command = new T();

    //    string cli = GetRequiredDefaultOptions() + $"--baseUrl \"file://clowd.squirrel.com\"";
    //    ParseResult parseResult = command.ParseAndApply(cli);

    //    Assert.Equal(1, parseResult.Errors.Count);
    //    //Assert.Equal(command.BaseUrl, parseResult.Errors[0].SymbolResult?.Symbol);
    //    Assert.StartsWith("--baseUrl must contain a Uri with one of the following schems: http, https.", parseResult.Errors[0].Message);
    //}

    //[Fact]
    //public void BaseUrl_WithRelativeUrl_ShowsError()
    //{
    //    var command = new T();
    //    string cli = GetRequiredDefaultOptions() + $"--baseUrl \"clowd.squirrel.com\"";
    //    ParseResult parseResult = command.ParseAndApply(cli);

    //    Assert.Equal(1, parseResult.Errors.Count);
    //    //Assert.Equal(command.BaseUrl, parseResult.Errors[0].SymbolResult?.Symbol);
    //    Assert.StartsWith("--baseUrl must contain an absolute Uri.", parseResult.Errors[0].Message);
    //}

    [Fact]
    public void NoDelta_BareOption_SetsFlag()
    {
        var command = new T();

        string cli = GetRequiredDefaultOptions() + "--delta none";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.True(command.DeltaMode == Packaging.DeltaMode.None);
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
    public void SplashImage_WithoutFile_ShowsError()
    {
        string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
        var command = new T();

        string cli = GetRequiredDefaultOptions() + $"--splashImage \"{file}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        //Assert.Equal(1, parseResult.Errors.Count);
        Assert.Contains("file is not found", parseResult.Errors[0].Message);
        //Assert.Equal(command.SplashImage, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
        //Assert.Contains(file, parseResult.Errors[0].Message);
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
    public void Icon_WithBadFileExtension_ShowsError()
    {
        FileInfo fileInfo = CreateTempFile(name: Path.ChangeExtension(Path.GetRandomFileName(), ".wrong"));
        var command = new T();

        string cli = GetRequiredDefaultOptions() + $"--icon \"{fileInfo.FullName}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        //Assert.Equal(1, parseResult.Errors.Count);
        Assert.Contains(parseResult.Errors, e => e.Message.Equals($"--icon does not have an .ico extension"));
        //Assert.Equal($"--icon does not have an .ico extension", parseResult.Errors[0].Message);
    }

    [Fact]
    public void Icon_WithoutFile_ShowsError()
    {
        string file = Path.GetFullPath(Path.ChangeExtension(Path.GetRandomFileName(), ".ico"));
        var command = new T();

        string cli = GetRequiredDefaultOptions() + $"--icon \"{file}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        //Assert.Equal(1, parseResult.Errors.Count);
        //Assert.Equal(command.Icon, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
        Assert.Contains("file is not found", parseResult.Errors[0].Message);
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
    public void PackId_WithInvalidNuGetId_ShowsError()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        var command = new WindowsPackCommand();

        ParseResult parseResult = command.ParseAndApply($"--packId $42@ -v 1.0.0 -p \"{packDir.FullName}\" -e main.exe");

        Assert.Equal(1, parseResult.Errors.Count);
        Assert.StartsWith("--packId is an invalid NuGet package id.", parseResult.Errors[0].Message);
        Assert.Contains("$42@", parseResult.Errors[0].Message);
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
    public void PackVersion_WithInvalidVersion_ShowsError()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);
        var command = new WindowsPackCommand();

        ParseResult parseResult = command.ParseAndApply($"-u Clowd.Squirrel --packVersion 1.a.c -p \"{packDir.FullName}\" -e main.exe");

        Assert.Equal(1, parseResult.Errors.Count);
        Assert.StartsWith("--packVersion contains an invalid package version", parseResult.Errors[0].Message);
        Assert.Contains("1.a.c", parseResult.Errors[0].Message);
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
    public void ReleaseNotes_WithoutFile_ShowsError()
    {
        string releaseNotes = Path.GetFullPath(Path.GetRandomFileName());
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + $"--releaseNotes \"{releaseNotes}\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(1, parseResult.Errors.Count);
        Assert.Contains("file is not found", parseResult.Errors[0].Message);
        //Assert.Equal(command.ReleaseNotes, parseResult.Errors[0].SymbolResult?.Symbol.Parents.Single());
        //Assert.Contains(releaseNotes, parseResult.Errors[0].Message);
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
    public void SignParameters_WithSignTemplate_ShowsError()
    {
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + "--signTemplate \"signtool {{file}}\" --signParams \"param1 param2\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(1, parseResult.Errors.Count);
        Assert.Contains("Cannot use", parseResult.Errors[0].Message);
        Assert.Contains("options together", parseResult.Errors[0].Message);
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
    public void SignParallel_WithBadNumericValue_ShowsError(int value)
    {
        var command = new WindowsPackCommand();

        string cli = GetRequiredDefaultOptions() + $"--signParallel {value}";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(1, parseResult.Errors.Count);
        //Assert.Equal(command.SignParallel, parseResult.Errors[0].SymbolResult?.Symbol);
        Assert.Equal($"The value for --signParallel must be greater than 1 and less than 1000", parseResult.Errors[0].Message);
    }

    //[WindowsOnlyFact]
    //public void SignParallel_WithNonNumericValue_ShowsError()
    //{
    //    var command = new WindowsPackCommand();

    //    string cli = GetRequiredDefaultOptions() + $"--signParallel abc";
    //    ParseResult parseResult = command.ParseAndApply(cli);

    //    Assert.Equal(1, parseResult.Errors.Count);
    //    //Assert.Equal(command.SignParallel, parseResult.Errors[0].SymbolResult?.Symbol);
    //    Assert.Equal($"abc is not a valid integer for --signParallel", parseResult.Errors[0].Message);
    //}

    protected override string GetRequiredDefaultOptions()
    {
        DirectoryInfo packDir = CreateTempDirectory();
        CreateTempFile(packDir);

        return $"-u Clowd.Squirrel -v 1.0.0 -p \"{packDir.FullName}\" -e main.exe ";
    }
}
