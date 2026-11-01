using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Velopack.Util;

namespace Velopack.Build;

/// <summary>
/// Executes dotnet CLI commands for tool management
/// </summary>
public class VpkToolRunner(string vpkVersion, string? vpkNugetSource, TaskLoggingHelper log)
{
    public string ProjectDirectory { get; set; } = Directory.GetCurrentDirectory();

    public string? NugetConfigFile { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Run a dotnet CLI command
    /// </summary>
    public async Task<int> RunVpk(
        IEnumerable<string> arguments,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(arguments);
        using var process = new Process {
            StartInfo = startInfo
        };

        if (environmentVariables != null) {
            foreach (var kvp in environmentVariables) {
                process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (sender, e) => {
            if (e.Data != null) {
                output.AppendLine(e.Data);
                log.LogMessage(MessageImportance.Low, e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) => {
            if (e.Data != null) {
                error.AppendLine(e.Data);
                log.LogWarning(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

#if NET8_0_OR_GREATER
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#else
        using (cancellationToken.Register(() => {
            try {
                process.Kill();
            } catch (InvalidOperationException) {
            } catch (Win32Exception ex) {
                log.LogWarning($"Failed to stop VPK tool after cancellation: {ex.Message}");
            }
        })) {
            await System.Threading.Tasks.Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
#endif

        if (process.ExitCode != 0) {
            log.LogWarning($"dotnet {startInfo.Arguments} exited with code {process.ExitCode}");
            if (output.Length > 0) {
                log.LogWarning(output.ToString());
            }
            if (error.Length > 0) {
                log.LogWarning(error.ToString());
            }
        }

        return process.ExitCode;
    }

    internal ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        //NB: dotnet tool exec, is the alias for dnx
        string[] processArguments = ["tool", "exec", "vpk", "-y", "--version", vpkVersion];

        var configFile = NugetConfigFile;
        if (string.IsNullOrWhiteSpace(configFile)) {
            for (var directory = new DirectoryInfo(ProjectDirectory); directory != null; directory = directory.Parent) {
                var config = directory.EnumerateFiles()
                    .FirstOrDefault(file => file.Name.Equals("nuget.config", StringComparison.OrdinalIgnoreCase));
                if (config != null) {
                    configFile = config.FullName;
                    break;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(configFile)) {
            processArguments = [.. processArguments, "--configfile",
                Path.GetFullPath(Path.IsPathRooted(configFile) ? configFile : Path.Combine(ProjectDirectory, configFile))];
        }

        if (!string.IsNullOrWhiteSpace(vpkNugetSource)) {
            processArguments = [.. processArguments, "--source", vpkNugetSource!];
        }

        processArguments = [.. processArguments, "--", .. arguments];

        ProcessStartInfo startInfo = new() {
            FileName = "dotnet",
            WorkingDirectory = ProjectDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.AppendArgumentListSafe(processArguments, out _);
        return startInfo;
    }

    public async Task<int> RunVpkJson(
        IEnumerable<string> command,
        Dictionary<string, object?> options,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        string configPath = Path.Combine(Path.GetTempPath(), $"velopack-{Guid.NewGuid():N}.json");
        try {
#if NETSTANDARD2_0
            using (var stream = new FileStream(configPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true)) {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                    Chmod(configPath, OwnerReadWritePermissions) != 0) {
                    throw new IOException($"Failed to restrict permissions on temporary VPK config '{configPath}'.",
                        new Win32Exception(Marshal.GetLastWin32Error()));
                }
                await JsonSerializer.SerializeAsync(stream, options, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
#else
            var fileOptions = new FileStreamOptions {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            };
            if (!OperatingSystem.IsWindows()) {
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var stream = new FileStream(configPath, fileOptions)) {
                await JsonSerializer.SerializeAsync(stream, options, JsonOptions, cancellationToken).ConfigureAwait(false);
            }
#endif

            string[] arguments = ["[json]", .. command, configPath];
            return await RunVpk(arguments, environmentVariables, cancellationToken).ConfigureAwait(false);
        } finally {
            try {
                File.Delete(configPath);
            } catch (IOException ex) {
                log.LogWarning($"Failed to remove temporary VPK JSON config '{configPath}': {ex.Message}");
            } catch (UnauthorizedAccessException ex) {
                log.LogWarning($"Failed to remove temporary VPK JSON config '{configPath}': {ex.Message}");
            }
        }
    }

#if NETSTANDARD2_0
    private const uint OwnerReadWritePermissions = 0x180;

    [DllImport("libc", EntryPoint = "chmod", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int Chmod(string path, uint mode);
#endif
}
