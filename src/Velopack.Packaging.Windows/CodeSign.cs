using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Windows
{
    [SupportedOSPlatform("windows")]
    public class CodeSign
    {
        public ILogger Log { get; }

        public CodeSign(ILogger logger)
        {
            Log = logger;
        }

        private bool CheckIsAlreadySigned(string filePath)
        {
            if (String.IsNullOrWhiteSpace(filePath)) return true;

            if (!File.Exists(filePath)) {
                Log.Warn($"Cannot sign '{filePath}', file does not exist.");
                return true;
            }

            try {
                if (AuthenticodeTools.IsTrusted(filePath)) {
                    Log.Debug($"'{filePath}' is already signed, skipping...");
                    return true;
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to determine signing status for " + filePath);
            }

            return false;
        }

        public void SignPEFilesWithSignTool(string rootDir, string[] filePaths, string signArguments, int parallelism, Action<int> progress)
        {
            Queue<string> pendingSign = new Queue<string>();

            foreach (var f in filePaths) {
                if (!CheckIsAlreadySigned(f)) {
                    // try to find the path relative to rootDir
                    if (String.IsNullOrEmpty(rootDir)) {
                        pendingSign.Enqueue(f);
                    } else {
                        var partialPath = Utility.NormalizePath(f).Substring(Utility.NormalizePath(rootDir).Length).Trim('/', '\\');
                        pendingSign.Enqueue(partialPath);
                    }
                } else {
                    Log.Debug($"'{f}' is already signed, and will not be signed again.");
                }
            }

            if (filePaths.Length != pendingSign.Count) {
                var diff = filePaths.Length - pendingSign.Count;
                Log.Info($"{pendingSign.Count} files will be signed, {diff} will be skipped because they are already signed.");
            }

            var totalToSign = pendingSign.Count;
            var baseSignArgs = CommandLineToArgvW(signArguments);

            do {
                List<string> args = new List<string>();
                args.Add("sign");
                args.AddRange(baseSignArgs);
                for (int i = Math.Min(pendingSign.Count, parallelism); i > 0; i--) {
                    args.Add(pendingSign.Dequeue());
                }

                var result = Exe.InvokeProcess(HelperFile.SignToolPath, args, rootDir);
                if (result.ExitCode != 0) {
                    var cmdWithPasswordHidden = new Regex(@"\/p\s+?[^\s]+").Replace(result.Command, "/p ********");
                    Log.Debug($"Signing command failed: {cmdWithPasswordHidden}");
                    throw new Exception(
                        $"Signing command failed. Specify --verbose argument to print signing command.\n\n" +
                        $"Output was:\n" + result.StdOutput);
                }

                int processed = totalToSign - pendingSign.Count;
                Log.Debug($"Signed {processed}/{totalToSign} successfully.\r\n" + result.StdOutput);
                progress((int) ((double) processed / totalToSign * 100));
            } while (pendingSign.Count > 0);
        }

        public void SignPEFileWithTemplate(string filePath, string signTemplate)
        {
            if (VelopackRuntimeInfo.IsWindows && CheckIsAlreadySigned(filePath)) {
                Log.Debug($"'{filePath}' is already signed, and will not be signed again.");
                return;
            }

            var command = signTemplate.Replace("\"{{file}}\"", "{{file}}").Replace("{{file}}", $"\"{filePath}\"");

            var result = Exe.InvokeProcess(command, null, null);
            if (result.ExitCode != 0) {
                var cmdWithPasswordHidden = new Regex(@"\/p\s+?[^\s]+").Replace(result.Command, "/p ********");
                Log.Debug($"Signing command failed: {cmdWithPasswordHidden}");
                throw new Exception(
                    $"Signing command failed. Specify --verbose argument to print signing command.\n\n" +
                    $"Output was:\n" + result.StdOutput);
            }

            Log.Info("Sign successful: " + result.StdOutput);
        }

        private const string WIN_KERNEL32 = "kernel32.dll";
        private const string WIN_SHELL32 = "shell32.dll";

        [DllImport(WIN_KERNEL32, EntryPoint = "LocalFree", SetLastError = true)]
        private static extern IntPtr _LocalFree(IntPtr hMem);

        [DllImport(WIN_SHELL32, EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode)]
        private static extern IntPtr _CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmdLine, out int numArgs);

        protected static string[] CommandLineToArgvW(string cmdLine)
        {
            IntPtr argv = IntPtr.Zero;
            try {
                argv = _CommandLineToArgvW(cmdLine, out var numArgs);
                if (argv == IntPtr.Zero) {
                    throw new Win32Exception();
                }
                var result = new string[numArgs];

                for (int i = 0; i < numArgs; i++) {
                    IntPtr currArg = Marshal.ReadIntPtr(argv, i * Marshal.SizeOf(typeof(IntPtr)));
                    result[i] = Marshal.PtrToStringUni(currArg);
                }

                return result;
            } finally {
                _LocalFree(argv);
            }
        }
    }
}
