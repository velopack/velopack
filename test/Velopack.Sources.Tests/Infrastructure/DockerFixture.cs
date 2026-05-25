using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using Velopack.Sources.Tests.Infrastructure;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Velopack.Sources.Tests.Infrastructure;

[CollectionDefinition("SourceTests")]
public class SourceTestCollection : ICollectionFixture<DockerFixture> { }

public class DockerFixture : IAsyncLifetime
{
    private readonly IMessageSink _diagnosticSink;

    public string TestDataDir { get; private set; } = "";
    public string ManifestPath { get; private set; } = "";
    public string PackagesDir { get; private set; } = "";
    public string FileSourceDir { get; private set; } = "";

    public string HttpBaseUrl { get; } = "http://localhost:8088";
    public string GiteaRepoUrl { get; private set; } = "";
    public string GiteaToken { get; private set; } = "";
    public string GitLabApiUrl { get; private set; } = "";
    public string GitLabToken { get; private set; } = "";

    public bool IsDockerAvailable { get; private set; }
    public bool IsGitLabAvailable { get; private set; }
    public bool IsPythonAvailable { get; private set; }

    public string RustHarnessPath { get; private set; } = "";
    public string CppHarnessPath { get; private set; } = "";

    public DockerFixture(IMessageSink diagnosticSink)
    {
        _diagnosticSink = diagnosticSink;
    }

    public async ValueTask InitializeAsync()
    {
        var projectDir = FindProjectDir();
        var repoRoot = PathHelper.GetProjectDir();

        Log("Source tests project directory: " + projectDir);
        Log("Repository root: " + repoRoot);

        // 1. Generate test data
        TestDataDir = Path.Combine(projectDir, "testdata");
        TestFeedData.GenerateTestData(TestDataDir);
        ManifestPath = Path.Combine(TestDataDir, "sq.version");
        PackagesDir = Path.Combine(TestDataDir, "packages");
        FileSourceDir = Path.Combine(TestDataDir, "file");
        Log("Test data generated at: " + TestDataDir);

        // 2. Check if Gitea is reachable
        IsDockerAvailable = await CheckEndpointReachable("http://localhost:3000/api/v1/version", "Gitea");

        // 3. Seed Gitea if available
        if (IsDockerAvailable) {
            try {
                var (repoUrl, token) = await GiteaSeeder.SeedAsync(_diagnosticSink);
                GiteaRepoUrl = repoUrl;
                GiteaToken = token;
                Log($"Gitea seeded. Repo: {GiteaRepoUrl}");
            } catch (Exception ex) {
                Log($"Gitea seeding failed: {ex.Message}");
                IsDockerAvailable = false;
            }
        }

        // 4. Check if GitLab is reachable (newer GitLab CE returns 404 for /-/readiness,
        //    so use /users/sign_in which returns 200 when the Rails app is ready)
        IsGitLabAvailable = await CheckEndpointReachable("http://localhost:8929/users/sign_in", "GitLab");

        // 5. Seed GitLab if available
        if (IsGitLabAvailable) {
            try {
                var (apiUrl, token) = await GitLabSeeder.SeedAsync(_diagnosticSink);
                GitLabApiUrl = apiUrl;
                GitLabToken = token;
                Log($"GitLab seeded. API URL: {GitLabApiUrl}");
            } catch (Exception ex) {
                Log($"GitLab seeding failed: {ex.Message}");
                IsGitLabAvailable = false;
            }
        }

        // 6. Build Rust harness
        await BuildRustHarness(projectDir, repoRoot);

        // 7. Build C++ harness
        await BuildCppHarness(projectDir, repoRoot);

        // 8. Check Python availability
        IsPythonAvailable = await CheckPythonAvailable();
    }

    public ValueTask DisposeAsync()
    {
        // No-op: containers stay running for reuse
        return ValueTask.CompletedTask;
    }

    private async Task BuildRustHarness(string projectDir, string repoRoot)
    {
        var harnessRustDir = Path.Combine(projectDir, "harness-rust");
        var cargoToml = Path.Combine(harnessRustDir, "Cargo.toml");

        if (!File.Exists(cargoToml)) {
            Log("Rust harness Cargo.toml not found at: " + cargoToml);
            return;
        }

        Log("Building Rust harness...");
        var (exitCode, stdout, stderr) = await RunProcessAsync(
            "cargo",
            $"build --manifest-path \"{cargoToml}\"",
            workingDir: harnessRustDir);

        if (exitCode != 0) {
            Log($"Rust harness build failed (exit code {exitCode}):\n{stderr}");
            return;
        }

        // Determine the binary path
        var binaryName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "velopack-source-harness.exe"
            : "velopack-source-harness";
        var binaryPath = Path.Combine(harnessRustDir, "target", "debug", binaryName);

        if (File.Exists(binaryPath)) {
            RustHarnessPath = binaryPath;
            Log("Rust harness built: " + RustHarnessPath);
        } else {
            Log("Rust harness binary not found at: " + binaryPath);
        }
    }

