namespace Velopack.Build.Tests;

/// <summary>
/// Tests for ArgumentBuilder class
/// </summary>
public class ArgumentBuilderTests
{
    [Fact]
    public void AddCommand_ShouldAddCommand()
    {
        var builder = new ArgumentBuilder();
        builder.AddCommand("pack");
        
        var args = builder.Build();
        
        Assert.Single(args);
        Assert.Equal("pack", args[0]);
    }

    [Fact]
    public void AddCommand_ShouldChainMultipleCommands()
    {
        var builder = new ArgumentBuilder();
        builder.AddCommand("flow").AddCommand("publish");
        
        var args = builder.Build();
        
        Assert.Equal(2, args.Length);
        Assert.Equal("flow", args[0]);
        Assert.Equal("publish", args[1]);
    }

    [Fact]
    public void AddOption_WithValue_ShouldAddOptionAndValue()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--packId", "MyApp");
        
        var args = builder.Build();
        
        Assert.Equal(2, args.Length);
        Assert.Equal("--packId", args[0]);
        Assert.Equal("MyApp", args[1]);
    }

    [Fact]
    public void AddOption_WithNullValue_ShouldNotAddOption()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--packId", (string?)null);
        
        var args = builder.Build();
        
        Assert.Empty(args);
    }

    [Fact]
    public void AddOption_WithEmptyValue_ShouldNotAddOption()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--packId", "");
        
        var args = builder.Build();
        
        Assert.Empty(args);
    }

    [Fact]
    public void AddOption_WithWhitespaceValue_ShouldNotAddOption()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--packId", "   ");
        
        var args = builder.Build();
        
        Assert.Empty(args);
    }

    [Fact]
    public void AddOption_WithSpacesInValue_ShouldQuoteValue()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--packTitle", "My Amazing App");
        
        var args = builder.Build();
        
        Assert.Equal(2, args.Length);
        Assert.Equal("--packTitle", args[0]);
        Assert.Equal("\"My Amazing App\"", args[1]);
    }

    [Fact]
    public void AddOption_WithPathContainingSpaces_ShouldQuoteValue()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--outputDir", "C:\\Program Files\\MyApp");
        
        var args = builder.Build();
        
        Assert.Equal(2, args.Length);
        Assert.Equal("--outputDir", args[0]);
        Assert.Equal("\"C:\\Program Files\\MyApp\"", args[1]);
    }

    [Fact]
    public void AddOption_BooleanTrue_ShouldAddFlag()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--verbose", true);
        
        var args = builder.Build();
        
        Assert.Single(args);
        Assert.Equal("--verbose", args[0]);
    }

    [Fact]
    public void AddOption_BooleanFalse_ShouldNotAddFlag()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--verbose", false);
        
        var args = builder.Build();
        
        Assert.Empty(args);
    }

    [Fact]
    public void AddOption_BooleanWithDefaultTrue_ShouldNotAddIfMatching()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--enabled", true, defaultValue: true);
        
        var args = builder.Build();
        
        Assert.Empty(args);
    }

    [Fact]
    public void AddOption_Integer_ShouldAddOptionAndValue()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--signParallel", 5);
        
        var args = builder.Build();
        
        Assert.Equal(2, args.Length);
        Assert.Equal("--signParallel", args[0]);
        Assert.Equal("5", args[1]);
    }

    [Fact]
    public void AddOption_IntegerWithDefault_ShouldNotAddIfMatching()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--signParallel", 10, defaultValue: 10);
        
        var args = builder.Build();
        
        Assert.Empty(args);
    }

    [Fact]
    public void AddOption_IntegerWithDifferentDefault_ShouldAdd()
    {
        var builder = new ArgumentBuilder();
        builder.AddOption("--signParallel", 5, defaultValue: 10);
        
        var args = builder.Build();
        
        Assert.Equal(2, args.Length);
        Assert.Equal("--signParallel", args[0]);
        Assert.Equal("5", args[1]);
    }

    [Fact]
    public void Build_ComplexScenario_ShouldBuildCorrectArguments()
    {
        var builder = new ArgumentBuilder();
        builder.AddCommand("pack");
        builder.AddOption("--packId", "MyApp");
        builder.AddOption("--packVersion", "1.0.0");
        builder.AddOption("--outputDir", "C:\\Releases");
        builder.AddOption("--verbose", true);
        builder.AddOption("--quiet", false);
        builder.AddOption("--packTitle", "My Application");
        builder.AddOption("--signParallel", 5, defaultValue: 10);
        builder.AddOption("--channel", (string?)null);
        
        var args = builder.Build();
        
        // Count: pack, --packId, MyApp, --packVersion, 1.0.0, --outputDir, C:\\Releases, 
        //        --verbose, --packTitle, "My Application", --signParallel, 5
        Assert.Equal(12, args.Length);
        Assert.Contains("pack", args);
        Assert.Contains("--packId", args);
        Assert.Contains("MyApp", args);
        Assert.Contains("--packVersion", args);
        Assert.Contains("1.0.0", args);
        Assert.Contains("--outputDir", args);
        Assert.Contains("C:\\Releases", args);
        Assert.Contains("--verbose", args);
        Assert.Contains("\"My Application\"", args);
        Assert.Contains("5", args);
    }

    [Fact]
    public void ToString_ShouldReturnCommandLineString()
    {
        var builder = new ArgumentBuilder();
        builder.AddCommand("pack");
        builder.AddOption("--packId", "MyApp");
        builder.AddOption("--verbose", true);
        
        var commandLine = builder.ToString();
        
        Assert.Equal("pack --packId MyApp --verbose", commandLine);
    }
}
