using System.IO.Compression;
using Velopack.Core;
using Velopack.Packaging.Commands;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class ApplyDeltaPackageTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ApplyMultipleDeltasFast()
    {
        var basePackage = PathHelper.GetFixture("Clowd-3.4.287-full.nupkg");
        var deltaPackage1 = PathHelper.GetFixture("Clowd-3.4.288-delta.nupkg");
        var deltaPackage2 = PathHelper.GetFixture("Clowd-3.4.291-delta.nupkg");
        var deltaPackage3 = PathHelper.GetFixture("Clowd-3.4.292-delta.nupkg");

        using var t2 = TempUtil.GetTempDirectory(out var temp);
        using var logger = output.BuildLoggerFor<ApplyDeltaPackageTests>();
        var console = new LoggerConsole(logger);

        var runner = new DeltaPatchCommandRunner(logger, console);
        await runner.Run(
            new DeltaPatchOptions() {
                BasePackage = basePackage,
                OutputFile = Path.Combine(temp, "Clowd-3.4.292-full.nupkg"),
                PatchFiles = [
                    new FileInfo(deltaPackage1),
                    new FileInfo(deltaPackage2),
                    new FileInfo(deltaPackage3),
                ]
            });
    }

    [Fact]
    public async Task ApplyThrowsOnLegacyBsdiffPatch()
    {
        using var t1 = TempUtil.GetTempDirectory(out var temp);
        using var logger = output.BuildLoggerFor<ApplyDeltaPackageTests>();
        var console = new LoggerConsole(logger);

        // deltas containing non-empty .bsdiff patches (produced by old vpk versions when
        // zstd was unavailable) are no longer supported and must fail rather than
        // silently producing a corrupt package
        var baseDir = Path.Combine(temp, "base", "lib", "app");
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "test.dll"), "base file content");
        var basePackage = Path.Combine(temp, "base.nupkg");
        ZipFile.CreateFromDirectory(Path.Combine(temp, "base"), basePackage);

        var deltaDir = Path.Combine(temp, "delta", "lib", "app");
        Directory.CreateDirectory(deltaDir);
        File.WriteAllText(Path.Combine(deltaDir, "test.dll.bsdiff"), "legacy bsdiff patch data");
        var deltaPackage = Path.Combine(temp, "delta.nupkg");
        ZipFile.CreateFromDirectory(Path.Combine(temp, "delta"), deltaPackage);

        var runner = new DeltaPatchCommandRunner(logger, console);
        await Assert.ThrowsAsync<NotSupportedException>(() => runner.Run(
            new DeltaPatchOptions() {
                BasePackage = basePackage,
                OutputFile = Path.Combine(temp, "out.nupkg"),
                PatchFiles = [new FileInfo(deltaPackage)],
            }));
    }
}
