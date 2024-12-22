using Velopack.Packaging.Abstractions;
using Velopack.Util;

namespace Velopack.Vpk.Logging;

public class BasicConsole : IFancyConsole
{
    private readonly ILogger logger;
    private readonly VelopackDefaults defaultFactory;

    public BasicConsole(ILogger logger, VelopackDefaults defaultFactory)
    {
        this.logger = logger;
        this.defaultFactory = defaultFactory;
    }

    public string EscapeMarkup(string text)
    {
        return text;
    }

    public async Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action)
    {
        var start = DateTime.UtcNow;
        await action(new Progress(logger));
        logger.Info($"Finished in {DateTime.UtcNow - start}.");
    }

    public Task<bool> PromptYesNo(string prompt, bool? defaultValue = null, TimeSpan? timeout = null)
    {
        return Task.FromResult(defaultValue ?? defaultFactory.DefaultPromptValue);
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

        public async Task<T> RunTask<T>(string name, Func<Action<int>, Task<T>> fn)
        {
            _logger.Info("Starting: " + name);
            var result = await Task.Run(() => fn(_ => { }));
            _logger.Info("Complete: " + name);
            return result;
        }
    }
}
