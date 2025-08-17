using System.IO.Compression;
using System.Runtime.InteropServices;
using Velopack.Logging;
using Velopack.Util;
using NCode.ReparsePoints;

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
        Assert.False(
            File.Exists(Path.Combine(junctionPoint, "AFile")),
            "File should not be located until junction point created.");

        Assert.False(SymbolicLink.Exists(junctionPoint), "Junction point not created yet.");

        // Create junction point and confirm its properties.
        SymbolicLink.Create(junctionPoint, targetFolder, false /*don't overwrite*/);

        Assert.True(SymbolicLink.Exists(junctionPoint), "Junction point exists now.");

        Assert.Equal(targetFolder, SymbolicLink.GetTarget(junctionPoint));

        Assert.True(
            File.Exists(Path.Combine(junctionPoint, "AFile")),
            "File should be accessible via the junction point.");

        // Delete junction point.
        SymbolicLink.Delete(junctionPoint);

        Assert.False(SymbolicLink.Exists(junctionPoint), "Junction point should not exist now.");

        Assert.False(
            File.Exists(Path.Combine(junctionPoint, "AFile")),
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

        await EasyZip.CreateZipFromDirectoryAsync(NullVelopackLogger.Instance, output, tempFolder);
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

    // ===== New comprehensive tests =====

    [Fact]
    public void Create_SymlinkToNonExistentTarget_ShouldWork()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var target = Path.Combine(tempFolder, "NonExistent");
        var link = Path.Combine(tempFolder, "Link");

        // Should be able to create symlink to non-existent target
        File.WriteAllText(target, "test");
        SymbolicLink.Create(link, target);
        Assert.True(SymbolicLink.Exists(link));
        Assert.Equal(target, SymbolicLink.GetTarget(link));

        // Clean up
        SymbolicLink.Delete(link);
    }

    [Fact]
    public void Create_MultipleLevelsOfSymlinks()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var file = Path.Combine(tempFolder, "Original.txt");
        var link1 = Path.Combine(tempFolder, "Link1.txt");
        var link2 = Path.Combine(tempFolder, "Link2.txt");
        var link3 = Path.Combine(tempFolder, "Link3.txt");

        File.WriteAllText(file, "Hello");

        // Create chain: link3 -> link2 -> link1 -> file
        SymbolicLink.Create(link1, file);
        SymbolicLink.Create(link2, link1);
        SymbolicLink.Create(link3, link2);

        // All should resolve to the same content
        Assert.Equal("Hello", File.ReadAllText(link1));
        Assert.Equal("Hello", File.ReadAllText(link2));
        Assert.Equal("Hello", File.ReadAllText(link3));

        // Each should point to their immediate target
        Assert.Equal(file, SymbolicLink.GetTarget(link1));
        Assert.Equal(link1, SymbolicLink.GetTarget(link2));
        Assert.Equal(link2, SymbolicLink.GetTarget(link3));
    }

    [Fact]
    public void GetTarget_WithTrailingSlash_ShouldWork()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var target = Path.Combine(tempFolder, "Target");
        var link = Path.Combine(tempFolder, "Link");

        Directory.CreateDirectory(target);
        SymbolicLink.Create(link, target);

        // Should work with and without trailing slash
        Assert.Equal(target, SymbolicLink.GetTarget(link));
        Assert.Equal(target, SymbolicLink.GetTarget(link + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Create_WithSpecialCharactersInPath()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var target = Path.Combine(tempFolder, "Target With Spaces & Special-Chars");
        var link = Path.Combine(tempFolder, "Link With Spaces & Special-Chars");

        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "file.txt"), "content");

        SymbolicLink.Create(link, target);
        Assert.True(SymbolicLink.Exists(link));
        Assert.Equal(target, SymbolicLink.GetTarget(link));
        Assert.Equal("content", File.ReadAllText(Path.Combine(link, "file.txt")));
    }

    [Fact]
    public void Create_AbsoluteVsRelativeComparison()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var subdir = Directory.CreateDirectory(Path.Combine(tempFolder, "subdir")).FullName;
        var target = Path.Combine(tempFolder, "target.txt");
        var linkAbs = Path.Combine(subdir, "link_abs.txt");
        var linkRel = Path.Combine(subdir, "link_rel.txt");

        File.WriteAllText(target, "test");

        // Create absolute and relative links
        SymbolicLink.Create(linkAbs, target, relative: false);
        SymbolicLink.Create(linkRel, target, relative: true);

        // Both should work
        Assert.Equal("test", File.ReadAllText(linkAbs));
        Assert.Equal("test", File.ReadAllText(linkRel));

        // Check targets
        Assert.Equal(target, SymbolicLink.GetTarget(linkAbs));
        Assert.Equal(target, SymbolicLink.GetTarget(linkRel));

        // Relative target should be different when requested
        var relTarget = SymbolicLink.GetTarget(linkRel, relative: true);
        Assert.Contains("..", relTarget);
    }

    [Fact]
    public void FileSymlink_AllImplementationsAgree()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var target = Path.Combine(tempFolder, "target.txt");
        var link = Path.Combine(tempFolder, "link.txt");

        File.WriteAllText(target, "test content");

        // Create with our implementation
        SymbolicLink.Create(link, target);

        // Verify all implementations agree
        var ourTarget = SymbolicLink.GetTarget(link);
        Assert.Equal(target, ourTarget);

        // Compare with NCode.ReparsePoints (Windows only)
        if (VelopackRuntimeInfo.IsWindows) {
            var provider = new ReparsePointProvider();
            var linkInfo = provider.GetLink(link);
            Assert.Equal(LinkType.Symbolic, linkInfo.Type);
            Assert.Equal(target, linkInfo.Target);
        }

