using Velopack.NuGet;
using Velopack.Util;

namespace Velopack.Tests;

public class PackageManifestTests
{
    private static PackageManifest Parse(string extraMetadata)
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        var nuspec = Path.Combine(tempDir, "test.nuspec");
        File.WriteAllText(
            nuspec,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
            <metadata>
            <id>AssocApp</id>
            <title>Assoc App</title>
            <version>1.0.0</version>
            <mainExe>app.exe</mainExe>
            <os>win</os>
            {extraMetadata}
            </metadata>
            </package>
            """);
        return PackageManifest.ParseFromFile(nuspec);
    }

    [Fact]
    public void ParsesFileAssociationsAndUrlProtocols()
    {
        var manifest = Parse("<fileAssociations>.myproj,.mytpl</fileAssociations><urlProtocols>myapp,myapp-beta</urlProtocols>");
        Assert.Equal(new[] { ".myproj", ".mytpl" }, manifest.FileAssociations);
        Assert.Equal(new[] { "myapp", "myapp-beta" }, manifest.UrlProtocols);
    }

    [Fact]
    public void AbsentAssociationsAreEmpty()
    {
        var manifest = Parse("");
        Assert.Empty(manifest.FileAssociations);
        Assert.Empty(manifest.UrlProtocols);
    }

    [Theory]
    [InlineData("<associationLaunchMode>SingleInstance</associationLaunchMode>", "SingleInstance", true)]
    [InlineData("<associationLaunchMode> singleinstance </associationLaunchMode>", "singleinstance", true)]
    [InlineData("<associationLaunchMode>NewInstance</associationLaunchMode>", "NewInstance", false)]
    [InlineData("<associationLaunchMode>Bogus</associationLaunchMode>", "Bogus", false)]
    [InlineData("", null, false)]
    public void ParsesAssociationLaunchMode(string extraMetadata, string? expected, bool singleInstance)
    {
        var manifest = Parse(extraMetadata);
        Assert.Equal(expected, manifest.AssociationLaunchMode);
        Assert.Equal(singleInstance, Velopack.Locators.VelopackLocator.IsSingleInstanceMode(manifest));
    }

    [Fact]
    public void EmptyAndWhitespaceAssociationEntriesAreFiltered()
    {
        var manifest = Parse("<fileAssociations></fileAssociations><urlProtocols>  , ; ,</urlProtocols>");
        Assert.Empty(manifest.FileAssociations);
        Assert.Empty(manifest.UrlProtocols);

        manifest = Parse("<fileAssociations> .myproj , , .mytpl ,</fileAssociations><urlProtocols>myapp;;myapp-beta</urlProtocols>");
        Assert.Equal(new[] { ".myproj", ".mytpl" }, manifest.FileAssociations);
        Assert.Equal(new[] { "myapp", "myapp-beta" }, manifest.UrlProtocols);
    }
}
