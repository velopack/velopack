using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Velopack.Build;

/// <summary>
/// Executes dotnet CLI commands for tool management
/// </summary>
public class DotNetToolRunner
{
    private readonly TaskLoggingHelper _log;

    public DotNetToolRunner(TaskLoggingHelper log)
    {
        _log = log;
    }

    /// <summary>
    /// Check if a tool is installed
    /// </summary>
    public async Task<string?> GetInstalledVersionAsync(string toolName, bool isLocal, string workingDirectory, CancellationToken cancellationToken)
    {
        var args = isLocal ? "tool list --local" : "tool list --global";
        var output = await RunDotNetCommandAsync(args, workingDirectory, null, captureOutput: true, cancellationToken);
        
        if (output is null)
            return null;

        // Parse output to find tool version
        // Format: "vpk         1.2.3          vpk"
        foreach (var line in output.Split('\n'))
        {
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Equals(toolName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Install a dotnet tool
    /// </summary>
    public async Task<bool> InstallToolAsync(
        string toolName,
        string version,
        bool isLocal,
        bool includePrerelease,
        string? source,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append("tool install ");
        sb.Append(isLocal ? "--local " : "--global ");
        sb.Append(toolName);
        sb.Append(" --version ");
        sb.Append(version);
        
        if (includePrerelease)
        {
            sb.Append(" --prerelease");
        }
        
        if (!string.IsNullOrWhiteSpace(source))
        {
            sb.Append(" --add-source ");
            sb.Append(source);
        }

        _log.LogMessage(MessageImportance.High, $"Installing {toolName}@{version} ({(isLocal ? "local" : "global")})...");
        
        var exitCode = await RunDotNetCommandAsync(sb.ToString(), workingDirectory, null, captureOutput: false, cancellationToken).ConfigureAwait(false);
        return exitCode != null;
    }

    /// <summary>
    /// Update a dotnet tool to a specific version
    /// </summary>
    public async Task<bool> UpdateToolAsync(
        string toolName,
        string version,
        bool isLocal,
        bool includePrerelease,
        string? source,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.Append("tool update ");
        sb.Append(isLocal ? "--local " : "--global ");
        sb.Append(toolName);
        sb.Append(" --version ");
        sb.Append(version);
        
        if (includePrerelease)
        {
            sb.Append(" --prerelease");
        }
        
        if (!string.IsNullOrWhiteSpace(source))
        {
            sb.Append(" --add-source ");
            sb.Append(source);
        }

        _log.LogMessage(MessageImportance.High, $"Updating {toolName} to version {version}...");
        
        var exitCode = await RunDotNetCommandAsync(sb.ToString(), workingDirectory, null, captureOutput: false, cancellationToken).ConfigureAwait(false);
        return exitCode != null;
    }

    /// <summary>
    /// Run a dotnet tool with arguments
    /// </summary>
    public async Task<int> RunToolAsync(
        string toolName,
        string[] arguments,
        bool isLocal,
        string workingDirectory,
        System.Collections.Generic.Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        
        if (isLocal)
        {
            sb.Append("tool run ");
            sb.Append(toolName);
        }
        else
        {
            sb.Append(toolName);
        }
        
        if (arguments.Length > 0)
        {
            sb.Append(' ');
            sb.Append(string.Join(" ", arguments));
        }

        var result = await RunDotNetCommandAsync(sb.ToString(), workingDirectory, environmentVariables, captureOutput: false, cancellationToken).ConfigureAwait(false);
        return result == null ? -1 : 0;
    }

    /// <summary>
    /// Run a dotnet CLI command
    /// </summary>
    private async Task<string?> RunDotNetCommandAsync(
        string arguments,
        string workingDirectory,
        System.Collections.Generic.Dictionary<string, string>? environmentVariables,
        bool captureOutput,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        // Add environment variables
        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                process.StartInfo.Environment[kvp.Key] = kvp.Value;
            }
        }

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                if (captureOutput)
                {
                    output.AppendLine(e.Data);
                }
                else
                {
                    _log.LogMessage(MessageImportance.Low, e.Data);
                }
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                if (captureOutput)
                {
                    error.AppendLine(e.Data);
                }
                else
                {
                    _log.LogWarning(e.Data);
                }
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

        if (process.ExitCode != 0)
        {
            if (captureOutput)
            {
                _log.LogWarning($"dotnet {arguments} exited with code {process.ExitCode}");
                if (error.Length > 0)
                {
                    _log.LogWarning(error.ToString());
                }
            }
            return null;
        }

        return captureOutput ? output.ToString() : string.Empty;
    }
}
