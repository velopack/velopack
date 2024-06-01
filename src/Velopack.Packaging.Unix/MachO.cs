namespace Velopack.Packaging;

public class MachO
{
    private enum MagicMachO : uint
    {
        MH_MAGIC = 0xfeedface,
        MH_CIGAM = 0xcefaedfe,
        MH_MAGIC_64 = 0xfeedfacf,
        MH_CIGAM_64 = 0xcffaedfe,
        // https://developer.apple.com/documentation/kernel/fat_header/1558632-magic/
        // https://opensource.apple.com/source/file/file-80.40.2/file/magic/Magdir/cafebabe.auto.html
        FAT_MAGIC = 0xcafebabe,
        FAT_CIGAM = 0xbebafeca,
    }

    public static bool IsMachOImage(string filePath)
    {
        using (BinaryReader reader = new BinaryReader(File.OpenRead(filePath))) {
            if (reader.BaseStream.Length < 256) // Header size
                return false;

            uint magic = reader.ReadUInt32();
            return Enum.IsDefined(typeof(MagicMachO), magic);
        }
    }
}