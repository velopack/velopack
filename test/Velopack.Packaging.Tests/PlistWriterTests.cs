#nullable enable
using System.Xml.Linq;
using Velopack.Packaging.Unix;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class PlistWriterTests
{
    private static OsxAppInfo CreateInfo() => new() {
        CFBundleName = "My App",
        CFBundleExecutable = "MyApp",
        CFBundleIdentifier = "com.example.myapp",
        CFBundlePackageType = "APPL",
        CFBundleShortVersionString = "1.0.0",
        CFBundleVersion = "1.0.0",
        CFBundleSignature = "????",
        NSPrincipalClass = "NSApplication",
        NSHighResolutionCapable = true,
        CFBundleIconFile = "MyApp.icns",
    };

    private static XElement WriteAndLoad(OsxAppInfo info)
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        new PlistWriter(NullLogger.Instance, info, dir).Write();
        var doc = XDocument.Load(Path.Combine(dir, PlistWriter.PlistFileName));
        return doc.Root!.Element("dict")!;
    }

    /// <summary>Returns the value element following the given key in a plist dict, or null.</summary>
    private static XElement? ValueOf(XElement dict, string key)
    {
        return dict.Elements("key").FirstOrDefault(k => k.Value == key)?.ElementsAfterSelf().FirstOrDefault();
    }

    [Fact]
    public void NoAssociations_OmitsTypeKeys()
    {
        var dict = WriteAndLoad(CreateInfo());

        Assert.Equal("My App", ValueOf(dict, "CFBundleName")!.Value);
        Assert.Null(ValueOf(dict, "CFBundleURLTypes"));
        Assert.Null(ValueOf(dict, "CFBundleDocumentTypes"));
    }

    [Fact]
    public void UrlSchemes_WriteSingleUrlType()
    {
        var info = CreateInfo();
        info.UrlSchemes = ["myapp", "myapp-beta"];

        var dict = WriteAndLoad(info);

        var urlTypes = ValueOf(dict, "CFBundleURLTypes")!;
        Assert.Equal("array", urlTypes.Name.LocalName);
        var entry = Assert.Single(urlTypes.Elements("dict"));
        Assert.Equal("com.example.myapp", ValueOf(entry, "CFBundleURLName")!.Value);
        Assert.Equal("Viewer", ValueOf(entry, "CFBundleTypeRole")!.Value);
        Assert.Equal(["myapp", "myapp-beta"], ValueOf(entry, "CFBundleURLSchemes")!.Elements("string").Select(e => e.Value));
    }

    [Fact]
    public void DocumentExtensions_WriteOneDocumentTypePerExtension()
    {
        var info = CreateInfo();
        info.DocumentExtensions = [".myproj", ".mytpl"];

        var dict = WriteAndLoad(info);

        var docTypes = ValueOf(dict, "CFBundleDocumentTypes")!;
        var entries = docTypes.Elements("dict").ToArray();
        Assert.Equal(2, entries.Length);

        Assert.Equal("My App MYPROJ File", ValueOf(entries[0], "CFBundleTypeName")!.Value);
        Assert.Equal("Viewer", ValueOf(entries[0], "CFBundleTypeRole")!.Value);
        Assert.Equal("Default", ValueOf(entries[0], "LSHandlerRank")!.Value);
        Assert.Equal(["myproj"], ValueOf(entries[0], "CFBundleTypeExtensions")!.Elements("string").Select(e => e.Value));

        Assert.Equal("My App MYTPL File", ValueOf(entries[1], "CFBundleTypeName")!.Value);
        Assert.Equal(["mytpl"], ValueOf(entries[1], "CFBundleTypeExtensions")!.Elements("string").Select(e => e.Value));

        // no UTI declarations are generated in v1
        Assert.Null(ValueOf(dict, "UTExportedTypeDeclarations"));
    }
}
