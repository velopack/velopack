using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Chunks
{
    /// <summary>The outcome of one <c>Update.exe</c> run driven by <see cref="UpdateExeRunner"/>.</summary>
    internal sealed class UpdateExeRunResult
    {
        public int ExitCode { get; }
        /// <summary>True when at least one <c>VPK:</c> protocol line was seen (an updater that lacks the command prints none).</summary>
        public bool SawProtocolLine { get; }
        /// <summary>The count reported by <c>VPK:MISSING</c>, if any.</summary>
        public int? MissingCount { get; }
        /// <summary>The message of the last <c>VPK:ERROR</c> line, if any.</summary>
        public string? ErrorMessage { get; }

        public UpdateExeRunResult(int exitCode, bool sawProtocolLine, int? missingCount, string? errorMessage)
        {
            ExitCode = exitCode;
            SawProtocolLine = sawProtocolLine;
            MissingCount = missingCount;
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>
    /// Runs an <c>Update.exe</c> sub-command with redirected output and parses its line protocol (§7.1):
    /// <c>VPK:PROGRESS n</c>, <c>VPK:MISSING n</c> and <c>VPK:ERROR msg</c> at line start. Other stdout lines go to the
    /// logger at Debug, stderr lines at Warn. The process is killed on cancellation and after ten minutes without output.
    /// </summary>
    internal sealed class UpdateExeRunner
    {
        public const string ProtocolPrefix = "VPK:";
        public const string ProgressPrefix = "VPK:PROGRESS ";
        public const string MissingPrefix = "VPK:MISSING ";
        public const string ErrorPrefix = "VPK:ERROR ";
        public static readonly TimeSpan DefaultInactivityTimeout = TimeSpan.FromMinutes(10);

        private readonly IVelopackLogger _log;

        /// <summary>Kill the process when no output line arrives for this long.</summary>
        public TimeSpan InactivityTimeout { get; set; } = DefaultInactivityTimeout;

        /// <summary>How often the inactivity clock is checked.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>The wall clock used for the inactivity cap. Tests replace this.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>Called with the id of the started process. Tests use it to check that a killed process is gone.</summary>
        internal Action<int>? OnStarted { get; set; }

        public UpdateExeRunner(IVelopackLogger log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>
        /// Runs <paramref name="exe"/> with <paramref name="args"/> and waits for it to exit. <paramref name="progress"/>
        /// receives the values of <c>VPK:PROGRESS</c> lines. Throws <see cref="OperationCanceledException"/> after killing
        /// the process when <paramref name="ct"/> is cancelled, and <see cref="AssembleFailedException"/> after killing it
        /// when it produces no output for <see cref="InactivityTimeout"/>.
        /// </summary>
        public async Task<UpdateExeRunResult> RunAsync(string exe, IEnumerable<string> args, Action<int>? progress, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(exe)) throw new ArgumentNullException(nameof(exe));
            if (args == null) throw new ArgumentNullException(nameof(args));

            var psi = new ProcessStartInfo {
                FileName = exe,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.AppendArgumentListSafe(args, out var debugArgs);
            _log.Debug($"Running: {exe} {debugArgs}");

            bool sawProtocol = false;
            int? missing = null;
            string? error = null;
            int lastProgress = -1;
            long lastActivityTicks = Clock().Ticks;
            var stdoutDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) => {
                if (e.Data == null) {
                    stdoutDone.TrySetResult(true);
                    return;
                }
                Interlocked.Exchange(ref lastActivityTicks, Clock().Ticks);
                var line = e.Data;
                if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal)) {
                    sawProtocol = true;
                    if (int.TryParse(line.Substring(ProgressPrefix.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)) {
                        if (p > lastProgress) {
                            lastProgress = p;
                            try {
                                progress?.Invoke(p);
                            } catch (Exception ex) {
                                _log.Warn(ex, "Progress callback threw.");
                            }
                        }
                    }
                } else if (line.StartsWith(MissingPrefix, StringComparison.Ordinal)) {
                    sawProtocol = true;
                    if (int.TryParse(line.Substring(MissingPrefix.Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)) {
                        missing = m;
                    }
                } else if (line.StartsWith(ErrorPrefix, StringComparison.Ordinal)) {
                    sawProtocol = true;
                    error = line.Substring(ErrorPrefix.Length).Trim();
                    _log.Error($"Update.exe: {error}");
                } else {
                    _log.Debug($"Update.exe: {line}");
                }
            };
            process.ErrorDataReceived += (_, e) => {
                if (e.Data == null) {
                    stderrDone.TrySetResult(true);
                    return;
                }
                Interlocked.Exchange(ref lastActivityTicks, Clock().Ticks);
                _log.Warn($"Update.exe: {e.Data}");
            };

            if (!process.Start()) throw new AssembleFailedException($"Failed to start '{exe}'.");
            OnStarted?.Invoke(process.Id);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var exitTask = process.GetExitCodeAsync();
            while (true) {
                var waited = await Task.WhenAny(exitTask, Task.Delay(PollInterval, CancellationToken.None)).ConfigureAwait(false);
                if (waited == exitTask) break;
                if (ct.IsCancellationRequested) {
                    Kill(process);
                    throw new OperationCanceledException("Update.exe was killed because the operation was cancelled.", ct);
                }
                var idle = Clock() - new DateTime(Interlocked.Read(ref lastActivityTicks), DateTimeKind.Utc);
                if (idle >= InactivityTimeout) {
                    Kill(process);
                    throw new AssembleFailedException($"Update.exe produced no output for {idle.TotalMinutes:0.#} minutes and was killed.");
                }
            }

            int exitCode = await exitTask.ConfigureAwait(false);
            // drain the async readers so no line is lost
            await Task.WhenAny(Task.WhenAll(stdoutDone.Task, stderrDone.Task), Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            _log.Debug($"Update.exe exited with code {exitCode} (protocol lines seen: {sawProtocol}).");
            return new UpdateExeRunResult(exitCode, sawProtocol, missing, error);
        }

        private void Kill(Process process)
        {
            try {
                if (process.HasExited) return;
#if NET5_0_OR_GREATER
                process.Kill(entireProcessTree: true);
#else
                process.Kill();
#endif
            } catch (Exception ex) {
                _log.Warn(ex, "Failed to kill Update.exe.");
            }
        }
    }
}
