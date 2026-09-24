using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Unix;

/// <summary>
/// Writes the macOS portable package, a zip of the .app bundle that Finder expands with a double-click, without
/// <c>ditto</c>, so it can be built on Linux.
///
/// What ditto gets right and a plain zip does not is Unix metadata: an executable bit on the main executable and on
/// UpdateMac (Finder refuses to open a bundle whose executable is not executable), and symlinks kept AS symlinks
/// (Velopack's own Contents/MacOS/sq.version is one, and frameworks are full of them). Both live in the high 16 bits
/// of each entry's external attributes, which Archive Utility honours when the zip says it was made on Unix. .NET's
/// ZipArchive records the platform it runs on, which is why this is only offered off Windows.
///
/// Resource forks and extended attributes (ditto's --rsrc / --sequesterRsrc) are not carried: a Linux filesystem has
/// neither to give, and a bundle that depends on them cannot have been built there.
/// </summary>
public static class OsxPortableZip
{
    private const int S_IFDIR = 0x4000;
    private const int S_IFREG = 0x8000;
    private const int S_IFLNK = 0xA000;

    /// <summary>
    /// Zips <paramref name="bundlePath"/> into <paramref name="outputZip"/> with the bundle itself as the single top-level
    /// entry, as `ditto -c -k --keepParent` does.
    /// </summary>
    public static void Create(ILogger log, string bundlePath, string outputZip)
    {
        if (OperatingSystem.IsWindows()) {
            throw new PlatformNotSupportedException("A macOS portable zip cannot carry Unix file modes when built on Windows.");
        }

        if (File.Exists(outputZip)) File.Delete(outputZip);

        var root = new DirectoryInfo(bundlePath);
        var prefix = root.Name + "/";
        log.LogDebug("Creating portable zip '{Output}' from '{Bundle}'", outputZip, bundlePath);

        using var stream = File.Create(outputZip);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        AddDirectory(zip, root, prefix);
    }

    private static void AddDirectory(ZipArchive zip, DirectoryInfo dir, string entryPrefix)
    {
        var dirEntry = zip.CreateEntry(entryPrefix, CompressionLevel.NoCompression);
        dirEntry.ExternalAttributes = (S_IFDIR | ModeOf(dir)) << 16;

        foreach (var info in dir.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal)) {
            var name = entryPrefix + info.Name;

            // A symlink, to a file or a directory, is stored as a link: its target path is the entry's content, and
            // S_IFLNK in its mode is what tells the unzipper so. Never followed, or a framework's Versions/Current
            // would be zipped twice and expand as a copy.
            if (info.LinkTarget != null) {
                var link = zip.CreateEntry(name, CompressionLevel.NoCompression);
                link.ExternalAttributes = (S_IFLNK | 0x1FF) << 16;
                using var writer = new StreamWriter(link.Open());
                writer.Write(info.LinkTarget);
                continue;
            }

            if (info is DirectoryInfo sub) {
                AddDirectory(zip, sub, name + "/");
                continue;
            }

            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.ExternalAttributes = (S_IFREG | ModeOf(info)) << 16;
            entry.LastWriteTime = info.LastWriteTime;
            using var input = File.OpenRead(info.FullName);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static int ModeOf(FileSystemInfo info) => (int) info.UnixFileMode & 0xFFF;
}
