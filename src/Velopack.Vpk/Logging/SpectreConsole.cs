using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;
using Velopack.Packaging.Abstractions;

namespace Velopack.Vpk.Logging
{
    public class SpectreConsole : IFancyConsole
    {
        private readonly ILogger logger;

        public SpectreConsole(ILogger logger)
        {
            this.logger = logger;
        }

        public async Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action)
        {
            var start = DateTime.UtcNow;
            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[] {
                        new SpinnerColumn(),
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new ElapsedTimeColumn(),
                })
                .StartAsync(async ctx => await action(new Progress(logger, ctx)));
            logger.Info($"[bold]Finished in {DateTime.UtcNow - start}.[/]");
        }

        private class Progress : IFancyConsoleProgress
        {
            private readonly ILogger _logger;
            private readonly ProgressContext _context;

            public Progress(ILogger logger, ProgressContext context)
            {
                _logger = logger;
                _context = context;
            }

            public async Task RunTask(string name, Func<Action<int>, Task> fn)
            {
                _logger.Log(LogLevel.Debug, "Starting: " + name);

                var task = _context.AddTask($"[italic]{name}[/]");
                task.StartTask();

                void progress(int p)
                {
                    if (p < 0) {
                        task.IsIndeterminate = true;
                    } else {
                        task.IsIndeterminate = false;
                        task.Value = Math.Min(100, p);
                    }
                }

                await Task.Run(() => fn(progress)).ConfigureAwait(false);
                task.IsIndeterminate = false;
                task.StopTask();

                _logger.Log(LogLevel.Debug, $"[bold]Complete: {name}[/]");
            }
        }
    }
}
