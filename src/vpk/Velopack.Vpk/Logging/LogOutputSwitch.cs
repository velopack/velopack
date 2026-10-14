namespace Velopack.Vpk.Logging;

/// <summary>
/// Selects whether log output is written to stdout (the default) or stderr. It is flipped at runtime,
/// once a command's options are known, for commands which print a machine-readable payload to stdout.
/// </summary>
public sealed class LogOutputSwitch
{
    public bool UseStandardError { get; set; }
}
