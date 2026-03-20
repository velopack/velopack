using System.Diagnostics;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Velopack.Util;

namespace Velopack.Build;

/// <summary>
/// Executes dotnet CLI commands for tool management
/// </summary>
public class VpkToolRunner(string vpkVersion, string? vpkNugetSource, TaskLoggingHelper log)
{
    /// <summary>
    /// Run a dotnet CLI command
    /// </summary>
    public async Task<int> RunVpk(
        IEnumerable<string> arguments,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        //NB: dotnet tool exec, is the alias for dnx
        string[] processArguments = ["tool", "exec", "vpk", "-y", "--version", vpkVersion];
        
        if (!string.IsNullOrWhiteSpace(vpkNugetSource)) {
            processArguments = [.. processArguments, "--add-source", vpkNugetSource!];
        }

        processArguments = [.. processArguments, "--", .. arguments];

        ProcessStartInfo startInfo = new() {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.AppendArgumentListSafe(processArguments, out _);
        using var process = new Process {
            StartInfo = startInfo
        };

        // Add environment variables
        if (environmentVariables != null) {
            foreach (var kvp in environmentVariables) {
                process.StartInfo.Environment[kvp.Key] = kvp.Value;
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
        await System.Threading.Tasks.Task.Run(() => process.WaitForExit(), cancellationToken);
#endif

        if (process.ExitCode != 0) {
            log.LogWarning($"dotnet {string.Join(" ", processArguments)} exited with code {process.ExitCode}");
            if (output.Length > 0) {
                log.LogWarning(output.ToString());
            }
            if (error.Length > 0) {
                log.LogWarning(error.ToString());
            }
        }

        return process.ExitCode;
    }
}