    private async Task BuildCppHarness(string projectDir, string repoRoot)
    {
        var harnessCppDir = Path.Combine(projectDir, "harness-cpp");
        var mainCpp = Path.Combine(harnessCppDir, "main.cpp");

        if (!File.Exists(mainCpp)) {
            Log("C++ harness main.cpp not found at: " + mainCpp);
            return;
        }

        var includePath = Path.Combine(repoRoot, "src", "lib-cpp", "include");
        var libPath = PathHelper.GetRustBuildOutputDir();
        var outputBinary = Path.Combine(harnessCppDir, "harness-cpp-bin");

        string compiler;
        string args;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            compiler = "clang++";
            args = $"-std=c++17 -I \"{includePath}\" main.cpp -L \"{libPath}\" -lvelopack_libc -o harness-cpp-bin";
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            compiler = "g++";
            args = $"-std=c++17 -I \"{includePath}\" main.cpp -L \"{libPath}\" -lvelopack_libc -Wl,-rpath,'$ORIGIN' -o harness-cpp-bin";
        } else {
            Log("C++ harness build is not supported on this platform.");
            return;
        }

        Log($"Building C++ harness with {compiler}...");
        var (exitCode, stdout, stderr) = await RunProcessAsync(compiler, args, workingDir: harnessCppDir);

        if (exitCode != 0) {
            Log($"C++ harness build failed (exit code {exitCode}):\n{stderr}");
            return;
        }

        if (File.Exists(outputBinary)) {
            CppHarnessPath = outputBinary;
            Log("C++ harness built: " + CppHarnessPath);
        } else {
            Log("C++ harness binary not found at: " + outputBinary);
        }
    }

    private async Task<bool> CheckPythonAvailable()
    {
        // Check if uv is available (used to manage the Python environment)
        try {
            var (exitCode, _, _) = await RunProcessAsync("uv", "--version", workingDir: null);
            if (exitCode != 0) {
                Log("uv is not available. Install it from https://docs.astral.sh/uv/");
                return false;
            }
        } catch {
            Log("uv is not available. Install it from https://docs.astral.sh/uv/");
            return false;
        }

        // Sync the harness-python project (creates venv and installs dependencies including velopack)
        var projectDir = FindProjectDir();
        var harnessDir = Path.Combine(projectDir, "harness-python");
        var pyproject = Path.Combine(harnessDir, "pyproject.toml");

        if (!File.Exists(pyproject)) {
            Log($"harness-python pyproject.toml not found at: {pyproject}");
            return false;
        }

        Log("Syncing Python harness dependencies with uv...");
        var (syncExit, syncOut, syncErr) = await RunProcessAsync("uv", "sync", workingDir: harnessDir);
        if (syncExit != 0) {
            Log($"uv sync failed (exit code {syncExit}):\n{syncErr}");
            return false;
        }

        Log("Python harness dependencies synced.");
        return true;
    }

    private async Task<bool> CheckEndpointReachable(string url, string serviceName)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        const int maxRetries = 3;
        for (int i = 0; i < maxRetries; i++) {
            try {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode) {
                    Log($"{serviceName} is reachable at {url}");
                    return true;
                }
                Log($"{serviceName} returned {response.StatusCode} at {url}");
            } catch (Exception ex) {
                Log($"{serviceName} not reachable (attempt {i + 1}/{maxRetries}): {ex.Message}");
            }

            if (i < maxRetries - 1) {
                await Task.Delay(TimeSpan.FromSeconds(2 * (i + 1)));
            }
        }

        Log($"{serviceName} is not available.");
        return false;
    }

    private static string FindProjectDir()
    {
        // Walk up from the assembly location looking for docker-compose.yml
        var dir = AppContext.BaseDirectory;
        while (dir != null) {
            var candidate = Path.Combine(dir, "docker-compose.yml");
            if (File.Exists(candidate)) {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: use PathHelper to get repo root and append the known path
        return Path.Combine(PathHelper.GetProjectDir(), "test", "Velopack.Sources.Tests");
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(
        string fileName, string arguments, string? workingDir)
    {
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

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private void Log(string message)
    {
        _diagnosticSink.OnMessage(new DiagnosticMessage($"[DockerFixture] {message}"));
    }
}
