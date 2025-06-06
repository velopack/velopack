﻿using System.Diagnostics;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Packaging.Compression
{
    internal class DeltaUpdateExe : DeltaPackage
    {
        private readonly string _updateExePath;

        public DeltaUpdateExe(IVelopackLogger logger, string baseTmpDir, string updateExePath) : base(logger, baseTmpDir)
        {
            _updateExePath = updateExePath;
        }

        protected override void ApplyZstdPatch(string baseFile, string patchFile, string outputFile)
        {
            var psi = new ProcessStartInfo(_updateExePath);
            psi.AppendArgumentListSafe(new string[] { "patch", "--old", baseFile, "--patch", patchFile, "--output", outputFile }, out var _);
            psi.CreateNoWindow = true;
            var p = psi.StartRedirectOutputToILogger(Log, VelopackLogLevel.Debug);
            if (!p.WaitForExit(30_000)) {
                p.Kill();
                throw new TimeoutException("zstd patch process timed out (30s).");
            }

            if (p.ExitCode != 0) {
                throw new Exception($"zstd patch process failed with exit code {p.ExitCode}.");
            }
        }
    }
}