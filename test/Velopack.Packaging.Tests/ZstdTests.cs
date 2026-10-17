using Velopack.Packaging.Compression;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class ZstdTests
{
    // NB: emoji can not be represented in any ANSI code page, so these always
    // reproduce facebook/zstd#3882 on Windows regardless of the system locale.
    [Theory]
    [InlineData("ascii", "app.exe")]
    [InlineData("ascii", "テストアプリ_테스트_🚀_ExecutionStub.exe")]
    [InlineData("ユーザー_🚀", "app.exe")]
    public void CreateAndApplyPatchWithNonAsciiPaths(string dirName, string fileName)
    {
        using var _ = TempUtil.GetTempDirectory(out var temp);
        var oldDir = Directory.CreateDirectory(Path.Combine(temp, dirName, "old")).FullName;
        var newDir = Directory.CreateDirectory(Path.Combine(temp, dirName, "new")).FullName;
        var oldFile = Path.Combine(oldDir, fileName);
        var newFile = Path.Combine(newDir, fileName);
        var patchFile = newFile + ".zsdiff";
        var outputFile = Path.Combine(oldDir, fileName + ".patched");

        var oldBytes = new byte[256 * 1024];
        new Random(1105).NextBytes(oldBytes);
        var newBytes = oldBytes.ToArray();
        new Random(1106).NextBytes(newBytes.AsSpan(1000, 4096));
        File.WriteAllBytes(oldFile, oldBytes);
        File.WriteAllBytes(newFile, newBytes);

        var zstd = new Zstd(HelperFile.GetZstdPath());
        zstd.CreatePatch(oldFile, newFile, patchFile, DeltaMode.BestSpeed);
        Assert.True(new FileInfo(patchFile).Length < newBytes.Length / 2);

        zstd.ApplyPatch(oldFile, patchFile, outputFile);
        Assert.Equal(newBytes, File.ReadAllBytes(outputFile));

        // inputs must be left where they were
        Assert.Equal(oldBytes, File.ReadAllBytes(oldFile));
        Assert.Equal(newBytes, File.ReadAllBytes(newFile));
    }
}
