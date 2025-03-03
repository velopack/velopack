#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;

namespace Velopack.Logging
{
    public class NullVelopackLogger : IVelopackLogger
    {
        public static readonly NullVelopackLogger Instance = new();

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            // Do nothing
        }
    }
}