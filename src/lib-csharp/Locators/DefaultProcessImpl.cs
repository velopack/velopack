#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Locators
{
    public class DefaultProcessImpl : IProcessImpl
    {
        [DllImport("user32", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        private Process? _currentProcess;
        private readonly IVelopackLogger _logger;

        public DefaultProcessImpl(IVelopackLogger logger)
        {
            _logger = logger;
        }

        public string GetCurrentProcessPath()
        {
            _currentProcess ??= Process.GetCurrentProcess();
            var fileName = _currentProcess.MainModule?.FileName ??
                   throw new InvalidOperationException($"Could not determine process path, please construct {nameof(IVelopackLocator)} manually.");
            return Path.GetFullPath(fileName);
        }

        public uint GetCurrentProcessId()
        {
            _currentProcess ??= Process.GetCurrentProcess();
            return (uint) _currentProcess.Id;
        }

        public void StartProcess(string exePath, IEnumerable<string> args, string? workDir, bool showWindow)
        {
            var psi = new ProcessStartInfo() {
                CreateNoWindow = true,
                FileName = exePath,
                WorkingDirectory = workDir,
            };

            psi.AppendArgumentListSafe(args, out var debugArgs);
            _logger.Debug($"Running: {psi.FileName} {debugArgs}");

            var p = Process.Start(psi);
            if (p == null) {
                throw new Exception("Failed to launch process.");
            }

            if (VelopackRuntimeInfo.IsWindows) {
                try {
                    // this is an attempt to work around a bug where the restarted app fails to come to foreground.
                    if (!AllowSetForegroundWindow(p.Id))
                        throw new Win32Exception();
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Failed to allow Update.exe to set foreground window.");
                }
            }
        }
    }
}