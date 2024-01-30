using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Abstractions
{
    public interface IFancyConsole
    {
        Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action);

        void WriteTable(string tableName, IEnumerable<IEnumerable<string>> rows, bool hasHeaderRow = true);

        void WriteLine(string text = "");
    }
}
