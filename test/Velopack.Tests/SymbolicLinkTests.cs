namespace Velopack.Tests;

public class SymbolicLinkTests
{
    [Fact]
    public void Exists_NoSuchFile()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        Assert.False(SymbolicLink.Exists(Path.Combine(tempFolder, "$$$NoSuchFolder$$$")));
    }

    [Fact]
    public void Exists_IsADirectory()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.False(SymbolicLink.Exists(Path.Combine(tempFolder, "AFile")));
    }

    [Fact]
    public void CreateDirectory_VerifyExists_GetTarget_Delete()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");

        Directory.CreateDirectory(targetFolder);
        File.Create(Path.Combine(targetFolder, "AFile")).Close();

        // Verify behavior before junction point created.
        Assert.False(File.Exists(Path.Combine(junctionPoint, "AFile")),
            "File should not be located until junction point created.");

        Assert.False(SymbolicLink.Exists(junctionPoint), "Junction point not created yet.");

        // Create junction point and confirm its properties.
        SymbolicLink.Create(junctionPoint, targetFolder, false /*don't overwrite*/);

        Assert.True(SymbolicLink.Exists(junctionPoint), "Junction point exists now.");

        Assert.Equal(targetFolder, SymbolicLink.GetTarget(junctionPoint));

        Assert.True(File.Exists(Path.Combine(junctionPoint, "AFile")),
            "File should be accessible via the junction point.");

        // Delete junction point.
        SymbolicLink.Delete(junctionPoint);

        Assert.False(SymbolicLink.Exists(junctionPoint), "Junction point should not exist now.");

        Assert.False(File.Exists(Path.Combine(junctionPoint, "AFile")),
            "File should not be located after junction point deleted.");

        Assert.False(Directory.Exists(junctionPoint), "Ensure directory was deleted too.");

        // Cleanup
        File.Delete(Path.Combine(targetFolder, "AFile"));
    }

    [Fact]
    public void CreateFile_VerifyExists_GetTarget_Delete()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        var tmpFile = Path.Combine(tempFolder, "AFile");
        var symFile = Path.Combine(tempFolder, "SymFile");
        File.Create(tmpFile).Close();

        Assert.False(File.Exists(symFile), "File should not be located until junction point created.");
        Assert.False(SymbolicLink.Exists(symFile), "File should not be located until junction point created.");

        SymbolicLink.Create(symFile, tmpFile, true);

        Assert.True(File.Exists(symFile), "Symfile point exists now.");
        Assert.True(SymbolicLink.Exists(symFile), "Junction point exists now.");

        Assert.Equal(tmpFile, SymbolicLink.GetTarget(symFile));

        // verify symlink contents match real file.
        Assert.Empty(File.ReadAllBytes(symFile));
        File.WriteAllText(tmpFile, "Hello, World!");
        Assert.Equal("Hello, World!", File.ReadAllText(symFile));

        SymbolicLink.Delete(symFile);
        Assert.False(File.Exists(symFile));
        Assert.False(SymbolicLink.Exists(symFile));
    }

    [Fact]
    public void CreateFile_RelativePath()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        var subDir = Directory.CreateDirectory(Path.Combine(tempFolder, "SubDir")).FullName;

        var tmpFile = Path.Combine(tempFolder, "AFile");
        var symFile1 = Path.Combine(tempFolder, "SymFile");
        var symFile2 = Path.Combine(subDir, "SymFile2");
        File.WriteAllText(tmpFile, "Hello!");

        SymbolicLink.Create(symFile1, tmpFile, relative: true);
        SymbolicLink.Create(symFile2, tmpFile, relative: true);

        Assert.Equal("Hello!", File.ReadAllText(symFile1));
        Assert.Equal("Hello!", File.ReadAllText(symFile2));

        Assert.Equal("AFile", SymbolicLink.GetTarget(symFile1, resolve: false));
        Assert.Equal("..\\AFile", SymbolicLink.GetTarget(symFile2, resolve: false));

        Assert.Equal(tmpFile, SymbolicLink.GetTarget(symFile1));
        Assert.Equal(tmpFile, SymbolicLink.GetTarget(symFile2));
    }

    [Fact]
    public void CreateDirectory_RelativePath()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        var subDir = Directory.CreateDirectory(Path.Combine(tempFolder, "SubDir")).FullName;
        var subSubDir = Directory.CreateDirectory(Path.Combine(subDir, "SubSub")).FullName;
        var subDir2 = Directory.CreateDirectory(Path.Combine(tempFolder, "SubDir2")).FullName;

        File.WriteAllText(Path.Combine(subSubDir, "AFile"), "Hello!");
        var sym1 = Path.Combine(subSubDir, "Sym1");
        var sym2 = Path.Combine(tempFolder, "Sym2");

        SymbolicLink.Create(sym1, subDir2, relative: true);
        SymbolicLink.Create(sym2, subSubDir, relative: true);

        Assert.Equal("Hello!", File.ReadAllText(Path.Combine(sym2, "AFile")));

        Assert.Equal(subSubDir, SymbolicLink.GetTarget(sym2));
        Assert.Equal(subDir2, SymbolicLink.GetTarget(sym1));
        Assert.Equal("..\\..\\SubDir2", SymbolicLink.GetTarget(sym1, resolve: false));
        Assert.Equal("SubDir\\SubSub", SymbolicLink.GetTarget(sym2, resolve: false));
    }

    [Fact]
    public void Create_ThrowsIfOverwriteNotSpecifiedAndDirectoryExists()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");

        Directory.CreateDirectory(junctionPoint);
        Assert.Throws<IOException>(() => SymbolicLink.Create(junctionPoint, targetFolder, false));
    }

    [Fact]
    public void Create_OverwritesIfSpecifiedAndDirectoryExists()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");

        Directory.CreateDirectory(junctionPoint);
        Directory.CreateDirectory(targetFolder);

        SymbolicLink.Create(junctionPoint, targetFolder, true);

        Assert.Equal(targetFolder, SymbolicLink.GetTarget(junctionPoint));
    }

    [Fact]
    public void Create_ThrowsIfTargetDirectoryDoesNotExist()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");
        Assert.Throws<IOException>(() => SymbolicLink.Create(junctionPoint, targetFolder, false));
    }

    [Fact]
    public void GetTarget_NonExistentJunctionPoint()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => SymbolicLink.GetTarget(Path.Combine(tempFolder, "SymLink")));
    }

    [Fact]
    public void GetTarget_CalledOnADirectoryThatIsNotAJunctionPoint()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => SymbolicLink.GetTarget(tempFolder));
    }

    [Fact]
    public void GetTarget_CalledOnAFile()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.Throws<IOException>(() => SymbolicLink.GetTarget(Path.Combine(tempFolder, "AFile")));
    }

    [Fact]
    public void Delete_NonExistentJunctionPoint()
    {
        // Should do nothing.
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        SymbolicLink.Delete(Path.Combine(tempFolder, "SymLink"));
    }

    [Fact]
    public void Delete_CalledOnADirectoryThatIsNotAJunctionPoint()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => SymbolicLink.Delete(tempFolder));
    }

    [Fact]
    public void Delete_CalledOnAFile()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.Throws<IOException>(() => SymbolicLink.Delete(Path.Combine(tempFolder, "AFile")));
    }
}