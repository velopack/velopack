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
        private readonly DefaultPromptValueFactory defaultFactory;

        public SpectreConsole(ILogger logger, DefaultPromptValueFactory defaultFactory)
        {
            this.logger = logger;
            this.defaultFactory = defaultFactory;
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

        public bool PromptYesNo(string prompt, bool? defaultValue = null)
        {
            return AnsiConsole.Confirm(prompt, defaultValue ?? defaultFactory.DefaultPromptValue);
        }

        public void WriteLine(string text = "")
        {
            AnsiConsole.Markup(text + Environment.NewLine);
        }

        public void WriteTable(string tableName, IEnumerable<IEnumerable<string>> rows, bool hasHeaderRow = true)
        {
            // Create a table
            var table = new Table();
            table.Title($"[bold underline]{tableName}[/]");
            table.Expand();
            table.LeftAligned();

            // Add some columns
            if (hasHeaderRow) {
                var headerRow = rows.First();
                rows = rows.Skip(1);
                foreach (var header in headerRow) {
                    table.AddColumn(header);
                }
            } else {
                var numColumns = rows.First().Count();
                for (int i = 0; i < numColumns; i++) {
                    table.AddColumn($"Column {i}");
                }
                table.HideHeaders();
            }

            // add rows
            foreach (var row in rows) {
                table.AddRow(row.ToArray());
            }

            // Render the table to the console
            AnsiConsole.Write(table);
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
