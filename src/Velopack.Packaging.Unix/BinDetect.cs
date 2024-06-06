namespace Velopack.Packaging;

public class BinDetect
{
    private enum MagicMachO : uint
    {
        MH_MAGIC = 0xfeedface,
        MH_CIGAM = 0xcefaedfe,
        MH_MAGIC_64 = 0xfeedfacf,
        MH_CIGAM_64 = 0xcffaedfe
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

    // First four bytes of valid ELF, as defined in https://github.com/torvalds/linux/blob/aae703b/include/uapi/linux/elf.h
    //    0x7f (DEL), 'E', 'L', 'F'
    private static ReadOnlySpan<byte> ElfMagic => "\u007f"u8 + "ELF"u8;

    public static bool IsElfImage(string filePath)
    {
        using FileStream fileStream = File.OpenRead(filePath);
        using BinaryReader reader = new(fileStream);

        if (reader.BaseStream.Length < 16) // EI_NIDENT = 16
        {
            return false;
        }

        byte[] eIdent = reader.ReadBytes(4);

        return
            eIdent[0] == ElfMagic[0] &&
            eIdent[1] == ElfMagic[1] &&
            eIdent[2] == ElfMagic[2] &&
            eIdent[3] == ElfMagic[3];
    }
}