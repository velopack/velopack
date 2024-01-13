using System.Diagnostics.CodeAnalysis;

namespace Velopack.Packaging;

[ExcludeFromCodeCoverage]
public class ProcessFailedException : Exception
{
    public string Command { get; }
    public string StdOutput { get; }

    public ProcessFailedException(string command, string stdOutput)
        : base($"Process failed: '{command}'{Environment.NewLine}Output was -{Environment.NewLine}{stdOutput}")
    {
        Command = command;
        StdOutput = stdOutput;
    }

    public static void ThrowIfNonZero((int ExitCode, string StdOutput, string Command) result)
    {
        if (result.ExitCode != 0)
            throw new ProcessFailedException(result.Command, result.StdOutput);
    }
}
