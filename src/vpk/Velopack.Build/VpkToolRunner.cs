using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Velopack.Util;

namespace Velopack.Build;

/// <summary>
/// Executes dotnet CLI commands for tool management
/// </summary>
public class VpkToolRunner(TaskLoggingHelper log)
{
    private static string FindVpk()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string vpkPackLocation = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(x => x.Key == "VelopackVpkPackLocation")
            .Value ?? throw new InvalidOperationException("Could not find VPK pack location");

        string assemblyDirectory = Path.GetDirectoryName(assembly.Location)!;
        string vpkPath = Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", vpkPackLocation, "vpk.dll"));

        if (!File.Exists(vpkPath)) {
            throw new FileNotFoundException("vpk tool not found at expected path.", vpkPath);
        }

        return vpkPath;
    }


    /// <summary>
    /// Run a dotnet CLI command
    /// </summary>
    public async Task<int> RunVpk(
        IEnumerable<string> arguments,
        Dictionary<string, string>? environmentVariables,
        CancellationToken cancellationToken)
    {
        arguments = [FindVpk(), .. arguments];
        ProcessStartInfo startInfo = new() {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.AppendArgumentListSafe(arguments, out _);
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
            log.LogWarning($"dotnet {string.Join(" ", arguments)} exited with code {process.ExitCode}");
            if (error.Length > 0) {
                log.LogWarning(error.ToString());
            }
        }

        return process.ExitCode;
    }
}