#if NET6_0_OR_GREATER
        var fileInfo = new FileInfo(link);
        Assert.NotNull(fileInfo.LinkTarget);
        Assert.Equal(target, Path.GetFullPath(fileInfo.LinkTarget));

        // Test interoperability: create with framework, read with ours
        var link2 = Path.Combine(tempFolder, "link2.txt");
        File.CreateSymbolicLink(link2, target);
        Assert.Equal(target, SymbolicLink.GetTarget(link2));
#endif
    }

    [Fact]
    public void DirectorySymlink_AllImplementationsAgree()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var target = Path.Combine(tempFolder, "targetDir");
        var link = Path.Combine(tempFolder, "linkDir");

        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "file.txt"), "content");

        // Create with our implementation
        SymbolicLink.Create(link, target);

        // Verify all implementations agree
        var ourTarget = SymbolicLink.GetTarget(link);
        Assert.Equal(target, ourTarget);

        // Compare with NCode.ReparsePoints (Windows only)
        if (VelopackRuntimeInfo.IsWindows) {
            var provider = new ReparsePointProvider();
            var linkInfo = provider.GetLink(link);
            // Directory symlinks on Windows are actually junctions
            Assert.True(linkInfo.Type == LinkType.Junction || linkInfo.Type == LinkType.Symbolic);
            Assert.Equal(target, linkInfo.Target);
        }

#if NET6_0_OR_GREATER
        var dirInfo = new DirectoryInfo(link);
        Assert.NotNull(dirInfo.LinkTarget);
        Assert.Equal(target, Path.GetFullPath(dirInfo.LinkTarget));

        // Test interoperability: create with framework, read with ours
        var link2 = Path.Combine(tempFolder, "linkDir2");
        Directory.CreateSymbolicLink(link2, target);
        Assert.Equal(target, SymbolicLink.GetTarget(link2));
#endif
    }

    [Fact]
    public void RelativeSymlink_AllImplementationsAgree()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var subdir = Directory.CreateDirectory(Path.Combine(tempFolder, "subdir")).FullName;
        var target = Path.Combine(tempFolder, "target.txt");
        var link = Path.Combine(subdir, "link.txt");

        File.WriteAllText(target, "test");

        // Create relative symlink with our implementation
        SymbolicLink.Create(link, target, relative: true);

        // Verify our implementation handles relative vs absolute correctly
        var ourAbsoluteTarget = SymbolicLink.GetTarget(link);
        var ourRelativeTarget = SymbolicLink.GetTarget(link, relative: true);
        Assert.Equal(target, ourAbsoluteTarget);
        Assert.Contains("..", ourRelativeTarget);

        // Compare with NCode.ReparsePoints (Windows only)
        if (VelopackRuntimeInfo.IsWindows) {
            var provider = new ReparsePointProvider();
            var linkInfo = provider.GetLink(link);
            Assert.Equal(LinkType.Symbolic, linkInfo.Type);
            // NCode returns the raw target path as stored in the symlink
            // For relative symlinks, this will be the relative path, not absolute
            Assert.Equal(ourRelativeTarget, linkInfo.Target);
        }

#if NET6_0_OR_GREATER
        var fileInfo = new FileInfo(link);
        Assert.NotNull(fileInfo.LinkTarget);
        // Framework returns the relative path for relative symlinks
        Assert.Contains("..", fileInfo.LinkTarget);
        Assert.Equal(fileInfo.LinkTarget, ourRelativeTarget);
