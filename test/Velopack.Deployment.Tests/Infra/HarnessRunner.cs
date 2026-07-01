using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Velopack.Deployment.Tests;

/// <summary> The client languages exercised by the cross-language source tests. </summary>
public enum HarnessLang
{
    /// <summary> In-process C# (does not use <see cref="HarnessRunner"/>; counts toward coverage). </summary>
    CSharp,
    /// <summary> External Rust harness (bin crate <c>deployment-test-harness</c> on top of lib-rust). </summary>
    Rust,
    /// <summary> External Node.js harness (src/lib-nodejs FFI bindings). </summary>
    NodeJs,
    /// <summary> External Python harness (src/lib-python PyO3 bindings, maturin develop into a venv). </summary>
    Python,
    /// <summary> External C++ harness (src/lib-cpp headers linked against velopack_libc). </summary>
    Cpp,
}

/// <summary> The result JSON emitted by a harness process as its last stdout line. </summary>
public sealed record HarnessResult(
    bool Ok,
    bool UpdateAvailable,
    string? TargetVersion,
    string? DownloadedFile,
    string? Sha256,
    string? Error);

/// <summary>
/// Builds and runs the external per-language source-test harnesses under
/// <c>test/Velopack.Deployment.Tests/harnesses/</c>. Each language is built at most once per test
/// session (semaphore-guarded, results cached — including failures). A missing toolchain causes a
/// dynamic test SKIP with an actionable reason; a build/compile failure while the toolchain IS
/// present FAILS the test with the captured build output; a runtime harness failure is returned
/// to the caller (and should FAIL the test).
///
/// Harness invocation contract: <c>&lt;harness&gt; &lt;config.json&gt;</c>, result JSON printed to
/// stdout as the LAST line, exit 0 on success.
/// </summary>
public static class HarnessRunner
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromMinutes(5);

    private static string RepoRoot => Path.GetFullPath(PathHelper.GetProjectDir());
    private static string HarnessesDir => PathHelper.GetTestRootPath("Velopack.Deployment.Tests", "harnesses");
    private static string ObjDir => PathHelper.GetTestRootPath("Velopack.Deployment.Tests", "obj", "harnesses");

    private enum BuildFailureKind
    {
        None,
        /// <summary> A required tool could not be launched/found — the row should SKIP. </summary>
        ToolchainMissing,
        /// <summary> The toolchain is present but the build failed — the row should FAIL. </summary>
        CompileError,
    }

    /// <summary> Thrown by build recipes when a required tool is absent (distinct from a compile error). </summary>
    private sealed class ToolchainMissingException(string message) : Exception(message);

    private sealed class LangState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool BuildAttempted;
        public BuildFailureKind FailureKind;
        public string? BuildFailure;
        // Launch recipe produced by a successful build: file name, leading args, working dir.
        public string? LaunchFile;
        public string[] LaunchPrefixArgs = Array.Empty<string>();
        public string? LaunchWorkDir;
    }

    private static readonly ConcurrentDictionary<HarnessLang, LangState> _states = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _toolProbes = new();
    private static string? _pythonExe; // resolved system interpreter used to create the venv
    private static string? _cmakeExe; // "cmake" from PATH, or the VS-bundled cmake found via vswhere

    /// <summary>
    /// Skips the current test when the language's harness sources are absent (a sibling build may not
    /// have produced them yet) or its toolchain is not installed. Cheap: does NOT build anything.
    /// </summary>
    public static async Task SkipUnlessAvailableAsync(HarnessLang lang, ILogger log)
    {
        if (lang == HarnessLang.CSharp)
            return; // in-process, always available

        var missingSource = GetMissingHarnessSource(lang);
        Assert.SkipWhen(missingSource != null, $"{lang} harness source '{missingSource}' is not present in the repo.");

        switch (lang) {
            case HarnessLang.Rust:
                Assert.SkipWhen(!await ProbeToolAsync("cargo", "--version"), "cargo (Rust toolchain) is not installed / not on PATH.");
                break;
            case HarnessLang.NodeJs:
                Assert.SkipWhen(!await ProbeToolAsync("node", "--version"), "node is not installed / not on PATH.");
                Assert.SkipWhen(!await ProbeNpmAsync(), "npm is not installed / not on PATH.");
                Assert.SkipWhen(!await ProbeToolAsync("cargo", "--version"), "cargo is required to build the Node.js native module.");
                break;
            case HarnessLang.Python:
                Assert.SkipWhen(await ResolvePythonAsync() == null, "python (3.x) is not installed / not on PATH.");
                Assert.SkipWhen(!await ProbeToolAsync("cargo", "--version"), "cargo is required to build the Python native module.");
                break;
            case HarnessLang.Cpp:
                Assert.SkipWhen(
                    await ResolveCMakeAsync() == null,
                    "cmake is not installed (not on PATH, and no Visual Studio-bundled cmake was found via vswhere).");
                Assert.SkipWhen(!await ProbeToolAsync("cargo", "--version"), "cargo is required to build velopack_libc.");
                break;
        }
    }

    /// <summary>
    /// Runs the harness for <paramref name="lang"/> with the given config file, building the harness
    /// first if this is the first use in the process. Skips on missing toolchain; FAILS (throws) when
    /// the toolchain is present but the harness fails to compile, or when the process produces no
    /// parseable result line.
    /// </summary>
    public static async Task<HarnessResult> RunAsync(HarnessLang lang, string configJsonPath, ILogger log, TimeSpan? timeout = null)
    {
        if (lang == HarnessLang.CSharp)
            throw new InvalidOperationException("The CSharp row runs in-process and does not use HarnessRunner.");

        await SkipUnlessAvailableAsync(lang, log);
        var state = await EnsureBuiltAsync(lang, log);
        Assert.SkipWhen(
            state.FailureKind == BuildFailureKind.ToolchainMissing,
            $"The {lang} harness toolchain is missing or could not be launched: {state.BuildFailure}");
        if (state.FailureKind == BuildFailureKind.CompileError) {
            throw new Exception(
                $"The {lang} harness failed to build even though its toolchain is present — the harness or library API is likely broken. " +
                $"Build output: {state.BuildFailure}");
        }

        var args = state.LaunchPrefixArgs.Append(configJsonPath).ToArray();
        var result = await ExecAsync(state.LaunchFile!, args, state.LaunchWorkDir!, null, log, timeout ?? DefaultRunTimeout, $"{lang}-harness");

        var lastLine = result.StdOut
            .Split('\n')
            .Select(l => l.Trim())
            .LastOrDefault(l => l.Length > 0);

        if (lastLine == null || !lastLine.StartsWith('{')) {
            throw new Exception(
                $"The {lang} harness (exit code {result.ExitCode}) did not emit a JSON result line. Output: {Truncate(result.Combined, 4000)}");
        }

        try {
            return JsonSerializer.Deserialize<HarnessResult>(lastLine, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        } catch (JsonException ex) {
            throw new Exception($"Failed to parse {lang} harness result line '{Truncate(lastLine, 1000)}': {ex.Message}", ex);
        }
    }

    private static string? GetMissingHarnessSource(HarnessLang lang)
    {
        string[] required = lang switch {
            HarnessLang.Rust => new[] { Path.Combine("rust", "Cargo.toml"), Path.Combine("rust", "src", "main.rs") },
            HarnessLang.NodeJs => new[] { Path.Combine("nodejs", "harness.mjs"), Path.Combine("nodejs", "package.json") },
            HarnessLang.Python => new[] { Path.Combine("python", "harness.py") },
            HarnessLang.Cpp => new[] { Path.Combine("cpp", "main.cpp"), Path.Combine("cpp", "CMakeLists.txt") },
            _ => Array.Empty<string>(),
        };
        return required.FirstOrDefault(rel => !File.Exists(Path.Combine(HarnessesDir, rel)));
    }

    private static async Task<LangState> EnsureBuiltAsync(HarnessLang lang, ILogger log)
    {
        var state = _states.GetOrAdd(lang, _ => new LangState());
        if (state.BuildAttempted)
            return state;

        await state.Gate.WaitAsync();
        try {
            if (state.BuildAttempted)
                return state;

            try {
                var sw = Stopwatch.StartNew();
                log.LogInformation("Building {Lang} harness (first use this session; this can take several minutes)...", lang);
                switch (lang) {
                    case HarnessLang.Rust:
                        await BuildRustAsync(state, log);
                        break;
                    case HarnessLang.NodeJs:
                        await BuildNodeJsAsync(state, log);
                        break;
                    case HarnessLang.Python:
                        await BuildPythonAsync(state, log);
                        break;
                    case HarnessLang.Cpp:
                        await BuildCppAsync(state, log);
                        break;
                    default:
                        throw new InvalidOperationException($"No build recipe for {lang}.");
                }

                log.LogInformation("{Lang} harness build completed in {Elapsed:0.0}s.", lang, sw.Elapsed.TotalSeconds);
            } catch (Exception ex) {
                // Classify the failure so rows can SKIP on a missing toolchain but FAIL on a real
                // compile error. Win32Exception means a tool executable could not be launched at all.
                state.FailureKind = ex is ToolchainMissingException or System.ComponentModel.Win32Exception
                    ? BuildFailureKind.ToolchainMissing
                    : BuildFailureKind.CompileError;
                state.BuildFailure = ex.Message;
                log.LogError(ex, "{Lang} harness build failed ({Kind}).", lang, state.FailureKind);
            } finally {
                state.BuildAttempted = true;
            }

            return state;
        } finally {
            state.Gate.Release();
        }
    }

    private static async Task BuildRustAsync(LangState state, ILogger log)
    {
        // The harness crate is deliberately excluded from the root workspace, so it builds into
        // its own target/ dir and never touches the workspace Cargo.lock.
        var harnessDir = Path.Combine(RepoRoot, "test", "Velopack.Deployment.Tests", "harnesses", "rust");
        var manifest = Path.Combine(harnessDir, "Cargo.toml");
        await ExecCheckedAsync("cargo", new[] { "build", "--manifest-path", manifest }, RepoRoot, null, log, BuildTimeout, "cargo-build");
        var exe = Path.Combine(harnessDir, "target", "debug", "deployment-test-harness" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        if (!File.Exists(exe))
            throw new Exception($"cargo build succeeded but the harness executable was not found at '{exe}'.");
        state.LaunchFile = exe;
        state.LaunchWorkDir = Path.GetDirectoryName(exe)!;
    }

    private static async Task BuildNodeJsAsync(LangState state, ILogger log)
    {
        var libDir = Path.Combine(RepoRoot, "src", "lib-nodejs");
        var harnessDir = Path.Combine(HarnessesDir, "nodejs");

        // Build src/lib-nodejs if its compiled output (lib/index.js + native module) is missing or stale.
        var libIndex = Path.Combine(libDir, "lib", "index.js");
        var srcIndex = Path.Combine(libDir, "src", "index.ts");
        var nativeDir = Path.Combine(libDir, "lib", "native");
        var nodeModules = Directory.Exists(nativeDir) ? Directory.GetFiles(nativeDir, "*.node") : Array.Empty<string>();
        // The native module is stale when any Rust source feeding it (the nodeffi crate or lib-rust
        // itself) is newer than the newest built *.node file.
        var newestNodeModule = nodeModules.Length == 0 ? DateTime.MinValue : nodeModules.Max(File.GetLastWriteTimeUtc);
        var newestNativeSource = new[] {
            Path.Combine(libDir, "velopack_nodeffi", "src"),
            Path.Combine(RepoRoot, "src", "lib-rust", "src"),
        }.SelectMany(d => Directory.Exists(d) ? Directory.GetFiles(d, "*", SearchOption.AllDirectories) : Array.Empty<string>())
            .Select(File.GetLastWriteTimeUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        var libStale = !File.Exists(libIndex)
            || nodeModules.Length == 0
            || (File.Exists(srcIndex) && File.GetLastWriteTimeUtc(srcIndex) > File.GetLastWriteTimeUtc(libIndex))
            || newestNativeSource > newestNodeModule;

        if (libStale) {
            if (!Directory.Exists(Path.Combine(libDir, "node_modules"))) {
                await ExecNpmCheckedAsync(new[] { "ci" }, libDir, log, "npm-ci-libnodejs");
            }

            // 'npm run dev' = cargo build -p velopack_nodeffi -p velopack_bins + tsc + copy-lib.js
            await ExecNpmCheckedAsync(new[] { "run", "dev" }, libDir, log, "npm-run-dev-libnodejs");
        } else {
            log.LogInformation("src/lib-nodejs is already built; skipping npm run dev.");
        }

        // Install the harness's file: dependency on src/lib-nodejs.
        await ExecNpmCheckedAsync(new[] { "install", "--no-audit", "--no-fund" }, harnessDir, log, "npm-install-harness");

        state.LaunchFile = "node";
        state.LaunchPrefixArgs = new[] { "harness.mjs" };
        state.LaunchWorkDir = harnessDir;
    }

    private static async Task BuildPythonAsync(LangState state, ILogger log)
    {
        var python = await ResolvePythonAsync() ?? throw new ToolchainMissingException("python is not on PATH.");
        var venvDir = Path.Combine(ObjDir, "pyvenv");
        var venvPython = OperatingSystem.IsWindows()
            ? Path.Combine(venvDir, "Scripts", "python.exe")
            : Path.Combine(venvDir, "bin", "python");

        if (!File.Exists(venvPython)) {
            Directory.CreateDirectory(ObjDir);
            await ExecCheckedAsync(python, new[] { "-m", "venv", venvDir }, RepoRoot, null, log, BuildTimeout, "python-venv");
        }

        await ExecCheckedAsync(venvPython, new[] { "-m", "pip", "install", "--quiet", "maturin" }, RepoRoot, null, log, BuildTimeout, "pip-maturin");

        // maturin develop installs the freshly-built extension module into the venv given by VIRTUAL_ENV.
        // It must run from src/lib-python: newer maturin invokes `pip install --group`, and pip resolves
        // dependency groups against the pyproject.toml in the CURRENT directory.
        var env = new Dictionary<string, string> { ["VIRTUAL_ENV"] = venvDir };
        var libPythonDir = Path.Combine(RepoRoot, "src", "lib-python");
        var manifest = Path.Combine(libPythonDir, "Cargo.toml");
        await ExecCheckedAsync(
            venvPython, new[] { "-m", "maturin", "develop", "--manifest-path", manifest }, libPythonDir, env, log, BuildTimeout, "maturin-develop");

        state.LaunchFile = venvPython;
        state.LaunchPrefixArgs = new[] { "harness.py" };
        state.LaunchWorkDir = Path.Combine(HarnessesDir, "python");
    }

    private static async Task BuildCppAsync(LangState state, ILogger log)
    {
        var cmake = await ResolveCMakeAsync() ?? throw new ToolchainMissingException("cmake could not be resolved (PATH or vswhere).");
        await ExecCheckedAsync("cargo", new[] { "build", "-p", "velopack_libc" }, RepoRoot, null, log, BuildTimeout, "cargo-libc");

        var srcDir = Path.Combine(HarnessesDir, "cpp");
        var buildDir = Path.Combine(ObjDir, "cpp-build");
        Directory.CreateDirectory(buildDir);

        var libcDir = Path.Combine(RepoRoot, "target", "debug");
        var configureArgs = new[] { "-S", srcDir, "-B", buildDir, $"-DVELOPACK_LIBC_DIR={libcDir}" };
        try {
            await ExecCheckedAsync(cmake, configureArgs, RepoRoot, null, log, BuildTimeout, "cmake-configure");
        } catch (Exception ex) {
            // The first configure does a FetchContent git clone (nlohmann/json) which can fail transiently.
            log.LogWarning("cmake configure failed ({Message}); retrying once...", Truncate(ex.Message, 500));
            await ExecCheckedAsync(cmake, configureArgs, RepoRoot, null, log, BuildTimeout, "cmake-configure-retry");
        }

        await ExecCheckedAsync(
            cmake,
            new[] { "--build", buildDir, "--config", "Debug" },
            RepoRoot, null, log, BuildTimeout, "cmake-build");

        // Generators differ: multi-config (VS) puts harness(.exe) in buildDir/Debug (matching
        // --config Debug above), single-config generators put it directly in buildDir. Check those
        // two locations deterministically first; only then fall back to a recursive search ordered
        // by newest write time, so a stale exe from a leftover config subdir (e.g. an old
        // Release/harness.exe in the cached build dir) can never shadow the one we just built.
        var exeName = OperatingSystem.IsWindows() ? "harness.exe" : "harness";
        var exe = new[] { Path.Combine(buildDir, "Debug", exeName), Path.Combine(buildDir, exeName) }.FirstOrDefault(File.Exists)
            ?? Directory.GetFiles(buildDir, exeName, SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (exe == null)
            throw new Exception($"cmake build succeeded but '{exeName}' was not found under '{buildDir}'.");
        state.LaunchFile = exe;
        state.LaunchWorkDir = Path.GetDirectoryName(exe)!; // dll/so is copied next to the exe by CMakeLists
    }

    // ---- process plumbing ----------------------------------------------------------------------

    private static Task<bool> ProbeToolAsync(string tool, string arg)
    {
        return _toolProbes.GetOrAdd(
            $"{tool} {arg}",
            _ => new Lazy<Task<bool>>(async () => {
                try {
                    var res = await ExecAsync(tool, new[] { arg }, RepoRoot, null, NullLogger.Instance, ProbeTimeout, $"probe-{tool}");
                    return res.ExitCode == 0;
                } catch {
                    return false;
                }
            })).Value;
    }

    private static Task<bool> ProbeNpmAsync()
    {
        return _toolProbes.GetOrAdd(
            "npm --version",
            _ => new Lazy<Task<bool>>(async () => {
                try {
                    var (file, args) = NpmCommand(new[] { "--version" });
                    var res = await ExecAsync(file, args, RepoRoot, null, NullLogger.Instance, ProbeTimeout, "probe-npm");
                    return res.ExitCode == 0;
                } catch {
                    return false;
                }
            })).Value;
    }

    private static async Task<string?> ResolvePythonAsync()
    {
        if (_pythonExe != null)
            return _pythonExe;
        foreach (var candidate in new[] { "python", "python3" }) {
            if (await ProbeToolAsync(candidate, "--version")) {
                _pythonExe = candidate;
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves cmake: PATH first, then (Windows) the Visual Studio-bundled cmake via vswhere — cmake
    /// is often installed as a VS component without being added to PATH.
    /// </summary>
    private static async Task<string?> ResolveCMakeAsync()
    {
        if (_cmakeExe != null)
            return _cmakeExe;

        if (await ProbeToolAsync("cmake", "--version")) {
            _cmakeExe = "cmake";
            return _cmakeExe;
        }

        if (OperatingSystem.IsWindows()) {
            var vswhere = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe");
            if (File.Exists(vswhere)) {
                try {
                    var res = await ExecAsync(
                        vswhere,
                        new[] { "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.CMake.Project", "-find", "**/cmake.exe" },
                        RepoRoot, null, NullLogger.Instance, ProbeTimeout, "vswhere-cmake");
                    var found = res.StdOut
                        .Split('\n')
                        .Select(l => l.Trim())
                        .FirstOrDefault(l => l.Length > 0 && File.Exists(l));
                    if (res.ExitCode == 0 && found != null) {
                        _cmakeExe = found;
                        return _cmakeExe;
                    }
                } catch { /* fall through to null */ }
            }
        }

        return null;
    }

    // npm is a .cmd shim on Windows and cannot be started directly with UseShellExecute=false.
    private static (string File, string[] Args) NpmCommand(string[] args)
        => OperatingSystem.IsWindows()
            ? ("cmd.exe", new[] { "/c", "npm" }.Concat(args).ToArray())
            : ("npm", args);

    private static async Task ExecNpmCheckedAsync(string[] args, string workDir, ILogger log, string label)
    {
        var (file, fullArgs) = NpmCommand(args);
        await ExecCheckedAsync(file, fullArgs, workDir, null, log, BuildTimeout, label);
    }

    private static async Task ExecCheckedAsync(
        string file, IReadOnlyList<string> args, string workDir, IDictionary<string, string>? env, ILogger log, TimeSpan timeout, string label)
    {
        var result = await ExecAsync(file, args, workDir, env, log, timeout, label);
        if (result.ExitCode != 0) {
            throw new Exception($"'{file} {String.Join(' ', args)}' exited with code {result.ExitCode}. Output: {Truncate(result.Combined, 6000)}");
        }
    }

    private static async Task<ProcessResult> ExecAsync(
        string file, IReadOnlyList<string> args, string workDir, IDictionary<string, string>? env, ILogger log, TimeSpan timeout, string label)
    {
        var psi = new ProcessStartInfo(file) {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        if (env != null) {
            foreach (var (k, v) in env)
                psi.Environment[k] = v;
        }

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => {
            if (e.Data != null) {
                stdout.AppendLine(e.Data);
                log.LogInformation("[{Label}] {Line}", label, e.Data);
            }
        };
        proc.ErrorDataReceived += (_, e) => {
            if (e.Data != null) {
                stderr.AppendLine(e.Data);
                log.LogInformation("[{Label}!] {Line}", label, e.Data);
            }
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try {
            await proc.WaitForExitAsync(cts.Token);
        } catch (OperationCanceledException) {
            try { proc.Kill(true); } catch { /* ignore */ }
            throw new TimeoutException($"'{file} {String.Join(' ', args)}' ({label}) timed out after {timeout.TotalSeconds:0}s.");
        }

        return new ProcessResult(proc.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(s.Length - max);
}
