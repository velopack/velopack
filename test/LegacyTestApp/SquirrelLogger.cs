using System;

namespace LegacyTestApp
{
    internal class SquirrelLogger : Squirrel.SimpleSplat.ILogger
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
}
