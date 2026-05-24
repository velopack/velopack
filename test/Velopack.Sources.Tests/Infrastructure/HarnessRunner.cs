using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Velopack.Sources.Tests.Infrastructure;

public static class HarnessRunner
{
    public static HarnessResult RunRust(
        DockerFixture f, string sourceType, string urlOrPath, string? token, ITestOutputHelper output)
    {
        if (string.IsNullOrEmpty(f.RustHarnessPath) || !File.Exists(f.RustHarnessPath)) {
            throw new InvalidOperationException(
                "Rust harness binary is not available. Ensure it was built successfully during fixture initialization.");
        }

        var args = BuildArgs(f, sourceType, urlOrPath, token);
        return RunHarness(f.RustHarnessPath, args, workingDir: null, env: null, output);
    }

    public static HarnessResult RunCpp(
        DockerFixture f, string sourceType, string urlOrPath, string? token, ITestOutputHelper output)
    {
        if (string.IsNullOrEmpty(f.CppHarnessPath) || !File.Exists(f.CppHarnessPath)) {
            throw new InvalidOperationException(
                "C++ harness binary is not available. Ensure it was built successfully during fixture initialization.");
        }

        var args = BuildArgs(f, sourceType, urlOrPath, token);

        // Set library path for dynamic linking
        var env = new Dictionary<string, string>();
        var libDir = PathHelper.GetRustBuildOutputDir();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            env["DYLD_LIBRARY_PATH"] = libDir;
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            env["LD_LIBRARY_PATH"] = libDir;
        }

        return RunHarness(f.CppHarnessPath, args, workingDir: null, env, output);
    }

    public static HarnessResult RunPython(
        DockerFixture f, string sourceType, string urlOrPath, string? token, ITestOutputHelper output)
    {
        if (!f.IsPythonAvailable) {
            throw new InvalidOperationException(
                "Python velopack module is not available. Ensure uv is installed and lib-python builds.");
        }

        var harnessDir = Path.Combine(
            PathHelper.GetProjectDir(), "test", "Velopack.Sources.Tests", "harness-python");
        var scriptPath = Path.Combine(harnessDir, "main.py");

        if (!File.Exists(scriptPath)) {
            throw new FileNotFoundException("Python harness script not found at: " + scriptPath);
        }

        var harnessArgs = BuildArgs(f, sourceType, urlOrPath, token);
        var args = $"run python \"{scriptPath}\" {harnessArgs}";

        return RunHarness("uv", args, workingDir: harnessDir, env: null, output);
    }

    private static string BuildArgs(DockerFixture f, string sourceType, string urlOrPath, string? token)
    {
        var tokenArg = string.IsNullOrEmpty(token) ? "\"\"" : $"\"{token}\"";
        return $"{sourceType} \"{urlOrPath}\" {tokenArg}"
            + $" --channel {TestFeedData.Channel}"
            + $" --manifest \"{f.ManifestPath}\""
            + $" --packages-dir \"{f.PackagesDir}\"";
    }

    private static HarnessResult RunHarness(
        string fileName, string arguments, string? workingDir,
        Dictionary<string, string>? env, ITestOutputHelper output)
    {
        output.WriteLine($"Running: {fileName} {arguments}");

        var psi = new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (workingDir != null) {
            psi.WorkingDirectory = workingDir;
        }

        if (env != null) {
            foreach (var (key, value) in env) {
                psi.Environment[key] = value;
            }
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        process.WaitForExit(TimeSpan.FromSeconds(60));

        if (!string.IsNullOrWhiteSpace(stderr)) {
            output.WriteLine($"STDERR: {stderr}");
        }

        if (process.ExitCode != 0) {
            throw new InvalidOperationException(
                $"Harness process exited with code {process.ExitCode}.\n"
                + $"STDOUT: {stdout}\n"
                + $"STDERR: {stderr}");
        }

        output.WriteLine($"STDOUT: {stdout}");

        var result = JsonSerializer.Deserialize<HarnessResult>(stdout)
            ?? throw new InvalidOperationException(
                "Failed to deserialize harness output. STDOUT was: " + stdout);

        return result;
    }
}
