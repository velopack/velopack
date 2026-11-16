using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;

namespace Velopack.Util
{
    [ExcludeFromCodeCoverage]
    internal static partial class ProcessStartExtensions
    {
        public static async Task<int> GetExitCodeAsync(this Process p)
        {
#if NET5_0_OR_GREATER
            await p.WaitForExitAsync().ConfigureAwait(false);
            return p.ExitCode;
#else
            var tcs = new TaskCompletionSource<int>();
            var thread = new Thread(
                () => {
                    try {
                        p.WaitForExit();
                        tcs.SetResult(p.ExitCode);
                    } catch (Exception ex) {
                        tcs.SetException(ex);
                    }
                });
            thread.IsBackground = true;
            thread.Start();
            await tcs.Task.ConfigureAwait(false);
            return p.ExitCode;
#endif
        }



        public static Process StartRedirectOutputToILogger(this ProcessStartInfo psi, IVelopackLogger log, VelopackLogLevel outputLevel)
        {
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;

            var p = Process.Start(psi);
            if (p == null) throw new Exception("Process.Start returned null.");

            p.BeginErrorReadLine();
            p.BeginOutputReadLine();

            p.ErrorDataReceived += (o, e) => {
                if (e.Data != null) {
                    log.LogError(e.Data);
                }
            };

            p.OutputDataReceived += (o, e) => {
                if (e.Data != null) {
                    log.Log(outputLevel, e.Data);
                }
            };

            return p;
        }

        public static string Output(this ProcessStartInfo psi, int timeoutMs)
        {
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;

            var p = Process.Start(psi);
            if (p == null) throw new Exception("Process.Start returned null.");

            if (!p.WaitForExit(timeoutMs)) {
                p.Kill();
                throw new TimeoutException("Process did not exit within allotted time.");
            }

            return p.StandardOutput.ReadToEnd().Trim();

            //TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            //psi.RedirectStandardOutput = true;
            //psi.RedirectStandardError = true;
            //psi.UseShellExecute = false;

            //bool killed = false;

            //var sb = new StringBuilder();
            //var p = new Process();
            //p.StartInfo = psi;
            //p.EnableRaisingEvents = true;

            //p.Exited += (o, e) => {
            //    if (killed) return;
            //    if (p.ExitCode != 0) {
            //        tcs.SetException(new Exception($"Process exited with code {p.ExitCode}."));
            //    } else {
            //        tcs.SetResult(sb.ToString());
            //    }
            //};

            //p.ErrorDataReceived += (o, e) => {
            //    if (killed) return;
            //    if (e.Data != null) {
            //        sb.AppendLine(e.Data);
            //    }
            //};

            //p.OutputDataReceived += (o, e) => {
            //    if (killed) return;
            //    if (e.Data != null) {
            //        sb.AppendLine(e.Data);
            //    }
            //};

            //p.Start();
            //p.BeginErrorReadLine();
            //p.BeginOutputReadLine();

            //Task.Delay(timeoutMs).ContinueWith(t => {
            //    killed = true;
            //    if (!tcs.Task.IsCompleted) {
            //        tcs.SetException(new TimeoutException($"Process timed out after {timeoutMs}ms."));
            //        p.Kill();
            //    }
            //});

            //return tcs.Task;
        }
    }
}