#nullable enable
using Velopack.Packaging.Flow;

namespace Velopack.Vpk.Commands.Flow;

public sealed class ApiOptions : VelopackServiceOptions
{
    public string Endpoint { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Body { get; set; }
}