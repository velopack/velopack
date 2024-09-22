﻿using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Velopack.Util;
using Velopack.Windows;

namespace Velopack.Tests;

public class UtilityTests
{
    private readonly ITestOutputHelper _output;

    public UtilityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableTheory]
    [InlineData("file.txt", "file.txt")]
    [InlineData("file", "file")]
    [InlineData("/file", "\\file")]
    [InlineData("/file/", "\\file")]
    [InlineData("one\\two\\..\\file", "one\\file")]
    [InlineData("C:/AnApp/file/", "C:\\AnApp\\file")]
    public void PathIsNormalized(string input, string expected)
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        var exp = Path.GetFullPath(expected);
        var normal = PathUtil.NormalizePath(input);
        Assert.Equal(exp, normal);
    }

    [SkippableTheory]
    [InlineData("C:\\AnApp", "C:\\AnApp\\file.exe", true)]
    [InlineData("C:\\AnApp\\", "C:\\AnApp\\file.exe", true)]
    [InlineData("C:\\AnApp", "C:\\AnApp\\sub\\dir\\file.exe", true)]
    [InlineData("C:\\AnApp\\", "C:\\AnApp\\sub\\dir\\file.exe", true)]
    [InlineData("C:\\AnAppTwo", "C:\\AnApp\\file.exe", false)]
    [InlineData("C:\\AnAppTwo\\", "C:\\AnApp\\file.exe", false)]
    [InlineData("C:\\AnAppTwo", "C:\\AnApp\\sub\\dir\\file.exe", false)]
    [InlineData("C:\\AnAppTwo\\", "C:\\AnApp\\sub\\dir\\file.exe", false)]
    [InlineData("AnAppThree", "AnAppThree\\file.exe", true)]
    public void FileIsInDirectory(string directory, string file, bool isIn)
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        var fileInDir = PathUtil.IsFileInDirectory(file, directory);
        Assert.Equal(isIn, fileInDir);
    }

    [SkippableFact]
    public void SetAppIdOnShortcutTest()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        var sl = new ShellLink() {
            Target = @"C:\Windows\Notepad.exe",
            Description = "It's Notepad",
        };

        sl.SetAppUserModelId("org.anaïsbetts.test");
        var path = Path.GetFullPath(@".\test.lnk");
        sl.Save(path);

        Console.WriteLine("Saved to " + path);
    }

    [Fact]
    public void RemoveByteOrderMarkerIfPresent()
    {
        var utf32Be = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
        var utf32Le = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
        var utf16Be = new byte[] { 0xFE, 0xFF };
        var utf16Le = new byte[] { 0xFF, 0xFE };
        var utf8 = new byte[] { 0xEF, 0xBB, 0xBF };

        var utf32BeHelloWorld = combine(utf32Be, Encoding.UTF8.GetBytes("hello world"));
        var utf32LeHelloWorld = combine(utf32Le, Encoding.UTF8.GetBytes("hello world"));
        var utf16BeHelloWorld = combine(utf16Be, Encoding.UTF8.GetBytes("hello world"));
        var utf16LeHelloWorld = combine(utf16Le, Encoding.UTF8.GetBytes("hello world"));
        var utf8HelloWorld = combine(utf8, Encoding.UTF8.GetBytes("hello world"));

        var asciiMultipleChars = Encoding.ASCII.GetBytes("hello world");
        var asciiSingleChar = Encoding.ASCII.GetBytes("A");

        var emptyString = string.Empty;
        string nullString = null;
        byte[] nullByteArray = { };
        Assert.Equal(string.Empty, CoreUtil.RemoveByteOrderMarkerIfPresent(emptyString));
        Assert.Equal(string.Empty, CoreUtil.RemoveByteOrderMarkerIfPresent(nullString));
        Assert.Equal(string.Empty, CoreUtil.RemoveByteOrderMarkerIfPresent(nullByteArray));

        Assert.Equal(string.Empty, CoreUtil.RemoveByteOrderMarkerIfPresent(utf32Be));
        Assert.Equal(string.Empty, CoreUtil.RemoveByteOrderMarkerIfPresent(utf32Le));
        Assert.Equal(string.Empty, CoreUtil.RemoveByteOrderMarkerIfPresent(utf16Be));
        Assert.Equal(string.Empty, CoreUtil.RemoveByteOrderMarkerIfPresent(utf16Le));
        Assert.Equal(string.Empty, CoreUtil.RemoveByteOrderMarkerIfPresent(utf8));

        Assert.Equal("hello world", CoreUtil.RemoveByteOrderMarkerIfPresent(utf32BeHelloWorld));
        Assert.Equal("hello world", CoreUtil.RemoveByteOrderMarkerIfPresent(utf32LeHelloWorld));
        Assert.Equal("hello world", CoreUtil.RemoveByteOrderMarkerIfPresent(utf16BeHelloWorld));
        Assert.Equal("hello world", CoreUtil.RemoveByteOrderMarkerIfPresent(utf16LeHelloWorld));
        Assert.Equal("hello world", CoreUtil.RemoveByteOrderMarkerIfPresent(utf8HelloWorld));

        Assert.Equal("hello world", CoreUtil.RemoveByteOrderMarkerIfPresent(asciiMultipleChars));
        Assert.Equal("A", CoreUtil.RemoveByteOrderMarkerIfPresent(asciiSingleChar));
    }

    [Fact]
    public void ShaCheckShouldBeCaseInsensitive()
    {
        var sha1FromExternalTool = "75255cfd229a1ed1447abe1104f5635e69975d30";
        var inputPackage = PathHelper.GetFixture("Squirrel.Core.1.0.0.0.nupkg");
        var stream = File.OpenRead(inputPackage);
        var sha1 = IoUtil.CalculateStreamSHA1(stream);

        Assert.NotEqual(sha1FromExternalTool, sha1);
        Assert.Equal(sha1FromExternalTool, sha1, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanDeleteDeepRecursiveDirectoryStructure()
    {
        using var logger = _output.BuildLoggerFor<UtilityTests>();
        string tempDir;
        using (TempUtil.GetTempDirectory(out tempDir)) {
            for (var i = 0; i < 50; i++) {
                var directory = Path.Combine(tempDir, newId());
                CreateSampleDirectory(directory);
            }

            var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);

            var count = files.Count();

            logger.Info($"Created {count} file(s) under directory {tempDir}");

            var sw = new Stopwatch();
            sw.Start();
            IoUtil.DeleteFileOrDirectoryHard(tempDir);
            sw.Stop();
            logger.Info($"Delete took {sw.ElapsedMilliseconds}ms");

            Assert.False(Directory.Exists(tempDir));
        }
    }

    //[Fact]
    //public void CreateFakePackageSmokeTest()
    //{
    //    string path;
    //    using (TempUtil.GetTempDirectory(out path)) {
    //        var output = IntegrationTestHelper.CreateFakeInstalledApp("0.3.0", path);
    //        Assert.True(File.Exists(output));
    //    }
    //}

    [Theory]
    [InlineData("foo.dll", true)]
    [InlineData("foo.DlL", true)]
    [InlineData("C:\\Foo\\Bar\\foo.Exe", true)]
    [InlineData("Test.png", false)]
    [InlineData(".rels", false)]
    public void FileIsLikelyPEImageTest(string input, bool result)
    {
        Assert.Equal(result, PathUtil.FileIsLikelyPEImage(input));
    }

    [Fact(Skip = "Only really need to run this test after changes to FileDownloader")]
    public async Task DownloaderReportsProgress()
    {
        // this probably should use a local http server instead.
        const string testUrl = "http://speedtest.tele2.net/1MB.zip";

        var dl = HttpUtil.CreateDefaultDownloader();

        List<int> prog = new List<int>();
        using (TempUtil.GetTempFileName(out var tempPath))
            await dl.DownloadFile(testUrl, tempPath, prog.Add);

        Assert.True(prog.Count > 10);
        Assert.Equal(100, prog.Last());
        Assert.True(prog[1] != 0);
    }

    static void CreateSampleDirectory(string directory)
    {
        Random prng = new Random();
        while (true) {
            Directory.CreateDirectory(directory);

            for (var j = 0; j < 100; j++) {
                var file = Path.Combine(directory, newId());
                if (file.Length > 260) continue;
                File.WriteAllText(file, Guid.NewGuid().ToString());
            }

            if (prng.NextDouble() > 0.5) {
                var childDirectory = Path.Combine(directory, newId());
                if (childDirectory.Length > 248) return;
                directory = childDirectory;
                continue;
            }

            break;
        }
    }

    static string newId()
    {
        var text = Guid.NewGuid().ToString();
        var bytes = Encoding.Unicode.GetBytes(text);
        var provider = SHA1.Create();
        var hashString = string.Empty;

        foreach (var x in provider.ComputeHash(bytes)) {
            hashString += String.Format("{0:x2}", x);
        }

        if (hashString.Length > 7) {
            return hashString.Substring(0, 7);
        }

        return hashString;
    }

    static byte[] combine(params byte[][] arrays)
    {
        var rv = new byte[arrays.Sum(a => a.Length)];
        var offset = 0;
        foreach (var array in arrays) {
            Buffer.BlockCopy(array, 0, rv, offset, array.Length);
            offset += array.Length;
        }
        return rv;
    }

}
