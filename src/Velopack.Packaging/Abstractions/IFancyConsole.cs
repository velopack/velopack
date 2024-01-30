using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Abstractions
{
    public interface IFancyConsole
    {
        Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action);

        void WriteTable(string tableName, IEnumerable<IEnumerable<string>> rows, bool hasHeaderRow = true);

        bool PromptYesNo(string prompt, bool? defaultValue = null);

        void WriteLine(string text = "");
    }
}
