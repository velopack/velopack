#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;

namespace Velopack.Logging
{
    public class ConsoleVelopackLogger : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            var logMessage = $"[{DateTime.Now.ToShortTimeString()}] [{logLevel}] {message}";
            if (exception != null) {
                logMessage += Environment.NewLine + exception;
            }

            Console.WriteLine(logMessage);
        }
    }
}