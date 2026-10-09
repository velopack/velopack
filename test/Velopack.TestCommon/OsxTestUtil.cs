#nullable enable
using System.IO.Compression;

namespace Velopack.TestCommon;

/// <summary>
/// Helpers for tests of macOS bundles and the zips they are packed into. Meant for <c>using static</c>.
/// </summary>
public static class OsxTestUtil
{
    // File type bits of a Unix mode, as a zip entry made on Unix carries it in the high 16 bits of its external attributes.
    public const int S_IFMT = 0xF000;
    public const int S_IFDIR = 0x4000;
    public const int S_IFREG = 0x8000;
    public const int S_IFLNK = 0xA000;

    /// <summary>The Unix mode (type and permission bits) stored in a zip entry's external attributes.</summary>
    public static int UnixModeOf(ZipArchiveEntry entry) => (int) ((uint) entry.ExternalAttributes >> 16);

    /// <summary>A zip entry's content as text: a symlink's target, or a small file such as sq.version.</summary>
    public static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Writes a fake 64-bit Mach-O image (an MH_MAGIC_64 header, padded past the 256 bytes BinDetect requires), creating
    /// its directory. On Unix it is made 0755, as a built one would be.
    /// </summary>
    public static void WriteMachO(string path)
    {
        var image = new byte[512];
        new byte[] { 0xCF, 0xFA, 0xED, 0xFE }.CopyTo(image, 0);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, image);
        if (!VelopackRuntimeInfo.IsWindows) {
            File.SetUnixFileMode(path, (UnixFileMode) 0x1ED);
        }
    }
}
