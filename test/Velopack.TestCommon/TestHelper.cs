#nullable enable
using System.Diagnostics;
using System.Threading;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.TestCommon;

public static class TestHelper
{
    /// <summary>
    /// Creates a temp directory whose name is unique across test and child-process lifetimes.
    /// Use this for installed app roots: detached updater processes can outlive TempUtil's
    /// in-process reservation and must never target a path later reused by another test.
    /// </summary>
    public static IDisposable GetIsolatedTempDirectory(out string path)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "velopack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        path = tempPath;
        return Disposable.Create(() => IoUtil.DeleteFileOrDirectoryHard(tempPath, throwOnFailure: false));
    }

    /// <summary>
    /// Repeatedly runs assertion until it stops throwing or timeoutMs elapses (the last attempt's
    /// exception propagates). Use instead of a fixed Thread.Sleep when waiting on work that happens
    /// in a separate process (e.g. update.exe applying an update) — the test continues as soon as
    /// the expected state is observable.
    /// </summary>
    public static void WaitUntil(Action assertion, int timeoutMs = 30_000, int pollDelayMs = 500)
    {
        var sw = Stopwatch.StartNew();
        while (true) {
            try {
                assertion();
                return;
            } catch when (sw.ElapsedMilliseconds < timeoutMs) {
                Thread.Sleep(pollDelayMs);
            }
        }
    }

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
            () => {
                using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                return reader.ReadToEnd();
            },
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
                // Use ArgumentList so .NET passes each item as a discrete argv entry
                // instead of re-tokenizing a single Arguments string.
                fix.ArgumentList.Add("-c");
                fix.ArgumentList.Add($"\"{psi.FileName}\" {debug} > \"{outputFile}\" 2>&1");
            }

            // Copy environment variables from the original PSI
            foreach (string key in psi.EnvironmentVariables.Keys) {
                fix.EnvironmentVariables[key] = psi.EnvironmentVariables[key];
            }

            Stopwatch sw = new Stopwatch();
            sw.Start();

            new ProcessStartInfo().AppendArgumentListSafe(
                [.. fix.ArgumentList], out var fixDebug);
            logger.Info($"TEST: Running {fix.FileName} {fix.Arguments}{fixDebug}");
            using var p = Process.Start(fix)!;

            var timeout = TimeSpan.FromMinutes(3);
            if (!p.WaitForExit(timeout))
                throw new TimeoutException($"Process did not exit within {timeout.TotalSeconds}s.");

            var elapsed = sw.Elapsed;
            sw.Stop();

            logger.Info($"TEST: Process exited with code {p.ExitCode} in {elapsed.TotalSeconds}s");

            // The command may launch a child process which inherits the shell's redirected output
            // handle and outlives the shell. Allow that child to retain the handle while we read the
            // output produced by the command that has already exited.
            using var fs = IoUtil.Retry(
                () => File.Open(outputFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
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

    /// <summary>
    /// Overwrites the -full.nupkg for each of the given versions with garbage, so an update can only
    /// succeed via delta packages - a fallback to a full download will fail its checksum validation.
    /// The full and delta packages must exist (in any channel naming), or this throws.
    /// </summary>
    public static void CorruptFullPackagesToForceDelta(string releaseDir, string id, string[] versions)
    {
        foreach (var version in versions) {
            var fulls = Directory.GetFiles(releaseDir, $"{id}-{version}*-full.nupkg");
            if (fulls.Length != 1)
                throw new Exception($"Expected exactly one {id}-{version}*-full.nupkg in {releaseDir}, found {fulls.Length}.");
            var deltas = Directory.GetFiles(releaseDir, $"{id}-{version}*-delta.nupkg");
            if (deltas.Length != 1)
                throw new Exception($"Expected exactly one {id}-{version}*-delta.nupkg in {releaseDir}, found {deltas.Length}.");
            File.WriteAllText(fulls[0], "nope");
        }
    }
}
