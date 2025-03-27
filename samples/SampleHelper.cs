using System.Linq;
using System.Reflection;

#nullable enable
internal static class SampleHelper
{
    public static string? GetReleasesDir() => Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Where(x => x.Key == "VelopackSampleReleaseDir")
        .Single().Value;
}
#nullable restore