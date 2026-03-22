#nullable enable
using System.Diagnostics;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.TestCommon;

public static class TestHelper
{
    private static readonly Random _random = Random.Shared;

    public static string RandomString(int length)
    {
        string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return new string(
            [.. Enumerable.Repeat(chars, length).Select(s => s[_random.Next(s.Length)])]);
    }

    public static string ReadFileWithRetry(string path, ILogger logger)
    {
        return IoUtil.Retry(
            () => File.ReadAllText(path),
            logger: logger.ToVelopackLogger(),
            retries: 10,
            retryDelay: 1000);
    }

    public static string RunImpl(ProcessStartInfo psi, ILogger logger, int? exitCode = 0)
    {
        var outputFile = PathHelper.GetTestRootPath($"run.{RandomString(8)}.log");

        try {
            var args = new string[psi.ArgumentList.Count];
            psi.ArgumentList.CopyTo(args, 0);
            new ProcessStartInfo().AppendArgumentListSafe(args, out var debug);

            ProcessStartInfo fix;
            if (VelopackRuntimeInfo.IsWindows) {
                fix = new ProcessStartInfo("cmd.exe");
                fix.CreateNoWindow = true;
                fix.WorkingDirectory = psi.WorkingDirectory;
                fix.Arguments = $"/s /c \"\"{psi.FileName}\" {debug} > \"{outputFile}\" 2>&1\"";
            } else {
                fix = new ProcessStartInfo("/bin/bash");
                fix.CreateNoWindow = true;
                fix.WorkingDirectory = psi.WorkingDirectory;
                fix.Arguments = $"-c '\"{psi.FileName}\" {debug} > \"{outputFile}\" 2>&1'";
            }

            // Copy environment variables from the original PSI
            foreach (string key in psi.EnvironmentVariables.Keys) {
                fix.EnvironmentVariables[key] = psi.EnvironmentVariables[key];
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            logger.Info($"TEST: Running {fix.FileName} {fix.Arguments}");
            using var p = Process.Start(fix)!;

            var timeout = TimeSpan.FromMinutes(3);
            if (!p.WaitForExit(timeout))
                throw new TimeoutException($"Process did not exit within {timeout.TotalSeconds}s.");

            var elapsed = sw.Elapsed;
            sw.Stop();

            logger.Info($"TEST: Process exited with code {p.ExitCode} in {elapsed.TotalSeconds}s");

            using var fs = IoUtil.Retry(
                () => File.Open(outputFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None),
                20,
                1000,
                logger.ToVelopackLogger());

            using var reader = new StreamReader(fs);
            var output = reader.ReadToEnd();

            if (String.IsNullOrWhiteSpace(output)) {
                logger.Warn($"TEST: Process output was empty");
            } else {
                logger.Info($"TEST: Process output: {Environment.NewLine}{output.Trim()}{Environment.NewLine}");
            }

            if (exitCode.HasValue && p.ExitCode != exitCode.Value) {
                throw new Exception($"Process exited with code {p.ExitCode} but expected {exitCode.Value}");
            }

            return String.Join(
                Environment.NewLine,
                output
                    .Split('\n')
                    .Where(l => !l.Contains("Code coverage results"))
                    .Select(l => l.Trim())
            ).Trim();
        } finally {
            try {
                File.Delete(outputFile);
            } catch { }
        }
    }

    public static string RunNoCoverage(string exe, string[] args, string workingDir, ILogger logger,
        int? exitCode = 0, IDictionary<string, string>? envVars = null)
    {
        if (!File.Exists(exe))
            throw new Exception($"File {exe} does not exist.");

        var psi = new ProcessStartInfo(exe);
        psi.WorkingDirectory = workingDir;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        if (envVars != null) {
            foreach (var kvp in envVars) {
                psi.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        return RunImpl(psi, logger, exitCode);
    }
}
