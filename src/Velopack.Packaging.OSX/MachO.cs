namespace Velopack.Packaging.OSX;

public class MachO
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
}