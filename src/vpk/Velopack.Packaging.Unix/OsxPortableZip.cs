using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

/// <summary>
/// Writes the macOS portable zip (the .app bundle, as `ditto -c -k --keepParent` would) without ditto, so it can be
/// built on Linux and Windows. Finder only launches the bundle if the zip carries Unix modes (an executable main binary)
/// and keeps symlinks as links; both live in the high 16 bits of the external attributes, and unzippers only apply
/// them when the entry is marked as made on Unix. Off Windows the modes are read from disk, masked to 0755 since
/// filesystems without real modes report 0777. Windows has none to read, so they are derived: 0755 for directories,
/// Contents/MacOS, Mach-O images and #! scripts, 0644 for anything else.
/// </summary>
public static class OsxPortableZip
{
    private const int S_IFDIR = 0x4000;
    private const int S_IFREG = 0x8000;
    private const int S_IFLNK = 0xA000;
    private const int Mode777 = 0x1FF; // rwxrwxrwx, the mode every symlink has
    private const int Mode755 = 0x1ED; // rwxr-xr-x
    private const int Mode644 = 0x1A4; // rw-r--r--

    // Inside the 1980-2107 range a zip entry can record, which a file's own mtime need not be (SOURCE_DATE_EPOCH=0).
    private static readonly DateTimeOffset EntryTimestamp = EasyZip.ZipFormatMinDate;

