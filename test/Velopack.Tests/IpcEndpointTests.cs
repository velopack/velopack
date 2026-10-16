using System.Runtime.InteropServices;
using Velopack.Ipc;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Tests;

public class IpcEndpointTests
{
    [Fact]
    public void DeriveKeyGoldenVectors()
    {
        // identical asserts live in the Rust ipc tests: both languages must derive the same endpoint
        Assert.Equal("8785120f64c79b6ab6fe628e", IpcEndpoint.DeriveKey("MyCoolApp", @"c:\users\me\appdata\local\mycoolapp", "S-1-5-21-1-2-3-1001"));
        Assert.Equal("92693b0765d0a044649c36a8", IpcEndpoint.DeriveKey("MyCoolApp", "/Applications/My Cool App.app", "501"));
        Assert.Equal("e8c1d0ef1783a52f5b53b6de", IpcEndpoint.DeriveKey("\u00DCnicode", "/Users/j\u00F6rg/Applications/x.app", "502"));
    }

    [Theory]
    [InlineData(@"C:\Users\Me\AppData\Local\MyCoolApp\")]
    [InlineData(@"\\?\C:\Users\Me\AppData\Local\MyCoolApp")]
    [InlineData("C:/Users/Me/AppData/Local/MyCoolApp")]
    public void NormalizeRootWindowsVectors(string input)
    {
        Assert.Equal(@"c:\users\me\appdata\local\mycoolapp", IpcEndpoint.NormalizeRootWindows(input));
    }

    [Fact]
    public void NormalizeRootWindowsUncAndNonAscii()
    {
        Assert.Equal(@"\\server\share\app", IpcEndpoint.NormalizeRootWindows(@"\\?\UNC\Server\Share\App"));
        // only ASCII letters are lowered; the final-path step already fixed the on-disk case of other segments
        Assert.Equal("c:\\pr\u00F6gram\\\u00C4pp", IpcEndpoint.NormalizeRootWindows("C:\\Pr\u00F6gram\\\u00C4PP"));
    }

    [Fact]
    public void TrimRootUnixVector()
    {
        Assert.Equal("/Applications/My App.app", IpcEndpoint.TrimRootUnix("/Applications/My App.app/"));
        Assert.Equal("/", IpcEndpoint.TrimRootUnix("/"));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, [Out] char[] shortPath, uint bufferLength);

    [Fact]
    public void ShortNameAndLongNameDeriveTheSameKey()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "8.3 short names are a Windows feature");
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var longPath = Path.Combine(tempPath, "velopack ipc long name test");
        Directory.CreateDirectory(longPath);

        var buffer = new char[1024];
        var length = GetShortPathNameW(longPath, buffer, (uint) buffer.Length);
        Assert.True(length > 0 && length < buffer.Length);
        var shortPath = new string(buffer, 0, (int) length);
        Assert.SkipWhen(shortPath == longPath, "the volume has no 8.3 names");

        Assert.Equal(IpcEndpoint.NormalizedRoot(longPath), IpcEndpoint.NormalizedRoot(shortPath));
        // a differently-cased spelling resolves to the on-disk name too
        Assert.Equal(IpcEndpoint.NormalizedRoot(longPath), IpcEndpoint.NormalizedRoot(longPath.ToUpperInvariant()));
        Assert.Equal(IpcEndpoint.NormalizeRootWindows(longPath), IpcEndpoint.NormalizedRoot(longPath));
    }

    [Fact]
    public void ForAppIsStableAndRejectsEmptyInput()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var log = NullVelopackLogger.Instance;
        var a = IpcEndpoint.ForApp("MyCoolApp", tempPath, log);
        var b = IpcEndpoint.ForApp("MyCoolApp", tempPath + Path.DirectorySeparatorChar, log);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a.Key, b.Key);
        Assert.Equal(a.Address, b.Address);
        Assert.Equal(a.LockPath, b.LockPath);
        Assert.Equal(24, a.Key.Length);
        Assert.Null(IpcEndpoint.ForApp("", tempPath, log));
        Assert.Null(IpcEndpoint.ForApp("MyCoolApp", "", log));

        if (VelopackRuntimeInfo.IsWindows) {
            Assert.Equal(@"\\.\pipe\velopack-" + a.Key, a.Address);
            Assert.EndsWith(Path.Combine("velopack", "ipc", a.Key + ".lock"), a.LockPath);
        } else {
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(a.Address) <= 103, a.Address);
            Assert.Equal(Path.GetDirectoryName(a.Address), Path.GetDirectoryName(a.LockPath));
            Assert.StartsWith("velopack-", Path.GetFileName(Path.GetDirectoryName(a.Address)!));
        }
    }

    [Fact]
    public void FromKeyValidatesTheKey()
    {
        var log = NullVelopackLogger.Instance;
        Assert.Null(IpcEndpoint.FromKey("", log));
        Assert.Null(IpcEndpoint.FromKey("ABC", log));
        Assert.Null(IpcEndpoint.FromKey("a b", log));
        Assert.Null(IpcEndpoint.FromKey(new string('a', 41), log));
        var ep = IpcEndpoint.FromKey("abc-123", log);
        Assert.NotNull(ep);
        Assert.Equal("abc-123", ep.Key);
        Assert.Contains("velopack-", ep.Address);
        Assert.EndsWith("abc-123.lock", ep.LockPath);
    }

    [Fact]
    public void SocketBaseDirIsTheSameRuleAsRust()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "Unix domain sockets only");
        if (VelopackRuntimeInfo.IsOSX) {
            // always the per-user temporary directory, whatever TMPDIR says
            var dir = IpcEndpoint.SocketBaseDir(null);
            Assert.Contains("/var/folders/", dir);
            Assert.False(dir.EndsWith("/"));
            Assert.Equal(dir, IpcEndpoint.SocketBaseDir("/elsewhere/"));
            Assert.Equal(dir, IpcEndpoint.SocketBaseDir(""));
        } else {
            Assert.Equal("/tmp", IpcEndpoint.SocketBaseDir(null));
            Assert.Equal("/tmp", IpcEndpoint.SocketBaseDir(""));
            Assert.Equal("/run/user/1000/tmp", IpcEndpoint.SocketBaseDir("/run/user/1000/tmp/"));
        }
    }

    [Fact]
    public void PrivateDirIsCreatedWithMode0700()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "Unix permissions only");
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var dir = Path.Combine(tempPath, "private");
        IpcEndpoint.EnsurePrivateDir(dir);
        IpcEndpoint.EnsurePrivateDir(dir);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));

        var link = Path.Combine(tempPath, "link");
        File.CreateSymbolicLink(link, dir);
        Assert.Throws<IOException>(() => IpcEndpoint.EnsurePrivateDir(link));
    }
}
