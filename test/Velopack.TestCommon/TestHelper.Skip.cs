#nullable enable
using Velopack.Util;

namespace Velopack.TestCommon;

// Also compiled into Velopack.Tests (linked, like PathHelper), which does not reference this project, so keep it
// self-contained: lib-csharp, PathHelper and xunit only.
public static partial class TestHelper
{
    /// <summary>
    /// Skips the test when <paramref name="blocker"/> (what this machine lacks, from one of the Get*Blocker helpers) is
    /// not null, but fails it in CI instead: a developer machine may lack a tool or binary, while the CI workflow provides
    /// all of them, where a skip would hide lost coverage.
    /// </summary>
    public static void SkipLocallyFailInCI(string? blocker)
    {
        if (blocker == null) {
            return;
        }

        if (PathHelper.IsCI) {
            Assert.Fail(blocker);
        }

        Assert.Skip(blocker);
    }

    // Probed once per process: Developer Mode and elevation cannot change while the tests run.
    private static readonly Lazy<string?> SymlinkBlocker = new(ProbeSymlinkBlocker);

    /// <summary>
    /// Returns why this machine cannot create symlinks, or null when it can. Windows creates them only with Developer
    /// Mode or elevation; every other OS always can.
    /// </summary>
    public static string? GetSymlinkBlocker() => SymlinkBlocker.Value;

    private static string? ProbeSymlinkBlocker()
    {
        if (!VelopackRuntimeInfo.IsWindows) {
            return null;
        }

        using var _ = TempUtil.GetTempDirectory(out var dir);
        var target = Path.Combine(dir, "target");
        File.WriteAllText(target, "");
        try {
            SymbolicLink.Create(Path.Combine(dir, "probe"), target);
            return null;
        } catch (UnauthorizedAccessException) {
            return "this Windows machine cannot create symlinks (Developer Mode is off and the process is not elevated)";
        }
    }

    /// <summary>
    /// Skips a test that needs symlinks on a local machine that cannot create them. CI runs elevated, so there a machine
    /// that cannot is a failure.
    /// </summary>
    public static void SkipUnlessSymlinksCanBeCreated() => SkipLocallyFailInCI(GetSymlinkBlocker());
}
