using System.Collections.Concurrent;
using Velopack.TestCommon;

namespace Velopack.Deployment.Tests;

/// <summary> A set of packed release versions produced into a single release directory. </summary>
public sealed record PackedRelease(string ReleaseDir, string Id, string[] Versions, string Channel);

/// <summary> Helpers that produce real Velopack packages in-process via <see cref="TestApp.PackTestApp"/>. </summary>
public static class ReleaseFixtures
{
    /// <summary>
    /// Packs each of <paramref name="versions"/> (in order, so later versions produce deltas) into a
    /// single fresh temp release directory. The caller owns the returned directory's lifetime.
    /// </summary>
    public static PackedRelease PackVersions(string id, string channel, ILogger log, params string[] versions)
    {
        if (versions == null || versions.Length == 0)
            throw new ArgumentException("At least one version is required.", nameof(versions));

        var releaseDir = Path.Combine(Path.GetTempPath(), "velopack-packedrelease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(releaseDir);

        for (var i = 0; i < versions.Length; i++) {
            log.LogInformation("Packing {Id} {Version} (channel {Channel}) into {Dir}", id, versions[i], channel, releaseDir);
            TestApp.PackTestApp(id, versions[i], $"packed-{i + 1}", releaseDir, log, channel: channel);
        }

        return new PackedRelease(releaseDir, id, versions, channel);
    }

    private static readonly ConcurrentDictionary<string, object> _cacheLocks = new();

    /// <summary>
    /// Packs a <paramref name="versions"/> set exactly once per process per (id, channel, versions) key into a
    /// pristine cache directory under the project's obj folder, then copies that whole directory into a fresh
    /// temp dir and returns the copy. Upload/download runners mutate the release dir (writing .incomplete files,
    /// regenerating indexes, etc.), so every caller receives its own disposable copy while the cache stays clean.
    /// Thread-safe via a per-key lock. Because packing is slow (~10-40s per version) this is the preferred way to
    /// obtain packages for the destination test suites, which pack the same small version sets repeatedly.
    /// </summary>
    public static PackedRelease GetCachedPack(string id, string channel, ILogger log, params string[] versions)
        => GetCachedPackCore(id, channel, log, notesContent: null, versions);

    /// <summary>
    /// Like <see cref="GetCachedPack"/> but packs with <paramref name="notesContent"/> as release notes markdown,
    /// which e.g. the git upload runners read out of the nupkg and use as the release body.
    /// </summary>
    public static PackedRelease GetCachedPackWithNotes(string id, string channel, ILogger log, string notesContent, params string[] versions)
    {
        if (notesContent == null)
            throw new ArgumentNullException(nameof(notesContent));
        return GetCachedPackCore(id, channel, log, notesContent, versions);
    }

    private static PackedRelease GetCachedPackCore(string id, string channel, ILogger log, string? notesContent, string[] versions)
    {
        if (versions == null || versions.Length == 0)
            throw new ArgumentException("At least one version is required.", nameof(versions));

        var key = $"{id}|{channel}|{String.Join(",", versions)}" + (notesContent != null ? "|notes" : "");
        var safeKey = key.Replace('|', '_').Replace(',', '-');
        var cacheDir = PathHelper.GetTestRootPath("Velopack.Deployment.Tests", "obj", "packcache", safeKey);

        var gate = _cacheLocks.GetOrAdd(key, _ => new object());
        lock (gate) {
            // Sentinel: the final version's full package exists only when the whole set packed successfully.
            // Its exact name varies by channel/OS defaults (default channels are omitted from the file name),
            // so probe by prefix instead of an exact name.
            var packed = Directory.Exists(cacheDir)
                && Directory.EnumerateFiles(cacheDir, $"{id}-{versions[^1]}*-full.nupkg").Any();
            if (!packed) {
                log.LogInformation("Packing cached release set {Key} into {Dir}", key, cacheDir);
                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, true);
                Directory.CreateDirectory(cacheDir);
                string? notesPath = null;
                if (notesContent != null) {
                    notesPath = Path.Combine(cacheDir, "NOTES.md");
                    File.WriteAllText(notesPath, notesContent);
                }

                for (var i = 0; i < versions.Length; i++)
                    TestApp.PackTestApp(id, versions[i], $"packed-{i + 1}", cacheDir, log, notesPath, channel: channel);
            } else {
                log.LogInformation("Reusing cached release set {Key} from {Dir}", key, cacheDir);
            }
        }

        var destDir = Path.Combine(Path.GetTempPath(), "velopack-packedrelease-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(cacheDir, destDir);
        return new PackedRelease(destDir, id, versions, channel);
    }

    /// <summary> Best-effort recursive delete for release dirs handed out by this fixture. </summary>
    public static void TryDeleteDir(string dir)
    {
        try {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        } catch { /* best-effort */ }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(dest, Path.GetRelativePath(source, file)), true);
    }
}
