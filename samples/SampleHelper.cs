using System.Linq;
using System.Reflection;

internal static class SampleHelper
{
    public static string GetReleasesDir() => Assembly.GetEntryAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Where(x => x.Key == "VelopackSampleReleaseDir")
        .Single().Value;
}