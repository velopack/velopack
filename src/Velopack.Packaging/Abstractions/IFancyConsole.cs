using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Abstractions
{
    public interface IFancyConsole
    {
        Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action);
    }
}
