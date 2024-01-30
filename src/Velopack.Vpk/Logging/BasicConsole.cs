﻿using Velopack.Packaging.Abstractions;

namespace Velopack.Vpk.Logging
{
    public class BasicConsole : IFancyConsole
    {
        private readonly ILogger logger;

        public BasicConsole(ILogger logger)
        {
            this.logger = logger;
        }

        public async Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action)
        {
            await action(new Progress(logger));
        }

        public void WriteLine(string text = "")
        {
            Console.WriteLine(text);
        }

        public void WriteTable(string tableName, IEnumerable<IEnumerable<string>> rows, bool hasHeaderRow = true)
        {
            Console.WriteLine(tableName);
            foreach (var row in rows) {
                Console.WriteLine("  " + String.Join("    ", row));
            }
        }

        private class Progress : IFancyConsoleProgress
        {
            private readonly ILogger _logger;

            public Progress(ILogger logger)
            {
                _logger = logger;
            }

            public async Task RunTask(string name, Func<Action<int>, Task> fn)
            {
                _logger.Info("Starting: " + name);
                await Task.Run(() => fn(_ => { }));
                _logger.Info("Complete: " + name);
            }
        }
    }
}