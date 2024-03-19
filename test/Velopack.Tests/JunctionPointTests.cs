namespace Velopack.Tests;

public class JunctionPointTests
{
    [Fact]
    public void Exists_NoSuchFile()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        Assert.False(JunctionPoint.Exists(Path.Combine(tempFolder, "$$$NoSuchFolder$$$")));
    }

    [Fact]
    public void Exists_IsADirectory()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.False(JunctionPoint.Exists(Path.Combine(tempFolder, "AFile")));
    }

    [Fact]
    public void Create_VerifyExists_GetTarget_Delete()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");

        Directory.CreateDirectory(targetFolder);
        File.Create(Path.Combine(targetFolder, "AFile")).Close();

        // Verify behavior before junction point created.
        Assert.False(File.Exists(Path.Combine(junctionPoint, "AFile")),
            "File should not be located until junction point created.");

        Assert.False(JunctionPoint.Exists(junctionPoint), "Junction point not created yet.");

        // Create junction point and confirm its properties.
        JunctionPoint.Create(junctionPoint, targetFolder, false /*don't overwrite*/);

        Assert.True(JunctionPoint.Exists(junctionPoint), "Junction point exists now.");

        Assert.Equal(targetFolder, JunctionPoint.GetTarget(junctionPoint));

        Assert.True(File.Exists(Path.Combine(junctionPoint, "AFile")),
            "File should be accessible via the junction point.");

        // Delete junction point.
        JunctionPoint.Delete(junctionPoint);

        Assert.False(JunctionPoint.Exists(junctionPoint), "Junction point should not exist now.");

        Assert.False(File.Exists(Path.Combine(junctionPoint, "AFile")),
            "File should not be located after junction point deleted.");

        Assert.False(Directory.Exists(junctionPoint), "Ensure directory was deleted too.");

        // Cleanup
        File.Delete(Path.Combine(targetFolder, "AFile"));
    }

    [Fact]
    public void Create_ThrowsIfOverwriteNotSpecifiedAndDirectoryExists()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");

        Directory.CreateDirectory(junctionPoint);
        Assert.Throws<IOException>(() => JunctionPoint.Create(junctionPoint, targetFolder, false));
    }

    [Fact]
    public void Create_OverwritesIfSpecifiedAndDirectoryExists()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");

        Directory.CreateDirectory(junctionPoint);
        Directory.CreateDirectory(targetFolder);

        JunctionPoint.Create(junctionPoint, targetFolder, true);

        Assert.Equal(targetFolder, JunctionPoint.GetTarget(junctionPoint));
    }

    [Fact]
    public void Create_ThrowsIfTargetDirectoryDoesNotExist()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");
        Assert.Throws<IOException>(() => JunctionPoint.Create(junctionPoint, targetFolder, false));
    }

    [Fact]
    public void GetTarget_NonExistentJunctionPoint()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => JunctionPoint.GetTarget(Path.Combine(tempFolder, "SymLink")));
    }

    [Fact]
    public void GetTarget_CalledOnADirectoryThatIsNotAJunctionPoint()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => JunctionPoint.GetTarget(tempFolder));
    }

    [Fact]
    public void GetTarget_CalledOnAFile()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.Throws<IOException>(() => JunctionPoint.GetTarget(Path.Combine(tempFolder, "AFile")));
    }

    [Fact]
    public void Delete_NonExistentJunctionPoint()
    {
        // Should do nothing.
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        JunctionPoint.Delete(Path.Combine(tempFolder, "SymLink"));
    }

    [Fact]
    public void Delete_CalledOnADirectoryThatIsNotAJunctionPoint()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => JunctionPoint.Delete(tempFolder));
    }

    [Fact]
    public void Delete_CalledOnAFile()
    {
        using var _1 = Utility.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.Throws<IOException>(() => JunctionPoint.Delete(Path.Combine(tempFolder, "AFile")));
    }
}