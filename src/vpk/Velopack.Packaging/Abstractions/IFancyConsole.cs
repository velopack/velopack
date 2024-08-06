namespace Velopack.Packaging.Abstractions;

public interface IFancyConsole : IConsole
{
    Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action);

    void WriteTable(string tableName, IEnumerable<IEnumerable<string>> rows, bool hasHeaderRow = true);

    Task<bool> PromptYesNo(string prompt, bool? defaultValue = null, TimeSpan? timeout = null);

    string EscapeMarkup(string text);
}
