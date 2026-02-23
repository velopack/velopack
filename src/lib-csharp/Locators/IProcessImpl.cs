using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Velopack.Locators
{
    /// <summary>
    /// Provides an abstraction for process-related operations, allowing for platform-specific implementations.
    /// </summary>
    public interface IProcessImpl
    {
        /// <summary>
        /// Gets the full path to the current process executable.
        /// </summary>
        /// <returns>The full path to the current process executable.</returns>
        string GetCurrentProcessPath();

        /// <summary>
        /// Gets the process ID of the current process.
        /// </summary>
        /// <returns>The process ID of the current process.</returns>
        uint GetCurrentProcessId();

        /// <summary>
        /// Starts a new process with the specified executable path, arguments, and options.
        /// </summary>
        /// <param name="exePath">The path to the executable to start.</param>
        /// <param name="args">The command-line arguments to pass to the process.</param>
        /// <param name="workDir">The working directory for the new process.</param>
        /// <param name="showWindow">Whether to show a window for the new process.</param>
        void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow);

        /// <summary>
        /// Exit the current process with the given exit code.
        /// </summary>
        /// <param name="exitCode">The exit code</param>
        void Exit(int exitCode);
    }
}