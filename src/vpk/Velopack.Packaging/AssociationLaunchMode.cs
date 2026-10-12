namespace Velopack.Packaging;

/// <summary>
/// What happens when a file association or URL protocol is activated while the app is already running.
/// Stored in the package manifest (<c>associationLaunchMode</c>) and read by the Velopack libraries at startup.
/// </summary>
public enum AssociationLaunchMode
{
    /// <summary>Every activation starts a new process which fires the callback (the default).</summary>
    NewInstance,

    /// <summary>
    /// The first running instance receives the activation over IPC (named pipe / unix socket) on a background
    /// thread, and the launching process exits.
    /// </summary>
    SingleInstance,
}