#endif
    }

    [Fact]
    public void MultipleLevelsOfSymlinks_AllImplementationsAgree()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var file = Path.Combine(tempFolder, "original.txt");
        var link1 = Path.Combine(tempFolder, "link1.txt");
        var link2 = Path.Combine(tempFolder, "link2.txt");

        File.WriteAllText(file, "content");

        // Create chain: link2 -> link1 -> file
        SymbolicLink.Create(link1, file);
        SymbolicLink.Create(link2, link1);

        // Verify our implementation
        Assert.Equal(file, SymbolicLink.GetTarget(link1));
        Assert.Equal(link1, SymbolicLink.GetTarget(link2));

        // Verify content access works through the chain
        Assert.Equal("content", File.ReadAllText(link1));
        Assert.Equal("content", File.ReadAllText(link2));

        // Compare with NCode.ReparsePoints (Windows only)
        if (VelopackRuntimeInfo.IsWindows) {
            var provider = new ReparsePointProvider();
            var link1Info = provider.GetLink(link1);
            var link2Info = provider.GetLink(link2);
            Assert.Equal(LinkType.Symbolic, link1Info.Type);
            Assert.Equal(LinkType.Symbolic, link2Info.Type);
            Assert.Equal(file, link1Info.Target);
            Assert.Equal(link1, link2Info.Target);
        }

#if NET6_0_OR_GREATER
        var fileInfo1 = new FileInfo(link1);
        var fileInfo2 = new FileInfo(link2);
        Assert.NotNull(fileInfo1.LinkTarget);
        Assert.NotNull(fileInfo2.LinkTarget);
        Assert.Equal(file, Path.GetFullPath(fileInfo1.LinkTarget));
        Assert.Equal(link1, Path.GetFullPath(fileInfo2.LinkTarget));

        // Test ResolveLinkTarget for final resolution
        var finalTarget = fileInfo2.ResolveLinkTarget(true);
        Assert.Equal(file, finalTarget?.FullName);
#endif
    }

    [Fact]
    public void Delete_OnlyDeletesLinkNotTarget()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var target = Path.Combine(tempFolder, "target.txt");
        var link = Path.Combine(tempFolder, "link.txt");

        File.WriteAllText(target, "important data");
        SymbolicLink.Create(link, target);

        // Delete the link
        SymbolicLink.Delete(link);

        // Link should be gone, but target should remain
        Assert.False(File.Exists(link));
        Assert.False(SymbolicLink.Exists(link));
        Assert.True(File.Exists(target));
        Assert.Equal("important data", File.ReadAllText(target));
    }

    [SkippableFact]
    public void Exists_ReturnsFalseForHardLink()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);

        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var target = Path.Combine(tempFolder, "target.txt");
        var hardLink = Path.Combine(tempFolder, "hardlink.txt");

        File.WriteAllText(target, "test");

        // Create hard link using P/Invoke
        if (!CreateHardLink(hardLink, target, IntPtr.Zero)) {
            // Skip test if hard link creation fails (may need elevation)
            return;
        }

        // Hard link should exist as a file but not as a symbolic link
        Assert.True(File.Exists(hardLink));
        Assert.False(SymbolicLink.Exists(hardLink));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [Fact]
    public void GetTarget_ErrorMessages_AreDescriptive()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var regularFile = Path.Combine(tempFolder, "regular.txt");
        var regularDir = Path.Combine(tempFolder, "regularDir");
        var nonExistent = Path.Combine(tempFolder, "nonExistent");

        File.WriteAllText(regularFile, "test");
        Directory.CreateDirectory(regularDir);

        // Test various error conditions
        var ex1 = Assert.Throws<IOException>(() => SymbolicLink.GetTarget(regularFile));
        Assert.Contains("junction", ex1.Message.ToLower());

        var ex2 = Assert.Throws<IOException>(() => SymbolicLink.GetTarget(regularDir));
        Assert.Contains("junction", ex2.Message.ToLower());

        var ex3 = Assert.Throws<IOException>(() => SymbolicLink.GetTarget(nonExistent));
        Assert.Contains("junction", ex2.Message.ToLower());
    }

    [Fact]
    public void Create_OverwriteExistingSymlink()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempFolder);
        var target1 = Path.Combine(tempFolder, "target1.txt");
        var target2 = Path.Combine(tempFolder, "target2.txt");
        var link = Path.Combine(tempFolder, "link.txt");

        File.WriteAllText(target1, "content1");
        File.WriteAllText(target2, "content2");

        // Create initial symlink
        SymbolicLink.Create(link, target1);
        Assert.Equal("content1", File.ReadAllText(link));

        // Overwrite with new target
        SymbolicLink.Create(link, target2, overwrite: true);
        Assert.Equal("content2", File.ReadAllText(link));
        Assert.Equal(target2, SymbolicLink.GetTarget(link));
    }
}