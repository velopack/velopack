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
        return LoadDict(Path.Combine(dir, PlistWriter.PlistFileName));
    }

    private static XElement LoadDict(string plistPath) => XDocument.Load(plistPath).Root!.Element("dict")!;

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
        Assert.Null(ValueOf(dict, "LSUIElement"));
        Assert.Null(ValueOf(dict, "LSMinimumSystemVersion"));
    }

    /// <summary>A fake main .app with a helper binary and icon to build the association helper into.</summary>
    private sealed class FakeApp : IDisposable
    {
        private readonly IDisposable _temp;

        public string AppDir { get; }
        public string HelperBinary { get; }
        public string Icon { get; }

        public FakeApp()
        {
            _temp = TempUtil.GetTempDirectory(out var dir);
            AppDir = Path.Combine(dir, "My App.app");
            Directory.CreateDirectory(Path.Combine(AppDir, "Contents", "MacOS"));
            HelperBinary = Path.Combine(dir, "assochelper");
            File.WriteAllBytes(HelperBinary, [0xCF, 0xFA, 0xED, 0xFE, 1, 2, 3]);
            Icon = Path.Combine(dir, "MyApp.icns");
            File.WriteAllBytes(Icon, [0x69, 0x63, 0x6E, 0x73]);
        }

        public string Build(string[] extensions, string[] schemes)
        {
            var info = OsxAssociationHelper.CreateInfo("My App", "com.x.y", "1.2.0", "1.2.0-beta.1", extensions, schemes);
            return OsxAssociationHelper.Build(NullLogger.Instance, AppDir, info, HelperBinary, Icon);
        }

        public void Dispose() => _temp.Dispose();
    }

    [Fact]
    public void Helper_BuildsNestedBundle()
    {
        using var app = new FakeApp();

        var helper = app.Build([".myproj"], ["myapp"]);

        Assert.Equal(Path.Combine(app.AppDir, "Contents", "Helpers", "My App.app"), helper);
        var exe = Path.Combine(helper, "Contents", "MacOS", "AssocHelper");
        Assert.Equal(File.ReadAllBytes(app.HelperBinary), File.ReadAllBytes(exe));
        Assert.True(File.Exists(Path.Combine(helper, "Contents", "Resources", "MyApp.icns")));
        Assert.Equal(helper, OsxAssociationHelper.Find(app.AppDir));

        var dict = LoadDict(Path.Combine(helper, "Contents", "Info.plist"));
        Assert.Equal("My App", ValueOf(dict, "CFBundleName")!.Value);
        Assert.Equal("My App", ValueOf(dict, "CFBundleDisplayName")!.Value);
        Assert.Equal("AssocHelper", ValueOf(dict, "CFBundleExecutable")!.Value);
        Assert.Equal("com.x.y.velopack-assoc", ValueOf(dict, "CFBundleIdentifier")!.Value);
        Assert.Equal("APPL", ValueOf(dict, "CFBundlePackageType")!.Value);
        Assert.Equal("1.2.0", ValueOf(dict, "CFBundleShortVersionString")!.Value);
        Assert.Equal("1.2.0-beta.1", ValueOf(dict, "CFBundleVersion")!.Value);
        Assert.Equal("MyApp.icns", ValueOf(dict, "CFBundleIconFile")!.Value);
        Assert.Equal("NSApplication", ValueOf(dict, "NSPrincipalClass")!.Value);
        Assert.Equal("true", ValueOf(dict, "NSHighResolutionCapable")!.Name.LocalName);
        Assert.Equal("true", ValueOf(dict, "LSUIElement")!.Name.LocalName);
        Assert.Equal("10.13", ValueOf(dict, "LSMinimumSystemVersion")!.Value);
        Assert.Null(ValueOf(dict, "LSBackgroundOnly"));
    }

    [Fact]
    public void Helper_UrlSchemes_WriteSingleUrlType()
    {
        using var app = new FakeApp();

        var helper = app.Build([], ["myapp", "myapp-beta"]);

        var dict = LoadDict(Path.Combine(helper, "Contents", "Info.plist"));
        Assert.Null(ValueOf(dict, "CFBundleDocumentTypes"));

        var urlTypes = ValueOf(dict, "CFBundleURLTypes")!;
        Assert.Equal("array", urlTypes.Name.LocalName);
        var entry = Assert.Single(urlTypes.Elements("dict"));
        Assert.Equal("com.x.y.velopack-assoc", ValueOf(entry, "CFBundleURLName")!.Value);
        Assert.Equal("Viewer", ValueOf(entry, "CFBundleTypeRole")!.Value);
        Assert.Equal(["myapp", "myapp-beta"], ValueOf(entry, "CFBundleURLSchemes")!.Elements("string").Select(e => e.Value));
    }

    [Fact]
    public void Helper_DocumentExtensions_WriteOneDocumentTypePerExtension()
    {
        using var app = new FakeApp();

        var helper = app.Build([".myproj", ".mytpl"], []);

        var dict = LoadDict(Path.Combine(helper, "Contents", "Info.plist"));
        Assert.Null(ValueOf(dict, "CFBundleURLTypes"));

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

    [Fact]
    public void Helper_SecondBuildReplacesExisting()
    {
        using var app = new FakeApp();
        Assert.Null(OsxAssociationHelper.Find(app.AppDir));

        var first = app.Build([".myproj"], []);
        var stale = Path.Combine(first, "Contents", "stale.txt");
        File.WriteAllText(stale, "old");

        var second = app.Build([], ["myapp"]);

        Assert.Equal(first, second);
        Assert.False(File.Exists(stale));
        Assert.Single(OsxAssociationHelper.FindAll(app.AppDir));
        var dict = LoadDict(Path.Combine(second, "Contents", "Info.plist"));
        Assert.Null(ValueOf(dict, "CFBundleDocumentTypes"));
        Assert.NotNull(ValueOf(dict, "CFBundleURLTypes"));
    }

    [Fact]
    public void Helper_Find_IgnoresBundlesWithoutHelperExecutable()
    {
        using var app = new FakeApp();
        Directory.CreateDirectory(Path.Combine(app.AppDir, "Contents", "Helpers", "Other.app", "Contents", "MacOS"));

        Assert.Null(OsxAssociationHelper.Find(app.AppDir));
        Assert.Empty(OsxAssociationHelper.FindAll(app.AppDir));
    }

    [Theory]
    [InlineData("A/B:C", "A_B_C")]
    [InlineData("  My App  ", "My App")]
    [InlineData("", "Associations")]
    [InlineData(null, "Associations")]
    public void SanitizeBundleName(string? input, string expected)
    {
        Assert.Equal(expected, OsxAssociationHelper.SanitizeBundleName(input!));
    }

    [Theory]
    [InlineData("com.a b.c", "com.a-b.c.velopack-assoc")]
    [InlineData("com.example.myapp", "com.example.myapp.velopack-assoc")]
    [InlineData("com_example", "com-example.velopack-assoc")]
    public void MakeBundleId(string input, string expected)
    {
        Assert.Equal(expected, OsxAssociationHelper.MakeBundleId(input));
    }
}
