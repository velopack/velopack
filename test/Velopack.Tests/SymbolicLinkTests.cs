﻿using System.IO.Compression;
using Velopack.Compression;
using Velopack.Util;

namespace Velopack.Tests;

public class SymbolicLinkTests
{
    [Fact]
    public void Exists_NoSuchFile()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        Assert.False(SymbolicLink.Exists(Path.Combine(tempFolder, "$$$NoSuchFolder$$$")));
    }

    [Fact]
    public void Exists_IsADirectory()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.False(SymbolicLink.Exists(Path.Combine(tempFolder, "AFile")));
    }

    [Fact]
    public void CreateDirectory_VerifyExists_GetTarget_Delete()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
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
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var tmpFile = Path.Combine(tempFolder, "AFile");
        var symFile = Path.Combine(tempFolder, "SymFile");
        File.Create(tmpFile).Close();

        Assert.False(File.Exists(symFile), "File should not be located until junction point created.");
        Assert.False(SymbolicLink.Exists(symFile), "File should not be located until junction point created.");

        SymbolicLink.Create(symFile, tmpFile, true);

        Assert.True(File.Exists(symFile), "Symlink should exist now.");
        Assert.True(SymbolicLink.Exists(symFile), "Symlink should exist now.");

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
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var subDir = Directory.CreateDirectory(Path.Combine(tempFolder, "SubDir")).FullName;

        var tmpFile = Path.Combine(tempFolder, "AFile");
        var symFile1 = Path.Combine(tempFolder, "SymFile");
        var symFile2 = Path.Combine(subDir, "SymFile2");
        var symFile3 = Path.Combine(subDir, "SymFile3");
        File.WriteAllText(tmpFile, "Hello!");

        SymbolicLink.Create(symFile1, tmpFile, relative: true);
        SymbolicLink.Create(symFile2, tmpFile, relative: true);
        SymbolicLink.Create(symFile3, tmpFile, relative: false);

        Assert.Equal("Hello!", File.ReadAllText(symFile1));
        Assert.Equal("Hello!", File.ReadAllText(symFile2));

        Assert.Equal("AFile", SymbolicLink.GetTarget(symFile1, relative: true));
        Assert.Equal($"..{Path.DirectorySeparatorChar}AFile", SymbolicLink.GetTarget(symFile2, relative: true));
        Assert.Equal($"..{Path.DirectorySeparatorChar}AFile", SymbolicLink.GetTarget(symFile3, relative: true));
        Assert.Equal(tmpFile, SymbolicLink.GetTarget(symFile1, relative: false));
        Assert.Equal(tmpFile, SymbolicLink.GetTarget(symFile2, relative: false));
        Assert.Equal(tmpFile, SymbolicLink.GetTarget(symFile3, relative: false));

        Assert.Equal(tmpFile, SymbolicLink.GetTarget(symFile1));
        Assert.Equal(tmpFile, SymbolicLink.GetTarget(symFile2));
    }

    [Fact]
    public void CreateDirectory_RelativePath()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
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
        Assert.Equal($"..{Path.DirectorySeparatorChar}..{Path.DirectorySeparatorChar}SubDir2", SymbolicLink.GetTarget(sym1, relative: true));
        Assert.Equal($"SubDir{Path.DirectorySeparatorChar}SubSub", SymbolicLink.GetTarget(sym2, relative: true));
    }

    [Fact]
    public void Create_ThrowsIfOverwriteNotSpecifiedAndDirectoryExists()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");

        Directory.CreateDirectory(junctionPoint);
        Assert.Throws<IOException>(() => SymbolicLink.Create(junctionPoint, targetFolder, false));
    }

    [Fact]
    public void Create_OverwritesIfSpecifiedAndDirectoryExists()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
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
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        string targetFolder = Path.Combine(tempFolder, "ADirectory");
        string junctionPoint = Path.Combine(tempFolder, "SymLink");
        Assert.Throws<IOException>(() => SymbolicLink.Create(junctionPoint, targetFolder, false));
    }

    [Fact]
    public void GetTarget_NonExistentJunctionPoint()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => SymbolicLink.GetTarget(Path.Combine(tempFolder, "SymLink")));
    }

    [Fact]
    public void GetTarget_CalledOnADirectoryThatIsNotAJunctionPoint()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => SymbolicLink.GetTarget(tempFolder));
    }

    [Fact]
    public void GetTarget_CalledOnAFile()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.Throws<IOException>(() => SymbolicLink.GetTarget(Path.Combine(tempFolder, "AFile")));
    }

    [Fact]
    public void Delete_NonExistentJunctionPoint()
    {
        // Should do nothing.
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        SymbolicLink.Delete(Path.Combine(tempFolder, "SymLink"));
    }

    [Fact]
    public void Delete_CalledOnADirectoryThatIsNotAJunctionPoint()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        Assert.Throws<IOException>(() => SymbolicLink.Delete(tempFolder));
    }

    [Fact]
    public void Delete_CalledOnAFile()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        File.Create(Path.Combine(tempFolder, "AFile")).Close();

        Assert.Throws<IOException>(() => SymbolicLink.Delete(Path.Combine(tempFolder, "AFile")));
    }

    [Fact]
    public async Task ComplexSymlinkDirGetsZippedCorrectly()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var temp = new DirectoryInfo(tempFolder);
        var versions = temp.CreateSubdirectory("Versions");
        var a = versions.CreateSubdirectory("A");
        var resources = a.CreateSubdirectory("Resources");
        File.WriteAllText(Path.Combine(resources.FullName, "Info.plist"), "Hello, Resources!");
        File.WriteAllText(Path.Combine(a.FullName, "App"), "Hello, App!");
        SymbolicLink.Create(Path.Combine(versions.FullName, "Current"), a.FullName, false, true);
        SymbolicLink.Create(Path.Combine(temp.FullName, "Resources"), Path.Combine(versions.FullName, "Current", "Resources"), false, true);
        SymbolicLink.Create(Path.Combine(temp.FullName, "App"), Path.Combine(versions.FullName, "Current", "App"), false, true);

        using var _2 = TempUtil.GetTempDirectory(out var tempOutput);
        var output = Path.Combine(tempOutput, "output.zip");

        await EasyZip.CreateZipFromDirectoryAsync(NullLogger.Instance, output, tempFolder);
        ZipFile.ExtractToDirectory(output, tempOutput);

        var appSym = Path.Combine(tempOutput, "App.__symlink");
        Assert.True(File.Exists(appSym));
        Assert.Equal("Versions/Current/App", File.ReadAllText(appSym));
        
        var resSym = Path.Combine(tempOutput, "Resources.__symlink");
        Assert.True(File.Exists(resSym));
        Assert.Equal("Versions/Current/Resources/", File.ReadAllText(resSym));
        
        Assert.True(Directory.Exists(Path.Combine(tempOutput, "Versions")));
        Assert.False(Directory.Exists(Path.Combine(tempOutput, "App")));
        Assert.False(Directory.Exists(Path.Combine(tempOutput, "Resources")));
        
        Assert.True(Directory.Exists(Path.Combine(tempOutput, "Versions", "A")));
        Assert.False(Directory.Exists(Path.Combine(tempOutput, "Versions", "Current")));
        Assert.False(File.Exists(Path.Combine(tempOutput, "Versions", "Current")));
        
        var currentSym = Path.Combine(tempOutput, "Versions", "Current.__symlink");
        Assert.True(File.Exists(currentSym));
        Assert.Equal("A/", File.ReadAllText(currentSym));
    }
}