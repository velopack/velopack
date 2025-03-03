#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;

namespace Velopack.Logging
{
    public interface IVelopackLogger
    {
        void Log(VelopackLogLevel logLevel, string? message, Exception? exception);
    }
}