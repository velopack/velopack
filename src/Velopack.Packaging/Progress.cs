using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Velopack.Packaging
{
    public class Progress
    {
        private readonly ILogger _logger;
        private readonly ProgressContext _context;

        public static bool IsEnabled { get; set; }

        private Progress(ILogger logger, ProgressContext context)
        {
            _logger = logger;
            _context = context;
        }

        public static async Task ExecuteAsync(ILogger logger, Func<Progress, Task> action)
        {
            var start = DateTime.UtcNow;
            if (IsEnabled) {
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
            } else {
                await action(new Progress(logger, null));
            }
            logger.Info($"[bold]Finished in {DateTime.UtcNow - start}.[/]");
        }

        public async Task RunTask(string name, Func<Action<int>, Task> fn)
        {
            var level = IsEnabled ? LogLevel.Debug : LogLevel.Information;
            _logger.Log(level, "Starting: " + name);

            if (IsEnabled) {
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
            } else {
                await Task.Run(() => fn((_) => { })).ConfigureAwait(false);
            }

            _logger.Log(level, $"[bold]Complete: {name}[/]");
        }
    }
}