    /// <summary>
    /// Zips <paramref name="bundlePath"/> into <paramref name="outputZip"/> with the bundle itself as the single top-level
    /// entry, as `ditto -c -k --keepParent` does. Nothing is left at <paramref name="outputZip"/> if this throws.
    /// </summary>
    public static void Create(ILogger log, string bundlePath, string outputZip)
    {
        var bundle = new DirectoryInfo(Path.GetFullPath(bundlePath));
        log.LogDebug("Creating portable zip '{Output}' from '{Bundle}'", outputZip, bundle.FullName);

        try {
            using var stream = File.Create(outputZip);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
                AddDirectory(zip, bundle, bundle, "");
            }

            MarkMadeOnUnix(stream);
        } catch {
            try { File.Delete(outputZip); } catch { }

            throw;
        }
    }

    /// <param name="relativePath">Where <paramref name="dir"/> is inside the bundle: empty, or ending in '/'.</param>
    private static void AddDirectory(ZipArchive zip, DirectoryInfo bundle, DirectoryInfo dir, string relativePath)
    {
        AddEntry(zip, bundle.Name + "/" + relativePath, S_IFDIR | DirectoryModeOf(dir), CompressionLevel.NoCompression);

        foreach (var info in dir.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal)) {
            var path = relativePath + info.Name;
            var name = bundle.Name + "/" + path;

            // Stored as a link (its target as content, S_IFLNK in its mode), never followed, or a framework's
            // Versions/Current would expand as a copy. Resolved before the entry exists, so a refusal leaves no half entry.
            if (info.LinkTarget is { } target) {
                var linkTarget = RelativeLinkTarget(bundle, info, path, target);
                var link = AddEntry(zip, name, S_IFLNK | Mode777, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(link.Open());
                writer.Write(linkTarget);
                continue;
            }

            if (info is DirectoryInfo sub) {
                AddDirectory(zip, bundle, sub, path + "/");
                continue;
            }

            // One open per file: on Windows its mode comes from its first bytes, read from the stream that is copied.
            using var input = File.OpenRead(info.FullName);
            var entry = AddEntry(zip, name, S_IFREG | FileModeOf((FileInfo) info, input, path), CompressionLevel.Optimal);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static ZipArchiveEntry AddEntry(ZipArchive zip, string name, int unixMode, CompressionLevel compression)
    {
        var entry = zip.CreateEntry(name, compression);
        entry.ExternalAttributes = unixMode << 16;
        entry.LastWriteTime = EntryTimestamp;
        return entry;
    }

    /// <summary>
    /// The link's target relative to the link itself, with forward slashes. Windows stores relative targets with
    /// backslashes, and either OS may store an absolute one, which would point at the build machine once on the Mac.
    /// A target outside the bundle cannot be packed, as EasyZip refuses it in the nupkg.
    /// </summary>
    private static string RelativeLinkTarget(DirectoryInfo bundle, FileSystemInfo link, string path, string target)
    {
        var linkDir = Path.GetDirectoryName(link.FullName)!;
        var resolved = Path.GetFullPath(Path.Combine(linkDir, target));

        var fromBundle = Path.GetRelativePath(bundle.FullName, resolved);
        if (fromBundle == ".." || fromBundle.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(fromBundle)) {
            throw new UserInfoException(
                $"Symlink '{bundle.Name}/{path}' points to '{target}', outside the bundle. Only links within the .app can be packed.");
        }

        return Path.GetRelativePath(linkDir, resolved).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static int DirectoryModeOf(DirectoryInfo dir)
    {
        if (VelopackRuntimeInfo.IsWindows) {
            return Mode755;
        }

        return (int) dir.UnixFileMode & Mode755;
    }

    /// <param name="content">The file opened for reading at its start, and left there.</param>
    /// <param name="relativePath">Where <paramref name="file"/> is inside the bundle.</param>
    private static int FileModeOf(FileInfo file, FileStream content, string relativePath)
    {
        if (!VelopackRuntimeInfo.IsWindows) {
            return (int) file.UnixFileMode & Mode755;
        }

        // The bundle's own Contents/MacOS, or that of a bundle nested in it: a helper app in Contents/Frameworks, an
        // XPC service, a plug-in or a login item each has its own.
        if (("/" + relativePath).Contains("/Contents/MacOS/", StringComparison.OrdinalIgnoreCase)) {
            return Mode755;
        }

        Span<byte> buffer = stackalloc byte[4];
        var header = buffer[..content.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false)];
        content.Position = 0;

        // The same 256-byte minimum as BinDetect.IsMachOImage, which would open the file again.
        var machO = content.Length >= 256 && BinDetect.HasMachOMagic(header);
        var script = header.StartsWith("#!"u8);
        return machO || script ? Mode755 : Mode644;
    }

    /// <summary>
    /// Sets the host byte of every central-directory entry's "version made by" to 3 (Unix), so unzippers apply the
    /// modes in the external attributes. .NET writes the platform it runs on there and has no setting for it.
    /// <paramref name="zip"/> is patched in place and must be seekable and hold a zip as <see cref="ZipArchive"/>
    /// writes one: no archive comment, and Zip64 records past 65534 entries or 4GB.
    /// </summary>
    public static void MarkMadeOnUnix(Stream zip)
    {
        const uint EndOfCentralDirectory = 0x06054b50;
        const uint Zip64EndOfCentralDirectoryLocator = 0x07064b50;
        const uint Zip64EndOfCentralDirectory = 0x06064b50;
        const uint CentralDirectoryEntry = 0x02014b50;
        const byte UnixHost = 3;

        using var reader = new BinaryReader(zip, Encoding.UTF8, leaveOpen: true);

        ushort UInt16At(long position)
        {
            zip.Position = position;
            return reader.ReadUInt16();
        }

        uint UInt32At(long position)
        {
            zip.Position = position;
            return reader.ReadUInt32();
        }

        long Int64At(long position)
        {
            zip.Position = position;
            return checked((long) reader.ReadUInt64());
        }

        // With no archive comment, the end record is the last 22 bytes.
        long end = zip.Length - 22;
        if (end < 0 || UInt32At(end) != EndOfCentralDirectory) {
            throw new InvalidDataException("No end of central directory record at the end of the zip.");
        }

        long entries = UInt16At(end + 10);
        long offset = UInt32At(end + 16);

        // Past 65534 entries or 4GB those fields are saturated, and the real count and offset are in the Zip64 end
        // record, found through the locator right before the end record. The entries themselves are laid out the same.
        long locator = end - 20;
        if (locator >= 0 && UInt32At(locator) == Zip64EndOfCentralDirectoryLocator) {
            long record = Int64At(locator + 8);
            if (UInt32At(record) != Zip64EndOfCentralDirectory) {
                throw new InvalidDataException($"No Zip64 end of central directory record at {record}, where its locator points.");
            }

            entries = Int64At(record + 32);
            offset = Int64At(record + 48);
        }

        for (long i = 0; i < entries; i++) {
            if (UInt32At(offset) != CentralDirectoryEntry) {
                throw new InvalidDataException($"Unexpected record in the central directory at {offset}.");
            }

            zip.Position = offset + 5; // high byte of "version made by"
            zip.WriteByte(UnixHost);

            int nameLength = UInt16At(offset + 28);
            int extraLength = reader.ReadUInt16();
            int commentLength = reader.ReadUInt16();
            offset += 46 + nameLength + extraLength + commentLength;
        }
    }
}
