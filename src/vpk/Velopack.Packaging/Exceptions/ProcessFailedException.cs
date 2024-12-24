using System.Diagnostics.CodeAnalysis;

namespace Velopack.Packaging.Exceptions;

[ExcludeFromCodeCoverage]
public class ProcessFailedException : Exception
{
    public string Command { get; }
    public string StdOutput { get; }

    public ProcessFailedException(string command, string stdOutput, string stdErr)
        : base(
            $"Process failed: '{command}'{Environment.NewLine}Output was -{Environment.NewLine}{stdOutput}{Environment.NewLine}StdErr was -{Environment.NewLine}{stdErr}")
    {
        Command = command;
        StdOutput = stdOutput;
    }

    public static void ThrowIfNonZero((int ExitCode, string StdOutput, string StdErr, string Command) result)
    {
        if (result.ExitCode != 0)
            throw new ProcessFailedException(result.Command, result.StdOutput, result.StdErr);
    }
}