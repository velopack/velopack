using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Unix;

/// <summary>
/// Writes the macOS portable package, a zip of the .app bundle that Finder expands with a double-click, without
/// <c>ditto</c>, so it can be built on Linux and Windows.
///
/// What ditto gets right and a plain zip does not is Unix metadata: an executable bit on the main executable and on
/// UpdateMac (Finder refuses to open a bundle whose executable is not executable), and symlinks kept AS symlinks
/// (Velopack's own Contents/MacOS/sq.version is one, and frameworks are full of them). Both live in the high 16 bits
/// of each entry's external attributes, which Archive Utility and unzip only honour when the entry says it was made
/// on Unix.
///
/// On Linux the modes are read from the filesystem. On Windows there are none to read, so they are derived from the
/// content, the way the macOS updater already treats files it extracts: directories and Mach-O images 0755, anything
/// else 0644. Symlinks are symlinks on every platform, with their targets written with forward slashes.
///
/// Every entry is then marked as made on Unix (<see cref="MarkMadeOnUnix"/>), because .NET records the platform it
/// runs on and has no setting for it. The result is the same zip whichever OS wrote it.
///
/// Resource forks and extended attributes (ditto's --rsrc / --sequesterRsrc) are not carried: neither Linux nor Windows
/// has them to give, and a bundle that depends on them cannot have been built there.
/// </summary>
public static class OsxPortableZip
{
    private const int S_IFDIR = 0x4000;
    private const int S_IFREG = 0x8000;
    private const int S_IFLNK = 0xA000;
    private const int Mode755 = 0x1ED;
    private const int Mode644 = 0x1A4;

    /// <summary>
    /// Zips <paramref name="bundlePath"/> into <paramref name="outputZip"/> with the bundle itself as the single top-level
    /// entry, as `ditto -c -k --keepParent` does.
    /// </summary>
    public static void Create(ILogger log, string bundlePath, string outputZip)
    {
        if (File.Exists(outputZip)) File.Delete(outputZip);

        var root = new DirectoryInfo(bundlePath);
        var prefix = root.Name + "/";
        log.LogDebug("Creating portable zip '{Output}' from '{Bundle}'", outputZip, bundlePath);

        using (var stream = File.Create(outputZip))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create)) {
            AddDirectory(zip, root, prefix);
        }

        MarkMadeOnUnix(outputZip);
    }

    private static void AddDirectory(ZipArchive zip, DirectoryInfo dir, string entryPrefix)
    {
        var dirEntry = zip.CreateEntry(entryPrefix, CompressionLevel.NoCompression);
        dirEntry.ExternalAttributes = (S_IFDIR | DirectoryModeOf(dir)) << 16;

        foreach (var info in dir.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal)) {
            var name = entryPrefix + info.Name;

            // A symlink, to a file or a directory, is stored as a link: its target path is the entry's content, and
            // S_IFLNK in its mode is what tells the unzipper so. Never followed, or a framework's Versions/Current
            // would be zipped twice and expand as a copy.
            if (info.LinkTarget != null) {
                var link = zip.CreateEntry(name, CompressionLevel.NoCompression);
                link.ExternalAttributes = (S_IFLNK | 0x1FF) << 16;
                using var writer = new StreamWriter(link.Open());
                writer.Write(info.LinkTarget.Replace('\\', '/'));
                continue;
            }

            if (info is DirectoryInfo sub) {
                AddDirectory(zip, sub, name + "/");
                continue;
            }

            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.ExternalAttributes = (S_IFREG | UnixModeOf(info)) << 16;
            entry.LastWriteTime = info.LastWriteTime;
            using var input = File.OpenRead(info.FullName);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static int DirectoryModeOf(FileSystemInfo dir) =>
        OperatingSystem.IsWindows() ? Mode755 : (int) dir.UnixFileMode & 0xFFF;

    private static int UnixModeOf(FileSystemInfo file)
    {
        if (!OperatingSystem.IsWindows()) {
            return (int) file.UnixFileMode & 0xFFF;
        }

        return BinDetect.IsMachOImage(file.FullName) ? Mode755 : Mode644;
    }

    /// <summary>
    /// Sets the host byte of every central-directory entry's "version made by" to 3 (Unix), which is what makes
    /// unzippers apply the modes in the external attributes. .NET writes the platform it is running on there and has
    /// no way to choose, so a zip written on Windows would say Windows and have its modes ignored. A byte per entry,
    /// patched in place.
    /// </summary>
    internal static void MarkMadeOnUnix(string zipPath)
    {
        const uint EndOfCentralDirectory = 0x06054b50;
        const uint CentralDirectoryEntry = 0x02014b50;
        const byte UnixHost = 3;

        using var file = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite);
        using var reader = new BinaryReader(file);

        // The end record is 22 bytes plus a comment of up to 64KB; ours has none, but search back as the spec allows.
        long end = -1;
        for (long pos = file.Length - 22; pos >= Math.Max(0, file.Length - 22 - 0xFFFF); pos--) {
            file.Position = pos;
            if (reader.ReadUInt32() == EndOfCentralDirectory) {
                end = pos;
                break;
            }
        }

        if (end < 0) {
            throw new InvalidDataException("Not a zip: no end of central directory record in " + zipPath);
        }

        file.Position = end + 10;
        int entries = reader.ReadUInt16();
        file.Position = end + 16;
        long offset = reader.ReadUInt32();
        if (entries == 0xFFFF || offset == 0xFFFFFFFF) {
            // Zip64: past 65535 entries or 4GB. Not something a portable app bundle reaches; refused rather than
            // half-patched.
            throw new NotSupportedException("The portable zip needs Zip64, which marking it as made on Unix does not support.");
        }

        for (int i = 0; i < entries; i++) {
            file.Position = offset;
            if (reader.ReadUInt32() != CentralDirectoryEntry) {
                throw new InvalidDataException($"Unexpected record in the central directory of {zipPath} at {offset}.");
            }

            file.Position = offset + 5; // high byte of "version made by"
            file.WriteByte(UnixHost);

            file.Position = offset + 28;
            int nameLength = reader.ReadUInt16();
            int extraLength = reader.ReadUInt16();
            int commentLength = reader.ReadUInt16();
            offset += 46 + nameLength + extraLength + commentLength;
        }
    }
}
