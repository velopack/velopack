using System;

namespace LegacyTestApp
{
#if VELOPACK
    using Microsoft.Extensions.Logging;
    class SquirrelLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Console.WriteLine(formatter(state, exception));
        }
    }
#else
    class SquirrelLogger : Squirrel.SimpleSplat.ILogger
    {
        protected SquirrelLogger()
        {
        }

        public Squirrel.SimpleSplat.LogLevel Level { get; set; }

        public static void Register()
        {
            Squirrel.SimpleSplat.SquirrelLocator.CurrentMutable.Register(() => new SquirrelLogger(), typeof(Squirrel.SimpleSplat.ILogger));
        }

        public void Write(string message, Squirrel.SimpleSplat.LogLevel logLevel)
        {
            Console.WriteLine(message);
        }
    }
#endif
}
