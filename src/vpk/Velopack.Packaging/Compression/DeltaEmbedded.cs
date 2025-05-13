using Microsoft.Extensions.Logging;
using Velopack.Core;

namespace Velopack.Packaging.Compression;

public class DeltaEmbedded
{
    private readonly DeltaImpl _delta;

    public DeltaEmbedded(string zstdPath, ILogger logger, string baseTmpDir)
    {
        _delta = new DeltaImpl(zstdPath, logger, baseTmpDir);
    }

    public void ApplyDeltaPackageFast(string workingPath, string deltaPackageZip, Action<int> progress = null)
    {
        _delta.ApplyDeltaPackageFast(workingPath, deltaPackageZip, progress);
    }

    private class DeltaImpl : DeltaPackage
    {
        private readonly Zstd _zstd;

        public DeltaImpl(string zstdPath, ILogger logger, string baseTmpDir) : base(logger.ToVelopackLogger(), baseTmpDir)
        {
            _zstd = new Zstd(zstdPath);
        }

        protected override void ApplyZstdPatch(string baseFile, string patchFile, string outputFile)
        {
            _zstd.ApplyPatch(baseFile, patchFile, outputFile);
        }
    }
}