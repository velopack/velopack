#if NET6_0_OR_GREATER
#else
using System.Net.Http;
#endif

#nullable enable
namespace Velopack.Packaging.Flow;

internal sealed class ReleaseGroup
{
    public Guid Id { get; set; }
    public string? Version { get; set; }
}