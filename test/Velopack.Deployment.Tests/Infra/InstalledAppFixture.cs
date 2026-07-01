using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.TestCommon;

namespace Velopack.Deployment.Tests;

/// <summary>
/// The absolute paths describing a fabricated installed-app layout, in the neutral shape consumed by
/// every language's <c>VelopackLocatorConfig</c> (see <c>src/lib-rust/src/locator.rs</c>).
/// </summary>
public sealed record LocatorSpec(
    string RootAppDir,
    string UpdateExePath,
    string PackagesDir,
    string ManifestPath,
    string CurrentBinaryDir,
    bool IsPortable)
{
    /// <summary> Serialises to the PascalCase JSON object accepted by the cross-language harness protocol. </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(new {
            RootAppDir,
            UpdateExePath,
            PackagesDir,
            ManifestPath,
            CurrentBinaryDir,
            IsPortable,
        });
    }
}

/// <summary>
/// A once-per-process fixture that packs a two-version <c>SourceTestApp</c> feed (1.0.0 installed,
/// 2.0.0 available) on the <c>stable</c> channel and can fabricate fake installed-app layouts that
/// point at a locator pointing at the 1.0.0 manifest. Used by every source test across all client
/// languages.
/// </summary>
public sealed class InstalledAppFixture
{
    /// <summary> The app id packed by this fixture. </summary>
    public const string AppId = "SourceTestApp";

    /// <summary> The channel the feed is packed on. </summary>
    public const string Channel = "stable";

    /// <summary> The version the fabricated install reports as currently installed. </summary>
    public const string InstalledVersion = "1.0.0";

    /// <summary> The version available in the feed as an update. </summary>
    public const string LatestVersion = "2.0.0";

    /// <summary> The release directory containing releases.stable.json and the packed nupkgs. </summary>
    public string FeedDir { get; }

    /// <summary> The uppercase hex SHA256 of the 2.0.0 full nupkg (matches the feed asset checksum format). </summary>
    public string Sha256OfLatestFullPackage { get; }

    private static readonly object _gate = new();
    private static InstalledAppFixture? _instance;

    /// <summary>
    /// Returns the process-wide fixture, packing the feed once on first use (deleting and recreating
    /// the cache dir). The first caller's <paramref name="log"/> captures the pack output.
    /// </summary>
    public static InstalledAppFixture GetOrCreate(ILogger log)
    {
        if (_instance != null)
            return _instance;
        lock (_gate) {
            _instance ??= new InstalledAppFixture(log);
            return _instance;
        }
    }

    private InstalledAppFixture(ILogger log)
    {
        FeedDir = PathHelper.GetTestRootPath("Velopack.Deployment.Tests", "obj", "sourcetest-feed");
        if (Directory.Exists(FeedDir))
            Directory.Delete(FeedDir, true);
        Directory.CreateDirectory(FeedDir);

        log.LogInformation("Packing {AppId} feed ({Installed} + {Latest}) into {FeedDir}", AppId, InstalledVersion, LatestVersion, FeedDir);
        TestApp.PackTestApp(AppId, InstalledVersion, "source-1", FeedDir, log, channel: Channel);
        TestApp.PackTestApp(AppId, LatestVersion, "source-2", FeedDir, log, channel: Channel);

        Sha256OfLatestFullPackage = ComputeSha256(FullNupkgPath(LatestVersion));
    }

    /// <summary> The absolute path of the full nupkg for the given version in <see cref="FeedDir"/>. </summary>
    public string FullNupkgPath(string version) => Path.Combine(FeedDir, $"{AppId}-{version}-{Channel}-full.nupkg");

    /// <summary>
    /// Builds a fresh temp install layout with an extracted <c>sq.version</c> (from the 1.0.0 full
    /// nupkg), an empty packages dir and a dummy update executable, and returns its absolute paths.
    /// The caller owns the returned directory's lifetime.
    /// </summary>
    public LocatorSpec CreateInstalledLayout(out string rootDir)
    {
        rootDir = Path.Combine(Path.GetTempPath(), "velopack-sourcetest-install-" + Guid.NewGuid().ToString("N"));
        var currentDir = Path.Combine(rootDir, "current");
        var packagesDir = Path.Combine(rootDir, "packages");
        Directory.CreateDirectory(currentDir);
        Directory.CreateDirectory(packagesDir);

        var manifestPath = Path.Combine(currentDir, "sq.version");
        ExtractSqVersion(FullNupkgPath(InstalledVersion), manifestPath);

        var updateExeName = OperatingSystem.IsWindows() ? "Update.exe" : OperatingSystem.IsMacOS() ? "UpdateMac" : "UpdateNix";
        var updateExePath = Path.Combine(rootDir, updateExeName);
        File.WriteAllBytes(updateExePath, new byte[] { 0 });

        return new LocatorSpec(rootDir, updateExePath, packagesDir, manifestPath, currentDir, IsPortable: true);
    }

    /// <summary>
    /// Fabricates the C# equivalent of the installed layout as a <see cref="TestVelopackLocator"/> for
    /// the in-process csharp source rows.
    /// </summary>
    public TestVelopackLocator CreateCSharpLocator(out string rootDir, IVelopackLogger? logger = null)
    {
        var spec = CreateInstalledLayout(out rootDir);
        return new TestVelopackLocator(
            AppId,
            InstalledVersion,
            spec.PackagesDir,
            appDir: spec.CurrentBinaryDir,
            rootDir: spec.RootAppDir,
            updateExe: spec.UpdateExePath,
            channel: Channel,
            logger: logger);
    }

    private static void ExtractSqVersion(string nupkgPath, string destPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        // Prefer the canonical path; otherwise pick the largest "*/sq.version" so we don't pick up a tiny
        // symlink entry (e.g. the macOS Contents/MacOS -> ../Resources link) over the real manifest.
        var entry = archive.GetEntry("lib/app/sq.version")
            ?? archive.Entries
                .Where(e => e.FullName.Replace('\\', '/').EndsWith("/sq.version", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Length)
                .FirstOrDefault()
            ?? throw new FileNotFoundException($"sq.version not found inside {nupkgPath}");
        entry.ExtractToFile(destPath, overwrite: true);
    }

    private static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", String.Empty);
    }
}
