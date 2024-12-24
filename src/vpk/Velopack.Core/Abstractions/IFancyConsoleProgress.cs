namespace Velopack.Core.Abstractions;

public interface IFancyConsoleProgress
{
    Task RunTask(string name, Func<Action<int>, Task> fn);
    Task<T> RunTask<T>(string name, Func<Action<int>, Task<T>> fn);
}
