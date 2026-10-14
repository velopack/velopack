using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Velopack.Tests;

public class LibraryCompatibilityTests
{
    [Fact]
    public void LibraryDoesNotReferenceValueTuple()
    {
        // System.ValueTuple is not part of every .NET Framework version Velopack may be loaded on, and a missing
        // System.ValueTuple.dll crashes the app inside VelopackApp.Run(). Scan type references rather than reflecting
        // over members so tuples that only appear in method bodies (locals, deconstruction) are also caught.
        var assemblyPath = typeof(UpdateManager).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var tupleReferences = metadata.TypeReferences
            .Select(h => metadata.GetTypeReference(h))
            .Where(t => metadata.GetString(t.Namespace) == "System" && metadata.GetString(t.Name).StartsWith("ValueTuple"))
            .Select(t => metadata.GetString(t.Name))
            .ToArray();

        Assert.Empty(tupleReferences);
    }
}
