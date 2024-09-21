using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using AsmResolver.PE;
using AsmResolver.PE.File;
using AsmResolver.PE.Win32Resources.Icon;
using AsmResolver.PE.Win32Resources.Version;
using Velopack.NuGet;
using Velopack.Packaging.Windows;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class ResourceEditTests
{
    private readonly ITestOutputHelper _output;

    public ResourceEditTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void CreateTestPEFileWithoutRsrc(string tempFile)
    {
        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateExecutableHeader(),
            new MetadataRootBuilder(new MetadataBuilder()),
            ilStream: new BlobBuilder());
        var peImageBuilder = new BlobBuilder();
        peBuilder.Serialize(peImageBuilder);

        using var fs = File.OpenWrite(tempFile);
        fs.Write(peImageBuilder.ToArray());
    }

    [Fact]
    public void CommitResourcesInCorrectOrder()
    {
        using var logger = _output.BuildLoggerFor<ResourceEditTests>();
        using var _1 = TempUtil.GetTempFileName(out var tempFile);
        var exe = PathHelper.GetRustAsset("setup.exe");
        File.Copy(exe, tempFile);

        var nuspec = PathHelper.GetFixture("FullNuspec.nuspec");
        var manifest = PackageManifest.ParseFromFile(nuspec);
        var pkgVersion = manifest.Version!;

        var edit = new ResourceEdit(tempFile, logger);
        edit.SetExeIcon(PathHelper.GetFixture("clowd.ico"));
        edit.SetVersionInfo(manifest);
        edit.Commit();

        var afterRsrc = PEImage.FromFile(PEFile.FromFile(tempFile)).Resources;
        Assert.NotNull(afterRsrc);

        uint lastId = 0;
        foreach (var e in afterRsrc.Entries) {
            Assert.True(e.Id > lastId, "Resource entry ID must be greater than the previous");
            lastId = e.Id;
        }
    }

    [Fact]
    public void CopyResourcesWithoutRsrc()
    {
        using var logger = _output.BuildLoggerFor<ResourceEditTests>();

        using var _1 = TempUtil.GetTempFileName(out var tempFile);
        CreateTestPEFileWithoutRsrc(tempFile);

        var edit = new ResourceEdit(tempFile, logger);
        edit.CopyResourcesFrom(PathHelper.GetFixture("Clowd.exe"));
        edit.Commit();

        AssertVersionInfo(tempFile, "3.4.439.61274", "3.4.439+ef5a83", "Copyright © Caelan Sayler, 2014-2022",
            "Clowd", "Clowd", "Caelan Sayler");
    }

    [Fact]
    public void CopyResourcesWithPreExistingRsrc()
    {
        using var logger = _output.BuildLoggerFor<ResourceEditTests>();

        using var _1 = TempUtil.GetTempFileName(out var tempFile);
        var exe = PathHelper.GetFixture("SquirrelAwareTweakedNetCoreApp.exe");
        File.Copy(exe, tempFile);

        var edit = new ResourceEdit(tempFile, logger);
        edit.CopyResourcesFrom(PathHelper.GetFixture("Clowd.exe"));
        edit.Commit();

        AssertVersionInfo(tempFile, "3.4.439.61274", "3.4.439+ef5a83", "Copyright © Caelan Sayler, 2014-2022",
            "Clowd", "Clowd", "Caelan Sayler");
    }

    [Fact]
    public void SetIconWithPreExistingRsrc()
    {
        using var logger = _output.BuildLoggerFor<ResourceEditTests>();

        using var _1 = TempUtil.GetTempFileName(out var tempFile);
        var exe = PathHelper.GetFixture("atom.exe");
        File.Copy(exe, tempFile);

        var beforeRsrc = PEImage.FromFile(PEFile.FromFile(tempFile)).Resources;
        Assert.NotNull(beforeRsrc);
        var beforeIcon = IconResource.FromDirectory(beforeRsrc);
        Assert.Single(beforeIcon.GetIconGroups());
        Assert.Equal(6, beforeIcon.GetIconGroups().ToList()[0].GetIconEntries().Count());

        var edit = new ResourceEdit(tempFile, logger);
        edit.SetExeIcon(PathHelper.GetFixture("clowd.ico"));
        edit.Commit();

        var afterRsrc = PEImage.FromFile(PEFile.FromFile(tempFile)).Resources;
        Assert.NotNull(afterRsrc);
        var afterIcon = IconResource.FromDirectory(afterRsrc);
        Assert.Single(afterIcon.GetIconGroups());
        Assert.Equal(1, afterIcon.GetIconGroups().Single().Type);
        Assert.Equal(7, afterIcon.GetIconGroups().ToList()[0].GetIconEntries().Count());
    }

    [Fact]
    public void SetIconWithoutRsrc()
    {
        using var logger = _output.BuildLoggerFor<ResourceEditTests>();

        using var _1 = TempUtil.GetTempFileName(out var tempFile);
        CreateTestPEFileWithoutRsrc(tempFile);

        var beforeRsrc = PEImage.FromFile(PEFile.FromFile(tempFile)).Resources;
        Assert.Null(beforeRsrc);

        var edit = new ResourceEdit(tempFile, logger);
        edit.SetExeIcon(PathHelper.GetFixture("clowd.ico"));
        edit.Commit();

        var afterRsrc = PEImage.FromFile(PEFile.FromFile(tempFile)).Resources;
        Assert.NotNull(afterRsrc);
        var afterIcon = IconResource.FromDirectory(afterRsrc);
        Assert.Single(afterIcon.GetIconGroups());
        Assert.Equal(7, afterIcon.GetIconGroups().ToList()[0].GetIconEntries().Count());
    }

    [Fact]
    public void SetVersionInfoWithPreExistingRsrc()
    {
        using var logger = _output.BuildLoggerFor<ResourceEditTests>();
        using var _1 = TempUtil.GetTempFileName(out var tempFile);
        var exe = PathHelper.GetFixture("atom.exe");
        File.Copy(exe, tempFile);

        var nuspec = PathHelper.GetFixture("FullNuspec.nuspec");
        var manifest = PackageManifest.ParseFromFile(nuspec);
        var pkgVersion = manifest.Version!;

        var edit = new ResourceEdit(tempFile, logger);
        edit.SetVersionInfo(manifest);
        edit.Commit();

        AssertVersionInfo(tempFile, manifest);
    }

    [Fact]
    public void SetVersionInfoWithoutRsrc()
    {
        using var logger = _output.BuildLoggerFor<ResourceEditTests>();
        using var _1 = TempUtil.GetTempFileName(out var tempFile);
        CreateTestPEFileWithoutRsrc(tempFile);

        var nuspec = PathHelper.GetFixture("FullNuspec.nuspec");
        var manifest = PackageManifest.ParseFromFile(nuspec);
        var pkgVersion = manifest.Version!;

        var edit = new ResourceEdit(tempFile, logger);
        edit.SetVersionInfo(manifest);
        edit.Commit();

        AssertVersionInfo(tempFile, manifest);
    }

    private void AssertVersionInfo(string exeFile, PackageManifest manifest)
    {
        AssertVersionInfo(exeFile, manifest.Version!.ToFullString(), manifest.Version!.ToFullString(),
            manifest.ProductCopyright, manifest.ProductName, manifest.ProductDescription, manifest.ProductCompany);
    }

    private void AssertVersionInfo(string exeFile, string fileVersion, string productVersion,
        string legalCopyright, string productName, string fileDescription, string companyName)
    {
        if (VelopackRuntimeInfo.IsWindows) {
            // on Windows FileVersionInfo uses win32 methods to retrieve info from the PE resources
            // on Unix, this function just looks for managed assembly attributes so is not suitable
            var versionInfo = FileVersionInfo.GetVersionInfo(exeFile);
            Assert.Equal(fileVersion, versionInfo.FileVersion);
            Assert.Equal(productVersion, versionInfo.ProductVersion);
            Assert.Equal(legalCopyright, versionInfo.LegalCopyright);
            Assert.Equal(productName, versionInfo.ProductName);
            Assert.Equal(fileDescription, versionInfo.FileDescription);
            Assert.Equal(companyName, versionInfo.CompanyName);
        } else {
            var file = PEFile.FromFile(exeFile);
            var image = PEImage.FromFile(file);
            Assert.NotNull(image.Resources);
            var versionInfo = VersionInfoResource.FromDirectory(image.Resources);

            var stringInfo = versionInfo.GetChild<StringFileInfo>(StringFileInfo.StringFileInfoKey);
            Assert.NotNull(stringInfo);
            Assert.Single(stringInfo.Tables);

            var stringTable = stringInfo.Tables[0];
            Assert.Equal(companyName, stringTable[StringTable.CompanyNameKey]);
            Assert.Equal(fileDescription, stringTable[StringTable.FileDescriptionKey]);
            Assert.Equal(fileVersion, stringTable[StringTable.FileVersionKey]);
            Assert.Equal(legalCopyright, stringTable[StringTable.LegalCopyrightKey]);
            Assert.Equal(productName, stringTable[StringTable.ProductNameKey]);
            Assert.Equal(productVersion, stringTable[StringTable.ProductVersionKey]);
        }
    }
}
