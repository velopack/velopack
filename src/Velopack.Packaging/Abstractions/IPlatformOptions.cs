namespace Velopack.Packaging.Abstractions;

public interface IPlatformOptions : IOutputOptions
{
    RID TargetRuntime { get; }
}
