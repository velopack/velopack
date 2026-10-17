#nullable enable
namespace Velopack.Packaging.Chunking;

/// <summary>
/// The 256-entry gear table of FastCDC-gear-v1 (delta-v2 spec §3). It is generated deterministically by SplitMix64
/// from a fixed seed so that every vpk build (on every runtime) cuts identical chunk boundaries; the frozen values
/// are pinned by <c>FastCdcTests.GearTableGolden</c>.
/// </summary>
public static class GearTable
{
    /// <summary>"VELOPACK" as big-endian ASCII.</summary>
    public const ulong Seed = 0x56454C4F5041434B;

    public static readonly ulong[] Table = Generate();

    /// <summary>Returns the next SplitMix64 output and advances the state.</summary>
    public static ulong SplitMix64Next(ref ulong state)
    {
        unchecked {
            state += 0x9E3779B97F4A7C15;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }

    private static ulong[] Generate()
    {
        var table = new ulong[256];
        ulong state = Seed;
        for (int i = 0; i < table.Length; i++) {
            table[i] = SplitMix64Next(ref state);
        }

        return table;
    }
}
