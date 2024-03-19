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