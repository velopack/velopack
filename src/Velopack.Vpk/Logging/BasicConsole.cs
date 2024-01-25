using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;
using Velopack.Packaging.Abstractions;

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
