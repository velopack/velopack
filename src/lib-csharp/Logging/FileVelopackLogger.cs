#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Diagnostics;
using System.IO;

namespace Velopack.Logging
{
    public class FileVelopackLogger : IVelopackLogger, IDisposable
    {
        private uint ProcessId { get; }
        private readonly object _lock = new();
        private readonly StreamWriter _writer;
        private readonly FileStream _fileStream;

        public FileVelopackLogger(string filePath, uint processId)
        {
            ProcessId = processId;
            _fileStream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(_fileStream);
        }

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            try {
                lock (_lock) {
                    var logMessage = $"[lib-csharp:{ProcessId}] [{DateTime.Now.ToShortTimeString()}] [{logLevel}] {message}";
                    if (exception != null) {
                        logMessage += Environment.NewLine + exception;
                    }

                    _writer.WriteLine(logMessage);
                }
            } catch (Exception ex) {
                Debug.WriteLine($"Error writing to log file: {ex}");
            }
        }

        public void Dispose()
        {
            try {
                lock (_lock) {
                    _writer.Flush();
                    _writer.Dispose();
                    _fileStream.Dispose();
                }
            } catch (Exception ex) {
                Debug.WriteLine($"Error disposing log file: {ex}");
            }
        }
    }
}