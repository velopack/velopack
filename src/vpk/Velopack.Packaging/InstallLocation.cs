namespace Velopack.Packaging;

[Flags]
public enum InstallLocation
{
    None = 0,
    PerUser = 1 << 0,
    PerMachine = 1 << 1,
    Either = PerUser | PerMachine,
}